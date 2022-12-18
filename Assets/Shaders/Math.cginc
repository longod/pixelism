#ifndef MATH_CGINC
#define MATH_CGINC

inline uint DivRoundUp(uint a, uint div) {
    return ((a + div - 1) / div);
}

inline uint3 DivRoundUp(uint3 a, uint3 div) {
    return ((a + div - 1) / div);
}

inline uint NextPowerOfTwo(uint x) {
    uint mask = (1 << firstbithigh(x)) - 1;
    return (x + mask) & ~mask;
}

// todo
// uint to float3
// float3 to uint
// uint to uint3
// uint3 to uint

// 4-bit * 3ch = 12-bit colors
// input range 0~255
inline uint3 Quantize4Bits(in uint3 v) {
    return (uint3)(round(v.rgb / 17.f) * 17.f);
}

// alphaは捨てられる
inline uint Quantize4Bits(in uint v) {
    uint3 c = uint3(v & 0xFF, (v >> 8) & 0xFF, (v >> 16) & 0xFF);
    c = Quantize4Bits(c);
    uint o = (uint)(c.r) | ((uint)(c.g) << 8) | ((uint)(c.b) << 16);
    return o;
}

#endif // MATH_CGINC
