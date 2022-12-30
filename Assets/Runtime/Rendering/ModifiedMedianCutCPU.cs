using System;
using System.Runtime.InteropServices;
using System.Threading;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;

namespace Pixelism {

    /// <summary>
    /// Color quantization using modified median cut
    /// http://leptonica.org/papers/mediancut.pdf
    /// http://www.leptonica.org/color-quantization.html
    /// </summary>
    public class ModifiedMedianCutCPU : IColorQuantizer {
        public int NumColor { get; set; } = 16;

        public ComputeBuffer ColorPalette => colorPalette;

        public ComputeBuffer ColorPaletteCount => colorPaletteCount;

        private ComputeBuffer colorPalette;
        private ComputeBuffer colorPaletteCount;

        private RenderTexture readback; // todo ringbuffer 内部でそうやってるのならいいけれど、その保証はない

        private bool disposed = false;

        // NumColor * PopulationOrVolume     : pixel-count
        // NumColor * (1-PopulationOrVolume) : pixel-count * volume 広く分布している領域に対して効果がある
        public float PopulationOrVolume { get; set; } = 0.6f; // 0.3 ~ 0.9

        public byte HistogramBinBit { get; set; } = 4; // 5;
        public bool FullColorSpace { get; set; } = true;
        public bool Color12Bit { get; set; } = false; // TODO

        public bool HasChanged => true; // readback後のSetBufferDataをコマンドに積めないので、常に有効

        public ModifiedMedianCutCPU() {
            colorPaletteCount = new ComputeBuffer(1, Marshal.SizeOf<int>());
            colorPaletteCount.SetData(new int[1] { 0 });
        }

        public void Dispose() {
            disposed = true;
            colorPalette?.Dispose();
            colorPalette = null;
            colorPaletteCount?.Dispose();
            colorPaletteCount = null;
            readback?.Release();
        }

        public void Quantize(CommandBuffer command, RenderTargetIdentifier source, int width, int height) {
            if (readback == null || readback.width != width || readback.height != height) {
                readback?.Release();
                readback = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            }
            command.BeginSample("Quantize");
            // readbackはRenderTargetIdentifier なし、non temporal RTのみなのでblit
            command.Blit(source, readback); // expect format conversion
            // TODO use native array version
            command.RequestAsyncReadback(readback, (request) => {
                if (request.done && !request.hasError && !disposed) {
                    var data = request.GetData<uint>();
                    if (Color12Bit) {
                        var bit = new BitInfo(4);
                        Quantize<FourBitConverter>(null, data, bit);
                    } else {
                        var bit = new BitInfo(HistogramBinBit);
                        Quantize<DefaultConverter>(null, data, bit); // ここでcommandを渡しても、順序が補償されないだろう
                    }
                }
            });
            command.EndSample("Quantize");
        }

        public void Quantize(CommandBuffer command, Texture2D source) {
            if (source.format != TextureFormat.RGBA32) {
                throw new ArgumentException("source format is not RGBA32.");
            }

            // TODO フォーマット見て、nativearrayそのままならそれ、そうでないならmanaged colorで取って変換する
            // RGBA LE (0xAABBGGRR)
            var pixels = source.GetPixelData<uint>(0);
            if (Color12Bit) {
                var bit = new BitInfo(4);
                Quantize<FourBitConverter>(command, pixels, bit);
            } else {
                var bit = new BitInfo(HistogramBinBit);
                Quantize<DefaultConverter>(command, pixels, bit);
            }
        }

