using System;
using System.IO;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Rendering;

namespace Pixelism {

    [ExecuteAlways]
    [RequireComponent(typeof(Camera))]
    public class DisplayController : MonoBehaviour {
        private CommandBuffer command = null;

        [SerializeField]
        private Camera target = null;

        private CameraEvent cameraEvent = CameraEvent.AfterImageEffects;
        private GraphicsController graphicsController = null;
        private Vector2 scrollPosition;

        private FrameTimer timer = new FrameTimer();

        private Screenshot screenshot = null;
        private string directory = "Screenshot";
        private string prefixScreenshot = "Screenshot_";
        private string prefixBurst = "Burst_";
        private Screenshot.FileFormat format = Screenshot.FileFormat.PNG;
        private bool enableBurstShot = false;
        private uint burstCount = 0;
        private string prefixCurrentBurst = null;
        private int captureFramerate = 60;
        private string saveDirectory;

        // gui
        public bool Menu { get; set; } = true;

        public bool MenuVisibility { get; set; } = true;

        private void Start() {
            var path = Application.persistentDataPath;
            if (!string.IsNullOrWhiteSpace(directory)) {
                path = Path.Combine(path, directory);
            }
            saveDirectory = path;
        }

        private void OnEnable() {
            if (target == null) {
                target = GetComponent<Camera>();
            }
            if (target) {
                graphicsController = new GraphicsController();
                command = new CommandBuffer() { name = "DisplayController" };
                target.AddCommandBuffer(cameraEvent, command);
            }
        }

        private void OnDisable() {
            if (target) {
                target.RemoveCommandBuffer(cameraEvent, command);
                target = null;
            }
            command = null;
            graphicsController?.Dispose();
            graphicsController = null;
        }

        private void OnDestroy() {
            graphicsController?.Dispose();
            screenshot?.Dispose();
        }

        private void Update() {
            if (Input.GetKeyDown(KeyCode.F1)) {
                MenuVisibility = !MenuVisibility;
            }
            if (Input.GetKeyDown(KeyCode.F4)) {
                CaptureScreenshotAsync();
            } else if (Input.GetKeyDown(KeyCode.F8)) {
                ToggleScreenshotBurstAsync();
            }

            if (Input.GetKeyDown(KeyCode.Escape)) {
                Application.Quit();
            }
        }

        private void LateUpdate() {
            timer.Capture();
            timer.Aquire();
            CaptureBurstScreenshotAsync();
        }

        private void OnPreRender() {
            // if needs to update
            var width = target.pixelWidth;
            var height = target.pixelHeight;
            if (HasChanged(width, height)) {
                command.Clear();
                PopulateCommand(width, height);
            }
        }

        private bool HasChanged(int width, int height) {
            if (graphicsController?.HasChanged(width, height) == true) {
                return true;
            }
            if (!Application.IsPlaying(gameObject)) {
                // ExecuteAlways でnon play実行しているときに、
                // リロード後コマンド自体は存在しても実行されない（リソースが古い？）ことがあるので常に更新する
                return true;
            }
            return false;
        }

        private void PopulateCommand(int width, int height) {
            if (graphicsController != null) {
                var inout = new RenderTargetIdentifier(BuiltinRenderTextureType.CurrentActive);
                var depth = new RenderTargetIdentifier(BuiltinRenderTextureType.Depth);
                graphicsController.Process(command, inout, depth, width, height);
            }
        }

        [Obsolete]
        private void Resize(uint renderingWidth = 640, uint renderingHeight = 400) {
            // DX12: ImageEffects phaseで bilinearでbackbuffer転送時に元解像度に戻されるので、イマイチ使いにくい
            // aspectが異なる場合も当然ウインドウ側に合わせられるのもやりにくい…
            target.allowDynamicResolution = true;

            // 1超えるsuper sampleは無理らしい
            float widthScale = Mathf.Clamp01(renderingWidth / (float)target.pixelWidth);
            float heightScale = Mathf.Clamp01(renderingHeight / (float)target.pixelHeight);

            var widthScaleFactor = ScalableBufferManager.widthScaleFactor;
            var heightScaleFactor = ScalableBufferManager.heightScaleFactor;

            if (widthScale != widthScaleFactor || heightScale != heightScaleFactor) {
                ScalableBufferManager.ResizeBuffers(widthScale, heightScale);
            }
        }

        public void CaptureScreenshotAsync() {
            if (enableBurstShot) {
                return;
            }

            if (screenshot == null) {
                screenshot = new Screenshot();
            }
            try {
                if (!Directory.Exists(saveDirectory)) {
                    Directory.CreateDirectory(saveDirectory);
                }
                var filename = string.Concat(prefixScreenshot, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss.fff_"), Time.frameCount, Screenshot.GetExtension(format));
                var path = Path.Combine(saveDirectory, filename);
                StartCoroutine(screenshot.CaptureAsync(path, Screenshot.FileFormat.PNG));
            } catch (Exception e) {
                Debug.LogError(e);
            }
        }

        public void CaptureBurstScreenshotAsync() {
            if (!enableBurstShot) {
                return;
            }

            try {
                var filename = string.Concat(prefixCurrentBurst, burstCount++, Screenshot.GetExtension(format));
                var path = Path.Combine(saveDirectory, filename);
                StartCoroutine(screenshot.CaptureAsync(path, format, false));
            } catch (Exception e) {
                Debug.LogError(e);
            }
        }

        public void ToggleScreenshotBurstAsync() {
            enableBurstShot = !enableBurstShot;
            if (enableBurstShot) {
                if (!Directory.Exists(saveDirectory)) {
                    Directory.CreateDirectory(saveDirectory);
                }
                if (screenshot == null) {
                    screenshot = new Screenshot();
                }
                burstCount = 0;
                prefixCurrentBurst = string.Concat(prefixBurst, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss.fff_"));
                Time.captureFramerate = captureFramerate;
                Debug.Log("Start to capture screenshot bursting...");
            } else {
                Debug.Log("End to capture screenshot bursting...");
                Time.captureFramerate = 0; // reset
            }
        }

        // uguiよりもpolymorphism friendly
        private void OnGUI() {
            if (MenuVisibility && graphicsController != null) {
                const int contentWidth = 200;
                using (new GUILayout.AreaScope(new Rect(0, 0, Screen.width, Screen.height))) {
                    using (new GUILayout.HorizontalScope()) {
                        GUILayout.FlexibleSpace();
                        Menu = GUILayout.Toggle(Menu, "Menu (F1)");
                    }
                    if (Menu) {
                        using (new GUILayout.HorizontalScope()) {
                            GUILayout.FlexibleSpace();
                            using (new GUILayout.VerticalScope("box", GUILayout.Width(contentWidth))) {
                                using (var scroll = new GUILayout.ScrollViewScope(scrollPosition)) {
                                    scrollPosition = scroll.scrollPosition;
                                    graphicsController.OnGUI();
                                }

                                GUILayout.Label($"CPU: {timer.CpuFrameTime(),8:F3} ms");
                                GUILayout.Label($"GPU: {timer.GpuFrameTime(),8:F3} ms");
                                GUILayout.Space(20); // avoid Development mark
                            }
                        }

                    }
                }
            }
        }

    }
}
