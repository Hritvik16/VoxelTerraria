using Unity.Mathematics;
using VoxelTerraria.World;
using VoxelTerraria.World.SDF;

namespace VoxelTerraria.World.Meshing
{
    public static class BlockMesher
    {
        public static MeshData BuildMesh(in ChunkData chunkData, WorldSettings settings)
        {
            int cells = chunkData.chunkSize;
            int r     = chunkData.voxelResolution;
            float voxelSize = settings.voxelSize;

            MeshData meshData = new MeshData(cells * cells * cells, 8);

            var voxels = chunkData.voxels;
            var coord  = chunkData.coord3;

            float chunkWorld = voxelSize * cells;
            float3 chunkOrigin = new float3(
                coord.x * chunkWorld,
                coord.y * chunkWorld,
                coord.z * chunkWorld
            );

            SdfContext ctx = SdfRuntime.Context;

            int IndexNode(int x, int y, int z)
            {
                return x + y * r + z * r * r;
            }

            bool IsCellSolidLocal(int cx, int cy, int cz)
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

                    int idx = IndexNode(nx, ny, nz);
                    if (voxels[idx].density > 0)
                        solidCount++;
                }

                return solidCount > 0;
            }

            bool IsCellSolidWithSdf(int cx, int cy, int cz)
            {
                if (cx >= 0 && cy >= 0 && cz >= 0 &&
                    cx <  cells && cy <  cells && cz <  cells)
                {
                    return IsCellSolidLocal(cx, cy, cz);
                }

                if (ctx.chunkSize <= 0 || ctx.voxelSize <= 0f)
                {
                    return true;
                }

                float3 cellCenter = new float3(
                    chunkOrigin.x + (cx + 0.5f) * voxelSize,
                    chunkOrigin.y + (cy + 0.5f) * voxelSize,
                    chunkOrigin.z + (cz + 0.5f) * voxelSize
                );

                float sdf = CombinedTerrainSdf.Evaluate(cellCenter, ref ctx);
                return sdf < 0f;
            }

            bool IsCellAir(int cx, int cy, int cz) => !IsCellSolidWithSdf(cx, cy, cz);

            ushort GetCellMaterialIdLocal(int cx, int cy, int cz)
            {
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

                        ushort MaterialForFace(float3 faceCenterLocal)
                        {
                            ushort matId = GetCellMaterialIdLocal(x, y, z);

                            if (matId == 0)
                                return 0;

                            float3 worldFaceCenter = chunkOrigin + faceCenterLocal;
                            float distFromSea = math.abs(worldFaceCenter.y - ctx.seaLevel);
                            float nearCoast = math.exp(-distFromSea * 0.6f);

                            if (nearCoast > 0.35f && matId != 3)
                                matId = 4;

                            return matId;
                        }

                        // Same face generation as before…

                        // +X face
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

                        // -X face
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

                        // +Y face
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

                        // -Y face
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

                        // +Z face
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

                        // -Z face
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
