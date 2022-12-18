using UnityEngine;
using UnityEngine.Rendering;

namespace Pixelism {

    public class PaletteDither : IColorReduction {
        private Shader shader;
        private Material material;
        private static readonly int _Palette = Shader.PropertyToID("_Palette");
        private static readonly int _PaletteCount = Shader.PropertyToID("_PaletteCount");
        private static readonly int _PaletteMax = Shader.PropertyToID("_PaletteMax");
        private static readonly int _Dimensions = Shader.PropertyToID("_Dimensions");

        private AddressablesHelper.HandleCollector collector = new AddressablesHelper.HandleCollector();

        public PaletteDither() {
            AddressablesHelper.LoadAssetAsync<Shader>("PaletteDither", res => {
                shader = res;
                material = new Material(shader);
                material.hideFlags = HideFlags.HideAndDontSave;

            }).Collect(collector);

        }

        public void Dispose() {
            collector?.Dispose();
            material?.DestoryOnRuntime();
        }

        public bool Reduce(CommandBuffer command, RenderTargetIdentifier source, RenderTargetIdentifier destination, int width, int height, ComputeBuffer palette, ComputeBuffer paletteCount) {
            if (palette == null || paletteCount == null) {
                return false;
            }
            if (material == null) {
                return false;
            }
            using (new GPUProfilerScope(command, "Pixelism.PaletteDither")) {
                command.SetGlobalBuffer(_Palette, palette);
                command.SetGlobalBuffer(_PaletteCount, paletteCount); // or SetGlobalConstantBuffer
                command.SetGlobalInteger(_PaletteMax, palette?.count ?? 0);
                command.SetGlobalVector(_Dimensions, new Vector4(width, height, 1.0f / width, 1.0f / height));
                command.Blit(source, destination, material, 0);
            }
            return true;
        }

        public bool HasChanged => false;

        public void OnGUI() {
            // toggle dither
            // dithering pattern bayer, blue...
            // select palette method
            // toggle palette
        }

    }
}
