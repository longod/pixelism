Shader "Hidden/Pixelism/PaletteDither"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
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
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 screenPos : TEXCOORD1;
                float4 vertex : SV_POSITION;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                // AfterImageEffects だと既にバックバッファに転送してsRGBになり、座標系が反転しているので戻す
#if UNITY_UV_STARTS_AT_TOP
                // このパスへの入出力で一致するようにしたので不要
                //o.uv.y = 1.f - o.uv.y;
#endif

                o.screenPos = ComputeScreenPos(o.vertex);
                return o;
            }

            sampler2D _MainTex;

            StructuredBuffer<float3> _Palette;
            StructuredBuffer<int> _PaletteCount;
            float4 _Dimensions;
            int _PaletteMax;

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv);
                // sample時にlinearに戻るのでgammaにまた戻す
                col.rgb = LinearToGamma(col);

                // dither
#if 01
                const int pitch4x4 = 4;
                const int count4x4 = 16;
                const float bayer4x4[count4x4] = {
                    0, 8, 2, 10,
                    12, 4, 14, 6,
                    3, 11, 1, 9,
                    15, 7, 13, 5.
                };
                const float step = 1.0f / count4x4; // LDR(0,1), 255諧調だと、255.f/count4x4
                // _ScreenParams は本当にカメラのレンダリングサイズであって、オフスクリーンレンダリングのレンダーターゲット解像度ではない
                //int2 loc = (int2)(i.screenPos.xy * _ScreenParams.xy) & 0x3;
                int2 loc = (int2)(i.screenPos.xy * _Dimensions.xy) & 0x3;
                float dm = (bayer4x4[loc.x + loc.y * pitch4x4]) / (float)(count4x4); // or + 1, or / (count4x4-1)
                float dr = (dm - 0.5f) * step;
                col.rgb = col.rgb + dr;
                //col.rgb = saturate(col.rgb + dr);
#endif

                // (0,15/16), (1/16,16/16), (0,15/15) どれが正しいのか…
                // linearで行なうのか、sRGBで行うのか
                // カラーグラデーションなどで試す必要がある
                // ditherの諧調変化をどちらによせるか、だから+0.5が良い可能性がある

                // #if UNITY_COLORSPACE_GAMMA でどっちでも成立するようにとか
                // 黒とか、ほんの少しでも色がつくとditherかかりはじめるからなんか工夫がいる

                // apply palette
#if 01
                // find nearest color
                // burte force
                // 16色くらいならこれでいいだろうけれど、256とかになるとライトよりマシとはいえ、最適化を考えたい
                // nearestの定義が曖昧なので、調整の余地がある
                float err = 1E+37; // max 精度的に少し低めの方が良いかも
                err = 1E+5;
                float3 pcol = col.rgb;
                pcol = 0; // no fallback
                int count = _PaletteCount[0];
                count = min(count, _PaletteMax); // 保険 最終的にはない方がよい
                UNITY_LOOP
                for(int i = 0; i < count; ++i) {
                    float3 p = _Palette[i];
                    //float d = distance(col.rgb, p.rgb); // non squared...but ok
                    float3 dc = (col.rgb - p.rgb);
                    float d = dc.r * dc.r + dc.g * dc.g + dc.b * dc.b;
                    if (d < err) {
                        err = d;
                        pcol.rgb = p.rgb;
                    }
                }
                col.rgb = pcol.rgb;
#endif

                // gammaに書き込むのでlienarに戻す
                col.rgb = GammaToLinear(col);
                return col;
            }
            ENDCG
        }
    }
}
