using Unity.Mathematics;
using VoxelTerraria.World;          // ChunkData, Voxel, WorldSettings
using VoxelTerraria.World.Meshing;  // MeshData

namespace VoxelTerraria.World.Meshing
{
    /// <summary>
    /// Naive block-based mesher for debugging voxel and SDF correctness.
    /// Uses the padded voxel grid (voxelResolution = chunkSize + 1).
    /// Treats each cell (between 8 corner samples) as a potential solid block.
    /// 
    /// Convention:
    ///   Voxel.density > 0 => solid
    ///   Voxel.density <= 0 => air
    /// </summary>
    public static class BlockMesher
    {
        public static MeshData BuildMesh(in ChunkData chunkData, WorldSettings settings)
        {
            int cells = chunkData.chunkSize;           // number of cells per axis
            int r     = chunkData.voxelResolution;     // = cells + 1
            float voxelSize = settings.voxelSize;

            MeshData meshData = new MeshData(cells * cells * cells);

            var voxels = chunkData.voxels;

            // Node index helper: matches ChunkData padded grid layout
            int IndexNode(int x, int y, int z)
            {
                return x + y * r + z * r * r;
            }

            // Returns true if the CELL at (cx,cy,cz) is solid.
            // We consider the 8 corner nodes; if any corner is solid, the block is solid.
            bool IsCellSolid(int cx, int cy, int cz)
            {
                if (cx < 0 || cy < 0 || cz < 0 ||
                    cx >= cells || cy >= cells || cz >= cells)
                    return false; // out-of-bounds = air

                int solidCount = 0;

                for (int dz = 0; dz <= 1; dz++)
                for (int dy = 0; dy <= 1; dy++)
                for (int dx = 0; dx <= 1; dx++)
                {
                    int nx = cx + dx;
                    int ny = cy + dy;
                    int nz = cz + dz;

                    int idx = IndexNode(nx, ny, nz);
                    if (voxels[idx].density > 0)
                        solidCount++;
                }

                return solidCount > 0;
            }

            bool IsCellAir(int cx, int cy, int cz) => !IsCellSolid(cx, cy, cz);

            // Main loop: walk each cell and emit faces where neighbor is air.
            for (int z = 0; z < cells; z++)
            {
                for (int y = 0; y < cells; y++)
                {
                    for (int x = 0; x < cells; x++)
                    {
                        if (!IsCellSolid(x, y, z))
                            continue;

                        float vx = x * voxelSize;
                        float vy = y * voxelSize;
                        float vz = z * voxelSize;

                        float3 basePos = new float3(vx, vy, vz);
                        float s = voxelSize;

                        // +X face (right)
                        if (IsCellAir(x + 1, y, z))
                        {
                            float3 normal = new float3(1, 0, 0);

                            float3 v0 = basePos + new float3(s, 0, 0);
                            float3 v1 = basePos + new float3(s, s, 0);
                            float3 v2 = basePos + new float3(s, s, s);
                            float3 v3 = basePos + new float3(s, 0, s);

                            meshData.AddQuad(v0, v1, v2, v3, normal);
                        }

                        // -X face (left)
                        if (IsCellAir(x - 1, y, z))
                        {
                            float3 normal = new float3(-1, 0, 0);

                            float3 v0 = basePos + new float3(0, 0, s);
                            float3 v1 = basePos + new float3(0, s, s);
                            float3 v2 = basePos + new float3(0, s, 0);
                            float3 v3 = basePos + new float3(0, 0, 0);

                            meshData.AddQuad(v0, v1, v2, v3, normal);
                        }

                        // +Y face (top)
                        if (IsCellAir(x, y + 1, z))
                        {
                            float3 normal = new float3(0, 1, 0);

                            float3 v0 = basePos + new float3(0, s, 0);
                            float3 v1 = basePos + new float3(0, s, s);
                            float3 v2 = basePos + new float3(s, s, s);
                            float3 v3 = basePos + new float3(s, s, 0);

                            meshData.AddQuad(v0, v1, v2, v3, normal);
                        }

                        // -Y face (bottom)
                        if (IsCellAir(x, y - 1, z))
                        {
                            float3 normal = new float3(0, -1, 0);

                            float3 v0 = basePos + new float3(0, 0, s);
                            float3 v1 = basePos + new float3(0, 0, 0);
                            float3 v2 = basePos + new float3(s, 0, 0);
                            float3 v3 = basePos + new float3(s, 0, s);

                            meshData.AddQuad(v0, v1, v2, v3, normal);
                        }

                        // +Z face (forward)
                        if (IsCellAir(x, y, z + 1))
                        {
                            float3 normal = new float3(0, 0, 1);

                            float3 v0 = basePos + new float3(s, 0, s);
                            float3 v1 = basePos + new float3(s, s, s);
                            float3 v2 = basePos + new float3(0, s, s);
                            float3 v3 = basePos + new float3(0, 0, s);

                            meshData.AddQuad(v0, v1, v2, v3, normal);
                        }

                        // -Z face (back)
                        if (IsCellAir(x, y, z - 1))
                        {
                            float3 normal = new float3(0, 0, -1);

                            float3 v0 = basePos + new float3(0, 0, 0);
                            float3 v1 = basePos + new float3(0, s, 0);
                            float3 v2 = basePos + new float3(s, s, 0);
                            float3 v3 = basePos + new float3(s, 0, 0);

                            meshData.AddQuad(v0, v1, v2, v3, normal);
                        }
                    }
                }
            }

            return meshData;
        }
    }
}
