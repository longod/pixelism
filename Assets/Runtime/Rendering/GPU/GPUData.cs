using System.Runtime.InteropServices;
using Unity.Mathematics;

namespace Pixelism {

    // using modified median cut
    [StructLayout(LayoutKind.Sequential)]
    public struct ColorVolume {

        // uint * uintでオーバーフローする可能性がある。uint64_tは SM 6.0が必要で、unityはサポートしていない！
        // uint2 で誤魔化すことも可能だが、乗算が大分厄介になる
        // normalizeした値を使用する
        public float priority;

        public uint3 min;
        public uint count;
        public uint3 max;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Scratch {
        public uint volumeCount; // == palette count
        public int index; // cutting index
        public uint3 swizzle;
        public float normalizer;
    }
}
