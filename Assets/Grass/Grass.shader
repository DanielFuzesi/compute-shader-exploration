Shader "Unlit/Grass"
{
    Properties
    {
        _Albedo1 ("Albedo 1", Color) = (1, 1, 1)
        _Albedo2 ("Albedo 2", Color) = (1, 1, 1)
        _AOColor ("Ambient Occlusion", Color) = (1, 1, 1)
        _TipColor ("Tip Color", Color) = (1, 1, 1)
        _Scale ("Scale", Range(0.0, 10.0)) = 0.0
        _Droop ("Droop", Range(0.0, 10.0)) = 0.0
        _FogColor ("Fog Color", Color) = (1, 1, 1)
        _FogDensity ("Fog Density", Range(0.0, 1.0)) = 0.0
        _FogOffset ("Fog Offset", Range(0.0, 10.0)) = 0.0
    }
    SubShader
    {
        Cull Off
        ZWrite On

        Tags {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
        }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma target 4.5

            #include "UnityPBSLighting.cginc"
            #include "AutoLight.cginc"
            #include "UnityCG.cginc"
            #include "Random.cginc"

            struct VertexData
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float4 worldPos : TEXCOORD1;
                float noiseVal : TEXCOORD2;
                float3 chunkNum : TEXCOORD3;
            };

            struct GrassData {
                float4 position;
                float2 uv;
                uint placePosition;
            };

            float4 _Albedo1, _Albedo2, _AOColor, _TipColor, _FogColor;
            float _Scale, _Droop, _FogDensity, _FogOffset;
            StructuredBuffer<GrassData> positionBuffer;

            float4 RotateAroundYInDegrees (float4 vertex, float degrees) {
                float alpha = degrees * UNITY_PI / 180.0;
                float sina, cosa;
                sincos(alpha, sina, cosa);
                float2x2 m = float2x2(cosa, -sina, sina, cosa);
                return float4(mul(m, vertex.xz), vertex.yw).xzyw;
            }

            float4 RotateAroundXInDegrees (float4 vertex, float degrees) {
                float alpha = degrees * UNITY_PI / 180.0;
                float sina, cosa;
                sincos(alpha, sina, cosa);
                float2x2 m = float2x2(cosa, -sina, sina, cosa);
                return float4(mul(m, vertex.yz), vertex.xw).zxyw;
            }

            v2f vert (VertexData v, uint instanceID : SV_INSTANCEID)
            {
                v2f o;

                // Check if grass should be rendered
                if (positionBuffer[instanceID].placePosition > 0) {
                    // Get local position of the vertices and manipulate grass height
                    float4 localPosition = RotateAroundXInDegrees(v.vertex, 90.0f);
                    localPosition = RotateAroundYInDegrees(v.vertex, instanceID * randValue(180.0f));
                    localPosition.y += _Scale * v.uv.y * v.uv.y * v.uv.y;
                    localPosition.xz += _Droop * lerp(0.5f, 1.0f, instanceID) * (v.uv.y * v.uv.y * _Scale);

                    // Get the grass position from the buffer
                    float4 grassPosition = positionBuffer[instanceID].position;

                    // Calculate world position
                    float4 worldPosition = float4(grassPosition.xyz + localPosition, 1.0f);

                    // Set vertex position and uv's
                    o.vertex = UnityObjectToClipPos(worldPosition);
                    o.uv = v.uv;
                    o.worldPos = worldPosition;

                } else {
                    o.vertex = 0.0f;
                }

                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float4 col = lerp(_Albedo1, _Albedo2, i.uv.y);
                float3 lightDir = _WorldSpaceLightPos0.xyz;
                float ndotl = DotClamped(lightDir, normalize(float3(0, 1, 0)));

                float4 ao = lerp(_AOColor, 1.0f, i.uv.y);
                float4 tip = lerp(0.0f, _TipColor, i.uv.y * i.uv.y * (1.0f + _Scale));

                float4 grassColor = (col + tip) * ndotl * ao;

                /* Fog */
                float viewDistance = length(_WorldSpaceCameraPos - i.worldPos);
                float fogFactor = (_FogDensity / sqrt(log(2))) * (max(0.0f, viewDistance - _FogOffset));
                fogFactor = exp2(-fogFactor * fogFactor);

                return lerp(_FogColor, grassColor, fogFactor);
            }
            ENDCG
        }
    }
}