        // todo static if available
        private void Quantize<T>(CommandBuffer command, NativeArray<uint> source, in BitInfo bit) where T : struct, IIndexConverter {
            //var worker = JobsUtility.JobWorkerMaximumCount; // 実際はmain thraedもcomplete待ち時に使用されることがあるので、用途によっては +1

            Profiler.BeginSample("Quantize");
            var converters = CreateConverter<T>(bit);
            var converter = converters[0]; // default
            using (var histogram = CreateHistogramBin(bit)) {

                var handle = new BuildHistogramJob<T>(source, histogram, converter).Schedule();

                // TODO histogramの1以上がnum color以下の場合、直に生成すればよい

                using (NativeArray<ColorVolumeCPU> volumes = new NativeArray<ColorVolumeCPU>(NumColor, Allocator.TempJob, NativeArrayOptions.UninitializedMemory)) {

                    // set 1st volume
                    if (FullColorSpace) {
                        // ditherありならこっちの方がいいらしいが…
                        SetupFullColorSpace(volumes.Slice(0, 1), new uint3(bit.channelMax), source.Length);
                    } else {
                        var minmax = new MinMaxVolumeJob<T>(source, volumes.Slice(0, 1), converter);
                        var h = minmax.Schedule();
                        handle = JobHandle.CombineDependencies(handle, h);
                    }
                    handle.Complete();

                    int paletteCount = 1;
                    // 通常のmedian cutと同様にpopulation (pixel count)を指標とする
                    int numColor = (int)(PopulationOrVolume * NumColor);
                    paletteCount = Cut<T>(volumes, histogram, paletteCount, numColor, false, bit, converters);

                    // modofiedでは、割合で、広域に分布したpoplation (pixel count * volume)を指標とする
                    // update priority for next
                    // jobでやってもいいんだけれど、num colorが多くない限りはボトルネックではない
                    UpdatePriority(volumes.Slice(0, paletteCount));
                    paletteCount = Cut<T>(volumes, histogram, paletteCount, NumColor, true, bit, converters);

                    // generate color palette
                    using (NativeArray<float3> palette = new NativeArray<float3>(paletteCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory)) {

                        new ComputeColorJob<T>(palette, volumes, histogram, converter).Schedule().Complete();
                        if (colorPalette == null || colorPalette.count != paletteCount) {
                            colorPalette?.Dispose();
                            colorPalette = new ComputeBuffer((int)paletteCount, Marshal.SizeOf<float3>());
                        }
                        // send to compute buffer
                        if (command != null) {
                            // TODO average bufferを恒常的にしたほうがよい。4フレーム寿命があるので、その間にcommandが実行されると成立するが…
                            command.SetBufferData(colorPalette, palette);
                            command.SetBufferData(colorPaletteCount, new int[1] { paletteCount });
                        } else {
                            colorPalette.SetData(palette);
                            colorPaletteCount.SetData(new int[1] { paletteCount });
                        }

                    }
                }
            }
            Profiler.EndSample();
        }

        private static void SetupFullColorSpace(NativeSlice<ColorVolumeCPU> volumes, uint3 max, int length) {
            volumes[0] = new ColorVolumeCPU(uint3.zero, max, (uint)length, false); // countはpixelと同じ数になるはず
        }

        private static void UpdatePriority(NativeSlice<ColorVolumeCPU> volumes, bool populationWithVolume = true) {
            for (int i = 0; i < volumes.Length; ++i) {
                volumes[i] = volumes[i].UpdatePriority(populationWithVolume);
            }
        }

        private static int Cut<T>(NativeArray<ColorVolumeCPU> volumes, NativeArray<uint> histogram, int paletteCount, int numColor, bool populationWithVolume, in BitInfo bit, T[] converters, int maxIterations = 64) where T : struct, IIndexConverter {
            // 外で確保すれば1回アロケートを減らせるが…
            using (NativeArray<uint> sumPerAxis = new NativeArray<uint>(bit.channelSize, Allocator.TempJob, NativeArrayOptions.UninitializedMemory)) {

                int iterations = 0;
                var converter = converters[0]; // default
                while (paletteCount < numColor) {
                    //Debug.Log(string.Join("\r\n", volumes.Select(x => "min:" + x.min + " max:" + x.max + " count:" + x.count + " prio:" + x.priority)));

                    // jobにできるけれど、数が多くない限り意味は無い、このあと見つからない場合に打ち切りになるので、sync pointになる
                    // GPGPUのように空回しを許容すればフルジョブ化して完全非同期化できそうだが、所詮はCPUなのでそこまでやらなくても…
                    var index = FindCuttingVolume(volumes.Slice(0, paletteCount));
                    if (index < 0) {
                        break;
                    }

                    var vbox1 = volumes.Slice(index, 1); // in out
                    var vbox2 = volumes.Slice(paletteCount, 1); // out

                    var axis = vbox1[0].ComputeAxis(); // 軸の決定
                    var axisconv = converters[axis];

                    var clear = new ClearBufferJob(sumPerAxis).Schedule();
                    var build = new BuildAxisHistogramJob<T>(histogram, vbox1, sumPerAxis, axisconv).Schedule(clear);
                    var sumup = new SumupAxisHistogramJob<T>(vbox1, sumPerAxis, axisconv).Schedule(build);

                    //Debug.Log("[" + index + "] axis:" + axis + " len: " + len + " partial: " + string.Join(", ", sumPerBin) + " med: " + median);

                    var cut = new CutVolumeJob<T>(vbox1, vbox2, sumPerAxis, axisconv).Schedule(sumup);

                    // 前で部分で求めてるのを使ればいいんだけれど
                    var count1 = new CountVolumeJob<T>(histogram, vbox1, converter).Schedule(cut);
                    var count2 = new CountVolumeJob<T>(histogram, vbox2, converter).Schedule(cut);
                    var handle = JobHandle.CombineDependencies(count1, count2);
                    handle.Complete();

                    // set priority
                    // volumeで分岐すると
                    // priority=countの場合、priority=0, count=C
                    // priority=count *volumeの場合、priority=0, count=C
                    // 分岐しない場合、
                    // priority = countの場合、priority=C, count=C
                    // priority = count * volumeの場合、priority=C, count=C
                    // volume=1ということは、最小単位まで分解されていること、分割対象にしてはいけないのでpriority=0が望ましいか
                    if (vbox1[0].Volume > 1) {
                        vbox1[0] = vbox1[0].UpdatePriority(populationWithVolume);
                    }
                    if (vbox2[0].Volume > 1) {
                        vbox2[0] = vbox2[0].UpdatePriority(populationWithVolume);
                    }

                    // 両方とも0になるのは何かおかしいきがする
                    if (vbox1[0].count == 0 && vbox2[0].count == 0) {
                        Debug.LogError("both are zero.");
                    }

                    // TODO それぞれ 0のときの適切な処理
                    // 捨てて問題ないのか？
                    // 元の色が全く含まれていない領域だが誤差拡散した結果、視覚的にはそれらしく見える可能性はあるが
                    if (vbox2[0].count == 0) {
                        //Debug.Log("vbox2 is zero.");
                    } else if (vbox1[0].count == 0) {
                        //Debug.Log("vbox1 is zero.");
                        vbox1[0] = vbox2[0]; // 後者は有効なので上書きして反映
                    } else {
                        ++paletteCount;
                    }

                    if (++iterations > maxIterations) {
                        break;
                    }
                }
            }
            return paletteCount;
        }

