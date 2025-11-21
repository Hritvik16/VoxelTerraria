using System;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using VoxelTerraria.World.Generation;


namespace VoxelTerraria.World.SDF
{
    /// <summary>
    /// Builds the SdfContext from WorldSettings + features and computes:
    ///   • Chunk bounds in X/Z
    ///   • TRUE vertical height range using SDF probing
    ///   • Vertical chunk bounds (min/max Y)
    /// </summary>
    public static class SdfBootstrapInternal
    {
        // ------------------------------------------------------------
        // PUBLIC ENTRY POINT
        // ------------------------------------------------------------
        public static SdfContext Build(
            WorldSettings world,
            MountainFeature[] mountainSOs,
            LakeFeature[] lakeSOs,
            ForestFeature[] forestSOs,
            CityPlateauFeature[] citySOs,
            Allocator allocator = Allocator.Persistent)
        {
            if (world == null)
            {
                Debug.LogError("SdfBootstrapInternal.Build: WorldSettings is null.");
                return default;
            }

            // --------------------------------------------------------
            // 1. Basic SdfContext allocation and feature array copy
            // --------------------------------------------------------
            SdfContext ctx = AllocateContext(world, mountainSOs, lakeSOs, forestSOs, citySOs, allocator);

            // --------------------------------------------------------
            // 2. Compute XZ chunk bounds using your existing logic
            // --------------------------------------------------------
            ComputeXZBounds(ref ctx, world);

            // --------------------------------------------------------
            // 3. Compute TRUE height envelope via SDF probing
            // --------------------------------------------------------
            ComputeVerticalBounds(ref ctx, world);

            return ctx;
        }

        // ============================================================
        // 1. Allocate context + copy features into NativeArrays
        // ============================================================
        private static SdfContext AllocateContext(
            WorldSettings world,
            MountainFeature[] mountainSOs,
            LakeFeature[] lakeSOs,
            ForestFeature[] forestSOs,
            CityPlateauFeature[] citySOs,
            Allocator allocator)
        {
            var ctx = new SdfContext
            {
                voxelSize = world.voxelSize,
                chunkSize = world.chunkSize,
                seaLevel = world.seaLevel,
                islandRadius = world.islandRadius,
                maxBaseHeight = world.maxBaseHeight,
                stepHeight = world.stepHeight,

                worldMinXZ = float2.zero,
                worldMaxXZ = float2.zero,
                minChunkX = 0,
                maxChunkX = 0,
                minChunkZ = 0,
                maxChunkZ = 0,
                chunksX = 0,
                chunksZ = 0,

                // vertical will be filled later
                minTerrainHeight = 0,
                maxTerrainHeight = 0,
                minChunkY = 0,
                maxChunkY = 0,
                chunksY = 0,

                mountains = new NativeArray<MountainFeatureData>(mountainSOs?.Length ?? 0, allocator),
                lakes = new NativeArray<LakeFeatureData>(lakeSOs?.Length ?? 0, allocator),
                forests = new NativeArray<ForestFeatureData>(forestSOs?.Length ?? 0, allocator),
                cities = new NativeArray<CityPlateauFeatureData>(citySOs?.Length ?? 0, allocator)
            };

            // mountains
            if (mountainSOs != null)
            {
                for (int i = 0; i < mountainSOs.Length; i++)
                {
                    var so = mountainSOs[i];
                    if (so == null) continue;

                    ctx.mountains[i] = new MountainFeatureData
                    {
                        centerXZ = new float2(so.CenterXZ.x, so.CenterXZ.y),
                        radius = so.Radius,
                        height = so.Height,
                        ridgeFrequency = so.RidgeFrequency,
                        ridgeAmplitude = so.RidgeAmplitude,
                        warpStrength = so.WarpStrength
                    };
                }
            }

            // lakes
            if (lakeSOs != null)
            {
                for (int i = 0; i < lakeSOs.Length; i++)
                {
                    var so = lakeSOs[i];
                    if (so == null) continue;

                    ctx.lakes[i] = new LakeFeatureData
                    {
                        centerXZ = new float2(so.CenterXZ.x, so.CenterXZ.y),
                        radius = so.Radius,
                        bottomHeight = so.BottomHeight,
                        shoreHeight = so.ShoreHeight
                    };
                }
            }

            // forests
            if (forestSOs != null)
            {
                for (int i = 0; i < forestSOs.Length; i++)
                {
                    var so = forestSOs[i];
                    if (so == null) continue;

                    ctx.forests[i] = new ForestFeatureData
                    {
                        centerXZ = new float2(so.CenterXZ.x, so.CenterXZ.y),
                        radius = so.Radius,
                        treeDensity = so.TreeDensity
                    };
                }
            }

            // cities
            if (citySOs != null)
            {
                for (int i = 0; i < citySOs.Length; i++)
                {
                    var so = citySOs[i];
                    if (so == null) continue;

                    ctx.cities[i] = new CityPlateauFeatureData
                    {
                        centerXZ = new float2(so.CenterXZ.x, so.CenterXZ.y),
                        radius = so.Radius,
                        plateauHeight = so.PlateauHeight
                    };
                }
            }

            return ctx;
        }

