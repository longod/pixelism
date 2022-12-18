using UnityEngine;
using UnityEngine.Rendering;

namespace Pixelism {

    public class ImageComparison : IDebugger {
        private Shader shader;
        private Material material;
        private AddressablesHelper.HandleCollector collector = new AddressablesHelper.HandleCollector();

        public bool HasChanged { get; private set; } = true;

        public bool Enabled { get; set; } = false;

        private bool flip = false;

        public bool Flip {
            get { return flip; }
            set {
                if (flip != value) {
                    flip = value;
                    HasChanged = true;
                }
            }
        }

        private bool vertical = false;

        public bool Vertical {
            get { return vertical; }
            set {
                if (vertical != value) {
                    vertical = value;
                    HasChanged = true;
                }
            }
        }

        private float split = 0.0f;

        public float Split {
            get { return split; }
            set {
                if (split != value) {
                    split = value;
                    HasChanged = true;
                }
            }
        }

        public ImageComparison() {
            AddressablesHelper.LoadAssetAsync<Shader>("ImageComparison", res => {
                shader = res;
                material = new Material(shader);
                material.hideFlags = HideFlags.HideAndDontSave;
                HasChanged = true;
            }).Collect(collector);
        }

        public void Dispose() {
            collector?.Dispose();
            shader = null;
            material?.DestoryOnRuntime();
        }

        public bool OnDebug(CommandBuffer command, RenderTargetIdentifier source, RenderTargetIdentifier destination, RenderTargetIdentifier original, RenderTargetIdentifier result, IColorQuantizer colorQuantizer, IColorReduction colorReduction, int width, int height) {
            if (material == null) {
                return false;
            }
            using (new GPUProfilerScope(command, "Pixelism.ImageComparison")) {
                command.SetGlobalInteger("_Flip", Flip ? 1 : 0);
                command.SetGlobalInteger("_Vertical", Vertical ? 1 : 0);
                command.SetGlobalFloat("_SplitLocation", Split);
                command.SetGlobalTexture("_MainTex", source);
                command.SetGlobalTexture("_SideTex", original);
                command.Blit(source, destination, material);
            }
            HasChanged = false;
            return true;
        }

        public void OnGUI() {
            Split = GUILayout.HorizontalSlider(Split, -1, 1); // TODO bar dragging
            Flip = GUILayout.Toggle(Flip, "Flip");
            Vertical = GUILayout.Toggle(Vertical, "Vertical");
        }
    }
}
