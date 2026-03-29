using Unity.Mathematics;
using Unity.Collections;
using VoxelEngine.World;

namespace VoxelEngine.Generation
{
    public static class CaveCarverWorker
    {
        public static void ApplyCavesAndTunnels_1Bit(
            ref NativeArray<uint> denseChunkPool, 
            uint denseBase, 
            float cx, float cy, float cz, float layerScale, 
            NativeArray<CavernNode> caverns, int cavernCount, 
            NativeArray<TunnelSpline> tunnels, int tunnelCount) 
        {
            float cSize = 32f * layerScale;
            float3 chunkCenter = new float3(cx + cSize * 0.5f, cy + cSize * 0.5f, cz + cSize * 0.5f);

            if (cavernCount > 0) {
                for (int c = 0; c < cavernCount; c++) {
                    CavernNode node = caverns[c];
                    float3 cPos = new float3(node.position.x, node.position.y, node.position.z);
                    float expandedRad = node.radius + (cSize * 0.866f);
                    if (math.lengthsq(chunkCenter - cPos) > expandedRad * expandedRad) continue;

                    float rSq = node.radius * node.radius;
                    for (int x = 0; x < 32; x++) {
                        for (int y = 0; y < 32; y++) {
                            for (int z = 0; z < 32; z++) {
                                float3 wPos = new float3(cx + x * layerScale, cy + y * layerScale, cz + z * layerScale);
                                if (math.lengthsq(wPos - cPos) < rSq) {
                                    int flatIdx = x + (y << 5) + (z << 10);
                                    denseChunkPool[(int)denseBase + (flatIdx >> 5)] &= ~(1u << (flatIdx & 31)); // Carve bit
                                }
                            }
                        }
                    }
                }
            }

            if (tunnelCount > 0) {
                for (int t = 0; t < tunnelCount; t++) {
                    TunnelSpline spline = tunnels[t];
                    float3 pA = new float3(spline.startPoint.x, spline.startPoint.y, spline.startPoint.z);
                    float3 pB = new float3(spline.endPoint.x, spline.endPoint.y, spline.endPoint.z);
                    float3 lineVec = pB - pA;
                    float lineLenSq = math.lengthsq(lineVec);
                    float tVal = math.clamp(math.dot(chunkCenter - pA, lineVec) / lineLenSq, 0f, 1f);
                    float3 closestPoint = pA + tVal * lineVec;

                    float expandedRad = spline.radius + (cSize * 0.866f) + 3f; 
                    if (math.lengthsq(chunkCenter - closestPoint) > expandedRad * expandedRad) continue;

                    for (int x = 0; x < 32; x++) {
                        for (int y = 0; y < 32; y++) {
                            for (int z = 0; z < 32; z++) {
                                float3 wPos = new float3(cx + x * layerScale, cy + y * layerScale, cz + z * layerScale);
                                float vtVal = math.clamp(math.dot(wPos - pA, lineVec) / lineLenSq, 0f, 1f);
                                float distSq = math.lengthsq(wPos - (pA + vtVal * lineVec));

                                if (distSq < (spline.radius - 2f) * (spline.radius - 2f)) {
                                    int flatIdx = x + (y << 5) + (z << 10);
                                    denseChunkPool[(int)denseBase + (flatIdx >> 5)] &= ~(1u << (flatIdx & 31));
                                }
                                else if (distSq < (spline.radius + 3f) * (spline.radius + 3f)) {
                                    float tunnelNoise = noise.snoise(new float2(vtVal * 50f, 0)) * spline.noiseIntensity * 3f;
                                    float dynamicRad = spline.radius + tunnelNoise;
                                    if (distSq < dynamicRad * dynamicRad) {
                                        int flatIdx = x + (y << 5) + (z << 10);
                                        denseChunkPool[(int)denseBase + (flatIdx >> 5)] &= ~(1u << (flatIdx & 31));
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        public static void ApplyCavesAndTunnels_32Bit(
            ref NativeArray<uint> denseChunkPool, 
            uint denseBase, 
            float cx, float cy, float cz, float layerScale, 
            NativeArray<CavernNode> caverns, int cavernCount, 
            NativeArray<TunnelSpline> tunnels, int tunnelCount) 
        {
            float cSize = 32f * layerScale;
            float3 chunkCenter = new float3(cx + cSize * 0.5f, cy + cSize * 0.5f, cz + cSize * 0.5f);

            if (cavernCount > 0) {
                for (int c = 0; c < cavernCount; c++) {
                    CavernNode node = caverns[c];
                    if (node.cavernType == 0) continue; // 32bit differs here

                    float3 cPos = new float3(node.position.x, node.position.y, node.position.z);
                    float expandedRad = node.radius + (cSize * 0.866f);
                    if (math.lengthsq(chunkCenter - cPos) > expandedRad * expandedRad) continue;

                    float rSq = node.radius * node.radius;
                    for (int x = 0; x < 32; x++) {
                        for (int y = 0; y < 32; y++) {
                            for (int z = 0; z < 32; z++) {
                                float3 wPos = new float3(cx + x * layerScale, cy + y * layerScale, cz + z * layerScale);
                                if (math.lengthsq(wPos - cPos) < rSq) {
                                    int flatIdx = x + (y << 5) + (z << 10);
                                    denseChunkPool[(int)denseBase + flatIdx] = 0; // 32bit Array access
                                }
                            }
                        }
                    }
                }
            }

            if (tunnelCount > 0) {
                for (int t = 0; t < tunnelCount; t++) {
                    TunnelSpline spline = tunnels[t];
                    float3 pA = new float3(spline.startPoint.x, spline.startPoint.y, spline.startPoint.z);
                    float3 pB = new float3(spline.endPoint.x, spline.endPoint.y, spline.endPoint.z);
                    float3 lineVec = pB - pA;
                    float lineLenSq = math.lengthsq(lineVec);
                    float tVal = math.clamp(math.dot(chunkCenter - pA, lineVec) / lineLenSq, 0f, 1f);
                    float3 closestPoint = pA + tVal * lineVec;

                    float expandedRad = spline.radius + (cSize * 0.866f) + 3f; 
                    if (math.lengthsq(chunkCenter - closestPoint) > expandedRad * expandedRad) continue;

                    for (int x = 0; x < 32; x++) {
                        for (int y = 0; y < 32; y++) {
                            for (int z = 0; z < 32; z++) {
                                float3 wPos = new float3(cx + x * layerScale, cy + y * layerScale, cz + z * layerScale);
                                float vtVal = math.clamp(math.dot(wPos - pA, lineVec) / lineLenSq, 0f, 1f);
                                float distSq = math.lengthsq(wPos - (pA + vtVal * lineVec));

                                if (distSq < (spline.radius - 2f) * (spline.radius - 2f)) {
                                    int flatIdx = x + (y << 5) + (z << 10);
                                    denseChunkPool[(int)denseBase + flatIdx] = 0;
                                }
                                else if (distSq < (spline.radius + 3f) * (spline.radius + 3f)) {
                                    float tunnelNoise = noise.snoise(new float2(vtVal * 50f, 0)) * spline.noiseIntensity * 3f;
                                    float dynamicRad = spline.radius + tunnelNoise;
                                    if (distSq < dynamicRad * dynamicRad) {
                                        int flatIdx = x + (y << 5) + (z << 10);
                                        denseChunkPool[(int)denseBase + flatIdx] = 0;
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
