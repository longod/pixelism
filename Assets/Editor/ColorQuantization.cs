using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Pixelism {

    public class ColorQuantization : EditorWindow {

        [MenuItem("Pixelism/Color Quantization")]
        private static void Open() {
            var w = CreateInstance<ColorQuantization>();
            w.titleContent = new GUIContent("Color Quantization");
            w.Show();
        }

        private Texture2D input;
        private Type[] quantizerTypes;
        private GUIContent[] quantizerNames;
        private GUIContent lightPlus;
        private GUIContent darkPlus;
        private GUIContent lightMinus;
        private GUIContent darkMinus;

        [SerializeField]
        private List<ColorQuantizerPanel> quantizers = new List<ColorQuantizerPanel>();

        [Serializable]
        private class ColorQuantizerPanel : IDisposable {
            public int type = 0;
            public IColorQuantizerEditor quantizer;
            public Vector2 scrollPalette;
            public Vector2 scrollImage;
            public Texture2D output;
            public Texture2D error;
            public bool dither = true;
            public float magnitude = 1.0f;
            public Color32[] palette;
            public double3? mse;
            public uint paletteCount;
            public int showImage = 0;

            public ColorQuantizerPanel(Type[] quantizerTypes) {
                quantizer = CreateInstance<IColorQuantizerEditor>(quantizerTypes[type]);
            }

            public void Dispose() {
                //Debug.Log("Dispose");
                quantizer?.Dispose();
                quantizer = null;
                DestroyImmediate(output);
                DestroyImmediate(error);
            }

            public void OnGUI(GUIContent[] quantizerNames, Type[] quantizerTypes, Texture2D source) {
                type = math.clamp(type, 0, quantizerTypes.Length - 1);
                var newtype = EditorGUILayout.Popup(type, quantizerNames);
                if (newtype != type) { // or changed
                    type = newtype;
                    quantizer?.Dispose();
                    quantizer = CreateInstance<IColorQuantizerEditor>(quantizerTypes[type]);
                }
                // for reloading
                if (quantizer == null) {
                    quantizer = CreateInstance<IColorQuantizerEditor>(quantizerTypes[type]);
                }
                using (new GUILayout.VerticalScope("box")) {
                    quantizer?.OnGUI();
                }

                using (new EditorGUILayout.HorizontalScope()) {
                    using (new EditorGUI.DisabledScope(source == null)) {
                        if (GUILayout.Button("Generate Palette")) {
                            var command = new CommandBuffer();
                            quantizer.Quantize(command, source);
                            Graphics.ExecuteCommandBuffer(command);
                            if (quantizer.ColorPalette != null) {
                                // ColorPaletteCountを使用するべきだが、未使用領域を確認するために全て表示
                                float3[] data = new float3[quantizer.ColorPalette.count];
                                quantizer.ColorPalette.GetData(data);
                                palette = data.Select(c => new Color(c.x, c.y, c.z, 1.0f)).Select(c => (Color32)c).ToArray();
                            }
                            paletteCount = quantizer.GetColorPaletteCount();

                        }
                    }

                    using (new EditorGUI.DisabledScope(source == null || paletteCount == 0)) {
                        if (GUILayout.Button("Reduce")) {
                            DestroyImmediate(output);
                            output = Reduce(source, palette.AsSpan(0, (int)paletteCount), dither);
                        }
                    }

                    using (new EditorGUI.DisabledScope(source == null || output == null)) {
                        if (GUILayout.Button("Estimate Error")) {
                            DestroyImmediate(error);
                            (error, mse) = EstimateError(source, output, magnitude);
                        }
                    }
                }
                magnitude = EditorGUILayout.Slider("Error Magnify", magnitude, 1.0f, 10.0f);

                if (palette?.Length > 0) {
                    EditorGUILayout.LabelField("Palette: " + paletteCount.ToString());
                    using (new EditorGUILayout.HorizontalScope()) {
                        using (var scrollView = new EditorGUILayout.ScrollViewScope(scrollPalette)) {
                            scrollPalette = scrollView.scrollPosition;
                            for (int i = 0; i < palette.Length; ++i) {
                                if (paletteCount < palette.Length && paletteCount == i) {
                                    EditorGUILayout.LabelField("", GUI.skin.horizontalSlider); // separator
                                }
                                //palette[i] = EditorGUILayout.ColorField((i + 1).ToString(), palette[i]);
                                var c = (Color32)palette[i];
                                palette[i] = EditorGUILayout.ColorField((c.r | c.g << 8 | c.b << 16).ToString("X6"), palette[i]);
                            }
                        }
                    }
                }

                // TODO compare mse between other panel, display rank
                if (mse.HasValue) {
                    var ch3 = (mse.Value.x + mse.Value.y + mse.Value.z) / 3.0;
                    EditorGUILayout.SelectableLabel($"MSE {ch3:r}, R: {mse.Value.x:r}, G: {mse.Value.y:r}, B: {mse.Value.z:r}");
                    if (ch3 > double.Epsilon) {
                        var psnr = 10 * math.log10(1.0 / ch3);
                        EditorGUILayout.SelectableLabel($"PSNR {psnr:r} dB");
                    }
                }

                using (new GUILayout.VerticalScope("box")) {
                    using (new EditorGUILayout.HorizontalScope()) {
                        string[] showImageItems = { "Quantized", "Difference", "Source" };
                        showImage = GUILayout.Toolbar(showImage, showImageItems);

                        GUILayout.FlexibleSpace();

                        using (new EditorGUI.DisabledScope(GetTexture(showImage, source) == null)) {
                            if (GUILayout.Button("Save")) {
                                string path = EditorUtility.SaveFilePanel("Save texture as PNG", "", source.name + (showImage == 0 ? ".reduction" : ".error") + ".png", "png"); // fixme suffix
                                if (!string.IsNullOrEmpty(path)) {
                                    // todo differenceはsRGBに変換しないといけない
                                    Texture2D dest = GetTexture(showImage, source);
                                    if (dest != null) {
                                        var bin = dest.EncodeToPNG();
                                        if (bin != null) {
                                            File.WriteAllBytes(path, bin);
                                            // if file was written in project then import this
                                        }
                                    }
                                }
                            }
                        }
                    }
                    using (var scrollView = new EditorGUILayout.ScrollViewScope(scrollImage)) {
                        scrollImage = scrollView.scrollPosition;
                        GUILayout.Box(GetTexture(showImage, source));
                    }
                }
            }

            private Texture2D GetTexture(int showImage, Texture2D source) {
                switch (showImage) {
                    case 0:
                        return output;

                    case 1:
                        return error;

                    case 2:
                        return source;

                    default:
                        break;
                }

                return null;
            }

            private T CreateInstance<T>(Type type) {
                return (T)Activator.CreateInstance(type);
            }

        }

        private IEnumerable<Type> EnumerateInterfaceTypes(Type interfaceType) {
            var types = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(s => s.GetTypes())
                .Where(p =>
                interfaceType.IsAssignableFrom(p) &&
                !p.IsInterface &&
                !p.IsAbstract &&
                !Attribute.GetCustomAttributes(p).Any(x => x is ObsoleteAttribute));
            return types;
        }

        private void OnEnable() {
            // re-allocate if need
        }

        private void OnDisable() {
            foreach (var q in quantizers) {
                q.Dispose();
            }
            // panelは残って欲しいのでclearしない
            // これはscript reload後に適切に破棄されて次回に残る
        }

        private void OnGUI() {
            // for assembly reload
            if (quantizerTypes == null) {
                quantizerTypes = EnumerateInterfaceTypes(typeof(IColorQuantizerEditor)).OrderBy(x => x.Name).ToArray();
                quantizerNames = quantizerTypes.Select(x => {
                    string name = (string)x.GetProperty("Name")?.GetValue(null) ?? x.Name;
                    return new GUIContent(name);
                }).ToArray();
            }

            if (darkMinus == null) {
                darkMinus = EditorGUIUtility.IconContent("d_Toolbar Minus");
            }
            if (darkPlus == null) {
                darkPlus = EditorGUIUtility.IconContent("d_Toolbar Plus");
            }
            if (lightMinus == null) {
                lightMinus = EditorGUIUtility.IconContent("Toolbar Minus");
            }
            if (lightPlus == null) {
                lightPlus = EditorGUIUtility.IconContent("Toolbar Plus");
            }

            using (new EditorGUILayout.HorizontalScope()) {
                input = EditorGUILayout.ObjectField("Input", input, typeof(Texture2D), true) as Texture2D;
                GUILayout.FlexibleSpace();
            }
            using (new EditorGUILayout.HorizontalScope()) {
                using (new EditorGUI.DisabledScope(quantizers.Count == 0)) {
                    if (GUILayout.Button(EditorGUIUtility.isProSkin ? darkMinus : lightMinus)) {
                        var q = quantizers[quantizers.Count - 1];
                        q.Dispose();
                        q = null;
                        quantizers.RemoveAt(quantizers.Count - 1);
                    }
                }
                if (GUILayout.Button(EditorGUIUtility.isProSkin ? darkPlus : lightPlus)) {
                    quantizers.Add(new ColorQuantizerPanel(quantizerTypes));
                }
                GUILayout.FlexibleSpace();
            }
            using (new EditorGUILayout.HorizontalScope()) {
                foreach (var q in quantizers) {
                    using (new EditorGUILayout.VerticalScope("box")) {
                        q.OnGUI(quantizerNames, quantizerTypes, input);
                        GUILayout.FlexibleSpace();
                    }
                }
            }

        }

        // todo use gpu
        private static (Texture2D, double3) EstimateError(Texture2D original, Texture2D result, float magnify) {
            var width = original.width;
            var height = original.height;
            var error = new Texture2D(width, height, TextureFormat.ARGB32, false, true);
            var expected = original.GetPixels().Select(x => x.linear).Select(x => new float3(x.r, x.g, x.b)).ToArray();
            var actual = result.GetPixels().Select(x => x.linear).Select(x => new float3(x.r, x.g, x.b)).ToArray();
            Color[] pixels = new Color[expected.Length];
            double3 sum = new double3(0, 0, 0);
            for (int i = 0; i < pixels.Length; i++) {
                var e = expected[i];
                var a = actual[i];
                var err = e - a;
                var mse = err * err;
                sum += mse;

                //err = math.abs(err) * magnify; // for visualize
                err = mse * magnify; // for visualize
                pixels[i] = new Color(err.x, err.y, err.z);
            }
            double3 avg = sum / pixels.Length;
            Debug.Log(avg);
            error.SetPixels(pixels);
            error.Apply();
            return (error, avg);
        }

        // todo use gpu
        private static Texture2D Reduce(Texture2D texture, Span<Color32> palette, bool dither) {

            float[] bayer4x4 = new float[] {
                    0, 8, 2, 10,
                    12, 4, 14, 6,
                    3, 11, 1, 9,
                    15, 7,13, 5,
            };

            var output = new Texture2D(texture.width, texture.height, TextureFormat.ARGB32, false);
            var width = texture.width;
            var height = texture.height;
            var pixels = texture.GetPixels32();
#if true
            //Debug.Log(output.width);
            //Debug.Log(output.height);
            //Debug.Log(texture.format);
            //Debug.Log(pixels.Length);
            for (int i = 0; i < pixels.Length; i++) {
                var c = pixels[i];
                // dither
                if (dither) {
                    var x = i % width;
                    var y = i / width;
                    var dx = x & 0x3;
                    var dy = y & 0x3;
                    var dm = bayer4x4[dx + dy * 4] / 16.0f; // or +1
                    var dr = (int)((dm - 0.5f) * (255.0f / 16.0f));
                    c.r = (byte)Mathf.Clamp(c.r + dr, 0, 0xff);
                    c.g = (byte)Mathf.Clamp(c.g + dr, 0, 0xff);
                    c.b = (byte)Mathf.Clamp(c.b + dr, 0, 0xff);
                }

                // brute force
                float err = float.MaxValue;
                Color32 select = default;
                foreach (var p in palette) {
                    // Euclidean distance
                    int r = (int)p.r - (int)c.r;
                    int g = (int)p.g - (int)c.g;
                    int b = (int)p.b - (int)c.b;
                    float d = (r * r + g * g + b * b);
                    if (d < err) {
                        err = d;
                        select = p;
                    }
                }
                pixels[i] = select;
            }
#endif
            output.SetPixels32(pixels);
            output.Apply();
            return output;
        }

    }
}
