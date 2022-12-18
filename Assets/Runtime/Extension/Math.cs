using System.Runtime.CompilerServices;
using Unity.Mathematics;
using UnityEngine.Jobs;

namespace Pixelism {

    /// <summary>
    /// math helper
    /// </summary>
    public static class Math {

        // 最小通常値
        public static readonly float MinNormalF = math.FLT_MIN_NORMAL; // = float.MinValue

        public static readonly float MinValueF = 1.401298464e-45F; // min positive value

        public static readonly float2 MinNormalF2 = new float2(MinNormalF, MinNormalF);
        public static readonly float3 MinNormalF3 = new float3(MinNormalF, MinNormalF, MinNormalF);
        public static readonly float4 MinNormalF4 = new float4(MinNormalF, MinNormalF, MinNormalF, MinNormalF);
        public static readonly double MinNormalD = math.DBL_MIN_NORMAL; // = double.MinValue
        public static readonly double MinValueD = 4.9406564584124654e-324; // min positive value
        public static readonly double2 MinNormalD2 = new double2(MinNormalD, MinNormalD);
        public static readonly double3 MinNormalD3 = new double3(MinNormalD, MinNormalD, MinNormalD);
        public static readonly double4 MinNormalD4 = new double4(MinNormalD, MinNormalD, MinNormalD, MinNormalD);

        public static readonly float M_E = math.EPSILON;
        public static readonly double M_E_D = math.EPSILON_DBL;

        public static readonly float M_PI = math.PI;
        public static readonly float M_PI_2 = math.PI / 2.0f;
        public static readonly float M_PI_4 = math.PI / 4.0f;
        public static readonly float M_1_PI = 1.0f / math.PI;
        public static readonly float M_2_PI = 2.0f / math.PI;
        public static readonly float M_4_PI = 4.0f / math.PI;
        public static readonly float M_2PI = 2.0f * math.PI;
        public static readonly float M_4PI = 4.0f * math.PI;

        public static readonly double M_PI_D = math.PI_DBL;
        public static readonly double M_PI_2_D = math.PI_DBL / 2.0;
        public static readonly double M_PI_4_D = math.PI_DBL / 4.0;
        public static readonly double M_1_PI_D = 1.0 / math.PI_DBL;
        public static readonly double M_2_PI_D = 2.0 / math.PI_DBL;
        public static readonly double M_4_P_DI = 4.0 / math.PI_DBL;
        public static readonly double M_2PI_D = 2.0 * math.PI_DBL;
        public static readonly double M_4PI_D = 2.0 * math.PI_DBL;

        /// <summary>
        /// nearly equals for floating points
        /// https://floating-point-gui.de/errors/comparison/
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Approximately(float x, float y, float epsilon = 0.00001f) {
            var a = math.abs(new float3(x, y, x - y));
            if (x == y) {
                return true;
            } else if (x == 0 || y == 0 || (a.x + a.y < MinNormalF)) {
                return a.z < (epsilon * MinNormalF);
            }
            return a.z < epsilon * math.min(a.x + a.y, float.MaxValue);
        }

