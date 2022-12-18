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

    public class HistogramTest {
        private CommandBuffer command = new CommandBuffer();
        private Histogram histogram = new Histogram();
        private AddressablesHelper.HandleCollector collector = new AddressablesHelper.HandleCollector();

        private Dictionary<string, Texture2D> cachedImage = new Dictionary<string, Texture2D>(); // cached test images
        private Dictionary<string, HistogramTest.CacheHistogram> cachedHistogram = new Dictionary<string, HistogramTest.CacheHistogram>();
        private ModifiedMedianCutCPU.BitInfo bit = new ModifiedMedianCutCPU.BitInfo(4);
        private ModifiedMedianCutCPU.DefaultConverter[] converters;

        private static readonly string[] addresses = {
                "img_640x400_3x8bit_RGB_color_bars_CMYKWRGB",
                "img_640x400_3x8bit_RGB_color_bars_CMYKWRGB_75IRE",
                "img_640x400_3x8bit_RGB_color_bars_CMYKWRGB_100IRE",
            };

        private static IEnumerable<string> TestImage() {
            foreach (var key in addresses) {
                yield return key; // ロードしてテクスチャを返せるが、テストパターン生成時にロードされる?
            }
        }

        public struct CacheHistogram : IDisposable {
            private JobHandle handle;
            private NativeArray<uint> data;
            private uint[] managed;

            public CacheHistogram(JobHandle handle, NativeArray<uint> data) {
                this.handle = handle;
                this.data = data;
                this.managed = null;
            }

            public NativeArray<uint> Value {
                get {
                    // 何度も呼ぶとオーバーヘッドなんだけれど
                    handle.Complete();
                    return data;
                }
            }

            public uint[] Managed {
                get {
                    if (managed == null) {
                        managed = Value.ToArray();
                    }
                    return managed;
                }
            }

            public void Dispose() {
                Value.Dispose();
                managed = null;
            }

            public static CacheHistogram Create<T>(Texture2D res, T converter) where T : ModifiedMedianCutCPU.IIndexConverter {
                if (res.format != TextureFormat.RGBA32) {
                    throw new ArgumentException("source format is not RGBA32.");
                }
                var histogram = ModifiedMedianCutCPU.CreateHistogramBin(converter.Bit, Allocator.Persistent);
                var handle = new ModifiedMedianCutCPU.BuildHistogramJob<T>(res.GetPixelData<uint>(0), histogram, converter).Schedule();
                //var handle = new ModifiedMedianCutCPU.MinMaxVolumeJob<T>(res.GetPixelData<uint>(0), volumes, converter).Schedule(); // todo
                return new CacheHistogram(handle, histogram);
            }
        }

        [OneTimeSetUp]
        public void OneTimeSetUp() {
            converters = ModifiedMedianCutCPU.CreateConverter<ModifiedMedianCutCPU.DefaultConverter>(bit);
            foreach (var key in addresses) {
                AddressablesHelper.LoadAssetAsync<Texture2D>(key, res => {
                    cachedImage.Add(key, res);
                    cachedHistogram.Add(key, CacheHistogram.Create(res, converters[0]));
                    JobHandle.ScheduleBatchedJobs(); // 呼ばないと起動が鈍い
                }).Collect(collector);
            }
            collector.WaitForCompletion();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown() {
            foreach (var value in cachedHistogram.Values) {
                value.Dispose();
            }
            cachedHistogram.Clear();
            cachedImage.Clear();
            collector.Dispose();
            histogram.Dispose();
        }

        [SetUp]
        public void Setup() {
            command.Clear();
        }

        [TearDown]
        public void TearDown() {
        }

        [Test, Order(-1)]
        public void ClearHistogram([Values(false, true)] bool fullRange) {
            using (var histogramBuffer = Histogram.CreateHistogramBuffer()) {
                Assert.AreEqual(Marshal.SizeOf<uint>(), histogramBuffer.stride);
                Assert.AreEqual(4096, histogramBuffer.count);

                using (var minmaxBuffer = Histogram.CreateMinMaxBuffer()) {
                    Assert.AreEqual(Marshal.SizeOf<uint3>(), minmaxBuffer.stride);
                    Assert.AreEqual(2, minmaxBuffer.count);

                    histogram.Clear(command, histogramBuffer, minmaxBuffer, fullRange);
                    Graphics.ExecuteCommandBuffer(command);

                    {
                        uint[] actual = new uint[histogramBuffer.count];
                        histogramBuffer.GetData(actual);
                        AssertHelper.AreEqual<uint>(0, actual);
                    }
                    {
                        uint3[] actual = new uint3[minmaxBuffer.count];
                        minmaxBuffer.GetData(actual);
                        if (fullRange) {
                            Assert.AreEqual(new uint3(0x0), actual[0]);
                            Assert.AreEqual(new uint3(0xF, 0xF, 0xF), actual[1]);
                        } else {
                            Assert.AreEqual(new uint3(0xFFFFFFFF), actual[0]);
                            Assert.AreEqual(new uint3(0), actual[1]);
                        }
                    }
                }
            }
        }

        [TestCaseSource(nameof(TestImage))]
        public void BuildHistogram(string key) {
            var histo = cachedHistogram[key];
            var image = cachedImage[key];

            bool fullRange = false; // todo parameter

            var expected = histo.Managed;

            using (var histogramBuffer = Histogram.CreateHistogramBuffer()) {
                Assert.AreEqual(histogramBuffer.stride, Marshal.SizeOf<uint>());
                Assert.AreEqual(histogramBuffer.count, 4096);
                using (var minmaxBuffer = Histogram.CreateMinMaxBuffer()) {
                    Assert.AreEqual(Marshal.SizeOf<uint3>(), minmaxBuffer.stride);
                    Assert.AreEqual(2, minmaxBuffer.count);

                    histogram.Clear(command, histogramBuffer, minmaxBuffer, fullRange);
                    histogram.Build(command, image, histogramBuffer, image.width, image.height, minmaxBuffer, fullRange);
                    Graphics.ExecuteCommandBuffer(command);
                    {
                        uint[] actual = new uint[histogramBuffer.count];
                        histogramBuffer.GetData(actual);
                        Assert.AreEqual(image.width * image.height, actual.Sum(x => x));

                        // 誤差は避けられないが、どれくらい必要か。累積的なので絶対値で決められない。
                        // 隣接する前後を足し合わせて n%以内とか？
                        // 厳密なら、誤差でbinningが分かれそうな境界付近の値を調べる
                        // シンプルな誤差の出にくいデータセットを用意する
                        AssertHelper.AreEqual<uint>(expected, actual);
                        //AssertHelper.AreEqual<uint>(expected, actual, new UintAbsoluteEqualityComparer(1));
                    }
                    {
                        uint3[] actual = new uint3[minmaxBuffer.count];
                        minmaxBuffer.GetData(actual);
                        // todo
                        if (fullRange) {
                            Assert.AreEqual(new uint3(0x0), actual[0]);
                            Assert.AreEqual(new uint3(0xF, 0xF, 0xF), actual[1]);
                        } else {
                            Assert.AreEqual(new uint3(0xFFFFFFFF), actual[0]);
                            Assert.AreEqual(new uint3(0), actual[1]);
                        }
                    }

                }

            }

        }

    }
}
