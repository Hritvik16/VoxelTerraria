using Unity.Mathematics;

public static class BaseIslandSdf
{
    /// <summary>
    /// Full terrain SDF for the island:
    /// - warped coastline
    /// - dome height
    /// - micro detail
    /// No hard cutoff; height naturally falls to 0 outside the island.
    /// SDF convention: negative = inside terrain, positive = air.
    /// </summary>
    public static float Evaluate(float3 p, in SdfContext ctx)
    {
        float R = ctx.islandRadius;
        float H = ctx.maxBaseHeight;

        float2 xz = p.xz;

        //------------------------------------------------------
        // 1. Coastline warp (same as before)
        //------------------------------------------------------
        float warpFreq = 2f / R;
        float warpAmp  = R * 0.35f;

        float2 warpOffset = new float2(
            NoiseUtils.Noise2D(xz * warpFreq, 1f, 1f),
            NoiseUtils.Noise2D((xz + 200f) * warpFreq, 1f, 1f)
        ) * warpAmp;

        // Clamp warp so it doesn't explode
        float warpLen = math.length(warpOffset);
        float warpMax = R * 0.20f;
        if (warpLen > warpMax)
            warpOffset *= warpMax / warpLen;

        //------------------------------------------------------
        // 2. Recompute warped distance
        //------------------------------------------------------
        float2 warpedXZ  = xz + warpOffset;
        float  warpedDist = math.length(warpedXZ);

        //------------------------------------------------------
        // 3. Dome falloff (controls height)
        //------------------------------------------------------
        float t = math.saturate(1f - warpedDist / R); // 1 at center, 0 at warped radius
        t = math.pow(t, 1.5f);

        // Base dome height
        float finalHeight = t * H * 0.6f;

        //------------------------------------------------------
        // 4. Micro-terrain detail (only where t > 0)
        //------------------------------------------------------
        float microFreq = 1f / R;
        float microAmp  = ctx.voxelSize * 0.15f;

        float micro = NoiseUtils.Noise2D(
            p.xz * microFreq,
            2f,
            0.5f
        ) * microAmp * t;   // scaled by t so it vanishes outside island

        finalHeight += micro;

        //------------------------------------------------------
        // 5. Return SDF (terrain below the height)
        //------------------------------------------------------
        return p.y - finalHeight;
    }

    /// <summary>
    /// RAW island field for biome / region logic.
    /// Matches the warped coastline footprint used in Evaluate().
    /// Negative = inside island footprint, positive = outside.
    /// No height, no micro detail, just the 2D warped boundary.
    /// </summary>
    public static float EvaluateRaw(float3 p, in SdfContext ctx)
    {
        float R = ctx.islandRadius;
        float2 xz = p.xz;

        // Same warp as Evaluate() so footprint matches the actual island shape
        float warpFreq = 2f / R;
        float warpAmp  = R * 0.35f;

        float2 warpOffset = new float2(
            NoiseUtils.Noise2D(xz * warpFreq, 1f, 1f),
            NoiseUtils.Noise2D((xz + 200f) * warpFreq, 1f, 1f)
        ) * warpAmp;

        float warpLen = math.length(warpOffset);
        float warpMax = R * 0.20f;
        if (warpLen > warpMax)
            warpOffset *= (warpMax / warpLen);

        float2 warpedXZ  = xz + warpOffset;
        float  warpedDist = math.length(warpedXZ);

        // Raw SDF: distance from warped coastline
        // negative = inside island, positive = outside
        return warpedDist - R;
    }
}
