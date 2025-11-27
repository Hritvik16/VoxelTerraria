using Unity.Mathematics;
using VoxelTerraria.World.Generation;
using VoxelTerraria.World.SDF;

/// <summary>
/// Adapter converting MountainFeature (SO) or packed Feature ↔ MountainFeatureData.
/// Used by SdfBootstrapInternal (to pack) and CombinedTerrainSdf (to evaluate),
/// and by FeatureBounds3DComputer for bounds + RAW evaluation.
/// </summary>
public static class MountainFeatureAdapter
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
            FeatureType.Mountain,
            ComputeAnalyticBounds
        );

        s_registered = true;
    }

    /// <summary>
    /// Convert a MountainFeature ScriptableObject into a generic Feature.
    /// </summary>
    // public static Feature ToFeature(MountainFeature so, int biomeId = 2)
    // {
    //     Feature f = new Feature();

    //     f.type    = FeatureType.Mountain;
    //     f.biomeId = biomeId;

    //     // Center is stored both as a dedicated field and inside data0
    //     f.centerXZ = so.CenterXZ;

    //     // data0 = center.x, center.y, radius
    //     f.data0 = new float3(so.CenterXZ.x, so.CenterXZ.y, so.Radius);

    //     // data1 = height, ridgeFrequency, ridgeAmplitude
    //     f.data1 = new float3(so.Height, so.RidgeFrequency, so.RidgeAmplitude);

    //     // data2 = warpStrength, (unused), (unused)
    //     f.data2 = new float3(so.WarpStrength, 0f, 0f);

    //     return f;
    // }

    /// <summary>
    /// SDF for geometry: used by CombinedTerrainSdf and bounds system.
    /// Called via FeatureBounds3DComputer.EvaluateSdf_Fast.
    /// </summary>
    public static float EvaluateShape(float3 p, in Feature f)
    {
        MountainFeatureData m = Unpack(f);

        // MountainSdf expects world-space p, and uses m.centerXZ itself.
        return MountainSdf.Evaluate(p, in m);
    }

    /// <summary>
    /// RAW field for biome classification.
    /// Called via FeatureBounds3DComputer.EvaluateRaw_Fast.
    /// </summary>
    // public static float EvaluateRaw(float3 p, in Feature f)
    // {
    //     MountainFeatureData m = Unpack(f);
    //     return MountainSdf.EvaluateRaw(p, in m);
    // }

    // -----------------------------------------
    // Helper: Feature → MountainFeatureData
    // -----------------------------------------
    private static MountainFeatureData Unpack(in Feature f)
    {
        return new MountainFeatureData
        {
            centerXZ        = f.data0.xy,
            radius          = f.data0.z,
            height          = f.data1.x,
            ridgeFrequency  = f.data1.y,
            ridgeAmplitude  = f.data1.z,
            warpStrength    = f.data2.x,
            seed            = f.data2.y,
            archThreshold   = f.data2.z,
            
            overhangStrength = f.data3.x
        };
    }

    // --------------------------------------------------------------------
    // Analytic bounds for mountain (used only by bounds system)
    // --------------------------------------------------------------------
    private static void ComputeAnalyticBounds(
        in Feature f,
        WorldSettings settings,
        out float3 center,
        out float3 halfExtents)
    {
        float2 centerXZ = f.data0.xy;
        float  radius   = f.data0.z;
        float  height   = math.max(f.data1.x, 1f);

        float sea = settings.seaLevel;

        // Horizontal: safe radius based on MountainSdf reject radius
        float rejectRadius = math.max(radius, 1f) * 1.25f;
        float horizontal   = rejectRadius;

        // Vertical: generous envelope to avoid clipping:
        //   minY ≈ sea - radius
        //   maxY ≈ sea + height + radius
        float centerY = sea + height * 0.5f;
        float halfY   = height * 0.5f + radius;

        center      = new float3(centerXZ.x, centerY, centerXZ.y);
        halfExtents = new float3(horizontal, halfY, horizontal);
    }

    /// <summary>
    /// Returns the approximate surface height of the mountain at the given XZ position.
    /// Used for placing cave entrances.
    /// </summary>
    public static float GetSurfaceHeight(float2 xz, in Feature f)
    {
        MountainFeatureData m = Unpack(f);
        
        float dist = math.distance(xz, m.centerXZ);
        if (dist > m.radius) return -999f; // Outside mountain

        // Simple analytic approximation:
        // Height falls off from center to radius.
        // Using a smooth falloff similar to the SDF envelope.
        // SDF uses: k = 1 - (dist/radius)^2 (roughly)
        
        float t = math.saturate(dist / m.radius);
        // Cubic falloff: (1-t^2)^2 or similar
        float envelope = (1f - t * t);
        envelope *= envelope;
        
        // Add some noise approximation if possible, or just return base shape.
        // For entrance placement, base shape is usually enough, 
        // but we can sample a simple noise if we want to be more accurate.
        // Let's stick to the main shape for now to avoid expensive noise calls here.
        
        return m.height * envelope; // + baseHeight (which is usually seaLevel for mountains)
    }
}
