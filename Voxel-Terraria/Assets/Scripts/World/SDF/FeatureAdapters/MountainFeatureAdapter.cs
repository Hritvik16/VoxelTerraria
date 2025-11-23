using Unity.Mathematics;
using VoxelTerraria.World.Generation;
using VoxelTerraria.World.SDF;

/// <summary>
/// Adapter converting MountainFeature (SO) or packed Feature ↔ MountainFeatureData.
/// Used by SdfBootstrapInternal (to pack) and CombinedTerrainSdf (to evaluate),
/// and now also by the agnostic bounds system via FeatureBounds3DComputer.
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
            ComputeAnalyticBounds,
            EvaluateShape
        );

        s_registered = true;
    }

    /// <summary>
    /// Convert a MountainFeature ScriptableObject into a generic Feature.
    /// </summary>
    public static Feature ToFeature(MountainFeature so, int biomeId = 2)
    {
        Feature f = new Feature();

        f.type    = FeatureType.Mountain;
        f.biomeId = biomeId;

        // Center is stored both as a dedicated field and inside data0
        f.centerXZ = so.CenterXZ;

        // data0 = center.x, center.y, radius
        f.data0 = new float3(so.CenterXZ.x, so.CenterXZ.y, so.Radius);

        // data1 = height, ridgeFrequency, ridgeAmplitude
        f.data1 = new float3(so.Height, so.RidgeFrequency, so.RidgeAmplitude);

        // data2 = warpStrength, (unused), (unused)
        f.data2 = new float3(so.WarpStrength, 0f, 0f);

        return f;
    }

    /// <summary>
    /// SDF for geometry: used by CombinedTerrainSdf and bounds system.
    /// Signature matches FeatureBounds3DComputer.SdfEvalFunc.
    /// </summary>
    public static float EvaluateShape(float3 p, in Feature f)
    {
        MountainFeatureData m = Unpack(f);
        return MountainSdf.Evaluate(p, in m);
    }

    /// <summary>
    /// RAW field for biome classification (not wired up yet, but ready).
    /// </summary>
    public static float EvaluateRaw(float3 p, in Feature f)
    {
        MountainFeatureData m = Unpack(f);
        return MountainSdf.EvaluateRaw(p, in m);
    }

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
            warpStrength    = f.data2.x
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
}
