using Unity.Mathematics;
using UnityEngine;
using System.Runtime.CompilerServices;

namespace VoxelTerraria.World
{
    // ------------------------
    //  STRUCT DEFINITIONS
    // ------------------------

    [System.Serializable]
    public struct ChunkCoord
    {
        public int x;
        public int z;

        public ChunkCoord(int x, int z)
        {
            this.x = x;
            this.z = z;
        }

        public override string ToString() => $"ChunkCoord({x}, {z})";
    }

    [System.Serializable]
    public struct VoxelCoord
    {
        public int x;
        public int y;
        public int z;

        public VoxelCoord(int x, int y, int z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public override string ToString() => $"VoxelCoord({x}, {y}, {z})";
    }


    // -----------------------------------------------------------------
    //  WORLD COORDINATE HELPERS
    // -----------------------------------------------------------------

    public static class WorldCoordUtils
    {
        // ----------- INTERNAL MATH UTILITIES --------------------------

        /// <summary>
        /// Floor division that correctly handles negative values.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int FloorDiv(int value, int divisor)
        {
            // Classic floor division rule:
            // floor(a / b) = (a - ((a < 0) ? (b-1) : 0)) / b;
            if (value >= 0) return value / divisor;
            return (value - divisor + 1) / divisor;
        }

        /// <summary>
        /// Fast floor-to-int for floats (Burst-safe).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int FastFloor(float f)
        {
            int i = (int)f;
            return (f < i) ? (i - 1) : i;
        }


        // ---------------------------------------------------------------
        // 1) World → Chunk
        // ---------------------------------------------------------------
        public static ChunkCoord WorldToChunk(float3 worldPos, WorldSettings settings)
        {
            float voxelSize = settings.voxelSize;
            int chunkSize  = settings.chunkSize;

            float wx = worldPos.x;
            float wz = worldPos.z;

            // Convert world → voxel index first
            int vx = FastFloor(wx / voxelSize);
            int vz = FastFloor(wz / voxelSize);

            // Convert voxel index → chunk index using floor division
            int cx = FloorDiv(vx, chunkSize);
            int cz = FloorDiv(vz, chunkSize);

            return new ChunkCoord(cx, cz);
        }


        // ---------------------------------------------------------------
        // 2) Chunk → World Origin
        // ---------------------------------------------------------------
        public static float3 ChunkOriginWorld(ChunkCoord chunk, WorldSettings settings)
        {
            float voxelSize = settings.voxelSize;
            int chunkSize   = settings.chunkSize;

            float wx = chunk.x * chunkSize * voxelSize;
            float wz = chunk.z * chunkSize * voxelSize;

            return new float3(wx, 0f, wz);
        }


        // ---------------------------------------------------------------
        // 3) World → Voxel
        // ---------------------------------------------------------------
        public static VoxelCoord WorldToVoxel(float3 worldPos, WorldSettings settings)
        {
            float v = settings.voxelSize;

            return new VoxelCoord(
                FastFloor(worldPos.x / v),
                FastFloor(worldPos.y / v),
                FastFloor(worldPos.z / v)
            );
        }




        // ---------------------------------------------------------------
        // 4) Chunk + InnerVoxel → World center position
        // ---------------------------------------------------------------
        public static float3 VoxelCenterWorld(ChunkCoord chunk, VoxelCoord inner, WorldSettings settings)
        {
            float voxelSize = settings.voxelSize;
            int chunkSize = settings.chunkSize;

            float baseX = chunk.x * chunkSize * voxelSize;
            float baseZ = chunk.z * chunkSize * voxelSize;

            return new float3(
                baseX + (inner.x + 0.5f) * voxelSize,
                (inner.y + 0.5f) * voxelSize,
                baseZ + (inner.z + 0.5f) * voxelSize
            );
        }
    }
}
