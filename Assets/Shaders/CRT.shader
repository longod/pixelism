Shader "Hidden/Pixelism/CRT"
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
            #include "Fullscreen.cginc"
            #include "ColorSpace.cginc"

            struct appdata
            {
                uint id : SV_VertexID;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = GetFullscreenTrianglePosition(v.id);
                o.uv = GetFullscreenTriangleTexcoord(o.vertex.xy);
                return o;
            }

            Texture2D _MainTex;
            // todo _ST

            float4 _MainTex_TexelSize; // set globalでも入るようだ xy=1/wh zw=wh
            float4 _Dimensions; // xy=wh, zw=1/wh
            float _Focus = 0.5f;
            float _ApertureGrill = 0.5f;
            float _Scanline = 0.25f;

            SamplerState _SamplerPointClamp;
            SamplerState _SamplerLinearClamp;

            fixed4 frag (v2f i) : SV_Target
            {

                // ideal CRT display
                // no distortion, flickering, noize, warp...
                // you had seen modern flat CRT display?

                float2 uv = i.uv;

                // TODO shadow mask, slot mask

                // aperture grille
                // from https://godotshaders.com/shader/vhs-and-crt-monitor-effect/
                // 3倍以上の解像度が前提、5倍以上が理想だが現実的ではない

                // アスペクトが異なると、squareになるように補正した方がよい？
                float2 resolution = _MainTex_TexelSize.zw;
                float2 dims = _Dimensions.xy;
                float2 offsetBase = _MainTex_TexelSize.xy;
                // あまり大きすぎると、隣のピクセルに完全に割り込む
                // TODO
                float2 aberration = float2(1, 0) * offsetBase * 0;//.125f;
                float3 phase = float3(1, 0, -1);
                float3 col = 0;
                SamplerState ss = _SamplerLinearClamp;
                // 上下方向にも欲しいかも
                // RGB完全分離ではなくて、他の成分もどこかに混ぜた方がよいかも
                col.r = _MainTex.SampleLevel(ss, uv + aberration * phase.r, 0).r;
                col.g = _MainTex.SampleLevel(ss, uv + aberration * phase.g, 0).g;
                col.b = _MainTex.SampleLevel(ss, uv + aberration * phase.b, 0).b;
                // linearだとブレすぎるので混ぜる
                // aberrationは無くてもよいが…
                SamplerState sp = _SamplerPointClamp;
                float3 p = 0;
                p.r = _MainTex.SampleLevel(sp, uv + aberration * phase.r, 0).r;
                p.g = _MainTex.SampleLevel(sp, uv + aberration * phase.g, 0).g;
                p.b = _MainTex.SampleLevel(sp, uv + aberration * phase.b, 0).b;
                // 輝度ベースでもよいかも
                float lum = (p.r + p.g + p.b) / 3.0f; // RGB分離されるので、均等なウェイト
                col.rgb = lerp(col.rgb, p.rgb, _Focus);

                float2 texelSize = _Dimensions.xy / _MainTex_TexelSize.zw;
                // 高すぎると色化けするので別の色空間でも試したほうがよい
                // TODO grill, scanlineから元からの減少量を求められないか…
                float brightness = 1 + (1/texelSize.x) * _ApertureGrill * 3 + (1/texelSize.y) * _Scanline;

                // pixel周期
                // modでもいいんだけれど、超高解像度のときはsmoothstepの方が固定幅にならなくて済むか
                float2 uvPI = uv * resolution * UNITY_PI;

                // 垂直開口部
                // サンプル後にフィルタしないと、超高解像度時にRGB間の隙間が出ないはず
                // 3倍以上必須なので、それ未満でも成立するようにしたいが…
                float3 apertureGrill = smoothstep(0.8f, 0.9f, abs(sin(uvPI.x + phase * UNITY_HALF_PI * 0.505f ))); // + 微小値で隙間を期待したいが…
                col.rgb *= lerp(1.0f, apertureGrill, _ApertureGrill);

                col *= brightness;
                col = saturate(col); // clamp brightness before scanline

                // 水平
                // 上下ともに被るべきだが、解像度が不足する
                float scanline = smoothstep(0.4f, 0.6f, abs(sin(uvPI.y + UNITY_HALF_PI * 0.5f))); // 位相を45度ずらすが、微調整が難しい
                col.rgb *= lerp(1.0f, scanline, _Scanline);

                return float4(col, 1);
            }
            ENDCG
        }
    }
}
