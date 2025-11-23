using Unity.Mathematics;

public static class MountainSdf
{
    /// <summary>
    /// Single-mountain SDF:
    /// negative = inside terrain, positive = air.
    /// </summary>
    public static float Evaluate(float3 p, in MountainFeatureData m)
    {
        float2 center = m.centerXZ;
        float2 local  = p.xz - center;
        float  dist   = math.length(local);

        // Fast reject: ellipse is fully inside this circle
        float rejectRadius = m.radius * 1.25f;
        if (dist > rejectRadius)
            return 9999f;

        //--------------------------------------------------
        // 1. Pseudo-random ridge orientation per mountain
        //--------------------------------------------------
        float orientNoise = NoiseUtils.Noise2D(center * 0.013f, 1f, 1f);
        float angle       = orientNoise * math.PI;

        float2 dir  = math.normalize(new float2(math.cos(angle), math.sin(angle)));
        float2 perp = new float2(-dir.y, dir.x);

        float along  = math.dot(local, dir);
        float across = math.dot(local, perp);

        //--------------------------------------------------
        // 2. Domain warp in ridge space (subtle)
        //--------------------------------------------------
        float2 ridgePos = new float2(along, across);

        float warpA = NoiseUtils.Noise2D(ridgePos * 0.05f, 1f, m.warpStrength);
        float warpB = NoiseUtils.Noise2D((ridgePos + 200f) * 0.05f, 1f, m.warpStrength);

        along  += warpA * 0.5f;
        across += warpB * 0.5f;

        //--------------------------------------------------
        // 3. Elliptical footprint (range)
        //--------------------------------------------------
        float longRadius  = m.radius;
        float shortRadius = m.radius * 0.4f;

        float alongNorm  = along  / longRadius;
        float acrossNorm = across / shortRadius;

        float radial = math.sqrt(alongNorm * alongNorm + acrossNorm * acrossNorm);
        float t      = 1f - radial;          // 1 at center, 0 at ellipse edge
        if (t <= 0f)
            return 9999f;                    // outside ellipse → no mountain

        // Profile: broad base, sharper top
        float profile = math.pow(t, 1.4f);

        //--------------------------------------------------
        // 4. Base height + ridged detail
        //--------------------------------------------------
        float baseHeight = profile * m.height;

        float ridge = NoiseUtils.RidgedNoise3D(
            new float3(along * 0.3f, p.y, across * 0.3f),
            m.ridgeFrequency,
            m.ridgeAmplitude * profile
        );

        float height = baseHeight + ridge;

        // Clamp to [0, m.height]
        height = math.clamp(height, 0f, m.height);

        //--------------------------------------------------
        // 5. Final SDF
        //--------------------------------------------------
        return p.y - height;
    }

    /// <summary>
    /// RAW field for biomes: negative inside footprint, positive outside.
    /// Matches the same orientation & warp as Evaluate().
    /// </summary>
    public static float EvaluateRaw(float3 p, in MountainFeatureData m)
    {
        float2 center = m.centerXZ;
        float2 local  = p.xz - center;

        float orientNoise = NoiseUtils.Noise2D(center * 0.013f, 1f, 1f);
        float angle       = orientNoise * math.PI;

        float2 dir  = math.normalize(new float2(math.cos(angle), math.sin(angle)));
        float2 perp = new float2(-dir.y, dir.x);

        float along  = math.dot(local, dir);
        float across = math.dot(local, perp);

        float2 ridgePos = new float2(along, across);

        float warpA = NoiseUtils.Noise2D(ridgePos * 0.05f, 1f, m.warpStrength);
        float warpB = NoiseUtils.Noise2D((ridgePos + 200f) * 0.05f, 1f, m.warpStrength);

        along  += warpA * 0.5f;
        across += warpB * 0.5f;

        float longRadius  = m.radius;
        float shortRadius = m.radius * 0.4f;

        float alongNorm  = along  / longRadius;
        float acrossNorm = across / shortRadius;

        float radial = math.sqrt(alongNorm * alongNorm + acrossNorm * acrossNorm);

        // raw: negative inside ellipse, positive outside
        return radial - 1f;
    }

    /// <summary>
    /// Context-based multi-mountain SDF (for legacy code / BiomeEvaluator).
    /// </summary>
    public static float Evaluate(float3 p, in SdfContext ctx)
    {
        if (!ctx.mountains.IsCreated || ctx.mountains.Length == 0)
            return 9999f;

        float outSdf = 9999f;

        for (int i = 0; i < ctx.mountains.Length; i++)
        {
            var m = ctx.mountains[i];
            float s = Evaluate(p, in m);
            outSdf = math.min(outSdf, s);
        }

        return outSdf;
    }

    /// <summary>
    /// Context-based RAW field (for legacy biome logic).
    /// </summary>
    public static float EvaluateRaw(float3 p, in SdfContext ctx)
    {
        if (!ctx.mountains.IsCreated || ctx.mountains.Length == 0)
            return 9999f;

        float outRaw = 9999f;

        for (int i = 0; i < ctx.mountains.Length; i++)
        {
            var m = ctx.mountains[i];
            float r = EvaluateRaw(p, in m);
            outRaw = math.min(outRaw, r);
        }

        return outRaw;
    }

    /// <summary>
    /// Kept for your Raw3DDebug script. Just forwards to the RAW context-based version.
    /// </summary>
    public static float EvaluateRaw3D(float3 p, in SdfContext ctx)
    {
        return EvaluateRaw(p, in ctx);
    }
}
