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
            RiverFeature[] riverSOs
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
            int lakeCount = lakeSOs != null ? lakeSOs.Length : 0;
            int forestCount = forestSOs != null ? forestSOs.Length : 0;
            int cityCount = citySOs != null ? citySOs.Length : 0;
            int volcanoCount  = volcanoSOs != null ? volcanoSOs.Length : 0;
            int riverCount    = riverSOs != null ? riverSOs.Length : 0;

            int baseCount     = baseIslandSO != null ? 1 : 0;
            int totalFeatures = baseCount + mountainCount + lakeCount + forestCount + cityCount + volcanoCount + riverCount;

            ctx.features = new NativeArray<Feature>(totalFeatures, Allocator.Persistent);
            ctx.featureCount = totalFeatures;

            int index = 0;

            // Base island → Feature
            if (baseIslandSO != null)
            {
                ctx.features[index++] = baseIslandSO.ToFeature(ws);
            }

            // Mountains → Feature
            for (int i = 0; i < mountainCount; i++)
            {
                // MountainFeature : FeatureSO and implements ToFeature()
                ctx.features[index++] = mountainSOs[i].ToFeature(ws);
            }

            // Volcanoes
            for (int i = 0; i < volcanoCount; i++)
            {
                ctx.features[index++] = volcanoSOs[i].ToFeature(ws);
            }
            
            // Rivers
            for (int i = 0; i < riverCount; i++)
            {
                ctx.features[index++] = riverSOs[i].ToFeature(ws);
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
                masterSeed = (uint)UnityEngine.Random.Range(1, 1000000);
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
