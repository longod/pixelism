#ifndef HISTOGRAM_CGINC
#define HISTOGRAM_CGINC

static const uint ChannelBit = 4;
static const uint SignificantBit = 8 - ChannelBit;
static const uint InverseShift = 1u << SignificantBit; // decode?
static const uint ChannelSize = 1 << ChannelBit;
static const uint ChannelMax = ChannelSize - 1;
static const uint HistogramSize = 4096; // 1 << (ChannelBit*3)

static const uint3 shift = uint3(0, ChannelBit, ChannelBit * 2);

uint3 Quantize(uint3 rgb) {
    return (rgb >> SignificantBit);
}

uint ToIndex(uint x, uint y, uint z) {
    return (x) | (y << ChannelBit) | (z << (ChannelBit*2));
}

uint ToIndex(uint x, uint y, uint z, uint3 swizzle) {
    return (x << shift[swizzle.x]) | (y << shift[swizzle.y]) | (z << shift[swizzle.z]);
}

uint ToIndex(float3 color) {
    uint3 c = uint3(saturate(color) * 255.f);
    uint3 q = Quantize(c);
    uint bin = ToIndex(q.x, q.y, q.z);
    return bin;
}

uint3 Inverse(uint r, uint g, uint b, uint count) {
    return (uint3)(count * (float3(r, g, b) + 0.5f) * InverseShift);
}

float3 ToColor(uint3 sum, uint total) {
#if COLOR_12BIT
    // ここに至るまでに12-bit化しておいた方がよいかもしれないが、検証しながら
    return round(((float3)sum / total) / 17.0f) * 17.0f;
#else
    return (float3)sum / total;
#endif
}

#endif // HISTOGRAM_CGINC
