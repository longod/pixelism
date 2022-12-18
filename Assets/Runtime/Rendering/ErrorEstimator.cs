using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Pixelism {

    public class ErrorEstimator : IDebugger {
        private AddressablesHelper.HandleCollector collector = new AddressablesHelper.HandleCollector();
        private ComputeShader shader;

        private KernelData pass;

        private double4 error;
        private ComputeBuffer errorBuffer;
        private bool disposed = false;

        private float amplifier = 1.0f;

        public float Amplifier {
            get { return amplifier; }
            set {
                if (amplifier != value) {
                    amplifier = value;
                    HasChanged = true;
                }
            }
        }

        private bool diffImage = false;

        public bool DiffImage {
            get { return diffImage; }
            set {
                if (diffImage != value) {
                    diffImage = value;
                    HasChanged = true;
                }
            }
        }

        public bool HasChanged { get; private set; } = true;

        public bool Enabled { get; set; } = true;

        public ErrorEstimator() {
            AddressablesHelper.LoadAssetAsync<ComputeShader>("ErrorEstimator", res => {
                shader = res;
                pass = new KernelData(shader, "CSMain");
                HasChanged = true;
            }).Collect(collector);
            errorBuffer = new ComputeBuffer(1, Marshal.SizeOf<uint4>());
        }

        public void Dispose() {
            disposed = true;
            collector?.Dispose();
            errorBuffer?.Dispose();
        }

        public bool OnDebug(CommandBuffer command, RenderTargetIdentifier source, RenderTargetIdentifier destination, RenderTargetIdentifier original, RenderTargetIdentifier result, IColorQuantizer colorQuantizer, IColorReduction colorReduction, int width, int height) {
            if (shader == null) {
                return false;
            }
            using (pass.SamplingScope(command)) {
                command.SetBufferData(errorBuffer, new uint4[1]); // clear
                command.SetComputeTextureParam(shader, pass.kernel, "_Expected", original);
                command.SetComputeTextureParam(shader, pass.kernel, "_Actual", result);
                command.SetComputeTextureParam(shader, pass.kernel, "_Result", destination);
                command.SetComputeBufferParam(shader, pass.kernel, "_Error", errorBuffer);
                command.SetComputeIntParams(shader, "_Dimensions", width, height, 0, 0);
                command.SetComputeFloatParam(shader, "_Amplifier", Amplifier);
                var thread = Math.DivRoundUp(new int2(width, height), (int2)pass.threadGroupSizes.xy);
                command.DispatchCompute(shader, pass.kernel, thread.x, thread.y, 1);
            }

            // reead back
            // todo native array
            command.RequestAsyncReadback(errorBuffer, request => {
                // doneにerrorは含まれている？
                if (request.done && !request.hasError && !disposed) {
                    var data = request.GetData<uint4>();
                    var err = data[0];
                    error = err / (double4)(255 * width * height); // shaderで255しているので戻す
                } else {
                    error = 0;
                }

            });
            HasChanged = false;
            return DiffImage; // falseでも書き込み済み
        }

        public void OnGUI() {
            DiffImage = GUILayout.Toggle(DiffImage, "Diff");
            if (DiffImage) {
                Amplifier = GUILayout.HorizontalSlider(Amplifier, 1.0f, 5.0f);
            }
            GUILayout.Label($"MSE:\r\n{error.x}\r\n{error.y}\r\n{error.z}");
        }
    }
}