        private static int FindCuttingVolume(NativeSlice<ColorVolumeCPU> volumes) {
            if (volumes.Length == 0) {
                return -1;
            }
            int index = 0;
            var max = volumes[0].priority;
            for (int i = 1; i < volumes.Length; ++i) {
                var v = volumes[i].priority;
                if (v > max) {
                    index = i;
                    max = v;
                }
            }
            return max > 0 ? index : -1;
        }

        public readonly struct BitInfo {

            // rgb別の方がRGB565が出来るかもしれないが…
            public BitInfo(int channelBit) {
                this.channelBit = channelBit;
                significant = (8 - channelBit);
                invert = 1u << significant;
                channelMask = 0xFFu >> significant;
                channelSize = 1 << channelBit;
                channelMax = channelSize - 1;
                histogramBit = channelBit * 3; // rgb
                histogramSize = 1 << histogramBit;
                histogramMax = histogramSize - 1;
            }

            // TODO rename
            public readonly int channelBit;

            public readonly int significant;
            public readonly uint invert;
            public readonly uint channelMask;
            public readonly int channelSize;
            public readonly int channelMax;
            public readonly int histogramBit;
            public readonly int histogramSize;
            public readonly int histogramMax;
        }

        public interface IIndexConverter {

            uint ToIndex(uint rgba); // RGBA (LE) 0xAABBGGRR

            uint ToIndex(uint x, uint y, uint z); // axisで入力した順

            uint3 Quantize(uint rgba); // for histogram bin

            uint3 Inverse(uint r, uint g, uint b, uint count); // invert quantize to weighted color [0, 255]

            float3 ToColor(uint3 sum, uint total); // final color (average) [0, 255]

            int3 Swizzle { get; }
            int ChannelSize { get; }
            BitInfo Bit { get; }
        }

        // axis分作って返す
        // generic constructor argumentは受け付けないが、readonlyでありたいのでこういう方式になるか
        // Tをそのまま返すとboxingは避けられないが、T[]で複数作ると避けられるか？
        internal static T[] CreateConverter<T>(in BitInfo bit) where T : struct, IIndexConverter {
            if (typeof(T) == typeof(DefaultConverter)) {
                return (new DefaultConverter[] { new DefaultConverter(bit, 0), new DefaultConverter(bit, 1), new DefaultConverter(bit, 2) }) as T[];
            } else if (typeof(T) == typeof(FourBitConverter)) {
                return (new FourBitConverter[] { new FourBitConverter(bit, 0), new FourBitConverter(bit, 1), new FourBitConverter(bit, 2) }) as T[];
            }
            return null;
        }

