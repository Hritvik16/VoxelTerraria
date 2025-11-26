using Unity.Mathematics;
using UnityEngine;
using VoxelTerraria.World;   // for ChunkCoord3, VoxelCoord

namespace VoxelTerraria.World.Generation
{
    /// <summary>
    /// Helpers to convert between world-space, chunk-space, and voxel-space.
    /// All math is based on WorldSettings.voxelSize + chunkSize.
    /// </summary>
    public static class WorldCoordUtils
    {
        /// <summary>
        /// World-space origin of the given chunk.
        /// </summary>
        public static Vector3 ChunkOriginWorld(ChunkCoord3 coord, WorldSettings settings)
        {
            float v = settings.voxelSize;
            int   cs = settings.chunkSize;

            float chunkWorld = v * cs;

            float originX = coord.x * chunkWorld;
            float originY = coord.y * chunkWorld;
            float originZ = coord.z * chunkWorld;

            return new Vector3(originX, originY, originZ);
        }

        /// <summary>
        /// World-space position of the voxel *sample node* (grid corner) for this chunk + voxel index.
        /// Used for SDF sampling (voxelResolution = cells+1).
        /// </summary>
        // public static float3 VoxelSampleWorld(ChunkCoord3 chunk, VoxelCoord index, WorldSettings settings)
        // {
        //     Vector3 origin = ChunkOriginWorld(chunk, settings);
        //     float v = settings.voxelSize;

        //     return new float3(
        //         origin.x + index.x * v,
        //         origin.y + index.y * v,
        //         origin.z + index.z * v
        //     );
        // }

        /// <summary>
        /// World-space center position of a voxel cell.
        /// </summary>
        // public static float3 VoxelCenterWorld(ChunkCoord3 chunk, VoxelCoord index, WorldSettings settings)
        // {
        //     Vector3 origin = ChunkOriginWorld(chunk, settings);
        //     float v = settings.voxelSize;

        //     return new float3(
        //         origin.x + (index.x + 0.5f) * v,
        //         origin.y + (index.y + 0.5f) * v,
        //         origin.z + (index.z + 0.5f) * v
        //     );
        // }

        /// <summary>
        /// Convert world position to chunk coordinates.
        /// </summary>
        // public static ChunkCoord3 WorldToChunk(float3 worldPos, WorldSettings settings)
        // {
        //     float v  = settings.voxelSize;
        //     int   cs = settings.chunkSize;
        //     float chunkWorld = v * cs;

        //     int cx = Mathf.FloorToInt(worldPos.x / chunkWorld);
        //     int cy = Mathf.FloorToInt(worldPos.y / chunkWorld);
        //     int cz = Mathf.FloorToInt(worldPos.z / chunkWorld);

        //     return new ChunkCoord3(cx, cy, cz);
        // }

        /// <summary>
        /// Convert world position to voxel indices in global voxel grid (not chunk-relative).
        /// </summary>
        // public static VoxelCoord WorldToVoxel(float3 worldPos, WorldSettings settings)
        // {
        //     float v = settings.voxelSize;

        //     int vx = Mathf.FloorToInt(worldPos.x / v);
        //     int vy = Mathf.FloorToInt(worldPos.y / v);
        //     int vz = Mathf.FloorToInt(worldPos.z / v);

        //     return new VoxelCoord(vx, vy, vz);
        // }
    }
}
