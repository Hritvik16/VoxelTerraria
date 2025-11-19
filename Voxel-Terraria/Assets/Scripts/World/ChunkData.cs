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

        // Number of cells per axis (matches WorldSettings.chunkSize)
        public int chunkSize;

        // Number of voxel samples per axis = chunkSize + 1
        public int voxelResolution;

        // --------------------------------------------------------------
        // Constructor
        // --------------------------------------------------------------
        public ChunkData(ChunkCoord coord, int chunkSize, Allocator allocator)
        {
            this.coord = coord;
            this.chunkSize = chunkSize;

            // padded grid: size+1 samples per axis
            this.voxelResolution = chunkSize + 1;

            int count = voxelResolution * voxelResolution * voxelResolution;

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
        // Linear index: x + y*voxelResolution + z*voxelResolution*voxelResolution
        // --------------------------------------------------------------
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int Index3D(int x, int y, int z)
        {
            int r = voxelResolution;
            return x + y * r + z * r * r;
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