        internal readonly struct DefaultConverter : IIndexConverter {

            public DefaultConverter(in BitInfo bit) : this(bit, 0) {
            }

            public DefaultConverter(in BitInfo bit, int axis) {
                this.bit = bit;
                this.shift = new int3(0, bit.channelBit, bit.channelBit * 2);
                swizzle = new int3(axis, (axis + 1) % 3, (axis + 2) % 3); // swizzling index
            }

            private readonly BitInfo bit;
            private readonly int3 shift;
            private readonly int3 swizzle;

            // interleave
            public uint ToIndex(uint rgba) {
                var rgb = Quantize(rgba);
                uint bin = ToIndex(rgb.x, rgb.y, rgb.z);
                return bin;
            }

            // interleave
            public uint ToIndex(uint x, uint y, uint z) {
                return (x << shift[swizzle.x]) | (y << shift[swizzle.y]) | (z << shift[swizzle.z]);
            }

            // deinterleave
            public uint3 Quantize(uint rgba) {
                var r = (rgba >> bit.significant) & bit.channelMask;
                var g = (rgba >> (8 + bit.significant)) & bit.channelMask;
                var b = (rgba >> (16 + bit.significant)) & bit.channelMask; // Aがある可能性がある
                return new uint3(r, g, b);
            }

            // inverse color
            public uint3 Inverse(uint r, uint g, uint b, uint count) {
                return (uint3)(count * (new float3(r, g, b) + 0.5f) * bit.invert);
            }

            public float3 ToColor(uint3 sum, uint total) {
                return (float3)sum / total;
            }

            public int3 Swizzle => swizzle;
            public int ChannelSize => bit.channelSize;
            public BitInfo Bit => bit;

        }

        // RGB444 4096 colors
        internal readonly struct FourBitConverter : IIndexConverter {

            public FourBitConverter(in BitInfo bit, int axis) {
                this.bit = bit; // 4-bit であってほしいが、そうでなくてもできるとよい
                this.shift = new int3(0, bit.channelBit, bit.channelBit * 2);
                swizzle = new int3(axis, (axis + 1) % 3, (axis + 2) % 3); // swizzling index
            }

            private readonly BitInfo bit;
            private readonly int3 shift;
            private readonly int3 swizzle;

            // interleave
            public uint ToIndex(uint rgba) {
                var rgb = Quantize(rgba);
                uint bin = ToIndex(rgb.x, rgb.y, rgb.z);
                return bin;
            }

            // interleave
            public uint ToIndex(uint x, uint y, uint z) {
                return (x << shift[swizzle.x]) | (y << shift[swizzle.y]) | (z << shift[swizzle.z]);
            }

            // deinterleave
            public uint3 Quantize(uint rgba) {
                // ヘタに弄らず、元のままの方がいいかも
                // そのままの方が任意ビットのbinを扱えるので精度がまだ残りやすいかも
#if true
                var r = (rgba) & 0xFF;
                var g = (rgba >> 8) & 0xFF;
                var b = (rgba >> 16) & 0xFF; // Aがある可能性がある
                var rgba444 = (uint3)(math.round(new float3(r, g, b) / 17.0f) * 17.0f);
                return rgba444 >> bit.significant;
#else
                var r = (rgba >> bit.significant) & bit.channelMask;
                var g = (rgba >> (8 + bit.significant)) & bit.channelMask;
                var b = (rgba >> (16 + bit.significant)) & bit.channelMask; // Aがある可能性がある
                return new uint3(r, g, b);
#endif
            }

            // inverse quantized to weight
            public uint3 Inverse(uint r, uint g, uint b, uint count) {
#if true
                // Quantizeで弄った分を戻す
                var c = new uint3(r, g, b);
                c |= c << bit.significant; // 0xA -> 0xAA, ここで戻さずに ToColorで戻した方が良いかも
                return count * c;
#else
                return (uint3)(count * (new float3(r, g, b) + 0.5f) * bit.mult);
#endif
            }

            public float3 ToColor(uint3 sum, uint total) {
                // sum は0x11の倍数になるはずなんだけれど、total除算で合わないか？要検証
                // 再度合わせる
                return math.round(((float3)sum / total) / 17.0f) * 17.0f;
            }

            public int3 Swizzle => swizzle;
            public int ChannelSize => bit.channelSize;
            public BitInfo Bit => bit;
        }

