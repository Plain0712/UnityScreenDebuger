Shader "Hidden/Waveform"
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
            StructuredBuffer<uint4> _WaveformBuffer;     // width × height 개 - RGBA 카운트
            float4 _Params;                              // (srcW, srcH, texH, _)
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

            float4 frag (v2f i) : SV_Target
            {
                int srcW = (int)_Params.x;
                int srcH = (int)_Params.y;

                int xi = clamp((int)(i.uv.x * srcW), 0, srcW - 1);
                int yi = clamp((int)(i.uv.y * srcH), 0, srcH - 1);
                uint4 sample = _WaveformBuffer[yi * srcW + xi];

                // 간단한 스케일링 – 필요하면 조정
                float3 rgb = float3(sample.x, sample.y, sample.z) * 0.0005;
                rgb = saturate(rgb);

                float  a = max(rgb.r, max(rgb.g, rgb.b));  // 알파 = 최고 밝기
                return float4(rgb, a);
            }
            ENDHLSL
        }
    }
}
