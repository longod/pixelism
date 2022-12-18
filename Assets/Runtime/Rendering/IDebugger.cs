using System;
using UnityEngine.Rendering;

namespace Pixelism {

    public interface IDebugger : IDisposable {

        // return: write dest
        bool OnDebug(CommandBuffer command, RenderTargetIdentifier source, RenderTargetIdentifier destination, RenderTargetIdentifier original, RenderTargetIdentifier result, IColorQuantizer colorQuantizer, IColorReduction colorReduction, int width, int height);

        void OnGUI();

        bool HasChanged { get; }

        bool Enabled { get; set; }
    }
}