        private void RegisterSpecializations() {
            // burstでconcrete typesが要求されるので作っておく
            // genric -> generic job呼び出しだと解決されないらしい
#pragma warning disable CS0219 // 変数は割り当てられていますが、その値は使用されていません
            {
                var a = new BuildAxisHistogramJob<DefaultConverter>();
                var b = new SumupAxisHistogramJob<DefaultConverter>();
                var c = new CountVolumeJob<DefaultConverter>();
                var d = new MinMaxVolumeJob<DefaultConverter>();
                var e = new BuildHistogramJob<DefaultConverter>();
                var f = new CutVolumeJob<DefaultConverter>();
                var g = new ComputeColorJob<DefaultConverter>();
            }
            {
                var a = new BuildAxisHistogramJob<FourBitConverter>();
                var b = new SumupAxisHistogramJob<FourBitConverter>();
                var c = new CountVolumeJob<FourBitConverter>();
                var d = new MinMaxVolumeJob<FourBitConverter>();
                var e = new BuildHistogramJob<FourBitConverter>();
                var f = new CutVolumeJob<FourBitConverter>();
                var g = new ComputeColorJob<FourBitConverter>();
            }
#pragma warning restore CS0219 // 変数は割り当てられていますが、その値は使用されていません
        }

        // 16bit未満のレンジだとushortも使えるが、高速化に寄与するかは不明
        [StructLayout(LayoutKind.Sequential)]
        public readonly struct ColorVolumeCPU {
            public readonly uint count; // offset 0 で書き込むジョブがあるので先頭
            public readonly uint3 min;
            public readonly uint3 max;
            public readonly ulong priority; // propertyで get functionでflagで count返すか、 count * volume返す方がよさそう。後者は32bitだとoverflowする可能性がある

            public ColorVolumeCPU(uint3 min, uint3 max, uint count, bool populationWithVolume) {
                this.min = min;
                this.max = max;
                this.count = count;
                this.priority = 0; // propertyより前に先に割り当てが必要
                this.priority = populationWithVolume ? (count * Volume) : count;
            }

            public ColorVolumeCPU(uint3 min, uint3 max) {
                this.min = min;
                this.max = max;
                this.count = 0; // later
                this.priority = 0; // later
            }

            public uint Volume {
                get {
                    var len = (max - min) + 1; // min<=max、+1 乗算して0になって欲しくないので
                    return len.x * len.y * len.z;
                }
            }

            public ColorVolumeCPU UpdatePriority(bool populationWithVolume) {
                return new ColorVolumeCPU(min, max, count, populationWithVolume);
            }

            public int ComputeAxis() {
                var edge = (max - min) + 1; // 比較なので +1 無くても成立するが、min<=maxであり、Volumeと一応整合性を保つ
                int axis;
                // 同値の場合 G>R>B で優先
                if (edge.y >= edge.z) {
                    if (edge.y >= edge.x) {
                        axis = 1; // g
                    } else {
                        axis = 0; // r
                    }

                } else {
                    if (edge.x >= edge.z) {
                        axis = 0; // r
                    } else {
                        axis = 2; // b
                    }
                }

                return axis;
            }
        }

        internal static NativeArray<uint> CreateHistogramBin(in BitInfo bit, Allocator allocator = Allocator.TempJob) {
            return new NativeArray<uint>(bit.histogramSize, allocator, NativeArrayOptions.ClearMemory);
        }

        [BurstCompile]
        internal unsafe struct BuildHistogramJob<T> : IJobParallelFor where T : IIndexConverter {

            public BuildHistogramJob(NativeArray<uint> pixels, NativeArray<uint> histogram, T conv) {
                this.pixels = pixels;
                this.histogram = histogram;
                this.conv = conv;
            }

            [ReadOnly] private NativeArray<uint> pixels;
            [WriteOnly, NativeDisableParallelForRestriction] private NativeArray<uint> histogram;
            private readonly T conv;

            // IJobParallelFor
            public void Execute(int index) {
                // TODO 前処理で4bitリダクションする別job
                var c = pixels[index];
                var bin = conv.ToIndex(c);
                Interlocked.Increment(ref ((int*)histogram.GetUnsafePtr())[bin]);
            }

            public JobHandle Schedule(JobHandle dependsOn = default, int worker = 32) {
                return this.Schedule(pixels.Length, Math.DivRoundUp(pixels.Length, worker), dependsOn);
            }
        }

        [BurstCompile]
        internal struct MinMaxVolumeJob<T> : IJob where T : IIndexConverter {