        /// <summary>
        /// swizleごとに求めるのが理想だが、高速化のため上記計算の分岐部を省略している
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool2 Approximately(float2 x, float2 y, float epsilon = 0.00001f) {
            return math.abs(x - y) < epsilon * math.min(math.abs(x) + math.abs(y), float.MaxValue);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool3 Approximately(float3 x, float3 y, float epsilon = 0.00001f) {
            return math.abs(x - y) < epsilon * math.min(math.abs(x) + math.abs(y), float.MaxValue);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool4 Approximately(float4 x, float4 y, float epsilon = 0.00001f) {
            return math.abs(x - y) < epsilon * math.min(math.abs(x) + math.abs(y), float.MaxValue);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Approximately(double x, double y, double epsilon = 0.0000000001d) {
            var a = math.abs(new double3(x, y, x - y));
            if (x == y) {
                return true;
            } else if (x == 0 || y == 0 || (a.x + a.y < MinNormalF)) {
                return a.z < (epsilon * MinNormalF);
            }
            return a.z < epsilon * math.min(a.x + a.y, double.MaxValue);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool2 Approximately(double2 x, double2 y, double epsilon = 0.0000000001d) {
            return math.abs(x - y) < epsilon * math.min(math.abs(x) + math.abs(y), float.MaxValue);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool3 Approximately(double3 x, double3 y, double epsilon = 0.0000000001d) {
            return math.abs(x - y) < epsilon * math.min(math.abs(x) + math.abs(y), float.MaxValue);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool4 Approximately(double4 x, double4 y, double epsilon = 0.0000000001d) {
            return math.abs(x - y) < epsilon * math.min(math.abs(x) + math.abs(y), float.MaxValue);
        }

        /// <summary>
        /// nealy equals to zero
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool NearlyZero(float x, float epsilon = float.Epsilon) {
            return Approximately(x, 0.0f, epsilon);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool2 NearlyZero(float2 x, float epsilon = float.Epsilon) {
            return Approximately(x, 0.0f, epsilon);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool3 NearlyZero(float3 x, float epsilon = float.Epsilon) {
            return Approximately(x, 0.0f, epsilon);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool4 NearlyZero(float4 x, float epsilon = float.Epsilon) {
            return Approximately(x, 0.0f, epsilon);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool NearlyZero(double x, double epsilon = double.Epsilon) {
            return Approximately(x, 0.0f, epsilon);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool2 NearlyZero(double2 x, double epsilon = double.Epsilon) {
            return Approximately(x, 0.0f, epsilon);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool3 NearlyZero(double3 x, double epsilon = double.Epsilon) {
            return Approximately(x, 0.0f, epsilon);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool4 NearlyZero(double4 x, double epsilon = double.Epsilon) {
            return Approximately(x, 0.0f, epsilon);
        }

        /// <summary>
        /// matrix conversion
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float4x4 ToMatrix(float3 translation, quaternion rotation, float3 scale) {
            var tr = new float4x4(rotation, translation);
            var s = float4x4.Scale(scale); // scale直にかけるよりも、SIMDが効いて速いかもしれない
            return math.mul(tr, s);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float4x4 ToMatrix(TransformAccess transform) {
            return ToMatrix(transform.position, transform.rotation, transform.localScale); // lossyScaleが無い!
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float3x4 ToMatrix34(float4x4 m) {
            return new float3x4(m.c0.xyz, m.c0.xyz, m.c1.xyz, m.c3.xyz);
        }

        /// <summary>
        /// 除算後切り上げ like a ceil function
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int DivRoundUp(int a, int div) {
            return ((a + div - 1) / div);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int2 DivRoundUp(int2 a, int2 div) {
            return ((a + div - 1) / div);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int3 DivRoundUp(int3 a, int3 div) {
            return ((a + div - 1) / div);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int4 DivRoundUp(int4 a, int4 div) {
            return ((a + div - 1) / div);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint DivRoundUp(uint a, uint div) {
            return ((a + div - 1) / div);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint2 DivRoundUp(uint2 a, uint2 div) {
            return ((a + div - 1) / div);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint3 DivRoundUp(uint3 a, uint3 div) {
            return ((a + div - 1) / div);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint4 DivRoundUp(uint4 a, uint4 div) {
            return ((a + div - 1) / div);
        }

        /// <summary>
        /// cube root
        /// float, doubleは誤差が生じるので必要になるまでint,uintのみ, roundupされる
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Cbrt(int v) {
            // ｖが大きすぎる場合、overflow を起こす可能性がある
            int i = 0;
            while ((i * i * i) <= v) {
                ++i;
            }
            if (i == 0) {
                return i;
            }
            // revert overlap
            int k = i - 1;
            if ((k * k * k) == v) {
                return k;
            }
            return i;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int2 Cbrt(int2 v) {
            return new int2(Cbrt(v.x), Cbrt(v.y));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int3 Cbrt(int3 v) {
            return new int3(Cbrt(v.x), Cbrt(v.y), Cbrt(v.z));

        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int4 Cbrt(int4 v) {
            return new int4(Cbrt(v.x), Cbrt(v.y), Cbrt(v.z), Cbrt(v.w));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint Cbrt(uint v) {
            // ｖが大きすぎる場合、overflow を起こす可能性がある
            uint i = 0;
            while ((i * i * i) <= v) {
                ++i;
            }
            // avoid overflow
            if (i == 0) {
                return i;
            }
            // revert overlap
            uint k = i - 1;
            if ((k * k * k) == v) {
                return k;
            }
            return i;

        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint2 Cbrt(uint2 v) {
            return new uint2(Cbrt(v.x), Cbrt(v.y));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint3 Cbrt(uint3 v) {
            return new uint3(Cbrt(v.x), Cbrt(v.y), Cbrt(v.z));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint4 Cbrt(uint4 v) {
            return new uint4(Cbrt(v.x), Cbrt(v.y), Cbrt(v.z), Cbrt(v.w));
        }
    }
}
