using UnityEngine;

namespace Pixelism {

    // frame の時間情報
    // 特定区間は CPUは UnityEngine.Profiling.Recorder
    // GPU はあってほしいんだけれど…
    public class FrameTimer {
        private FrameTiming[] frameTimings = new FrameTiming[3];
        private uint lastFrameCount = 0;

        public FrameTimer(int count = 1) {
            frameTimings = new FrameTiming[count];
        }

        // FrameTimingManager.IsFeatureEnabled();

        // 他色々とれる
        public double CpuFrameTime(int index = 0) {
            return frameTimings[index].cpuFrameTime;
        }

        public double GpuFrameTime(int index = 0) {
            return frameTimings[index].gpuFrameTime;
        }

        public void Capture() {
            // 呼び出したタイミングではなくて、呼び出しフレームのキャプチャのように見えるが…
            FrameTimingManager.CaptureFrameTimings();
        }

        public bool Aquire(int? count = null) {
            uint length = (uint)frameTimings.Length;
            if (count.HasValue) {
                length = (uint)count.Value;
            }
            lastFrameCount = FrameTimingManager.GetLatestTimings(length, frameTimings);
            if (lastFrameCount == 0) {
                return false;
            }
            // 成功時のみコピーしてそれをgetしたほうがよいかも
            return true;
        }
    }
}
