using System.Collections.Generic;
using Unity.Mathematics;

namespace Pixelism.Test {

    public readonly struct UintAbsoluteEqualityComparer : IEqualityComparer<uint> {
        private readonly int tolerance;

        public UintAbsoluteEqualityComparer(int tolerance = 0) {
            this.tolerance = tolerance;
        }

        public bool Equals(uint x, uint y) {
            uint d = default;
            if (x > y) {
                d = x - y;
            } else {
                d = y - x;
            }
            return d <= tolerance;
        }

        public int GetHashCode(uint obj) {
            return (int)obj;
        }
    }

    public readonly struct RGB24EqualityComparer : IEqualityComparer<uint> {
        private readonly int tolerance;

        public RGB24EqualityComparer(int tolerance = 0) {
            this.tolerance = tolerance;
        }

        public bool Equals(uint x, uint y) {
            int3 d = default;
            d.x = math.abs((int)(x & 0xff) - (int)(y & 0xff));
            d.y = math.abs((int)((x >> 8) & 0xff) - (int)((y >> 8) & 0xff));
            d.z = math.abs((int)((x >> 16) & 0xff) - (int)((y >> 16) & 0xff));
            return math.all(d <= tolerance);
        }

        public int GetHashCode(uint obj) {
            return (int)(obj & 0xffffffu);
        }
    }

    public readonly struct Float3EqualityComparer : IEqualityComparer<float3> {
        private readonly float? tolerance;

        public Float3EqualityComparer(float tolerance) {
            this.tolerance = tolerance;
        }

        public bool Equals(float3 x, float3 y) {
            if (tolerance.HasValue) {
                return math.all(Math.Approximately(x, y, tolerance.Value));
            }
            return x.Equals(y);
        }

        public int GetHashCode(float3 obj) {
            return obj.GetHashCode();
        }
    }

}
