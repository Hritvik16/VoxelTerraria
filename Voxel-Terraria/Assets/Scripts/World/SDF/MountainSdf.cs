using Unity.Mathematics;

public static class MountainSdf
{
    public static float Evaluate(float3 p, in SdfContext ctx)
    {
        if (!ctx.mountains.IsCreated || ctx.mountains.Length == 0)
            return 9999f;

        float result = 9999f;

        for (int i = 0; i < ctx.mountains.Length; i++)
        {
            var m = ctx.mountains[i];

            float2 center = m.centerXZ;
            float2 localXZ = p.xz - center;
            float dist = math.length(localXZ);

            // Ignore far away
            if (dist > m.radius * 1.6f)
                continue;

            // ------------------------------------------------------------
            // 1. DOMAIN WARP
            // ------------------------------------------------------------
            float2 warp = new float2(
                NoiseUtils.Noise2D(localXZ * 0.03f, 1f, m.warpStrength),
                NoiseUtils.Noise2D((localXZ + 200f) * 0.03f, 1f, m.warpStrength)
            );

            float2 warpedXZ = localXZ + warp;
            float warpedDist = math.length(warpedXZ);

            // ------------------------------------------------------------
            // 2. EXPONENTIAL PEAK SHAPING (NO FLAT TOP)
            // ------------------------------------------------------------
            float radial01 = math.saturate(warpedDist / m.radius);

            // Exponential falloff → smooth, non-flat top
            // THIS guarantees no plateau.
            float peak = math.exp(-6f * radial01 * radial01);

            float height = peak * m.height;

            // ------------------------------------------------------------
            // 3. RIDGED NOISE
            // ------------------------------------------------------------
            float ridge = NoiseUtils.RidgedNoise3D(
                new float3(warpedXZ.x, p.y, warpedXZ.y) * m.ridgeFrequency,
                1f,
                m.ridgeAmplitude
            );

            height += ridge;

            // ------------------------------------------------------------
            // 4. EDGE FADE
            // ------------------------------------------------------------
            float fade = math.smoothstep(m.radius * 0.9f, m.radius, warpedDist);
            height = math.lerp(height, 0f, fade);

            // ------------------------------------------------------------
            // 5. FINAL SDF
            // ------------------------------------------------------------
            float sdf = p.y - height;
            result = math.min(result, sdf);
        }

        return result;
    }
}
