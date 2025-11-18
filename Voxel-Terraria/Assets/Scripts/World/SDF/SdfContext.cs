using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

// ------------------------------------------------------------
// Burst-friendly feature data containers
// ------------------------------------------------------------
[System.Serializable]
public struct MountainFeatureData
{
    public float2 centerXZ;
    public float radius;
    public float height;
    public float ridgeFrequency;
    public float ridgeAmplitude;
    public float warpStrength;
}

[System.Serializable]
public struct LakeFeatureData
{
    public float2 centerXZ;
    public float radius;
    public float bottomHeight;
    public float shoreHeight;
}

[System.Serializable]
public struct ForestFeatureData
{
    public float2 centerXZ;
    public float radius;
    public float treeDensity;
}

[System.Serializable]
public struct CityPlateauFeatureData
{
    public float2 centerXZ;
    public float radius;
    public float plateauHeight;
}

// ------------------------------------------------------------
// SDF Context (passed into all SDF evaluation jobs)
// ------------------------------------------------------------
public struct SdfContext
{
    // -------------------------------
    // World settings
    // -------------------------------
    public int worldWidth;
    public int worldDepth;
    public int worldHeight;

    public float voxelSize;
    public int chunkSize;

    public float seaLevel;

    // -------------------------------
    // Feature arrays
    // -------------------------------
    public NativeArray<MountainFeatureData> mountains;
    public NativeArray<LakeFeatureData> lakes;
    public NativeArray<ForestFeatureData> forests;
    public NativeArray<CityPlateauFeatureData> cities;

    // -------------------------------
    // Lifecycle
    // -------------------------------
    public void Dispose()
    {
        if (mountains.IsCreated) mountains.Dispose();
        if (lakes.IsCreated) lakes.Dispose();
        if (forests.IsCreated) forests.Dispose();
        if (cities.IsCreated) cities.Dispose();
    }
}

// ------------------------------------------------------------
// Helper for building SdfContext from ScriptableObjects
// ------------------------------------------------------------
public static class SdfBootstrap
{
    public static SdfContext Build(WorldSettings world, 
                                   MountainFeature[] mountainSOs,
                                   LakeFeature[] lakeSOs,
                                   ForestFeature[] forestSOs,
                                   CityPlateauFeature[] citySOs,
                                   Allocator allocator = Allocator.Persistent)
    {
        SdfContext ctx = new SdfContext
        {
            worldWidth = world.worldWidth,
            worldDepth = world.worldDepth,
            worldHeight = world.worldHeight,

            voxelSize = world.voxelSize,
            chunkSize = world.chunkSize,
            seaLevel = world.seaLevel,

            mountains = new NativeArray<MountainFeatureData>(mountainSOs.Length, allocator),
            lakes     = new NativeArray<LakeFeatureData>(lakeSOs.Length, allocator),
            forests   = new NativeArray<ForestFeatureData>(forestSOs.Length, allocator),
            cities    = new NativeArray<CityPlateauFeatureData>(citySOs.Length, allocator)
        };

        // --------------------------------------
        // Convert Mountain features
        // --------------------------------------
        for (int i = 0; i < mountainSOs.Length; i++)
        {
            ctx.mountains[i] = new MountainFeatureData
            {
                centerXZ = new float2(mountainSOs[i].CenterXZ.x, mountainSOs[i].CenterXZ.y),
                radius = mountainSOs[i].Radius,
                height = mountainSOs[i].Height,
                ridgeFrequency = mountainSOs[i].RidgeFrequency,
                ridgeAmplitude = mountainSOs[i].RidgeAmplitude,
                warpStrength   = mountainSOs[i].WarpStrength
            };
        }

        // --------------------------------------
        // Convert Lake features
        // --------------------------------------
        for (int i = 0; i < lakeSOs.Length; i++)
        {
            ctx.lakes[i] = new LakeFeatureData
            {
                centerXZ = new float2(lakeSOs[i].CenterXZ.x, lakeSOs[i].CenterXZ.y),
                radius = lakeSOs[i].Radius,
                bottomHeight = lakeSOs[i].BottomHeight,
                shoreHeight  = lakeSOs[i].ShoreHeight
            };
        }

        // --------------------------------------
        // Convert Forest features
        // --------------------------------------
        for (int i = 0; i < forestSOs.Length; i++)
        {
            ctx.forests[i] = new ForestFeatureData
            {
                centerXZ = new float2(forestSOs[i].CenterXZ.x, forestSOs[i].CenterXZ.y),
                radius = forestSOs[i].Radius,
                treeDensity = forestSOs[i].TreeDensity
            };
        }

        // --------------------------------------
        // Convert City Plateau features
        // --------------------------------------
        for (int i = 0; i < citySOs.Length; i++)
        {
            ctx.cities[i] = new CityPlateauFeatureData
            {
                centerXZ = new float2(citySOs[i].CenterXZ.x, citySOs[i].CenterXZ.y),
                radius = citySOs[i].Radius,
                plateauHeight = citySOs[i].PlateauHeight
            };
        }

        return ctx;
    }
}
