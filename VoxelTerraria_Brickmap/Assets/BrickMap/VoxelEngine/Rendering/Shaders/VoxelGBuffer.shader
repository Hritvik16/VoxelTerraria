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
                // Use .GetDimensions() to handle High-DPI and Scale-2x perfectly
                float w, h;
                _VoxelData.GetDimensions(w, h);
                uint2 pixelPos = uint2(input.uv * float2(w, h));
                float4 data = _VoxelData.Load(uint3(pixelPos, 0));
                float linearDepth = data.a;
                
                // Stop discarding! Render the custom Raytraced Sky and push it to the far clipping plane
                if (linearDepth <= 0.001f) linearDepth = 10000.0f;

                FragmentOutput outData;
                outData.color = float4(data.rgb, 1.0);

                // Convert linear ray distance to Clip Space Depth
                float3 viewPos = float3(0, 0, -linearDepth);
                float4 clipPos = mul(UNITY_MATRIX_P, float4(viewPos, 1.0));
                outData.depth = clipPos.z / clipPos.w;

                return outData;
            }
            ENDHLSL
        }
    }
}