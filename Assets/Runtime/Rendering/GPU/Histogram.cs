﻿using System;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Pixelism {

    public class Histogram : IDisposable {
        private ComputeShader shader;
        private KernelData buildPass;
        private KernelData clearPass;
        private LocalKeyword minmaxKeyword;

        //private KernelData buildPartialPass;
        private AddressablesHelper.HandleCollector collector = new AddressablesHelper.HandleCollector();

        private const int channelBit = 4;
        private const int histogramBit = channelBit * 3; // rgb
        private const int histogramSize = 1 << histogramBit;

        public Histogram() {
            AddressablesHelper.LoadAssetAsync<ComputeShader>("Histogram", res => {
                shader = res;
                buildPass = new KernelData(shader, "Build");
                clearPass = new KernelData(shader, "Clear");
                minmaxKeyword = new LocalKeyword(shader, "MINMAX_RANGE");
            }).Collect(collector)
            .WaitForCompletion(); // インスタンスごとにやると効率悪いので、やるにしても親でまとめて
        }

        public void Dispose() {
            collector.Dispose();
        }

        public static ComputeBuffer CreateHistogramBuffer() {
            return new ComputeBuffer(histogramSize, Marshal.SizeOf<uint>());
        }

        public static ComputeBuffer CreateMinMaxBuffer() {
            return new ComputeBuffer(2, Marshal.SizeOf<uint3>());
        }

        private void SetKeywords(CommandBuffer command, bool minmax) {
            command.SetKeyword(shader, minmaxKeyword, minmax);
        }

        public void Build(CommandBuffer command, RenderTargetIdentifier source, ComputeBuffer histogram, int width, int height, ComputeBuffer minmax, bool fullRange) {
            // 暗黙的にclearしてもよい

            var pass = buildPass;
            using (pass.SamplingScope(command)) {
                SetKeywords(command, minmax != null);
                command.SetComputeBufferParam(pass.shader, pass.kernel, "_MinMax", minmax);
                command.SetComputeBufferParam(pass.shader, pass.kernel, "_Histogram", histogram);
                command.SetComputeTextureParam(pass.shader, pass.kernel, "_Source", source, 0);
                command.SetComputeIntParams(pass.shader, "_Dimensions", width, height, 0, 0);
                int2 threadGroups = Math.DivRoundUp(new int2(width, height), (int2)pass.threadGroupSizes.xy);
                command.DispatchCompute(pass.shader, pass.kernel, threadGroups.x, threadGroups.y, 1);
            }
        }

        public void Clear(CommandBuffer command, ComputeBuffer histogram, ComputeBuffer minmax, bool fullRange) {
            var pass = clearPass;
            using (pass.SamplingScope(command)) {
                SetKeywords(command, !fullRange);
                command.SetComputeBufferParam(pass.shader, pass.kernel, "_MinMax", minmax);
                command.SetComputeBufferParam(pass.shader, pass.kernel, "_Histogram", histogram);
                int threadGroupsX = Math.DivRoundUp(histogram.count, (int)pass.threadGroupSizes.x);
                command.DispatchCompute(pass.shader, pass.kernel, threadGroupsX, 1, 1);
            }
        }
    }
}
