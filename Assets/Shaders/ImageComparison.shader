Shader "Hidden/Pixelism/ImageComparison"
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
                o.screenPos = ComputeScreenPos(o.vertex);
                return o;
            }

            sampler2D _MainTex;
            sampler2D _SideTex;

            float _SplitLocation;
            int _Flip;
            int _Vertical;

            fixed4 frag (v2f i) : SV_Target
            {
                // cpu で色々求めておけるけれど、shader側で完結させるのを重視
                const bool flip = (int)_Flip != 0;
                const bool vertical = (int)_Vertical != 0;

                float splitLocation = saturate((flip ? -_SplitLocation : _SplitLocation) * 0.5f + 0.5f); // [-1,1] to [0,1]
                fixed4 left = tex2D(_MainTex, i.uv);
                fixed4 right = tex2D(_SideTex, i.uv);
                float split = vertical ? i.uv.y : i.uv.x;
                split = flip ? (1 - split) : split;
                fixed4 col = lerp(left, right, step(splitLocation, split));
                // split bar
                // xy=current pixel location, zw=split pixel location
                int4 loc = (int4)(float4(i.screenPos.xy, (flip ? (1 - splitLocation) : splitLocation).xx) * _ScreenParams.xyxy);
                int2 diff = abs(loc.xy - loc.zw);
                int spline = vertical ? diff.y : diff.x;
                col.rgb = lerp(col.rgb, 1 - col.rgb, step(spline, 0)); // 適当な色で線, 溶け込む箇所があってもそれほど重要ではないので
                return col;
            }
            ENDCG
        }
    }
}
