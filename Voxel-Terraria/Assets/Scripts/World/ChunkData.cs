using Unity.Collections;
using UnityEngine;
using Unity.Mathematics;
using System.Runtime.CompilerServices;

namespace VoxelTerraria.World
{
    public struct ChunkData
    {
        public ChunkCoord coord;

        // One NativeArray per chunk — Burst/job friendly.
        public NativeArray<Voxel> voxels;

        public bool isGenerated;
        public bool isDirty;

        public int chunkSize;

        // --------------------------------------------------------------
        // Constructor
        // --------------------------------------------------------------
        public ChunkData(ChunkCoord coord, int chunkSize, Allocator allocator)
        {
            this.coord = coord;
            this.chunkSize = chunkSize;

            int count = chunkSize * chunkSize * chunkSize;

            voxels = new NativeArray<Voxel>(count, allocator, NativeArrayOptions.ClearMemory);

            isGenerated = false;
            isDirty = false;
        }

        // --------------------------------------------------------------
        // Dispose (call when chunk is no longer needed)
        // --------------------------------------------------------------
        public void Dispose()
        {
            if (voxels.IsCreated)
                voxels.Dispose();
        }

        // --------------------------------------------------------------
        // Index Conversion
        // Linear index: x + y*chunkSize + z*chunkSize*chunkSize
        // --------------------------------------------------------------
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int Index3D(int x, int y, int z)
        {
            return x + y * chunkSize + z * chunkSize * chunkSize;
        }

        // --------------------------------------------------------------
        // Public Accessors
        // --------------------------------------------------------------
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Voxel Get(int x, int y, int z)
        {
            return voxels[Index3D(x, y, z)];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Set(int x, int y, int z, Voxel voxel)
        {
            voxels[Index3D(x, y, z)] = voxel;
            isDirty = true;
        }

        // Optional: inlined no-check write for jobs
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetFast(int index, Voxel voxel)
        {
            voxels[index] = voxel;
        }
    }
}
