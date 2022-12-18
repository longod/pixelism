Shader "Hidden/Pixelism/PaletteVisualizer"
{
    Properties
    {
    }
    SubShader
    {
        // No culling or depth
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"
            #include "ColorSpace.cginc"

            struct appdata
            {
                uint id : SV_VertexID;
                uint instanceID : SV_InstanceID; // UNITY_VERTEX_INPUT_INSTANCE_ID // UNITY_GET_INSTANCE_ID(v) がinternalだからどうすればいいの
            };

            struct v2f
            {
                float3 color : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            StructuredBuffer<float3> _Palette;

            v2f vert (appdata v)
            {
                v2f o;
                const uint topology = 6;
                uint q = v.id % topology; // quad
                uint i = v.instanceID; //v.id / topology; // index
                const float2 xy[topology] = {
                    {-0.5f, -0.5f},
                    {-0.5f, 0.5f},
                    {0.5f, 0.5f},
                    {0.5f, 0.5f},
                    {0.5f, -0.5f},
                    {-0.5f, -0.5f},
                };
                float aspect = _ScreenParams.x/_ScreenParams.y;
                float3 pos = 0;
                float sizex = 24 / _ScreenParams.x * 2.0f;
                float2 size = float2(sizex, sizex * aspect);
                float offsetx = 0 / _ScreenParams.x * 2.0f;
                float2 offset = float2(offsetx, offsetx * aspect);
                uint row = 16;
                pos.xy = xy[q];
                pos.xy *= size;
                pos.xy += float2(-1, 1); // top left
                pos.xy += size * float2(1, -1) * 0.5f; // offset pivot
                pos.xy += float2(i % row, i / row) * (size + offset) * float2(1, -1); // layout
                o.vertex = float4(pos, 1);
                o.color = _Palette[i];
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 col = i.color;
                // gammaに書き込むのでlienarに戻す
                col.rgb = GammaToLinear(col);
                return float4(col, 1);
            }
            ENDCG
        }
    }
}
