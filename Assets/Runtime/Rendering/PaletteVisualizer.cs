using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Pixelism {

    public class PaletteVisualizer : IDebugger {
        public bool HasChanged { get; private set; } = true;

        public bool Enabled { get; set; } = true;
        private bool disposed = false;

        private Color32[] palette = null;
        private int paletteCount = 0;

        private Shader shader;
        private Material material;
        private ComputeShader cs;
        private ComputeBuffer indirectArgs;
        private KernelData pass;

        private AddressablesHelper.HandleCollector collector = new AddressablesHelper.HandleCollector();

        public PaletteVisualizer() {
            indirectArgs = DrawArguments.Create(1);

            AddressablesHelper.LoadAssetAsync<ComputeShader>("PaletteVisualizerArgs", res => {
                cs = res;
                pass = new KernelData(cs, "Main");
                HasChanged = true;
            }).Collect(collector);

            AddressablesHelper.LoadAssetAsync<Shader>("PaletteVisualizer", res => {
                shader = res;
                material = new Material(shader);
                material.hideFlags = HideFlags.HideAndDontSave;
                HasChanged = true;
            }).Collect(collector);
        }

        public void Dispose() {
            disposed = true;
            palette = null; // managedで非同期に使用されると参照が残り続ける？
            paletteCount = 0;
            collector?.Dispose();

            shader = null;
            material?.DestoryOnRuntime();
            cs = null;
            indirectArgs?.Dispose();

        }

        public bool OnDebug(CommandBuffer command, RenderTargetIdentifier source, RenderTargetIdentifier destination, RenderTargetIdentifier original, RenderTargetIdentifier result, IColorQuantizer colorQuantizer, IColorReduction colorReduction, int width, int height) {
            if (colorQuantizer.ColorPalette == null || colorQuantizer.ColorPaletteCount == null) {
                return false;
            }

            if (cs == null) {
                return false;
            }
            if (material == null) {
                return false;
            }

            // TODO テクスチャに書き込むのでもよかったなそれだとOnGUIにも貼れるかも
            using (new GPUProfilerScope(command, "Pixelism.PaletteVisualizer")) {
                using (pass.SamplingScope(command)) {
                    // unityはCopyBufferがなぜかgraphicsしか対応していないし、範囲をしていできないので、draw argsをdebug側が作る
                    command.SetComputeBufferParam(pass.shader, pass.kernel, "_PaletteCount", colorQuantizer.ColorPaletteCount);
                    command.SetComputeBufferParam(pass.shader, pass.kernel, "_Args", indirectArgs);
                    command.DispatchCompute(pass.shader, pass.kernel, 1, 1, 1);
                }

                command.SetRenderTarget(source);
                command.SetGlobalBuffer("_Palette", colorQuantizer.ColorPalette);
                command.DrawProceduralIndirect(Matrix4x4.identity, material, 0, MeshTopology.Triangles, indirectArgs);
                // quadはindexバッファを使用してAPI吸収してるらしい…そのindex bufferはどう作られる？
            }

            // todo if null or mismatch stride

            // レイテンシーがある
            command.RequestAsyncReadback(colorQuantizer.ColorPalette, request => {
                if (request.done && !request.hasError && !disposed) {
                    var data = request.GetData<float3>();
                    if (palette == null || palette.Length != data.Length) {
                        palette = new Color32[data.Length];
                    }
                    for (int i = 0; i < data.Length; ++i) {
                        var c = data[i];
                        palette[i] = new Color32((byte)(c.x * 0xFF), (byte)(c.y * 0xFF), (byte)(c.z * 0xFF), 0xFF);
                    }
                }
            });
            command.RequestAsyncReadback(colorQuantizer.ColorPaletteCount, request => {
                if (request.done && !request.hasError && !disposed) {
                    var data = request.GetData<int>();
                    paletteCount = data[0];
                } else {
                    paletteCount = 0;
                }
            });

            HasChanged = false;
            return false;
        }

        public void OnGUI() {
            if (palette != null) {
                var bg = GUI.skin.box.normal.background;
                GUI.skin.box.normal.background = Texture2D.whiteTexture;

                var count = math.min(paletteCount, palette.Length);
                var oldbg = GUI.backgroundColor;
                var old = GUI.skin.box.normal.textColor;
                int row = 2;
                var col = Math.DivRoundUp(count, row);
                var w = GUILayout.Width(60);
                for (int y = 0; y < col; ++y) {
                    using (new GUILayout.HorizontalScope()) {
                        for (int x = 0; x < row; ++x) {
                            var i = x + y * row;
                            if (i < count) {
                                var c = palette[i];
                                GUI.backgroundColor = c;
                                Color.RGBToHSV(c, out _, out _, out float v);
                                GUI.skin.box.normal.textColor = v > 0.5f ? Color.black : Color.white;
                                GUILayout.Box((c.r | c.g << 8 | c.b << 16).ToString("X6"), w);
                            }
                        }
                    }
                }
                GUI.skin.box.normal.textColor = old;
                GUI.backgroundColor = oldbg;
                GUI.skin.box.normal.background = bg;
            }
        }
    }
}
