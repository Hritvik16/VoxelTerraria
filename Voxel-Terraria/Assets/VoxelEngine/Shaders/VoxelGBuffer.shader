Shader "Hidden/VoxelGBuffer"
{
    SubShader
    {
        Pass
        {
            ZWrite On
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Varyings { 
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0; 
            };

            Texture2D _VoxelData; 
            SamplerState sampler_VoxelData; 

            Varyings vert(uint vertexID : SV_VertexID) {
                Varyings output;
                float x = (vertexID == 1) ? 3.0 : -1.0;
                float y = (vertexID == 2) ? 3.0 : -1.0;
                output.positionCS = float4(x, y, UNITY_NEAR_CLIP_VALUE, 1.0);
                output.uv = float2((x + 1.0) * 0.5, (y + 1.0) * 0.5);
                #if UNITY_UV_STARTS_AT_TOP
                output.uv.y = 1.0 - output.uv.y;
                #endif
                return output;
            }

            struct FragmentOutput {
                float4 color : SV_Target0;
                float depth  : SV_Depth;
            };

            FragmentOutput frag(Varyings input) : SV_Target {
                float4 data = _VoxelData.Sample(sampler_VoxelData, input.uv);
                float linearDepth = data.a;
                
                // If depth is 0, we hit the sky. Discard to let Unity skybox show.
                if (linearDepth <= 0.001f) discard; 

                FragmentOutput outData;
                outData.color = float4(data.rgb, 1.0);

                // Convert linear ray distance to Clip Space Depth
                // This is required for SSAO and Post-Processing to work!
                float3 viewPos = float3(0, 0, -linearDepth);
                float4 clipPos = mul(UNITY_MATRIX_P, float4(viewPos, 1.0));
                outData.depth = clipPos.z / clipPos.w;

                return outData;
            }
            ENDHLSL
        }
    }
}