            public MinMaxVolumeJob(NativeArray<uint> pixels, NativeSlice<ColorVolumeCPU> volume, T conv) {
                this.pixels = pixels;
                this.volume = volume;
                this.conv = conv;
            }

            [ReadOnly] private NativeArray<uint> pixels;
            [WriteOnly, NativeFixedLength(1), NativeDisableContainerSafetyRestriction] private NativeSlice<ColorVolumeCPU> volume;
            private readonly T conv;

            // Interlocked compareで頑張ればparallelでいけるが…
            public void Execute() {
                uint3 min = new uint3(uint.MaxValue, uint.MaxValue, uint.MaxValue);
                uint3 max = new uint3(uint.MinValue, uint.MinValue, uint.MinValue);
                for (int i = 0; i < pixels.Length; ++i) {
                    var c = pixels[i];
                    var n = conv.Quantize(c);
                    min = math.min(min, n);
                    max = math.max(max, n);
                }
                volume[0] = new ColorVolumeCPU(min, max, (uint)pixels.Length, false);
            }

        }

        [BurstCompile]
        internal unsafe struct ClearBufferJob : IJobParallelFor {

            public ClearBufferJob(NativeArray<uint> sumPerAxis) {
                this.sumPerAxis = sumPerAxis;
            }

            [WriteOnly] private NativeArray<uint> sumPerAxis;

            public void Execute(int index) {
                sumPerAxis[index] = 0;
            }

            public JobHandle Schedule(JobHandle dependsOn = default, int worker = 16) {
                return this.Schedule(sumPerAxis.Length, Math.DivRoundUp(sumPerAxis.Length, worker), dependsOn);
            }
        }

        // カット軸に沿ったvolume範囲のヒストグラムの構築 part.1
        [BurstCompile]
        internal unsafe struct BuildAxisHistogramJob<T> : IJobParallelFor where T : IIndexConverter {

            public BuildAxisHistogramJob(NativeArray<uint> histogram, NativeSlice<ColorVolumeCPU> volume, NativeArray<uint> sumPerAxis, T conv) {
                this.histogram = histogram;
                this.volume = volume;
                this.sumPerAxis = sumPerAxis;
                this.conv = conv;
            }

            [ReadOnly] private NativeArray<uint> histogram;
            [ReadOnly, NativeFixedLength(1)] private NativeSlice<ColorVolumeCPU> volume;
            [WriteOnly, NativeDisableParallelForRestriction] private NativeArray<uint> sumPerAxis;
            private readonly T conv;

            public void Execute(int index) {
                var vol = volume[0];
                var len = vol.max - vol.min;

                var swizzle = conv.Swizzle; // 意識せずにiterationをまわせればいいんだけど
                if (index > len[swizzle.x]) { // out of range
                    return;
                }
                uint i = (uint)index + vol.min[swizzle.x];

                //uint total = 0;
                //for (var i = vol.min[swizzle.x]; i <= vol.max[swizzle.x]; ++i) {
                uint sum = 0;
                for (var j = vol.min[swizzle.y]; j <= vol.max[swizzle.y]; ++j) {
                    for (var k = vol.min[swizzle.z]; k <= vol.max[swizzle.z]; ++k) {
                        var bin = conv.ToIndex(i, j, k);
                        sum += histogram[(int)bin];
                    }
                }
                //total += sum;
                //sumPerAxis[(int)i] = total;
                Interlocked.Add(ref ((int*)sumPerAxis.GetUnsafePtr())[i], (int)sum);
                //}
            }

            public JobHandle Schedule(JobHandle dependsOn = default, int worker = 32) {
                // 3chのうち1chを並列化, histgramが持つべき？
                return this.Schedule(conv.ChannelSize, Math.DivRoundUp(conv.ChannelSize, worker), dependsOn);
            }
        }

        // カット軸に沿ったvolume範囲のヒストグラムの構築 part.2
        [BurstCompile]
        internal struct SumupAxisHistogramJob<T> : IJob where T : IIndexConverter {

            public SumupAxisHistogramJob(NativeSlice<ColorVolumeCPU> volume, NativeArray<uint> sumPerAxis, T conv) {
                this.volume = volume;
                this.sumPerAxis = sumPerAxis;
                this.conv = conv;
            }

            [ReadOnly, NativeFixedLength(1)] private NativeSlice<ColorVolumeCPU> volume;
            private NativeArray<uint> sumPerAxis;
            private readonly T conv;

