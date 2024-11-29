Shader "Unlit/GridImage"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha

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
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            sampler2D _OverlayTex;
            float4 _SourceRect;
            float4 _TargetRect;
            float2 _OverlayScale;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.vertex.xy *= _TargetRect.zw;
                o.vertex.xy += _TargetRect.xy * 2 - 1 + _TargetRect.zw;
                o.uv = v.uv * _SourceRect.zw + _SourceRect.xy;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float4 pixel = tex2D(_MainTex, i.uv);
                float overlay = tex2D(_OverlayTex, i.uv * _OverlayScale).r;
                //pixel.rgb += (0.25 - (1 - overlay) * (1 - overlay)) * 0.5;
                //pixel.rgb += (overlay - 0.5) * 0.5;
                //pixel.rgb = lerp(pixel.rgb, round(overlay), abs(overlay - 0.5));
                if (overlay >= 0.5) {
                    pixel.rgb += (overlay - 0.5) * 0.35;
                }
                else {
                    pixel.rgb *= overlay + 0.5;
                }
                return pixel;
            }
            ENDCG
        }
    }
}
