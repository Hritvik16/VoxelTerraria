using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using VoxelTerraria.World.SDF;

namespace VoxelTerraria.World.Generation
{
    /// <summary>
    /// Computes full 3D world-space chunk bounds based on per-feature
    /// SDF-sampled AABBs (via FeatureBounds3DComputer).
    ///
    /// Guarantees:
    ///  • No clipping of BaseIsland / Mountain features.
    ///  • Works when there is NO base island (isolated features).
    ///  • Works for arbitrary voxelSize / chunkSize.
    ///  • One small, fixed global margin (half a chunk) to avoid edge cases.
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

            public int minChunkY;
            public int maxChunkY;
            public int chunksY;

            public float minTerrainHeight;
            public float maxTerrainHeight;

            public bool valid;
        }

        public static Result ComputeFromFeatures(
            WorldSettings settings,
            NativeArray<Feature> features,
            int featureCount)
        {
            Result r = new Result { valid = false };

            if (settings == null)
                return r;

            float voxelSize = settings.voxelSize;
            int   chunkSize = settings.chunkSize;
            float chunkWorld = voxelSize * chunkSize;

            if (chunkWorld <= 0f)
                return r;

            // ------------------------------------------------------------
            // 1. Handle empty world (no features at all)
            // ------------------------------------------------------------
            if (featureCount <= 0)
            {
                float half = chunkWorld * 0.5f;
                r.worldMinXZ = new float2(-half, -half);
                r.worldMaxXZ = new float2(+half, +half);

                r.minChunkX = 0;
                r.maxChunkX = 0;
                r.minChunkZ = 0;
                r.maxChunkZ = 0;
                r.chunksX   = 1;
                r.chunksZ   = 1;

                r.minChunkY = -1;
                r.maxChunkY =  1;
                r.chunksY   =  3;

                r.minTerrainHeight = r.minChunkY * chunkWorld;
                r.maxTerrainHeight = (r.maxChunkY + 1) * chunkWorld;

                r.valid = true;
                return r;
            }

            // ------------------------------------------------------------
            // 2. Merge per-feature AABBs
            // ------------------------------------------------------------
            bool anyValid = false;
            float3 globalMin = new float3(float.MaxValue, float.MaxValue, float.MaxValue);
            float3 globalMax = new float3(float.MinValue, float.MinValue, float.MinValue);

            for (int i = 0; i < featureCount; i++)
            {
                Feature f = features[i];

                FeatureAabb fb = FeatureBounds3DComputer.ComputeAabb(in f, settings);
                if (!fb.valid)
                    continue;

                anyValid = true;
                globalMin = math.min(globalMin, fb.min);
                globalMax = math.max(globalMax, fb.max);
            }

            if (!anyValid)
                return r;

            // One half-chunk safety margin around the entire world.
            float3 worldMargin = new float3(chunkWorld * 0.5f);
            globalMin -= worldMargin;
            globalMax += worldMargin;

            // ------------------------------------------------------------
            // 3. Convert global bounds to chunk indices
            // ------------------------------------------------------------

            // XZ extents
            r.worldMinXZ = new float2(globalMin.x, globalMin.z);
            r.worldMaxXZ = new float2(globalMax.x, globalMax.z);

            float worldWidth  = r.worldMaxXZ.x - r.worldMinXZ.x;
            float worldDepth  = r.worldMaxXZ.y - r.worldMinXZ.y;

            r.chunksX = Mathf.CeilToInt(worldWidth / chunkWorld);
            r.chunksZ = Mathf.CeilToInt(worldDepth / chunkWorld);

            if (r.chunksX <= 0 || r.chunksZ <= 0)
                return r;

            int minChunkX = Mathf.FloorToInt(r.worldMinXZ.x / chunkWorld);
            int maxChunkX = Mathf.CeilToInt (r.worldMaxXZ.x / chunkWorld) - 1;

            int minChunkZ = Mathf.FloorToInt(r.worldMinXZ.y / chunkWorld);
            int maxChunkZ = Mathf.CeilToInt (r.worldMaxXZ.y / chunkWorld) - 1;

            r.minChunkX = minChunkX;
            r.maxChunkX = maxChunkX;
            r.chunksX   = maxChunkX - minChunkX + 1;

            r.minChunkZ = minChunkZ;
            r.maxChunkZ = maxChunkZ;
            r.chunksZ   = maxChunkZ - minChunkZ + 1;

            // Y extents from globalMin/globalMax
            float minY = globalMin.y;
            float maxY = globalMax.y;

            int minChunkY = Mathf.FloorToInt(minY / chunkWorld);
            int maxChunkY = Mathf.CeilToInt (maxY / chunkWorld) - 1;

            if (minChunkY > maxChunkY)
            {
                minChunkY = -1;
                maxChunkY =  1;
            }

            r.minChunkY = minChunkY;
            r.maxChunkY = maxChunkY;
            r.chunksY   = maxChunkY - minChunkY + 1;

            r.minTerrainHeight = minChunkY * chunkWorld;
            r.maxTerrainHeight = (maxChunkY + 1) * chunkWorld;

            r.valid = true;
            return r;
        }
    }
}
