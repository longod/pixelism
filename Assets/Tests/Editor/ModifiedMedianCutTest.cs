using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using Random = Unity.Mathematics.Random;

namespace Pixelism.Test {

    public class ModifiedMedianCutTest {
        private CommandBuffer command = new CommandBuffer();
        private ModifiedMedianCut medianCut = new ModifiedMedianCut();
        private AddressablesHelper.HandleCollector collector = new AddressablesHelper.HandleCollector();

        private Dictionary<string, Texture2D> cachedImage = new Dictionary<string, Texture2D>(); // cached test images
        private Dictionary<string, HistogramTest.CacheHistogram> cachedHistogram = new Dictionary<string, HistogramTest.CacheHistogram>(); // 別のテストと共有できるとよい
        private ModifiedMedianCutCPU.BitInfo bit = new ModifiedMedianCutCPU.BitInfo(4);
        private ModifiedMedianCutCPU.DefaultConverter[] converters;

        private ComputeBuffer histogramBuffer;
        private ComputeBuffer minmaxBuffer;
        private ComputeBuffer indirectBuffer;

        private static readonly string[] addresses = {
                "img_640x400_3x8bit_RGB_color_bars_CMYKWRGB",
                "img_640x400_3x8bit_RGB_color_rainbow",
                "img_640x400_3x8bit_RGB_gray_level_all",
                "img_2448x2448_3x8bit_SRC_RGB_pencils_a",
            };

        private static IEnumerable<string> TestImage() {
            foreach (var key in addresses) {
                yield return key; // ロードしてテクスチャを返せるが、テストパターン生成時にロードされる?
            }
        }

