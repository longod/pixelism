using System;
using System.Linq;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Pixelism {

    public interface IColorQuantizer : IDisposable {

        // format, srgbあたりは欲しいかも
        void Quantize(CommandBuffer command, RenderTargetIdentifier source, int width, int height);

        // offline用、cpu用に欲しいが、RenderTargetIdentifier でもreadbackすれば、効率は別として可能なので不要になるかも。
        void Quantize(CommandBuffer command, Texture2D source);

        void OnGUI();

        int NumColor { get; set; }

        ComputeBuffer ColorPalette { get; }
        ComputeBuffer ColorPaletteCount { get; } // offset:0, size:4, uint or int

        bool HasChanged { get; }

    }

    public class SystemColor : IColorQuantizer {

        public int NumColor {
            get => colorPaletteCount;
            set { // no changing
            }
        }

        public ComputeBuffer ColorPalette { get; }
        public ComputeBuffer ColorPaletteCount { get; }

        private readonly int colorPaletteCount;

        public SystemColor() {
            uint[] colors8 = {
                0xFF000000, // black
                0xFFFF0000, // blue
                0xFF0000FF, // red
                0xFFFF00FF, // magenta
                0xFF00FF00, // green
                0xFFFFFF00, // cyan
                0xFF00FFFF, // yellow
                0xFFFFFFFF, // white
            };
            uint[] colors16 = {
                0xFF000000, // black
                0xFFFF0000, // blue
                0xFF0000FF, // red
                0xFFFF00FF, // magenta
                0xFF00FF00, // green
                0xFFFFFF00, // cyan
                0xFF00FFFF, // yellow
                0xFFFFFFFF, // white
                0xFF777777, // gray
                0xFFAA0000, // dim blue
                0xFF0000AA, // dim red
                0xFFAA00AA, // dim magenta
                0xFF00AA00, // dim green
                0xFFAAAA00, // dim cyan
                0xFF00AAAA, // dim yellow
                0xFFAAAAAA, // dim white
            };

            var colors = colors16;

            var colorsf = colors.Select(x => new float3((x & 0xff) / 255.0f, ((x >> 8) & 0xff) / 255.0f, ((x >> 16) & 0xff) / 255.0f)).ToArray();
            ColorPalette = new ComputeBuffer(colors.Length, Marshal.SizeOf<float3>());
            ColorPalette.SetData(colorsf);
            colorPaletteCount = colors.Length;
            int[] count = new int[1] { colorPaletteCount };
            ColorPaletteCount = new ComputeBuffer(count.Length, Marshal.SizeOf<int>());
            ColorPaletteCount.SetData(count);
        }

        public void Dispose() {
            ColorPalette.Dispose();
            ColorPaletteCount.Dispose();
        }

        public void Quantize(CommandBuffer command, RenderTargetIdentifier source, int width, int height) {
            // nothing
        }

        public void Quantize(CommandBuffer command, Texture2D source) {
            // nothing
        }

        public void OnGUI() {
        }

        public bool HasChanged => false;

    }

    [Obsolete]
    public class PredefinedColor : IColorQuantizer {

        public int NumColor {
            get => colorPalette?.count ?? 0;
            set { // no changing
            }
        }

        public ComputeBuffer ColorPalette => colorPalette;
        public ComputeBuffer ColorPaletteCount => colorPaletteCount;
        private ComputeBuffer colorPalette;
        private ComputeBuffer colorPaletteCount;

        // todo args uint[]
        public PredefinedColor() {

            uint[] colors16 = {
                0xFF3D2A47,
                0xFF9B9A8D,
                0xFF6A4E93,
                0xFFDDC68B,
                0xFF755062,
                0xFFA2A4BA,
                0xFFA57596,
                0xFFE3B5DB,
                0xFF4E475F,
                0xFFC2AC89,
                0xFF7D7499,
                0xFFE2CAB8,
                0xFF8D716E,
                0xFFC6AFBD,
                0xFFBA74C3,
                0xFFEEE4DF,
            };

            SetColorPalette(colors16);

        }

        public void Dispose() {
            colorPalette?.Dispose();
            colorPaletteCount?.Dispose();
        }

        public void Quantize(CommandBuffer command, RenderTargetIdentifier source, int width, int height) {
            // nothing
        }

        public void Quantize(CommandBuffer command, Texture2D source) {
            // nothing
        }

        public void OnGUI() {
        }

        public void SetColorPalette(uint[] palette) {
            Dispose();
            // BGR
            var colorsf = palette.Select(x => new float3(((x >> 16) & 0xff) / 255.0f, ((x >> 8) & 0xff) / 255.0f, ((x) & 0xff) / 255.0f)).ToArray();
            colorPalette = new ComputeBuffer(colorsf.Length, Marshal.SizeOf<float3>());
            colorPalette.SetData(colorsf);
            int[] count = new int[1] { palette.Length };
            colorPaletteCount = new ComputeBuffer(count.Length, Marshal.SizeOf<int>());
            colorPaletteCount.SetData(count);
        }

        public bool HasChanged => false;

    }
}
