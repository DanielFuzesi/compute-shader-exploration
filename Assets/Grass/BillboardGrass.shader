Shader "Unlit/BillboardGrass"
{
    Properties
    {
        // _WindStrength ("Wind Strength", Range(0.5, 50.0)) = 1
        // _CullingBias ("Cull Bias", Range(0.1, 1.0)) = 0.5
        // _LODCutoff ("LOD Cutoff", Range(10.0, 500.0)) = 100
        _MaskTex ("Mask", 2D) = "white" {}
        _Albedo1 ("Albedo 1", Color) = (1, 1, 1)
        _Albedo2 ("Albedo 2", Color) = (1, 1, 1)
        _AOColor ("Ambient Occlusion", Color) = (1, 1, 1)
        _TipColor ("Tip Color", Color) = (1, 1, 1)
        _Scale ("Scale", Range(-0.3, 10.0)) = 0.0
        _HeightOffset("Texture Height Offset", Range(1.0, 10)) = 1.0
        _Droop ("Droop", Range(0.0, 10.0)) = 0.0
        _FogColor ("Fog Color", Color) = (1, 1, 1)
        _FogDensity ("Fog Density", Range(0.0, 1.0)) = 0.0
        _FogOffset ("Fog Offset", Range(0.0, 10.0)) = 0.0
    }
    SubShader
    {
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
            #include "Rotation.cginc"

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
                float displacement;
                bool placePosition;
            };

            sampler2D _WindTex, _MaskTex;
            float4 _Albedo1, _Albedo2, _AOColor, _TipColor, _FogColor;
            float _Scale, _Droop, _FogDensity, _FogOffset, _HeightOffset;
            StructuredBuffer<GrassData> positionBuffer;

            int _ChunkNum;

            v2f vert (VertexData v, uint instanceID : SV_INSTANCEID)
            {
                v2f o;

                // Get the grass position from the buffer
                float4 grassPosition = positionBuffer[instanceID].position;

                float idHash = randValue(abs(grassPosition.x * 10000 + grassPosition.y * 100 + grassPosition.z * 0.05f + 2));
                idHash = randValue(idHash * 100000);

                float4 animationDirection = float4(0.0f, 0.0f, 1.0f, 0.0f);
                animationDirection = normalize(RotateAroundYInDegrees(animationDirection, idHash * 180.0f));

                // Get local position of the vertices and manipulate grass height
                float4 localPosition = RotateAroundYInDegrees(v.vertex, idHash * 180.0f);
                localPosition.y += _Scale * v.uv.y * v.uv.y * v.uv.y;
                localPosition.xz += _Droop * lerp(0.5f, 1.0f, idHash) * (v.uv.y * v.uv.y * _Scale) * animationDirection;

                float4 worldUV = float4(positionBuffer[instanceID].uv, 0, 0);

                float swayVariance = lerp(0.1, 0.3, idHash);
                float movement = v.uv.y * v.uv.y * (tex2Dlod(_WindTex, worldUV).r);
                movement *= swayVariance;
                
                localPosition.xz += movement;

                // Calculate world position
                float4 worldPosition = float4(grassPosition.xyz + localPosition, 1.0f);
                
                worldPosition.y -= positionBuffer[instanceID].displacement - 0.5f;
                worldPosition.y *= (_HeightOffset + positionBuffer[instanceID].position.w * lerp(0.8f, 1.0f, idHash));
                worldPosition.y += positionBuffer[instanceID].displacement;

                // Set vertex position and uv's
                o.vertex = UnityObjectToClipPos(worldPosition);
                o.uv = v.uv;
                o.noiseVal = tex2Dlod(_WindTex, worldUV).r;
                o.worldPos = worldPosition;
                o.chunkNum = float3(randValue(_ChunkNum * 20 + 1024), randValue(randValue(_ChunkNum) * 10 + 2048), randValue(_ChunkNum * 4 + 4096));

                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 mask = tex2D(_MaskTex, i.uv).a;
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

                fixed4 fogCol = lerp(_FogColor, grassColor, fogFactor);
                clip(-(0.5 - mask));

                return fogCol;
            }
            ENDCG
        }
    }
}
