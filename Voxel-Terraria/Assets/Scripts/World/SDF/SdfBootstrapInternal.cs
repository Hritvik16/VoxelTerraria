using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
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
            CityPlateauFeature[] citySOs
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
            int mountainCount = mountainSOs != null ? mountainSOs.Length : 0;
            ctx.mountains = new NativeArray<MountainFeatureData>(mountainCount, Allocator.Persistent);
            for (int i = 0; i < mountainCount; i++)
            {
                var so = mountainSOs[i];
                ctx.mountains[i] = new MountainFeatureData
                {
                    centerXZ       = new float2(so.CenterXZ.x, so.CenterXZ.y),
                    radius         = so.Radius,
                    height         = so.Height,
                    ridgeFrequency = so.RidgeFrequency,
                    ridgeAmplitude = so.RidgeAmplitude,
                    warpStrength   = so.WarpStrength
                };
            }

            // Lakes
            int lakeCount = lakeSOs != null ? lakeSOs.Length : 0;
            ctx.lakes = new NativeArray<LakeFeatureData>(lakeCount, Allocator.Persistent);
            for (int i = 0; i < lakeCount; i++)
            {
                var so = lakeSOs[i];
                ctx.lakes[i] = new LakeFeatureData
                {
                    centerXZ     = new float2(so.CenterXZ.x, so.CenterXZ.y),
                    radius       = so.Radius,
                    bottomHeight = so.BottomHeight,
                    shoreHeight  = so.ShoreHeight
                };
            }

            // Forests
            int forestCount = forestSOs != null ? forestSOs.Length : 0;
            ctx.forests = new NativeArray<ForestFeatureData>(forestCount, Allocator.Persistent);
            for (int i = 0; i < forestCount; i++)
            {
                var so = forestSOs[i];
                ctx.forests[i] = new ForestFeatureData
                {
                    centerXZ    = new float2(so.CenterXZ.x, so.CenterXZ.y),
                    radius      = so.Radius,
                    treeDensity = so.TreeDensity
                };
            }

            // Cities
            int cityCount = citySOs != null ? citySOs.Length : 0;
            ctx.cities = new NativeArray<CityPlateauFeatureData>(cityCount, Allocator.Persistent);
            for (int i = 0; i < cityCount; i++)
            {
                var so = citySOs[i];
                ctx.cities[i] = new CityPlateauFeatureData
                {
                    centerXZ      = new float2(so.CenterXZ.x, so.CenterXZ.y),
                    radius        = so.Radius,
                    plateauHeight = so.PlateauHeight
                };
            }

            //----------------------------------------------------
            // 3. NEW GENERIC FEATURE LIST (agnostic, used for bounds)
            //----------------------------------------------------
            int baseCount     = baseIslandSO != null ? 1 : 0;
            int totalFeatures = baseCount + mountainCount + lakeCount + forestCount + cityCount;

            ctx.features = new NativeArray<Feature>(totalFeatures, Allocator.Persistent);
            ctx.featureCount = totalFeatures;

            int index = 0;

            // Base island → Feature
            if (baseIslandSO != null)
            {
                ctx.features[index++] = baseIslandSO.ToFeature();
            }

            // Mountains → Feature
            for (int i = 0; i < mountainCount; i++)
            {
                // MountainFeature : FeatureSO and implements ToFeature()
                ctx.features[index++] = mountainSOs[i].ToFeature();
            }

            // Lakes / Forests / Cities:
            // for now, they may still be plain ScriptableObjects and not FeatureSO.
            // If you later make them inherit FeatureSO and implement ToFeature(),
            // you can just add them here exactly like mountains/baseIsland.

            // ... everything up through filling ctx.features stays the same ...

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
