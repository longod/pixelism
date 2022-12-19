using System;
using System.Collections;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Profiling;
using UnityEngine.Rendering;

namespace Pixelism {

    public class Screenshot : IDisposable {

        public enum FileFormat {
            PNG,
            JPG,
            TGA,
            EXR,
        }

        public IEnumerator CaptureAsync(string uniquedPath, FileFormat format, bool outputLog = true) {
            yield return new WaitForEndOfFrame();
            int width = Screen.width;
            int height = Screen.height;
            GraphicsFormat graphicsFormat = GraphicsFormat.R8G8B8A8_SRGB; // TODO get gamma settings
            var index = FindUsableTexture(width, height, graphicsFormat);
            if (index < 0) {
                Debug.LogError("not enough buffer");
            } else {
                ScreenCapture.CaptureScreenshotIntoRenderTexture(buffers[index].renderTexture);
                buffers[index].request = AsyncGPUReadback.Request(buffers[index].renderTexture, 0, graphicsFormat,
                    request => {
                        if (request.done && !request.hasError && !disposed) {
                            ReadbackCompleted(request, uniquedPath, format, buffers[index].renderTexture, outputLog);
                        }
                    });
            }
        }

        private void ReadbackCompleted(AsyncGPUReadbackRequest request, string path, FileFormat format, RenderTexture renderTexture, bool outputLog) {
            // exclude sampler
            if (flipSampler == null) {
                flipSampler = CustomSampler.Create("Screenshot.FlipY");
            }
            if (encodeSampler == null) {
                encodeSampler = CustomSampler.Create("Screenshot.Encode");
            }
            if (writeSampler == null) {
                writeSampler = CustomSampler.Create("Screenshot.Write");
            }
            uint width = (uint)renderTexture.width;
            uint height = (uint)renderTexture.height;
            var graphicsFormat = renderTexture.graphicsFormat;

            // TODO use native array
            var managed = request.GetData<byte>().ToArray();

            // 専用の単一スレッドで実行すると同時に同じパスに書き込む可能性は解消できるが、詰まりやすくなる
            Task.Run(() => {
                Profiler.BeginThreadProfiling("Task", $"Thread {Thread.CurrentThread.ManagedThreadId}");
                // 上下逆なので、この辺で入れ替える
                byte[] image = null;
                if (NeedToFlipY()) {
                    flipSampler.Begin();
                    image = new byte[managed.Length];
                    int pitchBytes = TextureUtility.GetBitsPerPixel(graphicsFormat) * (int)width / 8;

                    // TODO alpha無しならここでアルファ潰す。encodeが面倒見てくれないので。
                    for (int y = 0; y < height; ++y) {
                        Buffer.BlockCopy(managed, pitchBytes * y, image, ((int)height - 1 - y) * pitchBytes, pitchBytes);
                    }
                    flipSampler.End();
                } else {
                    image = managed;
                }

                // TODO try catch
                encodeSampler.Begin();
                byte[] bin = Encode(format, image, width, height, graphicsFormat);
                encodeSampler.End();
                writeSampler.Begin();
                File.WriteAllBytes(path, bin);
                writeSampler.End();
                Profiler.EndThreadProfiling();
            });

            if (outputLog) {
                Debug.Log("Capture Screenshot: " + path); // 厳密にはキャプチャはしたが、ファイル出力していない状態
            }
        }

        private static bool NeedToFlipY() {
            // グラフィクスAPI依存？判定手段は？SystemInfo.graphicsDeviceType
            // vulkanもそうらしい。uGUIレンダリング後だから全部がそう？
            return true;
        }

        public void Dispose() {
            disposed = true;
            for (int i = 0; i < buffers.Length; ++i) {
                if (!buffers[i].request.done) {
                    buffers[i].request.WaitForCompletion(); // sync
                    // ReadbackCompletedはここまでで実行される？
                    buffers[i].renderTexture?.DestoryOnRuntime();
                }
            }
            buffers = null;
        }

        public static string GetExtension(FileFormat format) {
            switch (format) {
                case FileFormat.PNG:
                    return PNGExt;

                case FileFormat.JPG:
                    return JPGExt;

                case FileFormat.TGA:
                    return TGAExt;

                case FileFormat.EXR:
                    return EXRExt;

                default:
                    throw new NotImplementedException();
            }
        }

        // jpg qualityとexr flagは必要になれば
        private static byte[] Encode(FileFormat format, byte[] image, uint width, uint height, GraphicsFormat graphicsFormat) {
            // formatは入力フォーマットらしい、出力フォーマットの指定はできず等しくなる。
            // RGBA32の入力でrowByte = 4 * widthを指定しても、RGB24のJPGはアルファを読み飛ばしてくれることなく正しい画像にならない。
            uint rowBytes = 0;
            switch (format) {
                case FileFormat.PNG:
                    return ImageConversion.EncodeArrayToPNG(image, graphicsFormat, width, height, rowBytes);

                case FileFormat.JPG:
                    // only RGB24 fomrat?
                    return ImageConversion.EncodeArrayToJPG(image, graphicsFormat, width, height, rowBytes);

                case FileFormat.TGA:
                    // FIXME 0byte!
                    return ImageConversion.EncodeArrayToTGA(image, graphicsFormat, width, height, rowBytes);

                case FileFormat.EXR:
                    // only HDR format
                    return ImageConversion.EncodeArrayToEXR(image, graphicsFormat, width, height, rowBytes);

                default:
                    throw new NotImplementedException();
            }
        }

        // thread unsafe
        // return buffer index
        private int FindUsableTexture(int width, int height, GraphicsFormat graphicsFormat) {
            int index = -1;
            for (int i = 0; i < buffers.Length; ++i) {
                if (buffers[i].request.done) { // FIXME 厳密には、読み取り後のメモリをmanagedで参照しているか、揮発しても問題ないタイミングまで処理が進んだ後
                    if (index < 0) {
                        index = i;
                    }
                    if (buffers[i].renderTexture != null &&
                        buffers[i].renderTexture.width == width &&
                        buffers[i].renderTexture.height == height &&
                        buffers[i].renderTexture.graphicsFormat == graphicsFormat) {
                        // found reusable texture
                        return i;
                    }
                }
            }
            // recreate
            if (index >= 0) {
                buffers[index].renderTexture?.DestoryOnRuntime();
                buffers[index].renderTexture = new RenderTexture(width, height, 0, graphicsFormat, 0);
            }
            return index;
        }

        // 他に参照したりコピーしたりしないのでstruct
        private struct ReadbackBuffer {
            internal AsyncGPUReadbackRequest request;
            internal RenderTexture renderTexture;
        }

        private ReadbackBuffer[] buffers = new ReadbackBuffer[maxBufferCount];
        private bool disposed = false;

        // RenderTextureが要求される寿命は、CaptureScreenshotIntoRenderTextureから、
        // リードバック完了（コールバック呼び出し開始時まで）、それ以降は破棄されても問題ない。
        // ハードウェアと解像度によるが、大体リードバックは2,3フレームで完了するので、
        // 高解像度で毎フレーム1枚撮ったとしてもこれくらいあれば十分だろうというバッファ数。
        private static readonly int maxBufferCount = 8;

        private static volatile CustomSampler flipSampler = null;
        private static volatile CustomSampler encodeSampler = null;
        private static volatile CustomSampler writeSampler = null;

        private static readonly string PNGExt = ".png";
        private static readonly string JPGExt = ".jpg";
        private static readonly string TGAExt = ".tga";
        private static readonly string EXRExt = ".exr";

    }
}
