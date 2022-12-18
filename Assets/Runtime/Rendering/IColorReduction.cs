using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Pixelism {

    // reducer or reduction
    public interface IColorReduction : IDisposable {

        bool Reduce(CommandBuffer command, RenderTargetIdentifier source, RenderTargetIdentifier destination, int width, int height, ComputeBuffer palette, ComputeBuffer paletteCount);

        void OnGUI();

        bool HasChanged { get; }

    }

    public class NullReduction : IColorReduction {

        public void Dispose() {
        }

        public void OnGUI() {
        }

        public bool Reduce(CommandBuffer command, RenderTargetIdentifier source, RenderTargetIdentifier destination, int width, int height, ComputeBuffer palette, ComputeBuffer paletteCount) {
            return false;
        }

        public bool HasChanged => false;

    }

}
