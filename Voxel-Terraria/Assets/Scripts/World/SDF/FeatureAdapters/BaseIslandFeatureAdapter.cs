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
            ComputeAnalyticBounds,
            Evaluate
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
    // Signature matches FeatureBounds3DComputer.SdfEvalFunc
    // --------------------------------------------------------------------
 

public static float Evaluate(float3 p, in Feature f)
{
    SdfContext ctx = new SdfContext
    {
        islandRadius     = f.data0.x,
        maxBaseHeight    = f.data0.y,
        voxelSize        = 1f,  // irrelevant for this SDF
        seaLevel         = 0f   // if needed
    };

    return BaseIslandSdf.Evaluate(p, ctx);
}

    // --------------------------------------------------------------------
    // Analytic bounds for base island (used only by bounds system)
    // --------------------------------------------------------------------
    private static void ComputeAnalyticBounds(
        in Feature f,
        WorldSettings settings,
        out float3 center,
        out float3 halfExtents)
    {
        // Unpack
        float radius      = f.data0.x;
        float maxHeight   = f.data0.y;
        float microDetail = f.data1.x;

        float sea = settings.seaLevel;

        // Horizontal: radius + max warp (0.2R)
        float R        = math.max(radius, 1f);
        float warpMax  = R * 0.20f;
        // // Full safe horizontal envelope
float horizontal = R * 1.5f;   // 150% of radius = ALWAYS contains full warp

        // Vertical: based on BaseIslandSdf math
        float microAmp = 0.15f * microDetail;
        float maxTop   = maxHeight * 0.6f + microAmp;

        float centerY = sea + maxTop * 0.5f;

        center      = new float3(f.centerXZ.x, centerY, f.centerXZ.y);
        halfExtents = new float3(horizontal, maxTop * 0.5f, horizontal);
    }
}
