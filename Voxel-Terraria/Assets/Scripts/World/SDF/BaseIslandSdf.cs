using Unity.Mathematics;

public static class BaseIslandSdf
{
    /// <summary>
    /// Evaluate the island SDF at world-space position p.
    /// Returns a signed distance: negative = inside terrain, positive = air.
    /// </summary>
    public static float Evaluate(float3 p, in SdfContext ctx)
    {
        float islandRadius   = ctx.islandRadius;     // must exist in SdfContext
        float maxBaseHeight  = ctx.maxBaseHeight;    // must exist in SdfContext

        // ---------------------------------------------------------------------
        // EARLY OUT: outside island radius (safe margin)
        // ---------------------------------------------------------------------
        float distXZ = math.length(p.xz);
        float margin = 20f; // avoids evaluating expensive noise for far-away positions

        if (distXZ > islandRadius + margin)
            return 9999f;  // definitely air

        // ---------------------------------------------------------------------
        // Warp the XZ coordinate with low-frequency noise
        // ---------------------------------------------------------------------
        float2 warpOffset = new float2(
            NoiseUtils.Noise2D(p.xz * 0.01f, 0.1f, 10f),
            NoiseUtils.Noise2D(p.xz * 0.01f + 100, 0.1f, 10f)
        );

        float2 warpedXZ = p.xz + warpOffset;

        float warpedDist = math.length(warpedXZ);

        // ---------------------------------------------------------------------
        // Compute radial height falloff
        // baseHeight = (1 - d / r) * maxHeight
        // ---------------------------------------------------------------------
        float t = math.saturate(1f - (warpedDist / islandRadius));
        float baseHeight = t * maxBaseHeight;

        // ---------------------------------------------------------------------
        // SDF: terrain is where p.y <= baseHeight
        // return positive above surface, negative below
        // ---------------------------------------------------------------------
        return p.y - baseHeight;
    }
}