        [OneTimeSetUp]
        public void OneTimeSetUp() {
            converters = ModifiedMedianCutCPU.CreateConverter<ModifiedMedianCutCPU.DefaultConverter>(bit);
            foreach (var key in addresses) {
                AddressablesHelper.LoadAssetAsync<Texture2D>(key, res => {
                    cachedImage.Add(key, res);
                    cachedHistogram.Add(key, HistogramTest.CacheHistogram.Create(res, converters[0]));
                }).Collect(collector);
            }
            collector.WaitForCompletion();
            histogramBuffer = Histogram.CreateHistogramBuffer();
            minmaxBuffer = Histogram.CreateMinMaxBuffer();
            indirectBuffer = DispatchArguments.Create(1);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown() {
            foreach (var value in cachedHistogram.Values) {
                value.Dispose();
            }
            cachedHistogram.Clear();
            cachedImage.Clear();
            collector.Dispose();
            medianCut.Dispose();
            indirectBuffer.Dispose();
            minmaxBuffer.Dispose();
            histogramBuffer.Dispose();
        }

        [SetUp]
        public void Setup() {
            command.Clear();
            indirectBuffer.SetData(new DispatchArguments[] { new DispatchArguments() { threadGroupCountX = 1, threadGroupCountY = 1, threadGroupCountZ = 1 } });
        }

        [TearDown]
        public void TearDown() {
        }

        [Test]
        public void SetupVolume() {
            using (var volumeBuffer = new ComputeBuffer(1, Marshal.SizeOf<ColorVolume>())) {
                using (var scratchBuffer = ModifiedMedianCut.CreateScratchBuffer()) {
                    var min = new uint3(0x1, 0x2, 0x3);
                    var max = new uint3(0xd, 0xe, 0xf);
                    var count = 17;
                    minmaxBuffer.SetData(new uint3[] { min, max });

                    medianCut.SetupVolume(command, volumeBuffer, scratchBuffer, count, minmaxBuffer);
                    Graphics.ExecuteCommandBuffer(command);
                    {
                        Scratch actual = scratchBuffer.GetData<Scratch>();
                        Assert.AreEqual(1, actual.volumeCount);
                        Assert.AreEqual(0, actual.index);
                        Assert.AreEqual(new uint3(0, 1, 2), actual.swizzle);

                    }
                    {
                        ColorVolume actual = volumeBuffer.GetData<ColorVolume>();
                        Assert.AreEqual(count, actual.count);
                        Assert.AreEqual(count, actual.priority);
                        Assert.AreEqual(min, actual.min);
                        Assert.AreEqual(max, actual.max);
                    }
                }
            }
        }

        [TestCase(4, true)] // axis
        [TestCase(5, true)] // axis
        [TestCase(6, true)] // axis
        [TestCase(15, true)] // edge
        [TestCase(16, false)] // out of range
        [TestCase(0, false)] // zero pattern
        public void FindCuttingVolume(int volumeCount, bool valid) {

            float[] priorities = { 1, 2, 4, 11, 4, 7, 3, 10, 11, 7, 3, 0, 2, 4, 11, 4 }; // 16
            if (volumeCount == 0 && !valid) {
                priorities[0] = 0;
            }

            uint3 min;
            uint3 max;
            switch (volumeCount % 3) {
                case 0: // z
                    min = new uint3(0, 1, 2);
                    max = new uint3(8, 7, 6);
                    break;
                case 1: // y
                    min = new uint3(0, 2, 1);
                    max = new uint3(6, 9, 7);
                    break;
                case 2: // z
                    min = new uint3(2, 1, 0);
                    max = new uint3(2, 4, 6);
                    break;
                default:
                    throw new ArgumentException();
            }

            var volumes = priorities.Select(x => new ColorVolume() { priority = x, min = min, max = max, count = (uint)x }).ToArray();
            Assert.AreEqual(16, volumes.Length);

            using (var volumesBuffer = new ComputeBuffer(volumes.Length, Marshal.SizeOf<ColorVolume>())) {
                volumesBuffer.SetData(volumes);
                using (var scratchBuffer = ModifiedMedianCut.CreateScratchBuffer()) {
                    scratchBuffer.SetData(new Scratch() { index = -1, volumeCount = (uint)volumeCount });

                    medianCut.FindCuttingVolume(command, volumesBuffer, scratchBuffer, indirectBuffer, volumesBuffer.count);
                    Graphics.ExecuteCommandBuffer(command);
                    {
                        DispatchArguments actual = indirectBuffer.GetData<DispatchArguments>();
                        uint expected = valid ? 1u : 0u;
                        Assert.AreEqual(expected, actual.threadGroupCountX);
                        Assert.AreEqual(expected, actual.threadGroupCountY);
                        Assert.AreEqual(expected, actual.threadGroupCountZ);
                    }
                    if (valid) { // 無効時未定義
                        var maximum = volumes.Take(volumeCount + 1).Max(x => x.priority);
                        var index = volumes.Take(volumeCount + 1).ToList().FindIndex(x => x.priority == maximum);
                        Scratch actual = scratchBuffer.GetData<Scratch>();
                        Assert.AreEqual(index, actual.index);

                        var v = volumes[index];
                        ModifiedMedianCutCPU.ColorVolumeCPU e = new ModifiedMedianCutCPU.ColorVolumeCPU(v.min, v.max);
                        var swizzle = converters[e.ComputeAxis()].Swizzle;
                        Assert.AreEqual((uint3)swizzle, actual.swizzle);
                    }
                }
            }
        }

        [Test]
        public void BuildAxis([ValueSource(nameof(TestImage))] string key, [Values(0, 1, 2)] int axis, [Values(0u, 7u)] uint min, [Values(0xfu, 8u, 7u)] uint max) {
            var histo = cachedHistogram[key];
            histogramBuffer.SetData(histo.Histogram);

            // min, maxのみが必要
            var volumes = Enumerable.Repeat(new ColorVolume() { min = new uint3(min, min, min), max = new uint3(max, max, max), count = 0, priority = 0 }, 16).ToArray();
            var index = volumes.Length - 1; // なんでもいいが0以外がベター
            var converter = converters[axis];

            using (var vol = new NativeArray<ModifiedMedianCutCPU.ColorVolumeCPU>(volumes.Select(x => new ModifiedMedianCutCPU.ColorVolumeCPU(x.min, x.max, x.count, false)).ToArray(), Allocator.TempJob)) {
                using (NativeArray<uint> sumPerAxis = new NativeArray<uint>(converter.ChannelSize, Allocator.TempJob, NativeArrayOptions.ClearMemory)) {
                    var build = new ModifiedMedianCutCPU.BuildAxisHistogramJob<ModifiedMedianCutCPU.DefaultConverter>(histo.Histogram, vol.Slice(index, 1), sumPerAxis, converter).Schedule();
                    var sumup = new ModifiedMedianCutCPU.SumupAxisHistogramJob<ModifiedMedianCutCPU.DefaultConverter>(vol.Slice(index, 1), sumPerAxis, converter).Schedule(build);
                    JobHandle.ScheduleBatchedJobs();

                    using (var volumesBuffer = new ComputeBuffer(volumes.Length, Marshal.SizeOf<ColorVolume>())) {
                        volumesBuffer.SetData(volumes);

                        using (var scratchBuffer = ModifiedMedianCut.CreateScratchBuffer()) {
                            scratchBuffer.SetData(new Scratch() { index = index, volumeCount = (uint)volumes.Length, swizzle = (uint3)converter.Swizzle });

                            using (var sumPerAxisBuffer = ModifiedMedianCut.CreateAxisBuffer()) {

                                medianCut.BuildAxis(command, volumesBuffer, scratchBuffer, histogramBuffer, sumPerAxisBuffer, indirectBuffer);

                                Graphics.ExecuteCommandBuffer(command);

                                uint[] actual = new uint[sumPerAxisBuffer.count];
                                sumPerAxisBuffer.GetData(actual);

                                sumup.Complete();
                                AssertHelper.AreEqual<uint>(sumPerAxis, actual);
                            }
                        }
                    }
                }

            }
        }

        [Test]
        public void CutVolume([ValueSource(nameof(TestImage))] string key, [Values(0, 1, 2)] int axis, [Values(0u, 7u)] uint min, [Values(0xfu, 8u, 7u)] uint max) {
            var histo = cachedHistogram[key];
            var volumes = new ColorVolume[] {
                new ColorVolume() {  min = new uint3(min, min, min), max = new uint3(max, max, max), count = 0, priority = 0 },
                new ColorVolume(),
            };
            var converter = converters[axis];

            using (NativeArray<uint> sumPerAxis = new NativeArray<uint>(bit.channelSize, Allocator.TempJob, NativeArrayOptions.ClearMemory)) {
                using (var vol = new NativeArray<ModifiedMedianCutCPU.ColorVolumeCPU>(volumes.Select(x => new ModifiedMedianCutCPU.ColorVolumeCPU(x.min, x.max, x.count, false)).ToArray(), Allocator.TempJob)) {
                    var expected1 = vol.Slice(0, 1); // in out
                    var expected2 = vol.Slice(1, 1); // out
                    var build = new ModifiedMedianCutCPU.BuildAxisHistogramJob<ModifiedMedianCutCPU.DefaultConverter>(histo.Histogram, vol.Slice(0, 1), sumPerAxis, converter).Schedule();
                    var sumup = new ModifiedMedianCutCPU.SumupAxisHistogramJob<ModifiedMedianCutCPU.DefaultConverter>(expected1, sumPerAxis, converter).Schedule(build);
                    var cut = new ModifiedMedianCutCPU.CutVolumeJob<ModifiedMedianCutCPU.DefaultConverter>(expected1, expected2, sumPerAxis, converter).Schedule(sumup);
                    JobHandle.ScheduleBatchedJobs();

                    using (var volumesBuffer = new ComputeBuffer(volumes.Length, Marshal.SizeOf<ColorVolume>())) {
                        volumesBuffer.SetData(volumes);

                        using (var scratchBuffer = ModifiedMedianCut.CreateScratchBuffer()) {
                            scratchBuffer.SetData(new Scratch() { index = 0, volumeCount = 1, swizzle = (uint3)converter.Swizzle });

                            using (var sumPerAxisBuffer = ModifiedMedianCut.CreateAxisBuffer()) {
                                cut.Complete(); // この段階だとsumup まででよいが、sumup, cutの2回complete呼ぶ手間がかかる
                                sumPerAxisBuffer.SetData(sumPerAxis);

                                medianCut.CutVolume(command, volumesBuffer, scratchBuffer, sumPerAxisBuffer, indirectBuffer);

                                Graphics.ExecuteCommandBuffer(command);

                                ColorVolume[] actual = new ColorVolume[volumesBuffer.count];
                                volumesBuffer.GetData(actual);

                                Assert.AreEqual(expected1[0].min, actual[0].min);
                                Assert.AreEqual(expected1[0].max, actual[0].max);
                                Assert.AreEqual(expected2[0].min, actual[1].min);
                                Assert.AreEqual(expected2[0].max, actual[1].max);
                            }
                        }
                    }
                }

            }
        }

        [Test]
        public void CountVolume([ValueSource(nameof(TestImage))] string key, [Values(0u, 7u)] uint min, [Values(8u, 7u)] uint mid, [Values(0xfu, 8u)] uint max, [Values(false, true)] bool populationProductVolume) {
            var histogram = cachedHistogram[key].Histogram;
            histogramBuffer.SetData(histogram);

            var lmax = math.max(min, mid);
            var rmin = math.min(mid, max);
            var volumes = new ColorVolume[] {
                new ColorVolume() { min = new uint3(min, min, min), max = new uint3(lmax, lmax, lmax), count = 0, priority = 0 },
                new ColorVolume() { min = new uint3(rmin, rmin, rmin), max = new uint3(max, max, max), count = 0, priority = 0 }
            };
            float norm = populationProductVolume ? 1.0f / 4096.0f : 1.0f;
            var converter = converters[0];

            using (var vol = new NativeArray<ModifiedMedianCutCPU.ColorVolumeCPU>(volumes.Select(x => new ModifiedMedianCutCPU.ColorVolumeCPU(x.min, x.max)).ToArray(), Allocator.TempJob)) {
                var expected1 = vol.Slice(0, 1); // in out
                var expected2 = vol.Slice(1, 1); // out
                var count1 = new ModifiedMedianCutCPU.CountVolumeJob<ModifiedMedianCutCPU.DefaultConverter>(histogram, expected1, converter).Schedule();
                var count2 = new ModifiedMedianCutCPU.CountVolumeJob<ModifiedMedianCutCPU.DefaultConverter>(histogram, expected2, converter).Schedule();
                var handle = JobHandle.CombineDependencies(count1, count2);
                JobHandle.ScheduleBatchedJobs();

                using (var volumeBuffer = new ComputeBuffer(volumes.Length, Marshal.SizeOf<ColorVolume>())) {
                    volumeBuffer.SetData(volumes);
                    using (var scratchBuffer = ModifiedMedianCut.CreateScratchBuffer()) {
                        scratchBuffer.SetData(new Scratch() { volumeCount = 1, index = 0, swizzle = new uint3(0, 1, 2), normalizer = norm });

                        medianCut.CountVolume(command, volumeBuffer, scratchBuffer, histogramBuffer, indirectBuffer, populationProductVolume);

                        Graphics.ExecuteCommandBuffer(command);

                        Scratch[] scratch = new Scratch[scratchBuffer.count];
                        scratchBuffer.GetData(scratch);
                        ColorVolume[] actual = new ColorVolume[volumeBuffer.count];
                        volumeBuffer.GetData(actual);

                        handle.Complete();
                        // todo impl to cpu
                        uint expectedCount = 1;
                        if (expected2[0].count == 0) {
                        } else if (expected1[0].count == 0) {
                            expected1[0] = expected2[0]; // 後者は有効なので上書きして反映
                        } else {
                            expectedCount = 2;
                        }
                        Assert.AreEqual(expectedCount, scratch[0].volumeCount);
                        Assert.AreEqual(0, scratch[0].index);
                        Assert.AreEqual(expected1[0].count, actual[0].count);
                        Assert.AreEqual(expected1[0].Volume > 1 ? expected1[0].UpdatePriority(populationProductVolume).priority * norm : 0.0f, actual[0].priority);
                        if (expectedCount == 2) {
                            Assert.AreEqual(expected2[0].count, actual[1].count);
                            Assert.AreEqual(expected2[0].Volume > 1 ? expected2[0].UpdatePriority(populationProductVolume).priority * norm : 0.0f, actual[1].priority);
                        }

                    }
                }
            }
        }

        [Test]
        public void UpdatePriority([Values(false, true)] bool populationProductVolume, [Values(false, true)] bool zeroRange, [Values(false, true)] bool zeroCount) {
            // TODO range 0からフルレンジ、までほどほどに分布したり、全て0になるようなデータが良い
            // zeroRange: Trueのとき、全部が0だとあまり良くない気も
            Random rand = new Random(0x1234);
            var volumes = Enumerable.Range(1, 16).Select(x => {
                var mid = rand.NextUInt3(0, 0xf);
                var min = rand.NextUInt3(zeroRange ? mid : 0, mid);
                var max = rand.NextUInt3(mid, zeroRange ? mid : 0xf);
                var count = zeroCount ? 0u : rand.NextUInt(0x1, 0xf);
                return new ColorVolume() { min = min, max = max, count = count, priority = -1.0f };
            }).ToArray();
            Assert.AreEqual(16, volumes.Length); // 16 要素固定

            var expected = volumes.Select(x => new ModifiedMedianCutCPU.ColorVolumeCPU(x.min, x.max, x.count, populationProductVolume)).ToArray();

            using (var volumeBuffer = new ComputeBuffer(volumes.Length, Marshal.SizeOf<ColorVolume>())) {
                volumeBuffer.SetData(volumes);
                using (var scratchBuffer = ModifiedMedianCut.CreateScratchBuffer()) {
                    scratchBuffer.SetData(new Scratch() { volumeCount = (uint)volumes.Length, index = 0, swizzle = new uint3(0, 1, 2), normalizer = 1.0f });

                    medianCut.UpdatePriority(command, volumeBuffer, scratchBuffer, populationProductVolume);

                    Graphics.ExecuteCommandBuffer(command);

                    Scratch[] scratch = new Scratch[scratchBuffer.count];
                    scratchBuffer.GetData(scratch);
                    ColorVolume[] actual = new ColorVolume[volumeBuffer.count];
                    volumeBuffer.GetData(actual);
                    Assert.AreEqual(1.0f / expected.Max(x => x.Volume), scratch[0].normalizer);
                    AssertHelper.AreEqual(expected.Select(x => populationProductVolume ? (x.priority * scratch[0].normalizer) : x.priority).ToArray(), actual.Select(x => x.priority).ToArray(), 1e-6f);

                }
            }
        }

        [TestCaseSource(nameof(TestImage))]
        public void ComputeColor(string key) {
            Assert.Fail(); // yet
        }

    }
}
