using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using static Pixelism.ModifiedMedianCutCPU;

namespace Pixelism.Test {

    public class ModifiedMedianCutTest {
        private CommandBuffer command = new CommandBuffer();
        private ModifiedMedianCut medianCut = new ModifiedMedianCut();
        private AddressablesHelper.HandleCollector collector = new AddressablesHelper.HandleCollector();

        private Dictionary<string, Texture2D> cachedImage = new Dictionary<string, Texture2D>(); // cached test images
        private Dictionary<string, HistogramTest.CacheHistogram> cachedHistogram = new Dictionary<string, HistogramTest.CacheHistogram>(); // 別のテストと共有できるとよい
        private ModifiedMedianCutCPU.BitInfo bit = new ModifiedMedianCutCPU.BitInfo(4);
        private ModifiedMedianCutCPU.DefaultConverter[] converters;

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
        public void SetupVolume([Values(true, false)] bool fullRange) {
            using (var volumeBuffer = new ComputeBuffer(1, Marshal.SizeOf<ColorVolume>())) {
                using (var scratchBuffer = new ComputeBuffer(1, Marshal.SizeOf<Scratch>())) {
                    using (var minmaxBuffer = Histogram.CreateMinMaxBuffer()) {
                        minmaxBuffer.SetData(new uint3[] { new uint3(0, 0, 0), new uint3(0xf, 0xf, 0xf) });

                        medianCut.SetupVolume(command, volumeBuffer, scratchBuffer, 17, minmaxBuffer);
                        Graphics.ExecuteCommandBuffer(command);
                        {
                            Scratch[] actual = new Scratch[scratchBuffer.count];
                            scratchBuffer.GetData(actual);
                            Assert.AreEqual(1, actual[0].volumeCount);
                            Assert.AreEqual(0, actual[0].index);
                            Assert.AreEqual(new uint3(0, 1, 2), actual[0].swizzle);

                        }
                        {
                            ColorVolume[] actual = new ColorVolume[volumeBuffer.count];
                            volumeBuffer.GetData(actual);
                            Assert.AreEqual(17, actual[0].count);
                            Assert.AreEqual(17.0f, actual[0].priority);

                            if (fullRange) {
                                Assert.AreEqual(uint3.zero, actual[0].min);
                                Assert.AreEqual(new uint3(0xf, 0xf, 0xf), actual[0].max);
                            } else {
                                // TODO 適当なmin, maxを用意する
                                Assert.Fail();
                            }
                        }
                    }
                }
            }
        }

        [Test]
        public void FindCuttingVolume() {
            // todo range and swizzle, volumecountを制限, invalid case, 同値など

            float[] priorities = { 0, 2, 4, 11, 4, 7, 3, 10 };
            var volumes = priorities.Select(x => new ColorVolume() { priority = x }).ToArray();
            Scratch scratch = new Scratch() { index = -1, volumeCount = (uint)volumes.Length };
            var max = volumes.Max(x => x.priority);
            var index = volumes.ToList().FindIndex(x => x.priority == max);

            using (var volumesBuffer = new ComputeBuffer(volumes.Length, Marshal.SizeOf<ColorVolume>())) {
                volumesBuffer.SetData(volumes);
                using (var scratchBuffer = new ComputeBuffer(1, Marshal.SizeOf<Scratch>())) {
                    scratchBuffer.SetData(scratch);

                    medianCut.FindCuttingVolume(command, volumesBuffer, scratchBuffer, indirectBuffer, volumesBuffer.count);
                    Graphics.ExecuteCommandBuffer(command);
                    {
                        DispatchArguments[] actual = new DispatchArguments[indirectBuffer.count];
                        indirectBuffer.GetData(actual);
                        Assert.AreEqual(1, actual[0].threadGroupCountX);
                        Assert.AreEqual(1, actual[0].threadGroupCountY);
                        Assert.AreEqual(1, actual[0].threadGroupCountZ);
                    }
                    {
                        Scratch[] actual = new Scratch[scratchBuffer.count];
                        scratchBuffer.GetData(actual);
                        Assert.AreEqual(index, actual[0].index);
                        Assert.AreEqual(uint3.zero, actual[0].swizzle); // todo
                    }
                }
            }
        }

