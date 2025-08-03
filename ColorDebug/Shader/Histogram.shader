Shader "Hidden/PostProcessing/Debug/Histogram"
{
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            HLSLPROGRAM

                #pragma vertex Vert
                #pragma fragment Frag

                #pragma exclude_renderers gles gles3 d3d11_9x
                #pragma target 4.5

                #define HISTOGRAM_BINS 256

                struct Attributes
                {
                    float4 vertex : POSITION;
                    float2 texcoord : TEXCOORD0;
                };

                struct Varyings
                {
                    float4 vertex : SV_POSITION;
                    float2 texcoord : TEXCOORD0;
                };

                StructuredBuffer<uint> _HistogramBuffer;
                float2 _Params; // x: width, y: height
                float _UseLogScale;
                float _AmplificationFactor;

                float FindMaxHistogramValue()
                {
                    uint maxValue = 0u;
                    // Check all 4 channels (RGB + Luminance) for the max value
                    for (uint i = 0; i < HISTOGRAM_BINS * 4; i++)
                    {
                        maxValue = max(maxValue, _HistogramBuffer[i]);
                    }
                    return float(max(maxValue, 1u));
                }

                Varyings Vert(Attributes v)
                {
                    Varyings o;
                    o.vertex = float4(v.vertex.xy * 2.0 - 1.0, 0.0, 1.0);
                    o.texcoord = v.texcoord;
                    
                    #if UNITY_UV_STARTS_AT_TOP
                    o.texcoord.y = 1.0 - o.texcoord.y;
                    #endif
                    
                    return o;
                }

                float4 Frag(Varyings i) : SV_Target
                {
                    float maxHistogramValue = FindMaxHistogramValue();
    
                    const float kBinsMinusOne = HISTOGRAM_BINS - 1.0;
                    float remapI = i.texcoord.x * kBinsMinusOne;
                    uint index = floor(remapI);
                    float delta = frac(remapI);

                    // Fetch and normalize R, G, B, Luminance values
                    float v1_r = float(_HistogramBuffer[index]) / maxHistogramValue;
                    float v2_r = float(_HistogramBuffer[min(index + 1, kBinsMinusOne)]) / maxHistogramValue;
                    float normalizedValue_r = lerp(v1_r, v2_r, delta);

                    float v1_g = float(_HistogramBuffer[index + HISTOGRAM_BINS]) / maxHistogramValue;
                    float v2_g = float(_HistogramBuffer[min(index + 1, kBinsMinusOne) + HISTOGRAM_BINS]) / maxHistogramValue;
                    float normalizedValue_g = lerp(v1_g, v2_g, delta);

                    float v1_b = float(_HistogramBuffer[index + HISTOGRAM_BINS * 2]) / maxHistogramValue;
                    float v2_b = float(_HistogramBuffer[min(index + 1, kBinsMinusOne) + HISTOGRAM_BINS * 2]) / maxHistogramValue;
                    float normalizedValue_b = lerp(v1_b, v2_b, delta);

                    float v1_l = float(_HistogramBuffer[index + HISTOGRAM_BINS * 3]) / maxHistogramValue;
                    float v2_l = float(_HistogramBuffer[min(index + 1, kBinsMinusOne) + HISTOGRAM_BINS * 3]) / maxHistogramValue;
                    float normalizedValue_l = lerp(v1_l, v2_l, delta);

                    // Amplify
                    normalizedValue_r *= _AmplificationFactor;
                    normalizedValue_g *= _AmplificationFactor;
                    normalizedValue_b *= _AmplificationFactor;
                    normalizedValue_l *= _AmplificationFactor;

                    // Log scale (optional)
                    if (_UseLogScale > 0.5)
                    {
                        normalizedValue_r = log(normalizedValue_r * 9.0 + 1.0) / log(10.0);
                        normalizedValue_g = log(normalizedValue_g * 9.0 + 1.0) / log(10.0);
                        normalizedValue_b = log(normalizedValue_b * 9.0 + 1.0) / log(10.0);
                        normalizedValue_l = log(normalizedValue_l * 9.0 + 1.0) / log(10.0);
                    }

                    // Power curve for better visibility
                    normalizedValue_r = pow(saturate(normalizedValue_r), 0.7);
                    normalizedValue_g = pow(saturate(normalizedValue_g), 0.7);
                    normalizedValue_b = pow(saturate(normalizedValue_b), 0.7);
                    normalizedValue_l = pow(saturate(normalizedValue_l), 0.7);

                    float currentY = 1.0 - i.texcoord.y;

                    // 각 채널의 강도를 직접 표시
                    float r = step(currentY, normalizedValue_r);
                    float g = step(currentY, normalizedValue_g);
                    float b = step(currentY, normalizedValue_b);
                    float l = step(currentY, normalizedValue_l);

                    // RGB 채널 표시 + 휘도를 흰색으로 오버레이
                    float3 color = float3(r, g, b);
                    
                    // 휘도는 흰색으로 표시하고 다른 채널과 겹치지 않도록 적절히 블렌딩
                    color = lerp(color, float3(1, 1, 1), l * 0.5);
                    
                    // 또는 휘도를 노란색으로 표시하려면:
                    // color += float3(1, 1, 0) * l * 0.7;
                    
                    return float4(saturate(color), 1.0);
                }

            ENDHLSL
        }
    }
}