using Unity.Mathematics;
using Unity.Collections;
using VoxelTerraria.World;
using VoxelTerraria.World.Generation; // WorldCoordUtils
using VoxelTerraria.World;    // (optional, but fine to have)

namespace VoxelTerraria.World.Generation
{
    /// <summary>
    /// Pure terrain voxel generation: SDF → density + material ID.
    /// Called from editor tools (WorldGenerationWindow, ChunkTestWindow)
    /// and later from runtime world streaming.
    /// </summary>
    public static class VoxelGenerator
    {
        // How strongly we scale SDF to a signed short.
        private const float DensityScale = 64f;

        /// <summary>
        /// Fills the given ChunkData with densities + material IDs using the global SDF context.
        /// Uses a padded grid: voxelResolution = chunkSize + 1 samples per axis.
        /// </summary>
        public static void GenerateChunkVoxels(ref ChunkData chunkData, in SdfContext ctx, WorldSettings settings)
        {
            int cells   = chunkData.chunkSize;
            int voxRes  = chunkData.voxelResolution; // usually cells + 1
            ChunkCoord3 coord = chunkData.coord3;

            for (int z = 0; z < voxRes; z++)
            {
                for (int y = 0; y < voxRes; y++)
                {
                    for (int x = 0; x < voxRes; x++)
                    {
                        var index = new VoxelCoord(x, y, z);

                        // Sample position in world-space for this voxel node
                        float3 worldPos = WorldCoordUtils.VoxelSampleWorld(coord, index, settings);

                        // Evaluate combined terrain SDF
                        float sdf = CombinedTerrainSdf.Evaluate(worldPos, ctx);

                        // Convert SDF→density: convention density > 0 = solid
                        short density = (short)math.clamp(-sdf * DensityScale, short.MinValue, short.MaxValue);

                        // Choose material based on biome + SDF/height
                        // ushort materialId = TerrainMaterialLibrary.SelectMaterialId(worldPos, sdf, ctx);
                        ushort materialId = MaterialSelector.SelectMaterialId(worldPos, sdf, ctx);


                        var voxel = new Voxel(density, materialId);
                        chunkData.Set(x, y, z, voxel);
                    }
                }
            }

            chunkData.isGenerated = true;
            chunkData.isDirty     = true;
        }
    }
}