        [TestCaseSource(nameof(TestImage))]
        public void BuildAxisHistogram(string key) {
            var image = cachedImage[key];
            var histo = cachedHistogram[key];

            using (var histogramBuffer = Histogram.CreateHistogramBuffer()) {
                histogramBuffer.SetData(histo.Histogram);

                using (var volumesBuffer = new ComputeBuffer(1, Marshal.SizeOf<ColorVolume>())) {
                    // todo change range
                    var volumes = new ColorVolume[1] { new ColorVolume() { count = 0, max = new uint3(0xf, 0xf, 0xf), min = new uint3(0, 0, 0), priority = 0 } };
                    volumesBuffer.SetData(volumes);

                    using (var scratch = new ComputeBuffer(1, Marshal.SizeOf<Scratch>())) {
                        scratch.SetData(new Scratch[1] { new Scratch() { volumeCount = 1, index = 0, swizzle = new uint3(0, 1, 2) } });

                        using (var sumPerAxisBuffer = new ComputeBuffer(16, Marshal.SizeOf<uint>())) {

                            medianCut.BuildAxisHistogram(command, volumesBuffer, scratch, histogramBuffer, sumPerAxisBuffer, indirectBuffer);

                            Graphics.ExecuteCommandBuffer(command);

                            uint[] actual = new uint[sumPerAxisBuffer.count];
                            sumPerAxisBuffer.GetData(actual);

                            using (NativeArray<uint> sumPerAxis = new NativeArray<uint>(bit.channelSize, Allocator.TempJob, NativeArrayOptions.ClearMemory)) {
                                using (var vol = new NativeArray<ModifiedMedianCutCPU.ColorVolumeCPU>(volumes.Select(x => new ModifiedMedianCutCPU.ColorVolumeCPU(x.min, x.max)).ToArray(), Allocator.TempJob)) {

                                    var conv = converters[0];
                                    var build = new BuildAxisHistogramJob<ModifiedMedianCutCPU.DefaultConverter>(histo.Histogram, vol.Slice(0, 1), sumPerAxis, conv).Schedule();
                                    var sumup = new SumupAxisHistogramJob<ModifiedMedianCutCPU.DefaultConverter>(vol.Slice(0, 1), sumPerAxis, conv).Schedule(build);
                                    sumup.Complete();
                                    AssertHelper.AreEqual<uint>(sumPerAxis.ToArray(), actual);
                                }
                            }
                        }
                    }
                }

            }
        }

        [TestCaseSource(nameof(TestImage))]
        public void CutVolume(string key) {
            var image = cachedImage[key];
            var histo = cachedHistogram[key];

            // todo change range
            var volumes = new ColorVolume[] {
                new ColorVolume() { count = 0, max = new uint3(0xf, 0xf, 0xf), min = new uint3(0, 0, 0), priority = 0 },
                new ColorVolume(),
            };

            var conv = converters[0];

            using (var histogramBuffer = Histogram.CreateHistogramBuffer()) {
                histogramBuffer.SetData(histo.Histogram);

                using (var volumesBuffer = new ComputeBuffer(volumes.Length, Marshal.SizeOf<ColorVolume>())) {
                    volumesBuffer.SetData(volumes);

                    using (var scratch = new ComputeBuffer(1, Marshal.SizeOf<Scratch>())) {
                        scratch.SetData(new Scratch[1] { new Scratch() { volumeCount = 1, index = 0, swizzle = (uint3)conv.Swizzle } });

                        using (var sumPerAxisBuffer = new ComputeBuffer(16, Marshal.SizeOf<uint>())) {

                            medianCut.BuildAxisHistogram(command, volumesBuffer, scratch, histogramBuffer, sumPerAxisBuffer, indirectBuffer); // TODO これはcpu referenceを使う
                            medianCut.CutVolume(command, volumesBuffer, scratch, histogramBuffer, sumPerAxisBuffer, indirectBuffer);

                            Graphics.ExecuteCommandBuffer(command);

                            ColorVolume[] actual = new ColorVolume[volumesBuffer.count];
                            volumesBuffer.GetData(actual);

                            using (NativeArray<uint> sumPerAxis = new NativeArray<uint>(bit.channelSize, Allocator.TempJob, NativeArrayOptions.ClearMemory)) {
                                using (var vol = new NativeArray<ModifiedMedianCutCPU.ColorVolumeCPU>(volumes.Select(x => new ModifiedMedianCutCPU.ColorVolumeCPU(x.min, x.max)).ToArray(), Allocator.TempJob)) {

                                    var vbox1 = vol.Slice(0, 1); // in out
                                    var vbox2 = vol.Slice(1, 1); // out

                                    //var axis = vbox1[0].ComputeAxis(); // 軸の決定
                                    //var conv = converters[axis];

                                    var build = new BuildAxisHistogramJob<ModifiedMedianCutCPU.DefaultConverter>(histo.Histogram, vol.Slice(0, 1), sumPerAxis, conv).Schedule();
                                    var sumup = new SumupAxisHistogramJob<ModifiedMedianCutCPU.DefaultConverter>(vbox1, sumPerAxis, conv).Schedule(build);
                                    var cut = new CutVolumeJob<DefaultConverter>(vbox1, vbox2, sumPerAxis, conv).Schedule(sumup);
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

            using (var histogramBuffer = Histogram.CreateHistogramBuffer()) {
                histogramBuffer.SetData(histo.Histogram);

                using (var volumeBuffer = new ComputeBuffer(volumes.Length, Marshal.SizeOf<ColorVolume>())) {
                    volumeBuffer.SetData(volumes);
                    using (var scratchBuffer = new ComputeBuffer(1, Marshal.SizeOf<Scratch>())) {
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
