Shader "Unlit/BillboardGrass"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Cull Off
        ZWrite On

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma target 4.5

            #include "UnityCG.cginc"

            struct VertexData
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            struct GrassData {
                float4 position;
                float2 offset;
                uint placePosition;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _Scale;
            StructuredBuffer<GrassData> positionBuffer;

            v2f vert (VertexData v, uint instanceID : SV_INSTANCEID)
            {
                v2f o;

                if (positionBuffer[instanceID].placePosition > 0) {
                    float3 localPosition = v.vertex.xyz;
                    
                    float4 grassPosition = positionBuffer[instanceID].position;

                    localPosition.y *= v.uv.y * (0.5f + grassPosition.w);

                    float4 worldPosition = float4(grassPosition.xyz + localPosition, 1.0f);

                    o.vertex = UnityObjectToClipPos(worldPosition);
                    o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                } else {
                    o.vertex = 0.0f;
                }

                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv);
                clip(-(0.5 - col.a));

                return col;
            }
            ENDCG
        }
    }
}
