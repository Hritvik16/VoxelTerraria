using System;
using Unity.Collections;
using Unity.Mathematics;
// MountainFeatureData, LakeFeatureData, etc.

/// <summary>
/// Burst-friendly terrain context used by all SDF evaluators, voxel generators,
/// and editor generation tools.
/// 
/// This version supports FULL 3D world bounds:
///   • min/max chunk X
///   • min/max chunk Y  <-- NEW
///   • min/max chunk Z
///   
/// And also stores measured SDF height extents:
///   • minTerrainHeight
///   • maxTerrainHeight
///   
/// These are computed once by SdfBootstrapInternal via SDF probing.
/// </summary>
/// 
[Serializable]
public struct SdfContext
{
    // ------------------------------------------------------------
    // GLOBAL SCALAR WORLD SETTINGS
    // ------------------------------------------------------------
    public float voxelSize;        // world units per voxel corner
    public int   chunkSize;        // voxel cells per chunk (e.g. 32)
    public float seaLevel;         // sea level Y
    public float islandRadius;     // base island extent in XZ
    public float maxBaseHeight;    // island dome height
    public float stepHeight;       // quantization (0 to disable)

    // ------------------------------------------------------------
    // WORLD EXTENTS (2D XZ)   — computed by ChunkBoundsAutoComputer
    // ------------------------------------------------------------
    public float2 worldMinXZ;      // min X,Z of world
    public float2 worldMaxXZ;      // max X,Z of world

    public int minChunkX;
    public int maxChunkX;

    public int minChunkZ;
    public int maxChunkZ;

    public int chunksX;            
    public int chunksZ;

    // ------------------------------------------------------------
    // VERTICAL EXTENTS (NEW)
    // Vertical chunk bounds automatically computed by SDF sampling.
    // ------------------------------------------------------------
    public float minTerrainHeight;     // lowest SDF surface point detected
    public float maxTerrainHeight;     // highest SDF surface point detected

    public int minChunkY;              // lowest chunk index containing terrain
    public int maxChunkY;              // highest chunk index containing terrain
    public int chunksY;                // vertical chunk count

    // ------------------------------------------------------------
    // FEATURE ARRAYS (Burst-friendly)
    // ------------------------------------------------------------
    public NativeArray<MountainFeatureData>       mountains;
    public NativeArray<LakeFeatureData>           lakes;
    public NativeArray<ForestFeatureData>         forests;
    public NativeArray<CityPlateauFeatureData>    cities;

    // ------------------------------------------------------------
    // CLEANUP
    // ------------------------------------------------------------
    public void Dispose()
    {
        if (mountains.IsCreated) mountains.Dispose();
        if (lakes.IsCreated)     lakes.Dispose();
        if (forests.IsCreated)   forests.Dispose();
        if (cities.IsCreated)    cities.Dispose();
    }
}
