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
            Scratch scratch = new Scratch() { index = -1, volumeCount = (uint)volumeCount };

            using (var volumesBuffer = new ComputeBuffer(volumes.Length, Marshal.SizeOf<ColorVolume>())) {
                volumesBuffer.SetData(volumes);
                using (var scratchBuffer = ModifiedMedianCut.CreateScratchBuffer()) {
                    scratchBuffer.SetData(scratch);

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

            using (NativeArray<uint> expected = new NativeArray<uint>(converter.ChannelSize, Allocator.TempJob, NativeArrayOptions.ClearMemory)) {
                using (var vol = new NativeArray<ModifiedMedianCutCPU.ColorVolumeCPU>(volumes.Select(x => new ModifiedMedianCutCPU.ColorVolumeCPU(x.min, x.max, x.count, false)).ToArray(), Allocator.TempJob)) {
                    var build = new ModifiedMedianCutCPU.BuildAxisHistogramJob<ModifiedMedianCutCPU.DefaultConverter>(histo.Histogram, vol.Slice(index, 1), expected, converter).Schedule();
                    var sumup = new ModifiedMedianCutCPU.SumupAxisHistogramJob<ModifiedMedianCutCPU.DefaultConverter>(vol.Slice(index, 1), expected, converter).Schedule(build);
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
                                AssertHelper.AreEqual<uint>(expected, actual);
                            }
                        }
                    }
                }

            }
        }

        [TestCaseSource(nameof(TestImage))]
        public void CutVolume(string key) {
            var histo = cachedHistogram[key];

            // todo change range
            var volumes = new ColorVolume[] {
                new ColorVolume() { count = 0, max = new uint3(0xf, 0xf, 0xf), min = new uint3(0, 0, 0), priority = 0 },
                new ColorVolume(),
            };

            var conv = converters[0];

            histogramBuffer.SetData(histo.Histogram);

            using (var volumesBuffer = new ComputeBuffer(volumes.Length, Marshal.SizeOf<ColorVolume>())) {
                volumesBuffer.SetData(volumes);

                using (var scratchBuffer = ModifiedMedianCut.CreateScratchBuffer()) {
                    scratchBuffer.SetData(new Scratch[1] { new Scratch() { volumeCount = 1, index = 0, swizzle = (uint3)conv.Swizzle } });

                    using (var sumPerAxisBuffer = ModifiedMedianCut.CreateAxisBuffer()) {

                        medianCut.BuildAxis(command, volumesBuffer, scratchBuffer, histogramBuffer, sumPerAxisBuffer, indirectBuffer); // TODO これはcpu referenceを使う
                        medianCut.CutVolume(command, volumesBuffer, scratchBuffer, histogramBuffer, sumPerAxisBuffer, indirectBuffer);

                        Graphics.ExecuteCommandBuffer(command);

                        ColorVolume[] actual = new ColorVolume[volumesBuffer.count];
                        volumesBuffer.GetData(actual);

                        using (NativeArray<uint> sumPerAxis = new NativeArray<uint>(bit.channelSize, Allocator.TempJob, NativeArrayOptions.ClearMemory)) {
                            using (var vol = new NativeArray<ModifiedMedianCutCPU.ColorVolumeCPU>(volumes.Select(x => new ModifiedMedianCutCPU.ColorVolumeCPU(x.min, x.max)).ToArray(), Allocator.TempJob)) {

                                var vbox1 = vol.Slice(0, 1); // in out
                                var vbox2 = vol.Slice(1, 1); // out

                                //var axis = vbox1[0].ComputeAxis(); // 軸の決定
                                //var conv = converters[axis];

                                var build = new ModifiedMedianCutCPU.BuildAxisHistogramJob<ModifiedMedianCutCPU.DefaultConverter>(histo.Histogram, vol.Slice(0, 1), sumPerAxis, conv).Schedule();
                                var sumup = new ModifiedMedianCutCPU.SumupAxisHistogramJob<ModifiedMedianCutCPU.DefaultConverter>(vbox1, sumPerAxis, conv).Schedule(build);
                                var cut = new ModifiedMedianCutCPU.CutVolumeJob<ModifiedMedianCutCPU.DefaultConverter>(vbox1, vbox2, sumPerAxis, conv).Schedule(sumup);
                                cut.Complete();

                                Assert.AreEqual(vbox1[0].min, actual[0].min);
                                Assert.AreEqual(vbox1[0].max, actual[0].max);
                                Assert.AreEqual(vbox2[0].min, actual[1].min);
                                Assert.AreEqual(vbox2[0].max, actual[1].max);
                            }
                        }
                    }
                }

            }
        }

        [TestCaseSource(nameof(TestImage))]
        public void CountVolume(string key) {
            var image = cachedImage[key];
            var histo = cachedHistogram[key];

            // todo change range, priority type

            var volumes = new ColorVolume[] {
                new ColorVolume() { min = new uint3(0, 0, 0), max = new uint3(0xf, 0xf, 0xf), count = 0, priority = 0 },
                new ColorVolume() { min = new uint3(0, 0, 0), max = new uint3(0xf, 0xf, 0xf), count = 0, priority = 0 }
            };

            histogramBuffer.SetData(histo.Histogram);

            using (var volumeBuffer = new ComputeBuffer(volumes.Length, Marshal.SizeOf<ColorVolume>())) {
                volumeBuffer.SetData(volumes);
                using (var scratchBuffer = ModifiedMedianCut.CreateScratchBuffer()) {
                    scratchBuffer.SetData(new Scratch[1] { new Scratch() { volumeCount = 1, index = 0, swizzle = new uint3(0, 1, 2) } });

                    medianCut.CountVolume(command, volumeBuffer, scratchBuffer, histogramBuffer, indirectBuffer, false);

                    Graphics.ExecuteCommandBuffer(command);

                    volumeBuffer.GetData(volumes);

                    {
                        ColorVolume[] actual = new ColorVolume[volumeBuffer.count];
                        volumeBuffer.GetData(actual);

                        // todo use cpu reference
                        Assert.AreEqual(image.width * image.height, volumes[0].count);
                        Assert.AreEqual(volumes[0].count, volumes[0].priority); // todo * volume version
                        Assert.AreEqual(image.width * image.height, volumes[1].count);
                        Assert.AreEqual(volumes[1].count, volumes[1].priority); // todo * volume version
                    }
                    {
                        Scratch[] actual = new Scratch[scratchBuffer.count];
                        scratchBuffer.GetData(actual);
                        Assert.AreEqual(2, actual[0].volumeCount);
                        Assert.AreEqual(0, actual[0].index);

                    }

                }

            }
        }

        [Test]
        public void UpdatePriority() {
            Assert.Fail(); // yet
        }

        [TestCaseSource(nameof(TestImage))]
        public void ComputeColor(string key) {
            Assert.Fail(); // yet
        }

    }
}
