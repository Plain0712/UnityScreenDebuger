Shader "Hidden/Vectorscope"
{
    Properties { }
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

            // ──────────────────────────────────────────────
            // buffers & uniforms – ColorAnalyzerTool.cs와 동일한 이름
            StructuredBuffer<uint> _VectorscopeBuffer;   // size × size 개
            float2 _Params;                              // (size, texSize)
            // ──────────────────────────────────────────────

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
            };

            v2f vert (appdata_img v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = v.texcoord.xy;
                return o;
            }

            // HSV→RGB 헬퍼
            float3 HSVtoRGB(float3 hsv)
            {
                float3 rgb = saturate(abs(frac(hsv.x * 6 + float3(0,4,2)) * 2 - 1));
                return hsv.z * lerp(float3(1,1,1), rgb, hsv.y);
            }

            float4 frag (v2f i) : SV_Target
            {
                int size = (int)_Params.x;

                int xi = clamp((int)(i.uv.x * size), 0, size - 1);
                int yi = clamp((int)(i.uv.y * size), 0, size - 1);
                uint sample = _VectorscopeBuffer[yi * size + xi];

                // 밝기 스케일 – 로그 압축
                float intensity = saturate(log2(sample + 1) * 0.20);

                // 위치 기반 색상(Hue) 계산
                float2 p = i.uv * 2 - 1;             // (-1~1) 범위
                float  hue = atan2(p.y, p.x) / (2 * UNITY_PI) + 0.5;
                float  sat = saturate(length(p));    // 중심에서 멀수록 채도 ↑

                float3 baseColor = HSVtoRGB(float3(hue, sat, 1.0));
                float3 outColor  = baseColor * intensity;

                return float4(outColor, intensity);
            }
            ENDHLSL
        }
    }
}
