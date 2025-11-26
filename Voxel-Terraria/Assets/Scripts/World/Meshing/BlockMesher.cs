using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using VoxelTerraria.World;
using VoxelTerraria.World.SDF;

namespace VoxelTerraria.World.Meshing
{
    public static class BlockMesher
    {
        /// <summary>
        /// Burst-friendly core mesher.
        /// 
        /// - No WorldSettings, no SdfRuntime access.
        /// - All data passed in as blittable structs / NativeArray.
        /// - Suitable for calling from a Burst job.
        /// </summary>
        [BurstCompile]
        public static void BuildMeshCore(
            NativeArray<Voxel> voxels,
            ChunkCoord3 coord,
            int cells,
            int voxelResolution,
            float voxelSize,
            SdfContext ctx,
            ref MeshData meshData)
        {
            int r = voxelResolution;

            float chunkWorld = voxelSize * cells;
            float3 chunkOrigin = new float3(
                coord.x * chunkWorld,
                coord.y * chunkWorld,
                coord.z * chunkWorld
            );

            for (int z = 0; z < cells; z++)
            {
                for (int y = 0; y < cells; y++)
                {
                    for (int x = 0; x < cells; x++)
                    {
                        // Skip fully empty cells (no solid corners)
                        if (!IsCellSolidLocal(x, y, z, cells, r, voxels))
                            continue;

                        float vx = x * voxelSize;
                        float vy = y * voxelSize;
                        float vz = z * voxelSize;

                        float3 basePos = new float3(vx, vy, vz);
                        float s = voxelSize;

                        // +X face
                        if (IsCellAir(x + 1, y, z, cells, r, voxels, chunkOrigin, voxelSize, ref ctx))
                        {
                            float3 normal = new float3(1, 0, 0);

                            float3 v0 = basePos + new float3(s, 0, 0);
                            float3 v1 = basePos + new float3(s, s, 0);
                            float3 v2 = basePos + new float3(s, s, s);
                            float3 v3 = basePos + new float3(s, 0, s);

                            float3 faceCenterLocal = basePos + new float3(s, 0.5f * s, 0.5f * s);
                            ushort matId = MaterialForFace(
                                x, y, z,
                                cells, r, voxels,
                                chunkOrigin, faceCenterLocal,
                                ref ctx
                            );

                            if (matId != 0)
                                meshData.AddQuad(v0, v1, v2, v3, normal, matId);
                        }

                        // -X face
                        if (IsCellAir(x - 1, y, z, cells, r, voxels, chunkOrigin, voxelSize, ref ctx))
                        {
                            float3 normal = new float3(-1, 0, 0);

                            float3 v0 = basePos + new float3(0, 0, s);
                            float3 v1 = basePos + new float3(0, s, s);
                            float3 v2 = basePos + new float3(0, s, 0);
                            float3 v3 = basePos + new float3(0, 0, 0);

                            float3 faceCenterLocal = basePos + new float3(0f, 0.5f * s, 0.5f * s);
                            ushort matId = MaterialForFace(
                                x, y, z,
                                cells, r, voxels,
                                chunkOrigin, faceCenterLocal,
                                ref ctx
                            );

                            if (matId != 0)
                                meshData.AddQuad(v0, v1, v2, v3, normal, matId);
                        }

                        // +Y face
                        if (IsCellAir(x, y + 1, z, cells, r, voxels, chunkOrigin, voxelSize, ref ctx))
                        {
                            float3 normal = new float3(0, 1, 0);

                            float3 v0 = basePos + new float3(0, s, 0);
                            float3 v1 = basePos + new float3(0, s, s);
                            float3 v2 = basePos + new float3(s, s, s);
                            float3 v3 = basePos + new float3(s, s, 0);

                            float3 faceCenterLocal = basePos + new float3(0.5f * s, s, 0.5f * s);
                            ushort matId = MaterialForFace(
                                x, y, z,
                                cells, r, voxels,
                                chunkOrigin, faceCenterLocal,
                                ref ctx
                            );

                            if (matId != 0)
                                meshData.AddQuad(v0, v1, v2, v3, normal, matId);
                        }

                        // -Y face
                        if (IsCellAir(x, y - 1, z, cells, r, voxels, chunkOrigin, voxelSize, ref ctx))
                        {
                            float3 normal = new float3(0, -1, 0);

                            float3 v0 = basePos + new float3(0, 0, s);
                            float3 v1 = basePos + new float3(0, 0, 0);
                            float3 v2 = basePos + new float3(s, 0, 0);
                            float3 v3 = basePos + new float3(s, 0, s);

                            float3 faceCenterLocal = basePos + new float3(0.5f * s, 0f, 0.5f * s);
                            ushort matId = MaterialForFace(
                                x, y, z,
                                cells, r, voxels,
                                chunkOrigin, faceCenterLocal,
                                ref ctx
                            );

                            if (matId != 0)
                                meshData.AddQuad(v0, v1, v2, v3, normal, matId);
                        }

                        // +Z face
                        if (IsCellAir(x, y, z + 1, cells, r, voxels, chunkOrigin, voxelSize, ref ctx))
                        {
                            float3 normal = new float3(0, 0, 1);

                            float3 v0 = basePos + new float3(s, 0, s);
                            float3 v1 = basePos + new float3(s, s, s);
                            float3 v2 = basePos + new float3(0, s, s);
                            float3 v3 = basePos + new float3(0, 0, s);

                            float3 faceCenterLocal = basePos + new float3(0.5f * s, 0.5f * s, s);
                            ushort matId = MaterialForFace(
                                x, y, z,
                                cells, r, voxels,
                                chunkOrigin, faceCenterLocal,
                                ref ctx
                            );

                            if (matId != 0)
                                meshData.AddQuad(v0, v1, v2, v3, normal, matId);
                        }

                        // -Z face
                        if (IsCellAir(x, y, z - 1, cells, r, voxels, chunkOrigin, voxelSize, ref ctx))
                        {
                            float3 normal = new float3(0, 0, -1);

                            float3 v0 = basePos + new float3(0, 0, 0);
                            float3 v1 = basePos + new float3(0, s, 0);
                            float3 v2 = basePos + new float3(s, s, 0);
                            float3 v3 = basePos + new float3(s, 0, 0);

                            float3 faceCenterLocal = basePos + new float3(0.5f * s, 0.5f * s, 0f);
                            ushort matId = MaterialForFace(
                                x, y, z,
                                cells, r, voxels,
                                chunkOrigin, faceCenterLocal,
                                ref ctx
                            );

                            if (matId != 0)
                                meshData.AddQuad(v0, v1, v2, v3, normal, matId);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Old API: Build mesh from ChunkData + WorldSettings.
        /// This stays as a thin wrapper around the Burst-safe core.
        /// </summary>
        public static MeshData BuildMesh(in ChunkData chunkData, WorldSettings settings)
        {
            int cells      = chunkData.chunkSize;
            int r          = chunkData.voxelResolution;
            float voxelSize = settings.voxelSize;

            MeshData meshData = new MeshData(cells * cells * cells, 8);

            var voxels = chunkData.voxels;
            var coord  = chunkData.coord3;

            // Get SdfContext from SdfRuntime on the main thread.
            SdfContext ctx = SdfRuntime.Context;

            BuildMeshCore(
                voxels,
                coord,
                cells,
                r,
                voxelSize,
                ctx,
                ref meshData
            );

            return meshData;
        }

        // --------------------------------------------------------------------
        // Helper methods (Burst-friendly: no captures, no managed state)
        // --------------------------------------------------------------------

        private static int IndexNode(int x, int y, int z, int r)
        {
            return x + y * r + z * r * r;
        }

        private static bool IsCellSolidLocal(
            int cx, int cy, int cz,
            int cells,
            int r,
            NativeArray<Voxel> voxels)
        {
            if (cx < 0 || cy < 0 || cz < 0 ||
                cx >= cells || cy >= cells || cz >= cells)
            {
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

                int idx = IndexNode(nx, ny, nz, r);
                if (voxels[idx].density > 0)
                    solidCount++;
            }

            return solidCount > 0;
        }

        /// <summary>
        /// Returns true if the cell should be treated as air.
        /// Inside-chunk: just !IsCellSolidLocal.
        /// Outside-chunk: uses SDF if context is valid; otherwise treated as solid.
        /// </summary>
        private static bool IsCellAir(
            int cx, int cy, int cz,
            int cells,
            int r,
            NativeArray<Voxel> voxels,
            float3 chunkOrigin,
            float voxelSize,
            ref SdfContext ctx)
        {
            // Inside this chunk: rely on local voxel data.
            if (cx >= 0 && cy >= 0 && cz >= 0 &&
                cx <  cells && cy <  cells && cz <  cells)
            {
                return !IsCellSolidLocal(cx, cy, cz, cells, r, voxels);
            }

            // If SdfContext isn't valid, treat outside as solid (no gaps).
            if (ctx.chunkSize <= 0 || ctx.voxelSize <= 0f)
            {
                return false;
            }

            // Outside the chunk: query SDF at the cell center.
            float3 cellCenter = new float3(
                chunkOrigin.x + (cx + 0.5f) * voxelSize,
                chunkOrigin.y + (cy + 0.5f) * voxelSize,
                chunkOrigin.z + (cz + 0.5f) * voxelSize
            );

            float sdf = CombinedTerrainSdf.Evaluate(cellCenter, ref ctx);
            // Solid if sdf < 0 → air if sdf >= 0
            return sdf >= 0f;
        }

        private static ushort GetCellMaterialIdLocal(
            int cx, int cy, int cz,
            int cells,
            int r,
            NativeArray<Voxel> voxels)
        {
            // Default mat (fallback if outside chunk)
            ushort mat = 1;

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

                int idx = IndexNode(nx, ny, nz, r);
                Voxel v = voxels[idx];

                if (v.density > 0)
                {
                    mat = v.materialId;
                    break;
                }
            }

            return mat;
        }

        /// <summary>
        /// Computes the material for a face, including shoreline override.
        /// </summary>
        private static ushort MaterialForFace(
            int cx, int cy, int cz,
            int cells,
            int r,
            NativeArray<Voxel> voxels,
            float3 chunkOrigin,
            float3 faceCenterLocal,
            ref SdfContext ctx)
        {
            ushort matId = GetCellMaterialIdLocal(cx, cy, cz, cells, r, voxels);
            if (matId == 0)
                return 0;

            float3 worldFaceCenter = chunkOrigin + faceCenterLocal;
            float distFromSea = math.abs(worldFaceCenter.y - ctx.seaLevel);
            float nearCoast = math.exp(-distFromSea * 0.6f);

            // Coastline tint: if close enough to sea level, switch to coastal material.
            if (nearCoast > 0.35f && matId != 3)
                matId = 4;

            return matId;
        }
    }
}