            // Interlocked compareで頑張ればparallelでいけるが…
            public void Execute() {
                var vol = volume[0];
                var swizzle = conv.Swizzle;

                // 前の値を足しこんでいく、後の方のbinは前の累積値になる
                for (var i = vol.min[swizzle.x] + 1; i <= vol.max[swizzle.x]; ++i) {
                    sumPerAxis[(int)i] += sumPerAxis[(int)(i - 1)];
                }

            }

        }

        [BurstCompile]
        internal struct CutVolumeJob<T> : IJob where T : IIndexConverter {

            public CutVolumeJob(NativeSlice<ColorVolumeCPU> src, NativeSlice<ColorVolumeCPU> dest0, NativeArray<uint> sumPerAxis, T conv) {
                this.inout = src;
                this.output = dest0;
                this.sumPerAxis = sumPerAxis;
                this.conv = conv;
            }

            // TODO or given written index

            [NativeFixedLength(1), NativeDisableContainerSafetyRestriction] private NativeSlice<ColorVolumeCPU> inout;
            [WriteOnly, NativeFixedLength(1), NativeDisableContainerSafetyRestriction] private NativeSlice<ColorVolumeCPU> output;
            [ReadOnly] private NativeArray<uint> sumPerAxis;
            private readonly T conv;

            // Interlocked compareで頑張ればparallelでいけるが…
            public void Execute() {
                var vol = inout[0];
                var swizzle = conv.Swizzle;

                var total = sumPerAxis[(int)vol.max[swizzle.x]];
                var median = total / 2;

                for (var i = vol.min[swizzle.x]; i <= vol.max[swizzle.x]; ++i) {
                    if (sumPerAxis[(int)i] > median) { // 分割すべき中央値を探す 昇順なのでbinary searchいけるけれど、要素数は多くないから大差無いかな
                        var left = i - vol.min[swizzle.x];
                        var right = vol.max[swizzle.x] - i;

                        // modified では大きい方を入れ替えることで偏りを減らす
                        var max = vol.max;
                        if (left > right) {
                            max[swizzle.x] = math.max(vol.min[swizzle.x], (i - 1) - left / 2);
                        } else {
                            max[swizzle.x] = math.min(vol.max[swizzle.x] - 1, i + right / 2);
                        }
                        ColorVolumeCPU vbox1 = new ColorVolumeCPU(vol.min, max);
                        var min = vol.min;
                        min[swizzle.x] = vbox1.max[swizzle.x] + 1;
                        var vbox2 = new ColorVolumeCPU(min, vol.max);

                        inout[0] = vbox1;
                        output[0] = vbox2;
                        return;
                    }
                }

                // fallback
                // 原則見つかるはずだが…
                // inoutを無くすかは悩ましい。エラー？しかし放置していても次の試行時に選ばれて無限に終わらない可能性も
                output[0] = default; // zero

            }

        }

        [BurstCompile]
        internal unsafe struct CountVolumeJob<T> : IJobParallelFor where T : IIndexConverter {

            public CountVolumeJob(NativeArray<uint> histogram, NativeSlice<ColorVolumeCPU> volume, T conv) {
                this.histogram = histogram;
                this.volume = volume;
                this.conv = conv;

                // 事前にcount 0 埋めされてること
            }

            [ReadOnly] private NativeArray<uint> histogram;
            [NativeFixedLength(1), NativeDisableContainerSafetyRestriction] private NativeSlice<ColorVolumeCPU> volume;
            private readonly T conv;

            // batchかjobでtree的に求める方が早くなる可能性はある。が別のバッファも必要
            public void Execute(int index) {
                // constructorで求めておけるが、前ジョブからsyncせずに実行したい場合はjob中で求めることになる
                var min = volume[0].min;
                var max = volume[0].max;
                var len = max - min;

                // 最大の軸を並列すれば効率を最大化できるが、とりあえずswizzle Xベースで起動されたとする
                // 最大の軸は同期ポイントにschedule時に求めることはできない
                // 見切り発車するにしてもカットする軸以外いずれかが最大軸になりやすいが確実ではない
                // swizzleのswizzleが必要になる
                // var axis = ComputeAxis((int3)len);
                // int3 i = new int3(axis, (axis + 1) % 3, (axis + 2) % 3);

                var swizzle = conv.Swizzle;
                if (index > len[swizzle.x]) { // out of range
                    return;
                }
                uint x = (uint)index + min[swizzle.x];
                //for (var i = vol.min[swizzle.x]; i <= vol.max[swizzle.x]; ++i) {

                for (var y = min[swizzle.y]; y <= max[swizzle.y]; ++y) {
                    for (var z = min[swizzle.z]; z <= max[swizzle.z]; ++z) {
                        var b = conv.ToIndex(x, y, z);
                        if (histogram[(int)b] > 0) {
                            // readonly structだと都合が悪いかも？いけた。非効率なコードが生成されているかもしれないが…
                            // readonlyでもメモリ上書き出来てしまうな
                            Interlocked.Add(ref ((int*)volume.GetUnsafePtr())[0], (int)histogram[(int)b]); // write offset 0 (count)
                        }
                    }
                }

                // }
            }

