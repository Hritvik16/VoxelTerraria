Shader "VoxelEngine/Rasterizer" {
    Properties { }
    SubShader {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        
        // PASS 1: The Main Camera Pass
        Pass {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct VertexData { float3 position; uint packedData; };
            StructuredBuffer<VertexData> _VertexBuffer;

            struct v2f {
                float4 pos : SV_POSITION;
                float3 normal : TEXCOORD0;
            };

            v2f vert (uint vertexID : SV_VertexID) {
                v2f o;
                VertexData v = _VertexBuffer[vertexID];
                o.pos = TransformObjectToHClip(v.position);
                
                // Decode Face Direction into World Normals
                float3 n = float3(0,0,0);
                if (v.packedData == 0) n = float3(0,1,0);
                else if (v.packedData == 1) n = float3(0,-1,0);
                else if (v.packedData == 2) n = float3(0,0,1);
                else if (v.packedData == 3) n = float3(0,0,-1);
                else if (v.packedData == 4) n = float3(1,0,0);
                else if (v.packedData == 5) n = float3(-1,0,0);
                o.normal = n;

                return o;
            }

            half4 frag (v2f i) : SV_Target {
                Light mainLight = GetMainLight();
                float diff = saturate(dot(i.normal, mainLight.direction));
                return half4(float3(0.5, 0.5, 0.5) * (diff * 0.8 + 0.2), 1.0); // Simple Grey Blocks
            }
            ENDHLSL
        }

        // PASS 2: Allow the voxels to cast shadows!
        Pass {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            
            struct VertexData { float3 position; uint packedData; };
            StructuredBuffer<VertexData> _VertexBuffer;
            
            float4 vert (uint vertexID : SV_VertexID) : SV_POSITION {
                return TransformObjectToHClip(_VertexBuffer[vertexID].position);
            }
            half4 frag () : SV_Target { return 0; }
            ENDHLSL
        }
    }
}