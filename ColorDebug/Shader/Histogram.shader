Shader "Hidden/PostProcessing/Debug/Histogram"
{
    HLSLINCLUDE

        #pragma exclude_renderers gles gles3 d3d11_9x
        #pragma target 4.5

        #if SHADER_API_GLES3
            #define HISTOGRAM_BINS 128
        #else
            #define HISTOGRAM_BINS 256
        #endif

        struct Attributes
        {
            float4 vertex : POSITION;
            float2 texcoord : TEXCOORD0;
        };

        struct VaryingsHistogram
        {
            float4 vertex : SV_POSITION;
            float2 texcoord : TEXCOORD0;
        };

        StructuredBuffer<uint> _HistogramBuffer;
        float2 _Params; // x: width, y: height

        float FindMaxHistogramValue()
        {
            uint maxValue = 0u;

            UNITY_UNROLL
            for (uint i = 0; i < HISTOGRAM_BINS; i++)
            {
                uint h = _HistogramBuffer[i];
                maxValue = max(maxValue, h);
            }

            return float(max(maxValue, 1u));
        }

        VaryingsHistogram Vert(Attributes v)
        {
            VaryingsHistogram o;
            o.vertex = v.vertex;
            o.texcoord = v.texcoord;
            return o;
        }

        float4 Frag(VaryingsHistogram i) : SV_Target
        {
            float maxValue = _Params.y / FindMaxHistogramValue();

            const float kBinsMinusOne = HISTOGRAM_BINS - 1.0;
            float remapI = i.texcoord.x * kBinsMinusOne;
            uint index = floor(remapI);
            float delta = frac(remapI);
            float v1 = float(_HistogramBuffer[index]) * maxValue;
            float v2 = float(_HistogramBuffer[min(index + 1, kBinsMinusOne)]) * maxValue;
            float h = v1 * (1.0 - delta) + v2 * delta;
            uint y = (uint)round(i.texcoord.y * _Params.y);

            float3 color = (0.0).xxx;
            float fill = step(y, h);
            color = lerp(color, (1.0).xxx, fill);
            return float4(color, 1.0);
        }

    ENDHLSL

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            HLSLPROGRAM

                #pragma vertex Vert
                #pragma fragment Frag

            ENDHLSL
        }
    }
}
