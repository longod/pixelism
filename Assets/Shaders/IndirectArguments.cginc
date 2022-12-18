#ifndef INDIRECT_ARGUMENTS_CGINC
#define INDIRECT_ARGUMENTS_CGINC

struct DrawArguments {
    uint vertexCountPerInstance;
    uint instanceCount;
    uint startVertexLocation;
    uint startInstanceLocation;
};

struct DispatchArguments {
    uint3 threadGroupCount;
    uint padding; // for unity stride
};

#endif // INDIRECT_ARGUMENTS_CGINC
