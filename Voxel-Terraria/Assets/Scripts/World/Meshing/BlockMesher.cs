using Unity.Mathematics;
using VoxelTerraria.World;          // ChunkData, Voxel, WorldSettings
using VoxelTerraria.World.SDF;      // SdfRuntime, CombinedTerrainSdf

namespace VoxelTerraria.World.Meshing
{
    /// <summary>
    /// Naive block-based mesher for debugging voxel and SDF correctness.
    /// Uses the padded voxel grid (voxelResolution = chunkSize + 1).
    /// 
    /// Convention:
    ///   Voxel.density > 0 => solid
    ///   Voxel.density <= 0 => air
    /// 
    /// This version:
    ///   • Avoids internal faces at chunk borders via SDF sampling
    ///   • Routes faces into submeshes by voxel.materialId
    ///   • Coastline override: stone/grass become sand near sea level
    /// </summary>
    public static class BlockMesher
    {
        public static MeshData BuildMesh(in ChunkData chunkData, WorldSettings settings)
        {
            int cells = chunkData.chunkSize;           // number of cells per axis
            int r     = chunkData.voxelResolution;     // = cells + 1
            float voxelSize = settings.voxelSize;

            // materialCount = 8 => IDs 0..7
            MeshData meshData = new MeshData(cells * cells * cells, 8);

            var voxels = chunkData.voxels;
            var coord  = chunkData.coord3;

            // Precompute chunk origin in world space
            float chunkWorld = voxelSize * cells;
            float3 chunkOrigin = new float3(
                coord.x * chunkWorld,
                coord.y * chunkWorld,
                coord.z * chunkWorld
            );

            // Grab current SDF context (read-only)
            SdfContext ctx = SdfRuntime.Context;

            // Node index helper: matches ChunkData padded grid layout
            int IndexNode(int x, int y, int z)
            {
                return x + y * r + z * r * r;
            }

            // Is this CELL solid, using only this chunk's voxel data?
            // We consider the 8 corner nodes; if any corner is solid, the block is solid.
            bool IsCellSolidLocal(int cx, int cy, int cz)
            {
                if (cx < 0 || cy < 0 || cz < 0 ||
                    cx >= cells || cy >= cells || cz >= cells)
                {
                    // Out of local chunk range: handled by SDF path.
                    return false;
                }

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

            // Is this CELL solid, using local voxels when in-bounds,
            // and falling back to SDF when the cell is outside this chunk?
            bool IsCellSolidWithSdf(int cx, int cy, int cz)
            {
                // Inside this chunk → trust voxel data
                if (cx >= 0 && cy >= 0 && cz >= 0 &&
                    cx <  cells && cy <  cells && cz <  cells)
                {
                    return IsCellSolidLocal(cx, cy, cz);
                }

                // Outside this chunk → sample SDF at the cell center in world space
                if (ctx.chunkSize <= 0 || ctx.voxelSize <= 0f)
                {
                    // If context isn't initialized properly, be conservative:
                    // treat out-of-bounds as solid to avoid creating gaps.
                    return true;
                }

                float3 cellCenter = new float3(
                    chunkOrigin.x + (cx + 0.5f) * voxelSize,
                    chunkOrigin.y + (cy + 0.5f) * voxelSize,
                    chunkOrigin.z + (cz + 0.5f) * voxelSize
                );

                float sdf = CombinedTerrainSdf.Evaluate(cellCenter, ctx);
                return sdf < 0f;
            }

            bool IsCellAir(int cx, int cy, int cz) => !IsCellSolidWithSdf(cx, cy, cz);

            // Pick a materialId for this CELL by looking at its 8 corners.
            ushort GetCellMaterialIdLocal(int cx, int cy, int cz)
            {
                ushort mat = 1; // default grass if something weird

                if (cx < 0 || cy < 0 || cz < 0 ||
                    cx >= cells || cy >= cells || cz >= cells)
                {
                    return mat;
                }

                for (int dz = 0; dz <= 1; dz++)
                for (int dy = 0; dy <= 1; dy++)
                for (int dx = 0; dx <= 1; dx++)
                {
                    int nx = cx + dx;
                    int ny = cy + dy;
                    int nz = cz + dz;

                    int idx = IndexNode(nx, ny, nz);
                    Voxel v = voxels[idx];

                    if (v.density > 0)
                    {
                        mat = v.materialId;
                        break;
                    }
                }

                return mat;
            }

            // Main loop: walk each cell and emit faces where neighbor is air.
            for (int z = 0; z < cells; z++)
            {
                for (int y = 0; y < cells; y++)
                {
                    for (int x = 0; x < cells; x++)
                    {
                        if (!IsCellSolidLocal(x, y, z))
                            continue;

                        float vx = x * voxelSize;
                        float vy = y * voxelSize;
                        float vz = z * voxelSize;

                        float3 basePos = new float3(vx, vy, vz);
                        float s = voxelSize;

                        // Helper: material for this cell/face with coastline override
                        ushort MaterialForFace(float3 faceCenterLocal)
                        {
                            ushort matId = GetCellMaterialIdLocal(x, y, z);

                            if (matId == 0)
                                return 0;

                            float3 worldFaceCenter = chunkOrigin + faceCenterLocal;
                            float distFromSea = math.abs(worldFaceCenter.y - ctx.seaLevel);
                            float nearCoast = math.exp(-distFromSea * 0.6f);

                            // Near sea level, override to sand unless it's mountain stone
                            if (nearCoast > 0.35f && matId != 3)
                                matId = 4; // sand

                            return matId;
                        }

                        // +X face (right)
                        if (IsCellAir(x + 1, y, z))
                        {
                            float3 normal = new float3(1, 0, 0);

                            float3 v0 = basePos + new float3(s, 0, 0);
                            float3 v1 = basePos + new float3(s, s, 0);
                            float3 v2 = basePos + new float3(s, s, s);
                            float3 v3 = basePos + new float3(s, 0, s);

                            float3 faceCenterLocal = basePos + new float3(s, 0.5f * s, 0.5f * s);
                            ushort matId = MaterialForFace(faceCenterLocal);
                            if (matId != 0)
                                meshData.AddQuad(v0, v1, v2, v3, normal, matId);
                        }

                        // -X face (left)
                        if (IsCellAir(x - 1, y, z))
                        {
                            float3 normal = new float3(-1, 0, 0);

                            float3 v0 = basePos + new float3(0, 0, s);
                            float3 v1 = basePos + new float3(0, s, s);
                            float3 v2 = basePos + new float3(0, s, 0);
                            float3 v3 = basePos + new float3(0, 0, 0);

                            float3 faceCenterLocal = basePos + new float3(0f, 0.5f * s, 0.5f * s);
                            ushort matId = MaterialForFace(faceCenterLocal);
                            if (matId != 0)
                                meshData.AddQuad(v0, v1, v2, v3, normal, matId);
                        }

                        // +Y face (top)
                        if (IsCellAir(x, y + 1, z))
                        {
                            float3 normal = new float3(0, 1, 0);

                            float3 v0 = basePos + new float3(0, s, 0);
                            float3 v1 = basePos + new float3(0, s, s);
                            float3 v2 = basePos + new float3(s, s, s);
                            float3 v3 = basePos + new float3(s, s, 0);

                            float3 faceCenterLocal = basePos + new float3(0.5f * s, s, 0.5f * s);
                            ushort matId = MaterialForFace(faceCenterLocal);
                            if (matId != 0)
                                meshData.AddQuad(v0, v1, v2, v3, normal, matId);
                        }

                        // -Y face (bottom)
                        if (IsCellAir(x, y - 1, z))
                        {
                            float3 normal = new float3(0, -1, 0);

                            float3 v0 = basePos + new float3(0, 0, s);
                            float3 v1 = basePos + new float3(0, 0, 0);
                            float3 v2 = basePos + new float3(s, 0, 0);
                            float3 v3 = basePos + new float3(s, 0, s);

                            float3 faceCenterLocal = basePos + new float3(0.5f * s, 0f, 0.5f * s);
                            ushort matId = MaterialForFace(faceCenterLocal);
                            if (matId != 0)
                                meshData.AddQuad(v0, v1, v2, v3, normal, matId);
                        }

                        // +Z face (forward)
                        if (IsCellAir(x, y, z + 1))
                        {
                            float3 normal = new float3(0, 0, 1);

                            float3 v0 = basePos + new float3(s, 0, s);
                            float3 v1 = basePos + new float3(s, s, s);
                            float3 v2 = basePos + new float3(0, s, s);
                            float3 v3 = basePos + new float3(0, 0, s);

                            float3 faceCenterLocal = basePos + new float3(0.5f * s, 0.5f * s, s);
                            ushort matId = MaterialForFace(faceCenterLocal);
                            if (matId != 0)
                                meshData.AddQuad(v0, v1, v2, v3, normal, matId);
                        }

                        // -Z face (back)
                        if (IsCellAir(x, y, z - 1))
                        {
                            float3 normal = new float3(0, 0, -1);

                            float3 v0 = basePos + new float3(0, 0, 0);
                            float3 v1 = basePos + new float3(0, s, 0);
                            float3 v2 = basePos + new float3(s, s, 0);
                            float3 v3 = basePos + new float3(s, 0, 0);

                            float3 faceCenterLocal = basePos + new float3(0.5f * s, 0.5f * s, 0f);
                            ushort matId = MaterialForFace(faceCenterLocal);
                            if (matId != 0)
                                meshData.AddQuad(v0, v1, v2, v3, normal, matId);
                        }
                    }
                }
            }

            return meshData;
        }
    }
}
