using System;
using Unity.Collections;
using Unity.Mathematics;

/// <summary>
/// Burst-friendly terrain context used by all SDF evaluators, voxel generators,
/// and editor generation tools.
///
/// Supports:
///   • Full 3D world bounds
///   • Base island parameters
///   • Full feature arrays (old system)
///   • Unified generic feature array (new system)
/// </summary>
[Serializable]
public struct SdfContext
{
    // ------------------------------------------------------------
    // GLOBAL SCALAR WORLD SETTINGS
    // ------------------------------------------------------------
    public float voxelSize;        
    public int chunkSize;          
    public float seaLevel;         
    public float islandRadius;     
    public float maxBaseHeight;    
    public float stepHeight;       

    // ------------------------------------------------------------
    // WORLD EXTENTS (2D XZ)
    // ------------------------------------------------------------
    public float2 worldMinXZ;
    public float2 worldMaxXZ;

    public int minChunkX;
    public int maxChunkX;

    public int minChunkZ;
    public int maxChunkZ;

    public int chunksX;
    public int chunksZ;

    // ------------------------------------------------------------
    // VERTICAL EXTENTS
    // ------------------------------------------------------------
    public float minTerrainHeight;
    public float maxTerrainHeight;

    public int minChunkY;
    public int maxChunkY;
    public int chunksY;

    // ------------------------------------------------------------
    // OLD FEATURE ARRAYS (still here during migration)
    // ------------------------------------------------------------
    public NativeArray<MountainFeatureData> mountains;
    public NativeArray<LakeFeatureData> lakes;
    public NativeArray<ForestFeatureData> forests;
    public NativeArray<CityPlateauFeatureData> cities;

    // ------------------------------------------------------------
    // NEW UNIFIED FEATURE LIST (generic + Burst-friendly)
    // ------------------------------------------------------------
    public NativeArray<Feature> features;
    public int featureCount;

    // ------------------------------------------------------------
    // CLEANUP
    // ------------------------------------------------------------
    public void Dispose()
    {
        if (mountains.IsCreated) mountains.Dispose();
        if (lakes.IsCreated) lakes.Dispose();
        if (forests.IsCreated) forests.Dispose();
        if (cities.IsCreated) cities.Dispose();

        if (features.IsCreated) features.Dispose();
    }
}
