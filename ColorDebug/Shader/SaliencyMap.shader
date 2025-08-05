Shader "Hidden/SaliencyMap"
{
    Properties {}
    SubShader
    {
        Tags { "Queue"="Overlay" "RenderType"="Opaque" }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _Exposure; // optional multiplier

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
            };

            v2f vert(appdata_img v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = v.texcoord;
                return o;
            }

            // Heatmap 색상 그라디언트: 0 → 파랑 → 청록 → 초록 → 노랑 → 빨강
            float3 HeatmapColor(float t)
            {
                t = saturate(t);
                float3 c = float3(1.0, 1.0, 1.0);

                if (t < 0.25)      // Blue → Cyan
                    c = lerp(float3(0.0, 0.0, 1.0), float3(0.0, 1.0, 1.0), t / 0.25);
                else if (t < 0.5)  // Cyan → Green
                    c = lerp(float3(0.0, 1.0, 1.0), float3(0.0, 1.0, 0.0), (t - 0.25) / 0.25);
                else if (t < 0.75) // Green → Yellow
                    c = lerp(float3(0.0, 1.0, 0.0), float3(1.0, 1.0, 0.0), (t - 0.5) / 0.25);
                else               // Yellow → Red
                    c = lerp(float3(1.0, 1.0, 0.0), float3(1.0, 0.0, 0.0), (t - 0.75) / 0.25);

                return c;
            }

            float4 frag(v2f i) : SV_Target
            {
                float saliency = tex2D(_MainTex, i.uv).r;
                saliency *= _Exposure;

                float3 color = HeatmapColor(saliency);
                return float4(color, 1.0);
            }
            ENDHLSL
        }
    }
}
