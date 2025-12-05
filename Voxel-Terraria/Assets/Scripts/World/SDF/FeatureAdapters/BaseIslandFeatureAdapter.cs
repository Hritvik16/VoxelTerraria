using Unity.Mathematics;
using UnityEngine;
using VoxelTerraria.World.Generation;
using VoxelTerraria.World.SDF;

public static class BaseIslandFeatureAdapter
{
    // --------------------------------------------------------------------
    // Registration with FeatureBounds3DComputer (agnostic bounds system)
    // --------------------------------------------------------------------
    private static bool s_registered;

    public static void EnsureRegistered()
    {
        if (s_registered)
            return;

        FeatureBounds3DComputer.Register(
            FeatureType.BaseIsland,
            ComputeAnalyticBounds
        );

        s_registered = true;
    }

    // --------------------------------------------------------------------
    // ScriptableObject → Feature packing
    // --------------------------------------------------------------------
    public static Feature ToFeature(BaseIslandFeature so)
    {
        Feature f = new Feature();
        f.type    = FeatureType.BaseIsland;
        f.biomeId = 0;

        f.centerXZ = so.centerXZ;

        // data0 = (radius, maxHeight, coastlineRoughness)
        f.data0 = new float3(
            so.radius,
            so.maxHeight,
            so.coastlineRoughness
        );

        // data1 = (microDetailStrength, 0, 0)
        f.data1 = new float3(
            so.microDetailStrength,
            0f,
            0f
        );

        f.data2 = float3.zero;

        return f;
    }

    // --------------------------------------------------------------------
    // SDF evaluation for bounds & terrain
    // Signature is called from FeatureBounds3DComputer.EvaluateSdf_Fast
    // --------------------------------------------------------------------
    public static float Evaluate(float3 p, in Feature f)
    {
        // Local XZ relative to island center; world Y as-is.
        float2 centerXZ = f.centerXZ;

        float3 localP = new float3(
            p.x - centerXZ.x,
            p.y,
            p.z - centerXZ.y
        );

        float radius    = f.data0.x;
        float height    = f.data0.y;
        float voxelSize = 1f; // Default or passed in if needed
        float seed      = f.data1.y;

        return BaseIslandSdf.Evaluate(localP, radius, height, voxelSize, seed);
    }

    // --------------------------------------------------------------------
    // RAW footprint for biome logic.
    // Called via FeatureBounds3DComputer.EvaluateRaw_Fast
    // --------------------------------------------------------------------
    public static float EvaluateRaw(float3 p, in Feature f)
    {
        float2 centerXZ = f.centerXZ;

        float3 localP = new float3(
            p.x - centerXZ.x,
            p.y,
            p.z - centerXZ.y
        );

        float radius = f.data0.x;
        float seed   = f.data1.y;
        
        return BaseIslandSdf.EvaluateRaw(localP, radius, seed);
    }

    // --------------------------------------------------------------------
    // Analytic bounds for base island (used only by bounds system)
    // --------------------------------------------------------------------
    
    // in Feature f,
    // WorldSettings settings,
    // out float3 center,
    // out float3 halfExtents)
    // {
    //     // Unpack feature data
    //     float radius    = math.max(f.data0.x, 0.01f);
    //     float maxHeight = f.data0.y;

    //     float sea       = settings.seaLevel;
    //     float voxelSize = math.max(settings.voxelSize, 0.0001f);

    //     // ---- Horizontal: exact footprint based on BaseIslandSdf warp ----
    //     // In BaseIslandSdf:
    //     //   warpMax = R * 0.20f;
    //     //   inside island if warpedDist <= R;
    //     // So max |xz| inside island is R + warpMax = R * 1.20f.
    //     float warpMax       = radius * 0.20f;
    //     float horizontalRad = radius + warpMax; // = radius * 1.2f

    //     // ---- Vertical: from sea level up to maxHeight + micro ----
    //     float microAmp = voxelSize * 0.15f;      // same formula as BaseIslandSdf
    //     float minY     = sea;                    // base is around sea level
    //     float maxY     = sea + maxHeight + microAmp;

    //     float centerY  = (minY + maxY) * 0.5f;
    //     float halfY    = (maxY - minY) * 0.5f;

    //     center = new float3(f.centerXZ.x, centerY, f.centerXZ.y);
    //     halfExtents = new float3(horizontalRad, halfY, horizontalRad);
    // }

    private static void ComputeAnalyticBounds(
    in Feature f,
    WorldSettings settings,
    out float3 center,
    out float3 halfExtents)
{
    // Unpack feature data
    float radius    = math.max(f.data0.x, 0.01f);   // islandRadius
    float maxHeight = f.data0.y;                    // maxBaseHeight

    float sea       = settings.seaLevel;
    float voxelSize = math.max(settings.voxelSize, 0.0001f);

    // ---- Horizontal: exact footprint based on BaseIslandSdf warp ----
    // BaseIslandSdf:
    //   warpMax = R * 0.20f;
    //   inside island if warpedDist <= R;
    // => max |xz| inside island is R + warpMax = R * 1.20f.
    float warpMax       = radius * 0.20f;
    float horizontalRad = radius + warpMax; // = radius * 1.2f

    // ---- Vertical: from sea level up to maxHeight + micro ----
    float microAmp = voxelSize * 0.15f;      // same formula as BaseIslandSdf
    float minY     = sea;
    float maxY     = sea + maxHeight + microAmp;

    float centerY  = (minY + maxY) * 0.5f;
    float halfY    = (maxY - minY) * 0.5f;

    center = new float3(f.centerXZ.x, centerY, f.centerXZ.y);
    halfExtents = new float3(horizontalRad, halfY, horizontalRad);
}



}
