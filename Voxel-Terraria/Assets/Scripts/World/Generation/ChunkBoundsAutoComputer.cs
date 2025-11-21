using Unity.Mathematics;
using UnityEngine;

namespace VoxelTerraria.World.Generation
{
    /// <summary>
    /// Computes XZ chunk bounds based on island radius + feature radii.
    /// DOES NOT compute vertical (Y) bounds — those are computed in SdfBootstrapInternal.
    /// 
    /// Output:
    ///   - worldMinXZ / worldMaxXZ
    ///   - minChunkX, maxChunkX
    ///   - minChunkZ, maxChunkZ
    ///   - chunksX, chunksZ
    /// 
    /// Vertical bounds are added later by SdfBootstrapInternal.
    /// </summary>
    public static class ChunkBoundsAutoComputer
    {
        public struct Result
        {
            public float2 worldMinXZ;
            public float2 worldMaxXZ;

            public int minChunkX;
            public int maxChunkX;
            public int minChunkZ;
            public int maxChunkZ;

            public int chunksX;
            public int chunksZ;

            public bool valid;

            // NEW — Y bounds (not computed here, but included for completeness)
            public int minChunkY;
            public int maxChunkY;
            public int chunksY;
        }

        /// <summary>
        /// Compute XZ extents based on island + mountains.
        /// Vertical (Y) is handled separately.
        /// </summary>
        public static Result Compute(
            WorldSettings settings,
            MountainFeatureData[] mountains,
            float islandRadius)
        {
            Result r = new Result();

            if (settings == null)
            {
                r.valid = false;
                return r;
            }

            float voxelSize = settings.voxelSize;
            int chunkSize = settings.chunkSize;
            float chunkWorld = voxelSize * chunkSize;

            if (chunkWorld <= 0f)
            {
                r.valid = false;
                return r;
            }

            // ------------------------------------------------------------
            // 1. Start XZ extents from island radius
            // ------------------------------------------------------------
            float maxRadius = islandRadius;

            // ------------------------------------------------------------
            // 2. Expand with mountain extents
            // ------------------------------------------------------------
            if (mountains != null)
            {
                for (int i = 0; i < mountains.Length; i++)
                {
                    var m = mountains[i];
                    float distCenter = math.length(m.centerXZ);
                    float farthest = distCenter + m.radius;

                    if (farthest > maxRadius)
                        maxRadius = farthest;
                }
            }

            // Slight padding
            maxRadius *= 1.1f;

            // ------------------------------------------------------------
            // 3. Convert to world min/max in XZ
            // ------------------------------------------------------------
            r.worldMinXZ = new float2(-maxRadius, -maxRadius);
            r.worldMaxXZ = new float2(+maxRadius, +maxRadius);

            float worldWidth = r.worldMaxXZ.x - r.worldMinXZ.x;
            float worldDepth = r.worldMaxXZ.y - r.worldMinXZ.y;

            // ------------------------------------------------------------
            // 4. Chunk counts in X/Z
            // ------------------------------------------------------------
            r.chunksX = Mathf.CeilToInt(worldWidth / chunkWorld);
            r.chunksZ = Mathf.CeilToInt(worldDepth / chunkWorld);

            if (r.chunksX <= 0 || r.chunksZ <= 0)
            {
                r.valid = false;
                return r;
            }

            // ------------------------------------------------------------
            // 5. Chunk index ranges centered around origin
            // ------------------------------------------------------------
            int halfX = r.chunksX / 2;
            int halfZ = r.chunksZ / 2;

            r.minChunkX = -halfX;
            r.maxChunkX = r.chunksX - halfX - 1;

            r.minChunkZ = -halfZ;
            r.maxChunkZ = r.chunksZ - halfZ - 1;

            r.valid = true;

            // ------------------------------------------------------------
            // 6. Vertical Y bounds NOT computed here
            // They will be filled in later by SdfBootstrapInternal.
            // ------------------------------------------------------------
            r.minChunkY = 0;
            r.maxChunkY = 0;
            r.chunksY = 1;

            return r;
        }
    }
}
