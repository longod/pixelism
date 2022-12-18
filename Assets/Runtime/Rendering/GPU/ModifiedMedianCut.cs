using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace Pixelism {

    public class ModifiedMedianCut : IDisposable {
        private ComputeShader shader;
        private KernelData _SetupVolume;
        private KernelData _FindCuttingVolume;
        private KernelData _BuildAxisHistogram;
        private KernelData _CutVolume;
        private KernelData _CountVolume;
        private KernelData _UpdatePriority;
        private KernelData _ComputeColor;
        private LocalKeyword priorityKeyword;
        private LocalKeyword color12BitKeyword;
        private AddressablesHelper.HandleCollector collector = new AddressablesHelper.HandleCollector();

        public ModifiedMedianCut() {
            AddressablesHelper.LoadAssetAsync<ComputeShader>("ModifiedMedianCut", res => {
                shader = res;
                _SetupVolume = new KernelData(shader, "SetupVolume"); // 2pass目のセットアップもこれでできるとよい
                _FindCuttingVolume = new KernelData(shader, "FindCuttingVolume");
                _BuildAxisHistogram = new KernelData(shader, "BuildAxisHistogram"); // sum upもlocal index=0でできるとよい
                _CutVolume = new KernelData(shader, "CutVolume"); // ↑で出来ると良い
                _CountVolume = new KernelData(shader, "CountVolume");
                _UpdatePriority = new KernelData(shader, "UpdatePriority");
                _ComputeColor = new KernelData(shader, "ComputeColor");
                priorityKeyword = new LocalKeyword(shader, "PRODUCT_VOLUME");
                color12BitKeyword = new LocalKeyword(shader, "COLOR_12BIT");

            }).Collect(collector)
            .WaitForCompletion(); // インスタンスごとにやると効率悪いので、やるにしても親でまとめて
        }

        public void Dispose() {
            collector.Dispose();
        }

        public static ComputeBuffer CreateScratchBuffer() {
            return new ComputeBuffer(1, Marshal.SizeOf<Scratch>());
        }

        public static ComputeBuffer CreateAxisBuffer() {
            return new ComputeBuffer(16, Marshal.SizeOf<uint>());
        }

        private void SetKeywords(CommandBuffer command, bool populationProductVolume = false, bool color12Bit = false) {
            command.SetKeyword(shader, priorityKeyword, populationProductVolume);
            command.SetKeyword(shader, color12BitKeyword, color12Bit);
        }

        public void SetupVolume(CommandBuffer command, ComputeBuffer volumes, ComputeBuffer scratch, int pixelCount, ComputeBuffer minmax) {
            var pass = _SetupVolume;
            using (pass.SamplingScope(command)) {
                SetKeywords(command);
                command.SetComputeBufferParam(pass.shader, pass.kernel, "_MinMax", minmax);
                command.SetComputeBufferParam(pass.shader, pass.kernel, "_Scratch", scratch);
                command.SetComputeBufferParam(pass.shader, pass.kernel, "_Volumes", volumes);
                command.SetComputeIntParam(pass.shader, "_PixelCount", pixelCount);
                command.DispatchCompute(pass.shader, pass.kernel, 1, 1, 1);
            }
        }

        public void FindCuttingVolume(CommandBuffer command, ComputeBuffer volumes, ComputeBuffer scratch, ComputeBuffer indirect, int volumeCount) {
            var pass = _FindCuttingVolume;
            using (pass.SamplingScope(command)) {
                SetKeywords(command);
                command.SetComputeBufferParam(pass.shader, pass.kernel, "_Scratch", scratch);
                command.SetComputeBufferParam(pass.shader, pass.kernel, "_Volumes", volumes);
                command.SetComputeBufferParam(pass.shader, pass.kernel, "_Args", indirect);
                command.SetComputeIntParam(pass.shader, "_VolumeCount", volumeCount);
                command.DispatchCompute(pass.shader, pass.kernel, 1, 1, 1);
            }
        }

        // シェーダ内で無効判定するとgroupsharedが複雑なので、indirectのほうがよい
        public void BuildAxisHistogram(CommandBuffer command, ComputeBuffer volumes, ComputeBuffer scratch, ComputeBuffer histogram, ComputeBuffer sumPerAxis, ComputeBuffer indirect) {
            var pass = _BuildAxisHistogram;
            using (pass.SamplingScope(command)) {
                SetKeywords(command);
                command.SetComputeBufferParam(pass.shader, pass.kernel, "_Histogram", histogram);
                command.SetComputeBufferParam(pass.shader, pass.kernel, "_Scratch", scratch);
                command.SetComputeBufferParam(pass.shader, pass.kernel, "_Volumes", volumes);
                command.SetComputeBufferParam(pass.shader, pass.kernel, "_SumPerAxis", sumPerAxis);
                command.DispatchCompute(pass.shader, pass.kernel, indirect, 0);
            }
        }

        public void CutVolume(CommandBuffer command, ComputeBuffer volumes, ComputeBuffer scratch, ComputeBuffer histogram, ComputeBuffer sumPerAxis, ComputeBuffer indirect) {
            var pass = _CutVolume;
            using (pass.SamplingScope(command)) {
                SetKeywords(command);
                command.SetComputeBufferParam(pass.shader, pass.kernel, "_Histogram", histogram);
                command.SetComputeBufferParam(pass.shader, pass.kernel, "_Scratch", scratch);
                command.SetComputeBufferParam(pass.shader, pass.kernel, "_Volumes", volumes);
                command.SetComputeBufferParam(pass.shader, pass.kernel, "_SumPerAxis", sumPerAxis);
                command.DispatchCompute(pass.shader, pass.kernel, indirect, 0);
            }
        }

        // シェーダ内で無効判定するとgroupsharedが複雑なので、indirectのほうがよい
        public void CountVolume(CommandBuffer command, ComputeBuffer volumes, ComputeBuffer scratch, ComputeBuffer histogram, ComputeBuffer indirect, bool populationProductVolume) {
            var pass = _CountVolume;
            using (pass.SamplingScope(command)) {
                SetKeywords(command, populationProductVolume: populationProductVolume);
                command.SetKeyword(pass.shader, priorityKeyword, populationProductVolume);
                command.SetComputeBufferParam(pass.shader, pass.kernel, "_Histogram", histogram);
                command.SetComputeBufferParam(pass.shader, pass.kernel, "_Scratch", scratch);
                command.SetComputeBufferParam(pass.shader, pass.kernel, "_Volumes", volumes);
                command.DispatchCompute(pass.shader, pass.kernel, indirect, 0);
            }
        }

        public void UpdatePriority(CommandBuffer command, ComputeBuffer volumes, ComputeBuffer scratch, bool populationProductVolume = true) {
            var pass = _SetupVolume;
            using (pass.SamplingScope(command)) {
                SetKeywords(command, populationProductVolume: populationProductVolume);
                command.SetKeyword(pass.shader, priorityKeyword, populationProductVolume);
                command.SetComputeBufferParam(pass.shader, pass.kernel, "_Scratch", scratch);
                command.SetComputeBufferParam(pass.shader, pass.kernel, "_Volumes", volumes);
                //int threadGroupsX = Math.DivRoundUp(volumes.count, (int)pass.threadGroupSizes.x);
                command.DispatchCompute(pass.shader, pass.kernel, 1, 1, 1);
            }
        }

        public void ComputeColor(CommandBuffer command, ComputeBuffer volumes, ComputeBuffer scratch, ComputeBuffer histogram, ComputeBuffer color, bool color12Bit) {
            var pass = _ComputeColor;
            using (pass.SamplingScope(command)) {
                SetKeywords(command, color12Bit: color12Bit);
                command.SetComputeBufferParam(pass.shader, pass.kernel, "_Histogram", histogram);
                command.SetComputeBufferParam(pass.shader, pass.kernel, "_Scratch", scratch);
                command.SetComputeBufferParam(pass.shader, pass.kernel, "_Volumes", volumes);
                command.SetComputeBufferParam(pass.shader, pass.kernel, "_Colors", color);
                command.DispatchCompute(pass.shader, pass.kernel, 1, 1, 1);
            }
        }

    }

}
