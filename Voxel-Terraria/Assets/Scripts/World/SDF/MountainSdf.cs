using Unity.Mathematics;

public static class MountainSdf
{
    public static float Evaluate(float3 p, in SdfContext ctx)
    {
        // If no mountains, return "no effect" = large positive
        if (!ctx.mountains.IsCreated || ctx.mountains.Length == 0)
            return 9999f;

        float result = 9999f;

        for (int i = 0; i < ctx.mountains.Length; i++)
        {
            var m = ctx.mountains[i];

            // ------------------------------------------------------------
            // Shift point into mountain-local space
            // ------------------------------------------------------------
            float2 center = m.centerXZ;
            float2 localXZ = p.xz - center;

            float dist = math.length(localXZ);

            // Early-out for performance
            if (dist > m.radius + 30f)
                continue;

            // ------------------------------------------------------------
            // Domain warp: distort XZ to make mountain less uniform
            // ------------------------------------------------------------
            float warpX = NoiseUtils.Noise2D(localXZ * 0.02f, 0.3f, m.warpStrength);
            float warpZ = NoiseUtils.Noise2D(localXZ * 0.02f + 100f, 0.3f, m.warpStrength);

            float2 warpedXZ = localXZ + new float2(warpX, warpZ);

            float warpedDist = math.length(warpedXZ);

            // ------------------------------------------------------------
            // Base cone shape
            //
            // If:
            //   warpedDist = 0 => height = m.height
            //   warpedDist = radius => height = 0
            // ------------------------------------------------------------
            float radialT = math.saturate(1f - warpedDist / m.radius);
            float baseShapeHeight = radialT * m.height;

            // This is the core SDF difference:
            // "p.y - mountain surface height"
            float mountainSdf = p.y - baseShapeHeight;

            // ------------------------------------------------------------
            // Ridged noise for rocky peaks
            // ------------------------------------------------------------
            float3 noiseP = new float3(warpedXZ.x, p.y, warpedXZ.y) * m.ridgeFrequency;

            float ridge = NoiseUtils.RidgedNoise3D(
                noiseP,
                1f,                // frequency already baked above
                m.ridgeAmplitude   // amplitude slider in SO
            );

            // Ridge noise lowers the SDF (more terrain)
            mountainSdf -= ridge;

            // ------------------------------------------------------------
            // Smooth fade out at the edges
            // ------------------------------------------------------------
            float smoothFalloff = math.smoothstep(0f, m.radius, warpedDist);
            mountainSdf = math.lerp(mountainSdf, 9999f, smoothFalloff);

            // ------------------------------------------------------------
            // Combine multiple mountains using min()
            // ------------------------------------------------------------
            result = math.min(result, mountainSdf);
        }

        return result;
    }
}
