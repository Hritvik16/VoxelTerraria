using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace VoxelTerraria.World
{
    public struct ChunkData
    {
        public readonly int chunkSize;
        public readonly int voxelResolution;

        public ChunkCoord3 coord3;

        public NativeArray<Voxel> voxels;

        public bool isGenerated;
        public bool isDirty;

        public int lodLevel;
        public float currentVoxelSize;

        public ChunkData(ChunkCoord3 coord3, int chunkSize, int lodLevel, float currentVoxelSize, Allocator allocator)
        {
            this.coord3 = coord3;
            this.chunkSize = chunkSize;
            this.lodLevel = lodLevel;
            this.currentVoxelSize = currentVoxelSize;
            voxelResolution = chunkSize + 1;

            voxels = new NativeArray<Voxel>(
                voxelResolution * voxelResolution * voxelResolution,
                allocator,
                NativeArrayOptions.ClearMemory
            );

            isGenerated = false;
            isDirty = false;
        }

        public ChunkData(ChunkCoord coord, int chunkSize, int lodLevel, float currentVoxelSize, Allocator allocator)
            : this(coord.As3(), chunkSize, lodLevel, currentVoxelSize, allocator) {}

        public void Dispose()
        {
            if (voxels.IsCreated)
                voxels.Dispose();
        }

        private int Index(int x, int y, int z)
        {
            return (z * voxelResolution * voxelResolution) +
                   (y * voxelResolution) +
                    x;
        }

        public void Set(int x, int y, int z, in Voxel voxel)
        {
            voxels[Index(x, y, z)] = voxel;
        }

        public Voxel Get(int x, int y, int z)
        {
            return voxels[Index(x, y, z)];
        }
    }
}
