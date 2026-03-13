Shader "Voxel/DebugChunkBounds"
{
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Overlay" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #include "UnityCG.cginc"

            struct ChunkData {
                float4 position;
                uint rootNodeIndex;
                int currentLOD;
                uint pad1;
                uint pad2;
            };

            StructuredBuffer<ChunkData> _ChunkMap;
            float4 _ChunkSize; 

            struct v2f {
                float4 pos : SV_POSITION;
                float4 color : COLOR;
            };

            static const float3 cube[24] = {
                float3(0,0,0), float3(1,0,0), float3(1,0,0), float3(1,0,1),
                float3(1,0,1), float3(0,0,1), float3(0,0,1), float3(0,0,0),
                float3(0,1,0), float3(1,1,0), float3(1,1,0), float3(1,1,1),
                float3(1,1,1), float3(0,1,1), float3(0,1,1), float3(0,1,0),
                float3(0,0,0), float3(0,1,0), float3(1,0,0), float3(1,1,0),
                float3(1,0,1), float3(1,1,1), float3(0,0,1), float3(0,1,1)
            };

            v2f vert (uint vid : SV_VertexID, uint iid : SV_InstanceID)
            {
                v2f o;
                ChunkData chunk = _ChunkMap[iid];
                
                if (chunk.rootNodeIndex == 0xFFFFFFFF) {
                    o.pos = float4(0,0,0,0);
                    return o;
                }

                float scale = _ChunkSize.x * _ChunkSize.w; 
                float3 localPos = cube[vid] * scale;
                float3 worldPos = chunk.position.xyz + localPos;

                o.pos = mul(UNITY_MATRIX_VP, float4(worldPos, 1.0));

                if (chunk.currentLOD == 1) o.color = float4(0.2, 1.0, 0.2, 0.8);      
                else if (chunk.currentLOD == 2) o.color = float4(1.0, 1.0, 0.2, 0.8); 
                else if (chunk.currentLOD == 4) o.color = float4(1.0, 0.5, 0.0, 0.6); 
                else o.color = float4(1.0, 0.2, 0.2, 0.4);                            

                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                return i.color;
            }
            ENDCG
        }
    }
}