            public JobHandle Schedule(JobHandle dependsOn = default, int worker = 32) {
                // 3chのうち1chを並列化
                return this.Schedule(conv.ChannelSize, Math.DivRoundUp(conv.ChannelSize, worker), dependsOn);
            }
        }

        [BurstCompile]
        internal struct ComputeColorJob<T> : IJobParallelFor where T : IIndexConverter {

            public ComputeColorJob(NativeSlice<float3> colorPalette, NativeArray<ColorVolumeCPU> volumes, NativeArray<uint> histogram, T conv) {
                this.colorPalette = colorPalette;
                this.volumes = volumes;
                this.histogram = histogram;
                this.conv = conv;
            }

            [WriteOnly] private NativeSlice<float3> colorPalette;
            [ReadOnly] private NativeArray<ColorVolumeCPU> volumes;
            [ReadOnly] private NativeArray<uint> histogram;
            private readonly T conv;

            // palette単位で分割しているが、さらに軸単位で分割すると実行時間の偏りを減らせる可能性が高い
            public void Execute(int index) {
                // generate color palette
                // 加重平均

                // 12bit colorにする場合、ここでToColorを工夫するだけでもなりそうだが、誤差が広がる可能性があるので、
                // 事前のbin割り当ての段階でそういう量子化しておきたい

                var vol = volumes[index];
                uint total = 0;
                uint3 sum = 0;
                for (var r = vol.min.x; r <= vol.max.x; ++r) {
                    for (var g = vol.min.y; g <= vol.max.y; ++g) {
                        for (var b = vol.min.z; b <= vol.max.z; ++b) {
                            var bin = conv.ToIndex(r, g, b);
                            var count = histogram[(int)bin];
                            if (count > 0) { // converterでcountを無視するようにも出来るが、そんな変なことをする予定は無し
                                total += count;
                                sum += conv.Inverse(r, g, b, count);
                            }
                        }
                    }
                }

                if (total > 0) {
                    //colorPalette[index] = math.saturate((float3)(sum / total) / 255.0f);
                    colorPalette[index] = math.saturate(conv.ToColor(sum, total) / 255.0f);
                } else {
                    // full spaceだとあり得るかも
                    // 0 が許容されるということはcutで0は完全に捨ててはいけないのか？
                    var n = (vol.max + vol.min + 1) / 2;
                    colorPalette[index] = math.saturate(conv.ToColor(conv.Inverse(n.x, n.y, n.z, 1), 1) / 255.0f); // 下と微妙に値が違うはずだが
                    //colorPalette[index] = math.saturate((float3)(mult * (vol.max + vol.min + 1) / 2) / 255.0f);
                }

                // TODO 平均を求める都合上、0または255にはなりにくい。偏差とかである程度の偏りがある場合、0または255に近寄るようなウェイトを付けてもよいのでは？
                // もしくは全体的にそういうウェイトを用いる
            }

            public JobHandle Schedule(JobHandle dependsOn = default, int worker = 16) {
                return this.Schedule(colorPalette.Length, Math.DivRoundUp(colorPalette.Length, worker), dependsOn);
            }
        }

        public void OnGUI() {
            FullColorSpace = GUILayout.Toggle(FullColorSpace, "Full ColorSpace");
            GUILayout.Label("Population or Volume:\r\n" + PopulationOrVolume);
            PopulationOrVolume = GUILayout.HorizontalSlider(PopulationOrVolume, 0.0f, 1.0f);
            if (!Color12Bit) {
                using (new GUILayout.HorizontalScope()) {
                    int inc = 0;
                    if (GUILayout.Button("-")) {
                        --inc;
                    }
                    GUILayout.Label("Histogram Bit: " + HistogramBinBit);
                    if (GUILayout.Button("+")) {
                        ++inc;
                    }
                    HistogramBinBit = (byte)math.clamp((int)HistogramBinBit + inc, 4, 8);
                }
            }
            Color12Bit = GUILayout.Toggle(Color12Bit, "12-Bit Color");
        }

    }
}
