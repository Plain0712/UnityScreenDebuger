Shader "Hidden/SaliencyMap"
{
    Properties 
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Exposure ("Exposure", Float) = 1.0
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
            float _Exposure;

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

            // ������ ��Ʈ�� �÷� - 5�ܰ� �׶��̼�
            float3 HeatmapColor(float t)
            {
                t = saturate(t);
                
                // 5�ܰ� ��Ʈ��: ���� -> �Ķ� -> û�� -> �ʷ� -> ��� -> ����
                if (t < 0.2)  // ���� -> �Ķ�
                {
                    float factor = t / 0.2;
                    return lerp(float3(0.0, 0.0, 0.0), float3(0.0, 0.0, 1.0), factor);
                }
                else if (t < 0.4)  // �Ķ� -> û��
                {
                    float factor = (t - 0.2) / 0.2;
                    return lerp(float3(0.0, 0.0, 1.0), float3(0.0, 1.0, 1.0), factor);
                }
                else if (t < 0.6)  // û�� -> �ʷ�
                {
                    float factor = (t - 0.4) / 0.2;
                    return lerp(float3(0.0, 1.0, 1.0), float3(0.0, 1.0, 0.0), factor);
                }
                else if (t < 0.8) // �ʷ� -> ���
                {
                    float factor = (t - 0.6) / 0.2;
                    return lerp(float3(0.0, 1.0, 0.0), float3(1.0, 1.0, 0.0), factor);
                }
                else // ��� -> ����
                {
                    float factor = (t - 0.8) / 0.2;
                    return lerp(float3(1.0, 1.0, 0.0), float3(1.0, 0.0, 0.0), factor);
                }
            }

            // ���: Turbo �÷��� (�� ������ ���)
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
                // R ä�ο��� saliency �� �б�
                float saliency = tex2D(_MainTex, i.uv).r;
                
                // Exposure ����
                saliency *= _Exposure;
                saliency = saturate(saliency);
                
                // ��� ��ȭ�ϱ� ���� � ����
                saliency = smoothstep(0.0, 1.0, saliency);
                
                // �߰� ���� �������� �ذ� ����
                saliency = pow(saliency, 0.7);
                
                // ��Ʈ�� ���� ����
                float3 color = HeatmapColor(saliency);
                
                // ���: �� ������ Turbo �÷��� ���
                // float3 color = TurboColormap(saliency);
                
                return float4(color, 1.0);
            }
            ENDHLSL
        }
    }
}