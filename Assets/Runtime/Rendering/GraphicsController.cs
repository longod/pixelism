using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Pixelism {

    public class GraphicsController : IDisposable {

        private IColorQuantizer ColorQuantizer { get; set; }
        private IColorReduction ColorReduction { get; set; }
        private List<IDebugger> Debuggers { get; set; } = new List<IDebugger>();

        private Type[] quantizerTypes;
        private string[] quantizerNames;
        private DropdownState quantizerDropdownState;
        private Type nextquantizer = null;

        private Type[] reductionTypes;
        private string[] reductionNames;
        private DropdownState reductionDropdownState;
        private Type nextReduction = null;

        private Vector2Int resolution = Vector2Int.zero;

        private bool resize = false;

        public bool Resize {
            get { return resize; }
            set {
                if (resize != value) {
                    resize = value;
                    hasChanged = true;
                }
            }
        }

        private bool crt = false;

        public bool CRT {
            get { return crt; }
            set {
                if (crt != value) {
                    crt = value;
                    hasChanged = true;
                }
            }
        }

        private bool enableDebug = false;

        public bool EnableDebug {
            get { return enableDebug; }
            set {
                if (enableDebug != value) {
                    enableDebug = value;
                    hasChanged = true;
                }
            }
        }

        private bool hasChanged = true;

        private static readonly int inputRT = Shader.PropertyToID("InputRT");
        private static readonly int outputRT = Shader.PropertyToID("OutputRT");
        private static readonly int processedRT = Shader.PropertyToID("ResultRT");
        private static readonly int output2RT = Shader.PropertyToID("Output2RT");
        private static readonly int tempRT = Shader.PropertyToID("TempRT");

        private Shader shader;
        private Material material;

        private Shader crtShader;
        private Material crtMaterial;
        private float focus = 0.5f;

        public float Focus {
            get { return focus; }
            set {
                if (focus != value) {
                    focus = value;
                    hasChanged = true;
                }
            }
        }

        private float apertureGrill = 0.4f;

        public float ApertureGrill {
            get { return apertureGrill; }
            set {
                if (apertureGrill != value) {
                    apertureGrill = value;
                    hasChanged = true;
                }
            }
        }

        private float scanline = 0.4f;

        public float Scanline {
            get { return scanline; }
            set {
                if (scanline != value) {
                    scanline = value;
                    hasChanged = true;
                }
            }
        }

        private AddressablesHelper.HandleCollector collector = new AddressablesHelper.HandleCollector();

        public GraphicsController() : this(new ModifiedMedianCutCPU(), new PaletteDither()) {
        }

        public GraphicsController(IColorQuantizer colorQuantizer, IColorReduction colorReduction) {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            UnsafeUtility.SetLeakDetectionMode(Unity.Collections.NativeLeakDetectionMode.EnabledWithStackTrace);
#endif

            // not null
            ColorQuantizer = colorQuantizer;
            ColorReduction = colorReduction;

            quantizerTypes = EnumerateInterfaceTypes(typeof(IColorQuantizer)).OrderBy(x => x.Name).ToArray();
            quantizerNames = quantizerTypes.Select(x => x.Name).ToArray();
            quantizerDropdownState = new DropdownState(Array.FindIndex(quantizerTypes, x => x == ColorQuantizer.GetType()));

            reductionTypes = EnumerateInterfaceTypes(typeof(IColorReduction)).OrderBy(x => x.Name).ToArray();
            reductionNames = reductionTypes.Select(x => x.Name).ToArray();
            reductionDropdownState = new DropdownState(Array.FindIndex(reductionTypes, x => x == ColorReduction.GetType()));

            // add debuggers
            Debuggers.Add(new ErrorEstimator());
            Debuggers.Add(new ImageComparison());
            Debuggers.Add(new PaletteVisualizer());

            AddressablesHelper.LoadAssetAsync<Shader>("Fullscreen", res => {
                shader = res;
                material = new Material(shader);
                material.hideFlags = HideFlags.HideAndDontSave;

            }).Collect(collector)
            .WaitForCompletion(); // インスタンスごとにやると効率悪いので、やるにしても親でまとめて

            AddressablesHelper.LoadAssetAsync<Shader>("CRT", res => {
                crtShader = res;
                crtMaterial = new Material(crtShader);
                crtMaterial.hideFlags = HideFlags.HideAndDontSave;

            }).Collect(collector)
            .WaitForCompletion(); // インスタンスごとにやると効率悪いので、やるにしても親でまとめて

        }

        public void Dispose() {

            ColorQuantizer?.Dispose();
            ColorReduction?.Dispose();

            foreach (var d in Debuggers) {
                d.Dispose();
            }
            Debuggers.Clear();

            collector.Dispose();
            material?.DestoryOnRuntime();
            crtMaterial?.DestoryOnRuntime();
        }

        // maybe need int mipLevel = 0, int slice = 0
        private void Quantize(CommandBuffer command, RenderTargetIdentifier source, int width, int height) {
            ColorQuantizer?.Quantize(command, source, width, height);
        }

        // RenderTargetIdentifier だとwidth heightがunity任せなので、Texture2D RenderTextureとするかも
        private bool Reduce(CommandBuffer command, RenderTargetIdentifier source, RenderTargetIdentifier destination, int width, int height) {
            return ColorReduction?.Reduce(command, source, destination, width, height, ColorQuantizer.ColorPalette, ColorQuantizer.ColorPaletteCount) == true;
        }

        public void Process(CommandBuffer command, RenderTargetIdentifier inout, RenderTargetIdentifier depth, int width, int height) {
            resolution = new Vector2Int(width, height);
            ChangeProcessor();

            // unity builtinの場合、rendering解像度を任意にさらにscreenとは異なるaspectを維持したままレンダリングするのは手間がかかる
            // 正攻法だと、camearaから自前のrender textureに書き出す必要がある。dynamic resolutionの場合、aspectを変えることは難しい。
            // ここでは、そこがkey featureではないので、レンダリング済みの結果をリサイズする。
            int renderingWidth = Resize ? 640 : width;
            int renderingHeight = Resize ? 400 : height;

            command.GetTemporaryRT(inputRT, renderingWidth, renderingHeight, 0, FilterMode.Point, RenderTextureFormat.Default, RenderTextureReadWrite.Default, 1, true);
            command.GetTemporaryRT(outputRT, renderingWidth, renderingHeight, 0, FilterMode.Point, RenderTextureFormat.Default, RenderTextureReadWrite.Default, 1, true);
            // AfterImageEffectsの場合、backbufferに転送済みなので、そのままだとshader resourceとしてbindできない

            // aspectを維持しつつ長い方をcropして転送
            // 端数をどうにかしたほうがよい
            float2 scale;
            scale.x = (renderingWidth / (float)renderingHeight) / (width / (float)height);
            scale.y = (renderingHeight / (float)renderingWidth) / (height / (float)width);
            // フィットする方に合わせる
            if (scale.x < scale.y) {
                scale.y = 1;
            } else {
                scale.x = 1;
            }
            // centering
            float2 offset = (1.0f - scale) * 0.5f;

            if (Resize) {
                // backbufferから読み戻すときは、scale, offsetが適用されない仕様バグがある。内部的にそうだろうなという挙動だけれど、API的には同一なので非常に不親切。
                // しかもwidth, heightが異なる場合はscallingされずにcropされる。これも内部的にはそうなるが…逆の書き込み時のエラーすら出ないのよりはマシか。
                // なので、一旦等倍で読み戻してからリサイズする
                command.GetTemporaryRT(tempRT, width, height, 0, FilterMode.Point, RenderTextureFormat.Default, RenderTextureReadWrite.Default, 1);
                command.Blit(inout, tempRT);
                // 縮小される場合、情報が失われるので、super sample相当のフィルタを行なうか、検討の余地がある
                command.Blit(tempRT, inputRT, scale, offset);
            } else {
                command.Blit(inout, inputRT);
            }

            Quantize(command, inputRT, renderingWidth, renderingHeight);
            if (Reduce(command, inputRT, outputRT, renderingWidth, renderingHeight)) {
            } else {
                command.Blit(inputRT, outputRT); // debug passを考慮するとコピーした方がinputRTがimmutableで簡潔
            }

            var output = new RenderTargetIdentifier(outputRT);

            // debug
            // resize後の方がよいかもしれない
            command.GetTemporaryRT(output2RT, renderingWidth, renderingHeight, 0, FilterMode.Point, RenderTextureFormat.Default, RenderTextureReadWrite.Default, 1, true);
            if (enableDebug) {
                command.GetTemporaryRT(processedRT, renderingWidth, renderingHeight, 0, FilterMode.Point, RenderTextureFormat.Default, RenderTextureReadWrite.Default, 1, true);
                command.Blit(output, processedRT); // 処理済みオリジナルの保存
                var dest = new RenderTargetIdentifier(output2RT);
                foreach (var d in Debuggers) {
                    if (d.Enabled) {
                        if (d.OnDebug(command, output, dest, inputRT, processedRT, ColorQuantizer, ColorReduction, renderingWidth, renderingHeight)) {
                            var t = output;
                            output = dest;
                            dest = t;
                        }
                    }
                }
                command.ReleaseTemporaryRT(processedRT);
            }

            // TODO interface

            if (Resize) {
                //Debug.Log(scale);
                // backbufferに書こうとして内部挙動の違いでトラブりたくないので、一旦普通のRTでもとの解像度に戻す
                command.SetRenderTarget(tempRT);
                command.ClearRenderTarget(false, true, Color.black);
                float2 vp = new float2(width * scale.x, height * scale.y);
                command.SetViewport(new Rect((width - vp.x) * 0.5f, (height - vp.y) * 0.5f, vp.x, vp.y)); // これblitに対して効かないな…
                command.SetGlobalTexture("_MainTex", output);

                if (CRT) {
                    command.SetGlobalFloat("_Focus", Focus);
                    command.SetGlobalFloat("_ApertureGrill", ApertureGrill);
                    command.SetGlobalFloat("_Scanline", Scanline);
                    command.SetGlobalVector("_Dimensions", new Vector4(vp.x, vp.y, 1.0f / vp.x, 1.0f / vp.y));
                    command.DrawProcedural(Matrix4x4.identity, crtMaterial, 0, MeshTopology.Triangles, 3); // todo function
                } else {

                    command.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3); // todo function
                }
                //command.Blit(output, tempRT, 1 / scale, -offset); // 当然clampなので、クリアした意味がない

                // apply CRT emulation effect using
                //command.DrawProcedural　

                // AfterImageEffectsの場合、UIを考慮してかbottom leftに反転されているので戻す。上位レイヤーで吸収したいなあ
                command.SetRenderTarget(BuiltinRenderTextureType.BindableTexture); // 相変わらずback bufferへのblitはエラーも無しに失敗しているのでこれが必要。何年放置してるんだ？
                command.Blit(tempRT, inout, new Vector2(1, -1), new Vector2(0, 1));
                command.ReleaseTemporaryRT(tempRT);
            } else {
                // AfterImageEffectsの場合、UIを考慮してかbottom leftに反転されているので戻す。上位レイヤーで吸収したいなあ
                command.SetRenderTarget(BuiltinRenderTextureType.BindableTexture); // 相変わらずback bufferへのblitはエラーも無しに失敗しているのでこれが必要。何年放置してるんだ？
                command.Blit(output, inout, new Vector2(1, -1), new Vector2(0, 1));
            }

            command.ReleaseTemporaryRT(output2RT);
            command.ReleaseTemporaryRT(inputRT);
            command.ReleaseTemporaryRT(outputRT);

            hasChanged = false;
            //Debug.Log("Process");
        }

        // const
        public bool HasChanged(int width, int height) {
            // 解像度は外部からこのタイミングで分かるか、これを引数無しにして、public Resolutionを毎回セットする
            if (resolution.x != width || resolution.y != height) {
                return true;
            }
            if (hasChanged) {
                return true;
            }
            if (ColorQuantizer?.HasChanged == true) {
                return true;
            }
            if (ColorReduction?.HasChanged == true) {
                return true;
            }

            if (EnableDebug) {
                foreach (var d in Debuggers) {
                    if (d.HasChanged == true) {
                        return true;
                    }
                }
            }
            return false;
        }

        private void ChangeProcessor() {
            if (nextquantizer != null) {
                ColorQuantizer?.Dispose();
                ColorQuantizer = CreateInstance<IColorQuantizer>(nextquantizer);
                nextquantizer = null;
            }
            if (nextReduction != null) {
                ColorReduction?.Dispose();
                ColorReduction = CreateInstance<IColorReduction>(nextReduction);
                nextReduction = null;
            }
        }

        internal void OnGUI() {
            Resize = GUILayout.Toggle(Resize, "640x400");
            if (Resize) {
                CRT = GUILayout.Toggle(CRT, "CRT (experimental)");
                if (CRT) {
                    // todo label, value
                    Focus = GUILayout.HorizontalSlider(Focus, 0.0f, 1.0f);
                    ApertureGrill = GUILayout.HorizontalSlider(ApertureGrill, 0.0f, 1.0f);
                    Scanline = GUILayout.HorizontalSlider(Scanline, 0.0f, 1.0f);
                }
            }

            using (new GUILayout.VerticalScope("box")) {
                var cqState = Dropdown(quantizerNames, quantizerDropdownState);
                if (cqState.selectedIndex != quantizerDropdownState.selectedIndex) {
                    nextquantizer = quantizerTypes[cqState.selectedIndex];
                    hasChanged = true;
                }
                quantizerDropdownState = cqState;
                if (ColorQuantizer != null) {
                    ColorQuantizer?.OnGUI();
                }
            }
            using (new GUILayout.VerticalScope("box")) {
                var ppState = Dropdown(reductionNames, reductionDropdownState);
                if (ppState.selectedIndex != reductionDropdownState.selectedIndex) {
                    nextReduction = reductionTypes[ppState.selectedIndex];
                    hasChanged = true;
                }
                reductionDropdownState = ppState;
                if (ColorReduction != null) {
                    ColorReduction.OnGUI();
                }
            }

            EnableDebug = GUILayout.Toggle(EnableDebug, "Debug");
            if (EnableDebug) {
                using (new GUILayout.VerticalScope("box")) {
                    foreach (var d in Debuggers) {
                        d.Enabled = GUILayout.Toggle(d.Enabled, d.GetType().Name); // fixme cache name?
                        if (d.Enabled) {
                            using (new GUILayout.VerticalScope("box")) {
                                d.OnGUI();
                            }
                        }
                    }
                }
            }
            // どのレイヤーでいれるべきか…
            if (GUI.changed) {
                hasChanged = true;
            }
        }

        private readonly struct DropdownState {

            public DropdownState(int selectedIndex) : this() {
                this.selectedIndex = selectedIndex;
            }

            public DropdownState(int selectedIndex, bool opened, Vector2 scrollPos) {
                this.selectedIndex = selectedIndex;
                this.opened = opened;
                this.scrollPos = scrollPos;
            }

            public readonly int selectedIndex;
            public readonly bool opened;
            public readonly Vector2 scrollPos;
        }

        private static DropdownState Dropdown(string[] items, in DropdownState state) {
            int selectedIndex = state.selectedIndex;
            bool opened = state.opened;
            Vector2 scrollPos = state.scrollPos;
            if (GUILayout.Button(items[state.selectedIndex], "box")) {
                opened = !opened;
            }
            if (opened) {
                using (new GUILayout.VerticalScope("box")) {
                    // scroll view いれたいが、いれるとheight制限つけないとのびのびになるな
                    for (int i = 0; i < items.Length; ++i) {
                        if (GUILayout.Button((selectedIndex == i ? "> " : "   ") + items[i], "label")) {
                            selectedIndex = i;
                            opened = false;
                        }
                    }
                }
            }
            return new DropdownState(selectedIndex, opened, scrollPos);
        }

        private IEnumerable<Type> EnumerateInterfaceTypes(Type interfaceType) {
            var types = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(s => s.GetTypes())
                .Where(p => interfaceType.IsAssignableFrom(p) &&
                !p.IsInterface &&
                !p.IsAbstract &&
                !Attribute.GetCustomAttributes(p).Any(x => x is ObsoleteAttribute));
            return types;
        }

        private T CreateInstance<T>(Type type) {
            return (T)Activator.CreateInstance(type);
        }

    }

}
