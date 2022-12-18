#ifndef FULLSCREEN_CGINC
#define FULLSCREEN_CGINC

float4 GetFullscreenTrianglePosition(uint vertexId) {
    const uint topology = 3;
    const uint i = vertexId % topology;
    const float2 xy[topology] = {
        {-1, -1},
        {-1,  3},
        { 3, -1},
    };
    return float4(xy[i], 0, 1);
}

float2 GetFullscreenTriangleTexcoord(float2 position) {
    return position.xy * float2(0.5f, -0.5f) + float2(0.5f, 0.5f);
}

#endif // FULLSCREEN_CGINC
