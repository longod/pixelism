using System;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Pixelism {

    public class ModifiedMedianCutGPU : IColorQuantizer {
        private int numColor = 16;
        public int NumColor {
            get { return numColor; }
            set {
                throw new NotSupportedException(); // UpdatePriority が16固定
#if false
                if (numColor != value) {
                    numColor = value;
                    HasChanged = true;
                }
#endif
            }
        }
        public ComputeBuffer ColorPalette => colorPalette;
        public ComputeBuffer ColorPaletteCount => scratchBuffer;

        private ComputeBuffer colorPalette;

        private ComputeBuffer histogramBuffer;
        private ComputeBuffer minmaxBuffer;
        private ComputeBuffer volumeBuffer;
        private ComputeBuffer scratchBuffer;
        private ComputeBuffer sumPerAxisBuffer;
        private ComputeBuffer indirectBuffer;

        private Histogram histogram = new Histogram();
        private ModifiedMedianCut medianCut = new ModifiedMedianCut();

        // NumColor * PopulationOrVolume     : pixel-count
        // NumColor * (1-PopulationOrVolume) : pixel-count * volume 広く分布している領域に対して効果がある
        private float populationOrVolume = 0.6f; // 0.3 ~ 0.9

        public float PopulationOrVolume {
            get { return populationOrVolume; }
            set {
                if (populationOrVolume != value) {
                    populationOrVolume = value;
                    HasChanged = true;
                }
            }
        }

        //public byte HistogramBinBit { get; set; } = 5; // fixed 4-bit

        private bool fullColorSpace = true;

        public bool FullColorSpace {
            get { return fullColorSpace; }
            set {
                if (fullColorSpace != value) {
                    fullColorSpace = value;
                    HasChanged = true;
                }
            }
        }

        private bool color12Bit = false;

        public bool Color12Bit {
            get { return color12Bit; }
            set {
                if (color12Bit != value) {
                    color12Bit = value;
                    HasChanged = true;
                }
            }
        }

        public bool HasChanged { get; private set; } = true;

        public ModifiedMedianCutGPU() {
            histogramBuffer = Histogram.CreateHistogramBuffer();
            minmaxBuffer = Histogram.CreateMinMaxBuffer();
            scratchBuffer = ModifiedMedianCut.CreateScratchBuffer();
            sumPerAxisBuffer = ModifiedMedianCut.CreateAxisBuffer();
            indirectBuffer = DispatchArguments.Create(1);
        }

        public void Dispose() {
            histogram.Dispose();
            medianCut.Dispose();

            colorPalette?.Dispose();

            histogramBuffer.Dispose();
            minmaxBuffer.Dispose();
            scratchBuffer.Dispose();
            sumPerAxisBuffer.Dispose();
            volumeBuffer?.Dispose();
            indirectBuffer.Dispose();
        }

        public void Quantize(CommandBuffer command, Texture2D source) {
            Quantize(command, source, source.width, source.height);
        }

        public void Quantize(CommandBuffer command, RenderTargetIdentifier source, int width, int height) {

            using (new GPUProfilerScope(command, "Pixelism.ModifiedMedianCutGPU.Quantize")) {
                colorPalette = AllocateBuffer<float3>(colorPalette, NumColor, true);
                volumeBuffer = AllocateBuffer<ColorVolume>(volumeBuffer, NumColor, true);

                // build histogram
                histogram.Clear(command, histogramBuffer, minmaxBuffer, FullColorSpace);
                histogram.Build(command, source, histogramBuffer, width, height, minmaxBuffer, FullColorSpace);

                // set 1st volume
                // FullColorSpace
                medianCut.SetupVolume(command, volumeBuffer, scratchBuffer, width * height, minmaxBuffer);

                int maxIteration = NumColor * 2; // 適当
                // 通常のmedian cutと同様にpopulation (pixel count)を指標とする
                int num = (int)(PopulationOrVolume * NumColor);
                // population iteration
                for (int i = 0; i < maxIteration; ++i) {
                    medianCut.FindCuttingVolume(command, volumeBuffer, scratchBuffer, indirectBuffer, num);
                    medianCut.BuildAxis(command, volumeBuffer, scratchBuffer, histogramBuffer, sumPerAxisBuffer, indirectBuffer);
                    medianCut.CutVolume(command, volumeBuffer, scratchBuffer, sumPerAxisBuffer, indirectBuffer);
                    medianCut.CountVolume(command, volumeBuffer, scratchBuffer, histogramBuffer, indirectBuffer, false);
                }

                // re-calc priprity
                if (num != NumColor) {
                    medianCut.UpdatePriority(command, volumeBuffer, scratchBuffer);

                    num = NumColor;
                    // population * volume iteration
                    for (int i = 0; i < maxIteration; ++i) {
                        medianCut.FindCuttingVolume(command, volumeBuffer, scratchBuffer, indirectBuffer, num);
                        medianCut.BuildAxis(command, volumeBuffer, scratchBuffer, histogramBuffer, sumPerAxisBuffer, indirectBuffer);
                        medianCut.CutVolume(command, volumeBuffer, scratchBuffer, sumPerAxisBuffer, indirectBuffer);
                        medianCut.CountVolume(command, volumeBuffer, scratchBuffer, histogramBuffer, indirectBuffer, true);
                    }
                }

            }

            // compute palette color
            medianCut.ComputeColor(command, volumeBuffer, scratchBuffer, histogramBuffer, colorPalette, Color12Bit);

#if false
            Graphics.ExecuteCommandBuffer(command);

            ColorVolume[] volumes = new ColorVolume[volumeBuffer.count];
            volumeBuffer.GetData(volumes);
            Debug.Log(string.Join(", ", volumes.Select(x => x.count)));
#endif

            HasChanged = false;
        }

        public void OnGUI() {
            FullColorSpace = GUILayout.Toggle(FullColorSpace, "Full ColorSpace");
            GUILayout.Label("Population or Volume:\r\n" + PopulationOrVolume);
            PopulationOrVolume = GUILayout.HorizontalSlider(PopulationOrVolume, 0.0f, 1.0f);
            Color12Bit = GUILayout.Toggle(Color12Bit, "12-Bit Color");
        }

        private static ComputeBuffer AllocateBuffer<T>(ComputeBuffer buffer, int count, bool initialClear) where T : struct {
            return AllocateBuffer(buffer, count, Marshal.SizeOf<T>(), initialClear ? new T[count] : null);
        }

        private static ComputeBuffer AllocateBuffer(ComputeBuffer buffer, int count, int stride, Array data) {
            if (buffer != null && buffer.count == count && buffer.stride == stride) {
                return buffer;
            }
            buffer?.Dispose();
            buffer = new ComputeBuffer(count, stride);
            if (data != null) {
                buffer.SetData(data);
            }
            return buffer;
        }
    }
}
