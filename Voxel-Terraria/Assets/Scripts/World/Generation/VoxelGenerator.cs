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

            var job = new GenerateVoxelsJob
            {
                voxels          = chunkData.voxels,
                coord           = coord,
                voxelResolution = voxRes,
                voxelSize       = voxelSize,
                chunkOrigin     = origin,
                ctx             = ctx
            };

            JobHandle handle = job.Schedule(chunkData.voxels.Length, 64);
            handle.Complete();

            chunkData.isGenerated = true;
            chunkData.isDirty     = true;
        }
    }
}
