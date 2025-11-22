using Unity.Mathematics;

public static class MountainSdf
{
    /// <summary>
    /// Full terrain SDF for ridge-style mountains.
    /// - Each mountain has a pseudo-random ridge direction (range-like).
    /// - Footprint is an ellipse contained inside Radius.
    /// - Height is clamped to [0, m.height].
    /// SDF: negative = inside terrain, positive = air.
    /// </summary>
    public static float Evaluate(float3 p, in SdfContext ctx)
    {
        if (!ctx.mountains.IsCreated || ctx.mountains.Length == 0)
            return 9999f;

        float outSdf = 9999f;

        for (int i = 0; i < ctx.mountains.Length; i++)
        {
            var m = ctx.mountains[i];

            float2 center = m.centerXZ;
            float2 local  = p.xz - center;
            float  dist   = math.length(local);

            // Fast reject: ellipse is fully inside this circle
            float rejectRadius = m.radius * 1.25f;
            if (dist > rejectRadius)
                continue;

            //--------------------------------------------------
            // 1. Pseudo-random ridge orientation per mountain
            //--------------------------------------------------
            // Use centerXZ as a seed so it’s stable per mountain.
            float orientNoise = NoiseUtils.Noise2D(center * 0.013f, 1f, 1f); // ~[-1,1]
            float angle       = orientNoise * math.PI;                        // [-π, π]

            float2 dir  = math.normalize(new float2(math.cos(angle), math.sin(angle))); // along-ridge
            float2 perp = new float2(-dir.y, dir.x);                                    // across-ridge

            float along   = math.dot(local, dir);
            float across  = math.dot(local, perp);

            //--------------------------------------------------
            // 2. Domain warp in ridge space (subtle)
            //--------------------------------------------------
            float2 ridgePos = new float2(along, across);

            float warpA = NoiseUtils.Noise2D(ridgePos * 0.05f, 1f, m.warpStrength);
            float warpB = NoiseUtils.Noise2D((ridgePos + 200f) * 0.05f, 1f, m.warpStrength);

            along  += warpA * 0.5f;     // small offsets so we don’t leave radius
            across += warpB * 0.5f;

            //--------------------------------------------------
            // 3. Elliptical footprint (range)
            //    long axis = radius
            //    short axis = radius * 0.4
            //--------------------------------------------------
            float longRadius  = m.radius;
            float shortRadius = m.radius * 0.4f;

            float alongNorm  = along  / longRadius;
            float acrossNorm = across / shortRadius;

            float radial = math.sqrt(alongNorm * alongNorm + acrossNorm * acrossNorm);
            float t      = 1f - radial;              // 1 at center, 0 at ellipse edge
            if (t <= 0f)
                continue;                            // outside ellipse → no mountain

            // Profile: broad base, sharper top
            float profile = math.pow(t, 1.4f);

            //--------------------------------------------------
            // 4. Base height + ridged detail
            //--------------------------------------------------
            float baseHeight = profile * m.height;

            // Ridge noise in ridge space (stronger where profile is high)
            float ridge = NoiseUtils.RidgedNoise3D(
                new float3(along * 0.3f, p.y, across * 0.3f),
                m.ridgeFrequency,
                m.ridgeAmplitude * profile
            );

            float height = baseHeight + ridge;

            // Clamp to [0, m.height] so we obey the SO bounds
            height = math.clamp(height, 0f, m.height);

            //--------------------------------------------------
            // 5. Final SDF for this mountain
            //--------------------------------------------------
            float sdf = p.y - height;
            outSdf = math.min(outSdf, sdf);
        }

        return outSdf;
    }

    /// <summary>
    /// RAW mountain field for biomes:
    /// - Uses the SAME ridge orientation, warp, and ellipse as Evaluate().
    /// - Ignores vertical shape (height/ridges).
    /// Returns min(raw) over all mountains:
    ///   raw < 0 => inside mountain footprint
    ///   raw > 0 => outside
    /// </summary>
    public static float EvaluateRaw(float3 p, in SdfContext ctx)
    {
        if (!ctx.mountains.IsCreated || ctx.mountains.Length == 0)
            return 9999f;

        float outRaw = 9999f;

        for (int i = 0; i < ctx.mountains.Length; i++)
        {
            var m = ctx.mountains[i];

            float2 center = m.centerXZ;
            float2 local  = p.xz - center;

            // Same orientation as Evaluate
            float orientNoise = NoiseUtils.Noise2D(center * 0.013f, 1f, 1f);
            float angle       = orientNoise * math.PI;

            float2 dir  = math.normalize(new float2(math.cos(angle), math.sin(angle)));
            float2 perp = new float2(-dir.y, dir.x);

            float along  = math.dot(local, dir);
            float across = math.dot(local, perp);

            // Same warp as Evaluate
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
            float raw = radial - 1f;

            outRaw = math.min(outRaw, raw);
        }

        return outRaw;
    }
}
