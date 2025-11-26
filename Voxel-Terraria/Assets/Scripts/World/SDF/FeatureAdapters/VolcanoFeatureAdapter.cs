using Unity.Mathematics;
using UnityEngine;
using VoxelTerraria.World.Generation;
using VoxelTerraria.World.SDF;

/// <summary>
/// Adapter converting packed Feature ↔ VolcanoFeatureData.
/// Used by SdfBootstrapInternal (via FeatureSO.ToFeature),
/// CombinedTerrainSdf (via FeatureBounds3DComputer),
/// and bounds system.
/// </summary>
public static class VolcanoFeatureAdapter
{
    // --------------------------------------------------------------------
    // Registration with FeatureBounds3DComputer
    // --------------------------------------------------------------------
    private static bool s_registered;

    public static void EnsureRegistered()
    {
        if (s_registered)
            return;

        FeatureBounds3DComputer.Register(
            FeatureType.Volcano,
            ComputeAnalyticBounds
            
                // RAW field for biomes; hook later if you want
        );

        s_registered = true;
    }

    /// <summary>
    /// SDF for geometry: used by CombinedTerrainSdf and bounds system.
    /// Called via FeatureBounds3DComputer.EvaluateSdf_Fast.
    /// </summary>
    public static float EvaluateShape(float3 p, in Feature f)
    {
        VolcanoFeatureData v = Unpack(f);
        return VolcanoSdf.Evaluate(p, in v);
    }

    // -----------------------------------------
    // Helper: Feature → VolcanoFeatureData
    // -----------------------------------------
    private static VolcanoFeatureData Unpack(in Feature f)
    {
        return new VolcanoFeatureData
        {
            centerXZ      = f.centerXZ,

            radius        = f.data0.x,
            height        = f.data0.y,
            baseHeight    = f.data0.z,

            craterRadius  = f.data1.x,
            craterDepth   = f.data1.y,
            pathWidth     = f.data1.z,

            pathDepth     = f.data2.x,
            pathNoiseFreq = f.data2.y,
            pathNoiseAmp  = f.data2.z,
            seed          = f.data3.x
        };
    }

    // --------------------------------------------------------------------
    // Analytic bounds for volcano (used only by bounds system)
    // --------------------------------------------------------------------
    private static void ComputeAnalyticBounds(
        in Feature f,
        WorldSettings settings,
        out float3 center,
        out float3 halfExtents)
    {
        float2 centerXZ   = f.centerXZ;
        float  radius     = f.data0.x;
        float  height     = math.max(f.data0.y, 1f);
        float  baseHeight = f.data0.z;

        // Horizontal extents: radius + a bit for crater / path noise
        float horizontal = radius * 1.4f;

        // Vertical extents: baseHeight .. baseHeight + height (+ some padding)
        float minY = baseHeight - 5f;
        float maxY = baseHeight + height + 5f;

        float centerY = (minY + maxY) * 0.5f;
        float halfY   = (maxY - minY) * 0.5f;

        center      = new float3(centerXZ.x, centerY, centerXZ.y);
        halfExtents = new float3(horizontal, halfY, horizontal);
    }
}
