using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using VoxelTerraria.World;
using VoxelTerraria.World.SDF;

namespace VoxelTerraria.World.Generation
{
    public static class VoxelGenerator
    {
        private const float DensityScale = 64f;

        [BurstCompile]
        private struct GenerateVoxelsJob : IJobParallelFor
        {
            public NativeArray<Voxel> voxels;

            [ReadOnly] public ChunkCoord3 coord;
            [ReadOnly] public int voxelResolution;
            [ReadOnly] public float voxelSize;
            [ReadOnly] public float3 chunkOrigin;
            [ReadOnly] public SdfContext ctx;

            public void Execute(int index)
            {
                int res = voxelResolution;

                int z = index / (res * res);
                int rem = index - z * res * res;
                int y = rem / res;
                int x = rem - y * res;

                float3 worldPos = new float3(
                    chunkOrigin.x + x * voxelSize,
                    chunkOrigin.y + y * voxelSize,
                    chunkOrigin.z + z * voxelSize
                );

                float sdf = CombinedTerrainSdf.Evaluate(worldPos, ref ctx);

                short density = (short)math.clamp(-sdf * DensityScale, short.MinValue, short.MaxValue);

                ushort materialId = MaterialSelector.SelectMaterialId(worldPos, sdf, ctx);

                voxels[index] = new Voxel(density, materialId);
            }
        }

        public static void GenerateChunkVoxels(ref ChunkData chunkData, in SdfContext ctx, WorldSettings settings)
        {
            int voxRes = chunkData.voxelResolution;
            ChunkCoord3 coord = chunkData.coord3;

            float voxelSize = settings.voxelSize;
            float3 origin = WorldCoordUtils.ChunkOriginWorld(coord, settings);
            float chunkExtent = voxRes * voxelSize;
            float3 chunkMin = origin;
            float3 chunkMax = origin + new float3(chunkExtent);

            // ----------------------------------------------------------------
            // SPATIAL FILTERING (Pre-pass)
            // ----------------------------------------------------------------
            // Filter features that overlap with this chunk's AABB.
            // This significantly reduces the inner loop count in Evaluate().
            
            NativeList<Feature> filteredFeatures = new NativeList<Feature>(Allocator.TempJob);

            // We need to access featureBounds. If it's not created (old path?), fallback to all features.
            if (ctx.featureBounds.IsCreated)
            {
                for (int i = 0; i < ctx.featureCount; i++)
                {
                    FeatureAabb bounds = ctx.featureBounds[i];
                    
                    // If bounds are invalid, we assume global/infinite feature (keep it)
                    // If valid, check intersection
                    if (!bounds.valid || AabbOverlap(chunkMin, chunkMax, bounds.min, bounds.max))
                    {
                        filteredFeatures.Add(ctx.features[i]);
                    }
                }
            }
            else
            {
                // Fallback: copy all
                filteredFeatures.AddRange(ctx.features);
            }

            // Create a job-specific context with the filtered list
            SdfContext jobCtx = ctx;
            jobCtx.features = filteredFeatures.AsArray();
            jobCtx.featureCount = filteredFeatures.Length;

            // Debug logging to verify optimization (Optional: remove later)
            // if (filteredFeatures.Length < ctx.featureCount)
            // {
            //     UnityEngine.Debug.Log($"[VoxelGenerator] Chunk {coord}: Filtered features {ctx.featureCount} -> {filteredFeatures.Length}");
            // }

            var job = new GenerateVoxelsJob
            {
                voxels          = chunkData.voxels,
                coord           = coord,
                voxelResolution = voxRes,
                voxelSize       = voxelSize,
                chunkOrigin     = origin,
                ctx             = jobCtx
            };

            JobHandle handle = job.Schedule(chunkData.voxels.Length, 64);
            handle.Complete();

            filteredFeatures.Dispose();

            chunkData.isGenerated = true;
            chunkData.isDirty     = true;
        }

        private static bool AabbOverlap(float3 min1, float3 max1, float3 min2, float3 max2)
        {
            return (min1.x <= max2.x && max1.x >= min2.x) &&
                   (min1.y <= max2.y && max1.y >= min2.y) &&
                   (min1.z <= max2.z && max1.z >= min2.z);
        }
    }
}
