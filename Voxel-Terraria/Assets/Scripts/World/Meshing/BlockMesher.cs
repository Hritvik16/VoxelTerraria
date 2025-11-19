using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using VoxelTerraria.World;          // ChunkData, Voxel, WorldSettings
using VoxelTerraria.World.Meshing;  // MeshData

namespace VoxelTerraria.World.Meshing
{
    /// <summary>
    /// Naive block-based mesher for debugging voxel and SDF correctness.
    /// Generates axis-aligned cubes for solid voxels and only emits faces
    /// where the neighbor is air or out-of-bounds.
    /// 
    /// Convention:
    ///   Voxel.density > 0 => solid
    ///   Voxel.density <= 0 => air
    /// </summary>
    public static class BlockMesher
    {
        public static MeshData BuildMesh(in ChunkData chunkData, WorldSettings settings)
        {
            int size = chunkData.chunkSize;
            float voxelSize = settings.voxelSize;

            MeshData meshData = new MeshData(size * size * size);

            var voxels = chunkData.voxels;

            // Local index helper matching ChunkData.Index3D
            int Index3D(int x, int y, int z)
            {
                return x + y * size + z * size * size;
            }

            bool IsSolid(int x, int y, int z)
            {
                if (x < 0 || y < 0 || z < 0 ||
                    x >= size || y >= size || z >= size)
                    return false; // out-of-bounds = air

                int idx = Index3D(x, y, z);
                return voxels[idx].density > 0;
            }

            bool IsAir(int x, int y, int z)
            {
                return !IsSolid(x, y, z);
            }

            // Main loop: walk every voxel and emit exposed faces
            for (int z = 0; z < size; z++)
            {
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        if (!IsSolid(x, y, z))
                            continue;

                        float vx = x * voxelSize;
                        float vy = y * voxelSize;
                        float vz = z * voxelSize;

                        // +X face (right)
                        if (IsAir(x + 1, y, z))
                        {
                            float3 normal = new float3(1, 0, 0);

                            float3 v0 = new float3(vx + voxelSize, vy,              vz);
                            float3 v1 = new float3(vx + voxelSize, vy + voxelSize,  vz);
                            float3 v2 = new float3(vx + voxelSize, vy + voxelSize,  vz + voxelSize);
                            float3 v3 = new float3(vx + voxelSize, vy,              vz + voxelSize);

                            meshData.AddQuad(v0, v1, v2, v3, normal);
                        }

                        // -X face (left)
                        if (IsAir(x - 1, y, z))
                        {
                            float3 normal = new float3(-1, 0, 0);

                            float3 v0 = new float3(vx, vy,              vz + voxelSize);
                            float3 v1 = new float3(vx, vy + voxelSize,  vz + voxelSize);
                            float3 v2 = new float3(vx, vy + voxelSize,  vz);
                            float3 v3 = new float3(vx, vy,              vz);

                            meshData.AddQuad(v0, v1, v2, v3, normal);
                        }

                        // +Y face (top)
                        if (IsAir(x, y + 1, z))
                        {
                            float3 normal = new float3(0, 1, 0);

                            float3 v0 = new float3(vx,             vy + voxelSize, vz);
                            float3 v1 = new float3(vx,             vy + voxelSize, vz + voxelSize);
                            float3 v2 = new float3(vx + voxelSize, vy + voxelSize, vz + voxelSize);
                            float3 v3 = new float3(vx + voxelSize, vy + voxelSize, vz);

                            meshData.AddQuad(v0, v1, v2, v3, normal);
                        }

                        // -Y face (bottom)
                        if (IsAir(x, y - 1, z))
                        {
                            float3 normal = new float3(0, -1, 0);

                            float3 v0 = new float3(vx,             vy, vz + voxelSize);
                            float3 v1 = new float3(vx,             vy, vz);
                            float3 v2 = new float3(vx + voxelSize, vy, vz);
                            float3 v3 = new float3(vx + voxelSize, vy, vz + voxelSize);

                            meshData.AddQuad(v0, v1, v2, v3, normal);
                        }

                        // +Z face (forward)
                        if (IsAir(x, y, z + 1))
                        {
                            float3 normal = new float3(0, 0, 1);

                            float3 v0 = new float3(vx + voxelSize, vy,             vz + voxelSize);
                            float3 v1 = new float3(vx + voxelSize, vy + voxelSize, vz + voxelSize);
                            float3 v2 = new float3(vx,             vy + voxelSize, vz + voxelSize);
                            float3 v3 = new float3(vx,             vy,             vz + voxelSize);

                            meshData.AddQuad(v0, v1, v2, v3, normal);
                        }

                        // -Z face (back)
                        if (IsAir(x, y, z - 1))
                        {
                            float3 normal = new float3(0, 0, -1);

                            float3 v0 = new float3(vx,             vy,             vz);
                            float3 v1 = new float3(vx,             vy + voxelSize, vz);
                            float3 v2 = new float3(vx + voxelSize, vy + voxelSize, vz);
                            float3 v3 = new float3(vx + voxelSize, vy,             vz);

                            meshData.AddQuad(v0, v1, v2, v3, normal);
                        }
                    }
                }
            }

            return meshData;
        }
    }
}