        // ============================================================
        // 2. XZ BOUNDS
        // ============================================================
        private static void ComputeXZBounds(ref SdfContext ctx, WorldSettings world)
        {
            try
            {
                // Convert NativeArray → managed array for your existing bounds logic
                MountainFeatureData[] managedMountains = null;
                if (ctx.mountains.IsCreated && ctx.mountains.Length > 0)
                {
                    managedMountains = new MountainFeatureData[ctx.mountains.Length];
                    ctx.mountains.CopyTo(managedMountains);
                }

                var result = ChunkBoundsAutoComputer.Compute(world, managedMountains, ctx.islandRadius);

                if (result.valid)
                {
                    ctx.worldMinXZ = result.worldMinXZ;
                    ctx.worldMaxXZ = result.worldMaxXZ;

                    ctx.minChunkX = result.minChunkX;
                    ctx.maxChunkX = result.maxChunkX;
                    ctx.minChunkZ = result.minChunkZ;
                    ctx.maxChunkZ = result.maxChunkZ;

                    ctx.chunksX = result.chunksX;
                    ctx.chunksZ = result.chunksZ;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"SdfBootstrapInternal: Bounds compute failed: {ex.Message}");
            }
        }

        // ============================================================
        // 3. TRUE VERTICAL HEIGHT SCANNER (Hybrid SDF probing)
        // ============================================================
        private static void ComputeVerticalBounds(ref SdfContext ctx, WorldSettings world)
        {
            float voxelSize = world.voxelSize;
            int chunkSize = world.chunkSize;
            float chunkWorldHeight = voxelSize * chunkSize;

            // Probe grid resolution (you can bump to 24 or 32 if needed)
            int probeCount = 16;

            // TEMP local vars for tracking vertical range
            float minH = float.MaxValue;
            float maxH = float.MinValue;

            // Probe XZ grid across world extents
            for (int iz = 0; iz < probeCount; iz++)
            {
                for (int ix = 0; ix < probeCount; ix++)
                {
                    float u = ix / (probeCount - 1f);
                    float v = iz / (probeCount - 1f);

                    float px = math.lerp(ctx.worldMinXZ.x, ctx.worldMaxXZ.x, u);
                    float pz = math.lerp(ctx.worldMinXZ.y, ctx.worldMaxXZ.y, v);

                    float2 xz = new float2(px, pz);

                    // ---- Find top surface ----
                    float surfaceTop = FindSurfaceHeightUpwards(xz, ref ctx, world);
                    if (surfaceTop > maxH) maxH = surfaceTop;

                    // ---- Find bottom surface ----
                    float surfaceBottom = FindSurfaceHeightDownwards(xz, ref ctx, world);
                    if (surfaceBottom < minH) minH = surfaceBottom;
                }
            }

            // Save to context
            ctx.minTerrainHeight = minH;
            ctx.maxTerrainHeight = maxH;

            // Convert heights to chunk indices
            ctx.minChunkY = Mathf.FloorToInt(minH / chunkWorldHeight);
            ctx.maxChunkY = Mathf.CeilToInt(maxH / chunkWorldHeight);
            ctx.chunksY = ctx.maxChunkY - ctx.minChunkY + 1;
        }

        // ============================================================
        // SURFACE FINDERS — Hybrid march + binary search
        // ============================================================

        private static float FindSurfaceHeightUpwards(float2 xz, ref SdfContext ctx, WorldSettings world)
        {
            float step = 2f;       // coarse upward march step (meters)
            float maxSearch = world.maxBaseHeight * 4f + 200f;  // generous max altitude

            float lastY = world.seaLevel;
            float lastSdf = CombinedTerrainSdf.Evaluate(new float3(xz.x, lastY, xz.y), ctx);

            // march upward
            for (float y = lastY; y < maxSearch; y += step)
            {
                float sdf = CombinedTerrainSdf.Evaluate(new float3(xz.x, y, xz.y), ctx);

                if (sdf > 0f && lastSdf < 0f)
                    return BinarySearchSurface(xz, lastY, y, ref ctx);

                lastY = y;
                lastSdf = sdf;
            }
            return world.seaLevel; // fallback: no terrain hit in this column

            // return lastY; // fallback
        }

        private static float FindSurfaceHeightDownwards(float2 xz, ref SdfContext ctx, WorldSettings world)
        {
            float step = 2f;
            float minSearch = world.seaLevel - 200f;

            float lastY = world.seaLevel;
            float lastSdf = CombinedTerrainSdf.Evaluate(new float3(xz.x, lastY, xz.y), ctx);

            // march downward
            for (float y = lastY; y > minSearch; y -= step)
            {
                float sdf = CombinedTerrainSdf.Evaluate(new float3(xz.x, y, xz.y), ctx);

                if (sdf < 0f && lastSdf > 0f)
                    return BinarySearchSurface(xz, y, lastY, ref ctx);

                lastY = y;
                lastSdf = sdf;
            }
            return world.seaLevel; // fallback: no terrain hit in this column

            // return lastY; // fallback
        }

        private static float BinarySearchSurface(float2 xz, float yMin, float yMax, ref SdfContext ctx)
        {
            for (int i = 0; i < 10; i++)   // ~0.1m precision
            {
                float mid = (yMin + yMax) * 0.5f;
                float sdf = CombinedTerrainSdf.Evaluate(new float3(xz.x, mid, xz.y), ctx);

                if (sdf > 0f)
                    yMax = mid;
                else
                    yMin = mid;
            }
            return (yMin + yMax) * 0.5f;
        }
    }
}
