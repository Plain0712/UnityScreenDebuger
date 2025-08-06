Shader "Hidden/Waveform"
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

            StructuredBuffer<uint4> _WaveformBuffer;
            // x: srcWidth, y: srcHeight, z: exposure
            float3 _Params;
            float _ShowRed, _ShowGreen, _ShowBlue;

            struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; };

            v2f vert(appdata_img v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = v.texcoord;
                return o;
            }

            float3 Tonemap(float3 x, float exposure)
            {
                const float a = 6.2, b = 0.5, c = 1.7, d = 0.06;
                x = max(x * exposure - 0.004, 0);
                x = (x * (a * x + b)) / (x * (a * x + c) + d);
                return x * x;
            }

            float4 frag(v2f i) : SV_Target
            {
                int w = (int)_Params.x, h = (int)_Params.y;
                int xi = clamp((int)(i.uv.x * w), 0, w - 1);
                int yi = clamp((int)(i.uv.y * h), 0, h - 1);

                // ─── 핵심 수정: x * height + y ───
                uint4 s = _WaveformBuffer[xi * h + yi];

                float3 finalColor = float3(0,0,0);
                if (_ShowRed > 0) finalColor += float3(1.4, 0.03, 0.02) * s.r;
                if (_ShowGreen > 0) finalColor += float3(0.02, 1.1, 0.05) * s.g;
                if (_ShowBlue > 0) finalColor += float3(0.00, 0.25, 1.5) * s.b;

                finalColor = Tonemap(finalColor, _Params.z);
                return float4(saturate(finalColor), 1);
            }
            ENDHLSL
        }
    }
}
