Shader "Hidden/SaliencyMap"
{
    Properties 
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Normalize ("Normalize", Float) = 0.0
        _MinMax ("MinMax", Vector) = (0,1,0,0)
    }
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
            float4 _MainTex_ST;
            float _Normalize;
            float4 _MinMax;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            // 블루-그린-레드 히트맵 컬러 (3단계 그라데이션)
            float3 HeatmapColor(float t)
            {
                t = saturate(t);
                
                // 3단계 히트맵: 파랑 -> 녹색 -> 빨강
                if (t < 0.5)  // 파랑 -> 녹색
                {
                    float factor = t / 0.5;
                    return lerp(float3(0.0, 0.0, 1.0), float3(0.0, 1.0, 0.0), factor);
                }
                else  // 녹색 -> 빨강
                {
                    float factor = (t - 0.5) / 0.5;
                    return lerp(float3(0.0, 1.0, 0.0), float3(1.0, 0.0, 0.0), factor);
                }
            }

            // 대안: Turbo 컬러맵 (더 부드러운 표현)
            float3 TurboColormap(float t)
            {
                t = saturate(t);
                
                const float4 kRedVec4   = float4(0.13572138, 4.61539260, -42.66032258, 132.13108234);
                const float4 kGreenVec4 = float4(0.09140261, 2.19418839, 4.84296658, -14.18503333);
                const float4 kBlueVec4  = float4(0.10667330, 12.64194608, -60.58204836, 110.36276771);
                const float2 kRedVec2   = float2(-152.94239396, 59.28637943);
                const float2 kGreenVec2 = float2(4.27729857, 2.82956604);
                const float2 kBlueVec2  = float2(-89.90310912, 27.34824973);

                float4 v4 = float4(1.0, t, t * t, t * t * t);
                float2 v2 = v4.zw * v4.y;

                return saturate(float3(
                    dot(v4, kRedVec4)   + dot(v2, kRedVec2),
                    dot(v4, kGreenVec4) + dot(v2, kGreenVec2),
                    dot(v4, kBlueVec4)  + dot(v2, kBlueVec2)
                ));
            }

            float4 frag(v2f i) : SV_Target
            {
                // R 채널에서 saliency 값 읽기
                float saliency = tex2D(_MainTex, i.uv).r;
                
                if (_Normalize > 0.5)
                {
                    float minVal = _MinMax.x;
                    float maxVal = _MinMax.y;
                    if (maxVal - minVal > 0.0001)
                    {
                        saliency = (saliency - minVal) / (maxVal - minVal);
                        
                        // 오토 노멀라이즈 후 중간값 보존하는 대비 강화
                        saliency = pow(saliency, 1.2); // 중간값도 보이는 적당한 대비
                    }
                    else
                    {
                        saliency = 0.0;
                    }
                }
                else
                {
                    // 기본 대비 강화만 적용
                    saliency = pow(saliency, 1.8);
                } 
                
                // 히트맵 색상 적용
                float3 color = HeatmapColor(saliency);
                
                // 대안: 더 부드러운 Turbo 컬러맵 사용
                //float3 color = TurboColormap(saliency);
                
                return float4(color, 1.0);
            }
            ENDHLSL
        }
    }
}