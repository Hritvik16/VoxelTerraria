using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using VoxelTerraria.Data.Features;
using VoxelTerraria.World.SDF.FeatureAdapters;
using VoxelTerraria.World.Generation;

namespace VoxelTerraria.World.SDF
{
    /// <summary>
    /// Builds SdfContext from ScriptableObject features.
    /// 
    /// Responsibilities:
    ///   • Copy WorldSettings scalars into SdfContext.
    ///   • Build the generic Feature[] list from FeatureSO assets.
    ///   • (Transitional) Fill old typed arrays (mountains, lakes, forests, cities)
    ///     so existing SDFs can still use them.
    ///   • Compute full 3D chunk bounds (X/Y/Z) using ChunkBoundsAutoComputer.
    /// </summary>
    public static class SdfBootstrapInternal
    {
        public static SdfContext Build(
            WorldSettings ws,
            BaseIslandFeature baseIslandSO,
            MountainFeature[] mountainSOs,
            LakeFeature[] lakeSOs,
            ForestFeature[] forestSOs,
            CityPlateauFeature[] citySOs,
            VolcanoFeature[] volcanoSOs,
            RiverFeature[] riverSOs,
            CaveRoomFeature[] caveRooms,
            CaveTunnelFeature[] caveTunnels
        )
        {
            SdfContext ctx = new SdfContext();

            //----------------------------------------------------
            // 1. World settings → context scalars
            //----------------------------------------------------
            ctx.voxelSize     = ws.voxelSize;
            ctx.chunkSize     = ws.chunkSize;
            ctx.seaLevel      = ws.seaLevel;
            ctx.islandRadius  = ws.islandRadius;   // still used by old BaseIslandSdf
            ctx.maxBaseHeight = ws.maxBaseHeight;  // ditto
            ctx.stepHeight    = ws.stepHeight;

            //----------------------------------------------------
            // 2. OLD TYPED ARRAYS (still driving actual SDFs)
            //    - We keep these so BaseIslandSdf / MountainSdf / etc.
            //      continue to work during the migration.
            //----------------------------------------------------

            // Mountains
            // int mountainCount = mountainSOs != null ? mountainSOs.Length : 0;
            // ctx.mountains = new NativeArray<MountainFeatureData>(mountainCount, Allocator.Persistent);
            // for (int i = 0; i < mountainCount; i++)
            // {
            //     var so = mountainSOs[i];
            //     ctx.mountains[i] = new MountainFeatureData
            //     {
            //         centerXZ       = new float2(so.CenterXZ.x, so.CenterXZ.y),
            //         radius         = so.Radius,
            //         height         = so.Height,
            //         ridgeFrequency = so.RidgeFrequency,
            //         ridgeAmplitude = so.RidgeAmplitude,
            //         warpStrength   = so.WarpStrength
            //     };
            // }

            // // Lakes
            // int lakeCount = lakeSOs != null ? lakeSOs.Length : 0;
            // ctx.lakes = new NativeArray<LakeFeatureData>(lakeCount, Allocator.Persistent);
            // for (int i = 0; i < lakeCount; i++)
            // {
            //     var so = lakeSOs[i];
            //     ctx.lakes[i] = new LakeFeatureData
            //     {
            //         centerXZ     = new float2(so.CenterXZ.x, so.CenterXZ.y),
            //         radius       = so.Radius,
            //         bottomHeight = so.BottomHeight,
            //         shoreHeight  = so.ShoreHeight
            //     };
            // }

            // // Forests
            // int forestCount = forestSOs != null ? forestSOs.Length : 0;
            // ctx.forests = new NativeArray<ForestFeatureData>(forestCount, Allocator.Persistent);
            // for (int i = 0; i < forestCount; i++)
            // {
            //     var so = forestSOs[i];
            //     ctx.forests[i] = new ForestFeatureData
            //     {
            //         centerXZ    = new float2(so.CenterXZ.x, so.CenterXZ.y),
            //         radius      = so.Radius,
            //         treeDensity = so.TreeDensity
            //     };
            // }

            // // Cities
            // int cityCount = citySOs != null ? citySOs.Length : 0;
            // ctx.cities = new NativeArray<CityPlateauFeatureData>(cityCount, Allocator.Persistent);
            // for (int i = 0; i < cityCount; i++)
            // {
            //     var so = citySOs[i];
            //     ctx.cities[i] = new CityPlateauFeatureData
            //     {
            //         centerXZ      = new float2(so.CenterXZ.x, so.CenterXZ.y),
            //         radius        = so.Radius,
            //         plateauHeight = so.PlateauHeight
            //     };
            // }

            //----------------------------------------------------
            // 3. NEW GENERIC FEATURE LIST (agnostic, used for bounds)
            //----------------------------------------------------
            // Calculate counts for generic array sizing
            int mountainCount = mountainSOs != null ? mountainSOs.Length : 0;
            int baseCount = baseIslandSO != null ? 1 : 0;
            int lakeCount = lakeSOs != null ? lakeSOs.Length : 0;
            int forestCount = forestSOs != null ? forestSOs.Length : 0;
            int cityCount = citySOs != null ? citySOs.Length : 0;
            int volcanoCount  = volcanoSOs != null ? volcanoSOs.Length : 0;
            int caveRoomCount = caveRooms != null ? caveRooms.Length : 0;
            int caveTunnelCount = caveTunnels != null ? caveTunnels.Length : 0;
            
            // Rivers - Generate Segments
            int riverCount = 0; // Will be updated after segments are generated
            System.Collections.Generic.List<Feature> riverSegments = new System.Collections.Generic.List<Feature>();
            
            // RNG for random rivers
            Unity.Mathematics.Random riverRng = new Unity.Mathematics.Random((uint)ws.globalSeed + 54321);

            if (riverSOs != null)
            {
                foreach (var r in riverSOs)
                {
                    if (r == null) continue;

                    // 1. Manual Chain
                    if (r.manualPath != null && r.manualPath.Count >= 2)
                    {
                        for (int k = 0; k < r.manualPath.Count - 1; k++)
                        {
                            var f1 = r.manualPath[k];
                            var f2 = r.manualPath[k + 1];
                            if (f1 == null || f2 == null) continue;

                            // Revert to GetBaseHeight (Base/Sea Level) as requested for a "simpler" system.
                            float h1 = f1.GetBaseHeight(ws) + r.startHeightOffset;
                            float h2 = f2.GetBaseHeight(ws) + r.endHeightOffset;
                            
                            // But we need XZ from the features.
                            Vector2 c1 = f1.GetCenter();
                            Vector2 c2 = f2.GetCenter();
                            float rad1 = f1.GetRadius();
                            float rad2 = f2.GetRadius();
                            
                            // Calculate edge points
                            float2 dir = math.normalize(new float2(c2.x - c1.x, c2.y - c1.y));
                            float2 startPos = new float2(c1.x, c1.y) + dir * (rad1 * 0.8f);
                            float2 endPos   = new float2(c2.x, c2.y) - dir * (rad2 * 0.8f);
                            
                            Vector3 start = new Vector3(startPos.x, h1, startPos.y);
                            Vector3 end   = new Vector3(endPos.x, h2, endPos.y);
                            
                            riverSegments.Add(RiverFeature.CreateSegment(start, end, r, k * 13.1f));
                        }
                    }
                    // 2. Random Generation (if enabled and no manual path)
                    else if (ws.randomizeFeatures)
                    {
                        // Pick a random start feature (Mountain or Volcano)
                        var potentialStarts = new System.Collections.Generic.List<FeatureSO>();
                        if (volcanoSOs != null) potentialStarts.AddRange(volcanoSOs);
                        if (mountainSOs != null) potentialStarts.AddRange(mountainSOs);
                        
                        if (potentialStarts.Count > 0)
                        {
                            int idx = riverRng.NextInt(0, potentialStarts.Count);
                            FeatureSO startFeat = potentialStarts[idx];
                            
                            // Start Point: Base of the feature
                            Vector2 c1 = startFeat.GetCenter();
                            float rad1 = startFeat.GetRadius();
                            float h1 = startFeat.GetBaseHeight(ws) + r.startHeightOffset;
                            
                            // End Point: Random point at Sea Level
                            float angle = riverRng.NextFloat(0, math.PI * 2);
                            float dist = riverRng.NextFloat(100f, 300f); // Random length
                            float2 dir = new float2(math.cos(angle), math.sin(angle));
                            
                            float2 startPos = new float2(c1.x, c1.y) + dir * (rad1 * 0.8f);
                            float2 endPos = startPos + dir * dist;
                            
                            float h2 = ws.seaLevel + r.endHeightOffset;
                            
                            Vector3 start = new Vector3(startPos.x, h1, startPos.y);
                            Vector3 end   = new Vector3(endPos.x, h2, endPos.y);
                            
                            riverSegments.Add(RiverFeature.CreateSegment(start, end, r, riverRng.NextFloat()));
                        }
                    }
                    // 3. Fallback (Inspector Values)
                    else
                    {
                        riverSegments.Add(r.ToFeature(ws));
                    }
                }
            }
            
            riverCount = riverSegments.Count;
            
            // Total count (excluding lakes/forests/cities for now as they are not FeatureSO)
            int totalFeatures = baseCount + mountainCount + volcanoCount + riverCount + caveRoomCount + caveTunnelCount;
            
            ctx.featureCount = totalFeatures;
            ctx.features = new NativeArray<Feature>(totalFeatures, Allocator.Persistent);
            int featureIndex = 0;

            // 1. Base Island
            if (baseIslandSO != null)
            {
                ctx.features[featureIndex++] = baseIslandSO.ToFeature(ws);
            }

            // 2. Mountains
            if (mountainSOs != null)
            {
                for (int i = 0; i < mountainCount; i++)
                {
                    ctx.features[featureIndex++] = mountainSOs[i].ToFeature(ws);
                }
            }

            // 3. Volcanoes
            if (volcanoSOs != null)
            {
                for (int i = 0; i < volcanoCount; i++)
                {
                    ctx.features[featureIndex++] = volcanoSOs[i].ToFeature(ws);
                }
            }

            // 4. Rivers
            for (int i = 0; i < riverCount; i++)
            {
                ctx.features[featureIndex++] = riverSegments[i];
            }
            
            // 5. Cave Rooms
            for (int i = 0; i < caveRoomCount; i++)
            {
                ctx.features[featureIndex++] = caveRooms[i].ToFeature(ws);
            }

            // 6. Cave Tunnels
            for (int i = 0; i < caveTunnelCount; i++)
            {
                ctx.features[featureIndex++] = caveTunnels[i].ToFeature(ws);
            }


            // Lakes / Forests / Cities:
            // for now, they may still be plain ScriptableObjects and not FeatureSO.
            // If you later make them inherit FeatureSO and implement ToFeature(),
            // you can just add them here exactly like mountains/baseIsland.

            // ... everything up through filling ctx.features stays the same ...

            //----------------------------------------------------
            // 3b. INJECT RANDOMIZATION SEEDS
            //----------------------------------------------------
            // "When checked, every feature generates a unique version of itself."
            // "When not, every generation should be the same."
            
            uint masterSeed;
            if (ws.randomizeFeatures)
            {
                // Randomize every time
                // Fix: Use System.DateTime.Now.Ticks to guarantee uniqueness in Editor (Edit Mode)
                // UnityEngine.Random can be deterministic or "stuck" in ExecuteAlways.
                masterSeed = (uint)(System.DateTime.Now.Ticks % 1000000000); 
            }
            else
            {
                // Deterministic based on global seed
                masterSeed = (uint)ws.globalSeed;
            }

            // Use a deterministic RNG sequence to assign seeds to features
            Unity.Mathematics.Random rng = new Unity.Mathematics.Random(math.max(1, masterSeed));

            for (int i = 0; i < ctx.featureCount; i++)
            {
                Feature f = ctx.features[i];
                float featureSeed = rng.NextFloat(0f, 10000f);

                if (f.type == FeatureType.BaseIsland)
                {
                    // BaseIsland stores seed in data1.y
                    f.data1.y = featureSeed;
                }
                else if (f.type == FeatureType.Mountain)
                {
                    // Mountain stores seed in data2.y
                    f.data2.y = featureSeed;
                }
                else if (f.type == FeatureType.Volcano)
                {
                    // Volcano stores seed in data3.x
                    f.data3.x = featureSeed;
                }
                else if (f.type == FeatureType.River)
                {
                    // River stores seed in data3.x
                    f.data3.x = featureSeed;
                }
                // Add other feature types here as needed

                ctx.features[i] = f;
            }

//----------------------------------------------------
// 4. Chunk bounds (XZ + Y) from generic features
//----------------------------------------------------

// Ensure bounds/SDF registry is populated for any feature types we actually use.
if (baseIslandSO != null)
{
    BaseIslandFeatureAdapter.EnsureRegistered();
}
if (mountainCount > 0)
{
    MountainFeatureAdapter.EnsureRegistered();
}

if (volcanoCount > 0)
{
    VolcanoFeatureAdapter.EnsureRegistered();
}
            if (riverCount > 0)
            {
                RiverFeatureAdapter.EnsureRegistered();
            }
            // Caves - REMOVED
            if (caveRoomCount > 0)
            {
                CaveRoomFeatureAdapter.EnsureRegistered();
            }
            if (caveTunnelCount > 0)
            {
                CaveTunnelFeatureAdapter.EnsureRegistered();
            }

// (Later: when lakes/forests/cities get adapters, call their EnsureRegistered()
//  here too before computing bounds.)

var boundsResult = ChunkBoundsAutoComputer.ComputeFromFeatures(
    ws,
    ctx.features,
    ctx.featureCount
);

if (!boundsResult.valid)
{
    Debug.LogWarning("SdfBootstrapInternal: Chunk bounds computation failed. WorldGenerationWindow will stay disabled.");
}
else
{
    ctx.worldMinXZ = boundsResult.worldMinXZ;
    ctx.worldMaxXZ = boundsResult.worldMaxXZ;

    ctx.minChunkX = boundsResult.minChunkX;
    ctx.maxChunkX = boundsResult.maxChunkX;
    ctx.minChunkZ = boundsResult.minChunkZ;
    ctx.maxChunkZ = boundsResult.maxChunkZ;

    ctx.chunksX = boundsResult.chunksX;
    ctx.chunksZ = boundsResult.chunksZ;

    ctx.minChunkY        = boundsResult.minChunkY;
    ctx.maxChunkY        = boundsResult.maxChunkY;
    ctx.chunksY          = boundsResult.chunksY;
    ctx.minTerrainHeight = boundsResult.minTerrainHeight;
    ctx.maxTerrainHeight = boundsResult.maxTerrainHeight;
}

return ctx;

        }
    }
}
