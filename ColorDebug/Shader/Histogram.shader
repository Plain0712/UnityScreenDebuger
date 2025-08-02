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
                    for (uint i = 0; i < HISTOGRAM_BINS; i++)
                    {
                        maxValue = max(maxValue, _HistogramBuffer[i]);
                    }
                    return float(max(maxValue, 1u));
                }

                Varyings Vert(Attributes v)
                {
                    Varyings o;
                    // 명시적으로 full-screen quad 좌표 설정
                    o.vertex = float4(v.vertex.xy * 2.0 - 1.0, 0.0, 1.0);
                    o.texcoord = v.texcoord;
                    
                    // D3D/OpenGL 호환성을 위한 Y 좌표 수정
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
                    
                    // 히스토그램 값을 0-1 범위로 정규화
                    float v1 = float(_HistogramBuffer[index]) / maxHistogramValue;
                    float v2 = float(_HistogramBuffer[min(index + 1, kBinsMinusOne)]) / maxHistogramValue;
                    float normalizedValue = v1 * (1.0 - delta) + v2 * delta;
                    
                    // 증폭 적용
                    normalizedValue *= _AmplificationFactor;
                    
                    // 로그 스케일 적용 (선택적)
                    if (_UseLogScale > 0.5)
                    {
                        normalizedValue = log(normalizedValue * 9.0 + 1.0) / log(10.0);
                    }
                    
                    // 감마 보정으로 낮은 값들 더 잘 보이게
                    normalizedValue = pow(saturate(normalizedValue), 0.7);
                    
                    // 현재 픽셀의 Y 좌표를 0-1 범위로 변환 (아래에서 위로)
                    float currentY = 1.0 - i.texcoord.y;
                    
                    // 히스토그램 바의 높이와 비교
                    float3 color = (0.0).xxx;
                    float fill = step(currentY, normalizedValue);
                    color = lerp(color, (1.0).xxx, fill);
                    
                    return float4(color, 1.0);
                }

            ENDHLSL
        }
    }
}