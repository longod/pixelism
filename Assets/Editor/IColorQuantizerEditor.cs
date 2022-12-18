using System;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Pixelism {

    // editor 向けの機能を提供する decorator の亜種
    public interface IColorQuantizerEditor : IColorQuantizer {
        //static string Name { get; } // C#11

        // aliasで読めないのでリードバックインターフェイス…uint arrayでなんでも読ませてくれ
        uint GetColorPaletteCount();
    }

    public class ModifiedMedianCutCPUEditor : IColorQuantizerEditor {
        private ModifiedMedianCutCPU instance = new ModifiedMedianCutCPU();
        public static string Name => nameof(ModifiedMedianCutCPU);

        public int NumColor { get => instance.NumColor; set => instance.NumColor = value; }
        public ComputeBuffer ColorPalette => instance.ColorPalette;
        public ComputeBuffer ColorPaletteCount => instance.ColorPaletteCount;
        public bool HasChanged => instance.HasChanged;

        public void Dispose() {
            instance.Dispose();
            instance = null;
        }

        public uint GetColorPaletteCount() {
            if (ColorPaletteCount != null) {
                return ColorPaletteCount.GetData<uint>();
            }
            return 0;
        }

        public void OnGUI() {
            instance.FullColorSpace = EditorGUILayout.Toggle("Full ColorSpace", instance.FullColorSpace);
            instance.PopulationOrVolume = EditorGUILayout.Slider("Population or Volume", instance.PopulationOrVolume, 0.0f, 1.0f);
            using (new EditorGUI.DisabledScope(instance.Color12Bit)) {
                instance.HistogramBinBit = (byte)EditorGUILayout.IntSlider("Histogram Bit", (int)instance.HistogramBinBit, 4, 8);
            }
            instance.Color12Bit = EditorGUILayout.Toggle("12-Bit Color", instance.Color12Bit);
        }

        public void Quantize(CommandBuffer command, RenderTargetIdentifier source, int width, int height) {
            instance.Quantize(command, source, width, height);
        }

        public void Quantize(CommandBuffer command, Texture2D source) {
            instance.Quantize(command, source);
        }
    }

    public class ModifiedMedianCutGPUEditor : IColorQuantizerEditor {
        private ModifiedMedianCutGPU instance = new ModifiedMedianCutGPU();
        public static string Name => nameof(ModifiedMedianCutGPU);

        public int NumColor { get => instance.NumColor; set => instance.NumColor = value; }
        public ComputeBuffer ColorPalette => instance.ColorPalette;
        public ComputeBuffer ColorPaletteCount => instance.ColorPaletteCount;
        public bool HasChanged => instance.HasChanged;

        public void Dispose() {
            instance.Dispose();
            instance = null;
        }

        public uint GetColorPaletteCount() {
            if (ColorPaletteCount != null) {
                return ColorPaletteCount.GetData<Scratch>().volumeCount;
            }
            return 0;
        }

        public void OnGUI() {
            instance.FullColorSpace = EditorGUILayout.Toggle("Full ColorSpace", instance.FullColorSpace);
            instance.PopulationOrVolume = EditorGUILayout.Slider("Population or Volume", instance.PopulationOrVolume, 0.0f, 1.0f);
            instance.Color12Bit = EditorGUILayout.Toggle("12-Bit Color", instance.Color12Bit);
        }

        public void Quantize(CommandBuffer command, RenderTargetIdentifier source, int width, int height) {
            instance.Quantize(command, source, width, height);
        }

        public void Quantize(CommandBuffer command, Texture2D source) {
            instance.Quantize(command, source);
        }
    }

}
