#ifndef COLOR_SPACE_CGINC
#define COLOR_SPACE_CGINC

#include "UnityCG.cginc"

// rgb is gamma? or linear?
static const float3x3 RGB_to_YUV = {
    0.299, 0.587, 0.114,
    -0.14713, -0.28886, 0.436,
    0.615, -0.51499, -0.10001,
};

static const float3x3 YUV_to_RGB = {
    1, 0, 1.13983,
    1, -0.39465, -0.58060,
    1, 2.03211, 0,
};

inline float3 RGBToYUV(float3 x) {
    return mul(RGB_to_YUV, x);
}

inline float3 YUVToRGB(float3 x) {
    return mul(YUV_to_RGB, x);
}

inline float3 LinearToGamma(float3 x) {
    // high precision
    return float3(LinearToGammaSpaceExact(x.r), LinearToGammaSpaceExact(x.g), LinearToGammaSpaceExact(x.b));
}

inline float3 GammaToLinear(float3 x) {
    // high precision
    return float3(GammaToLinearSpaceExact(x.r), GammaToLinearSpaceExact(x.g), GammaToLinearSpaceExact(x.b));
}

#endif // COLOR_SPACE_CGINC
