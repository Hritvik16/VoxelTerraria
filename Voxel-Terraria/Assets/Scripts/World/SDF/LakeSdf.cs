using Unity.Mathematics;

public static class LakeSdf
{
    public static float Evaluate(float3 p, in SdfContext ctx)
    {
        // If no lakes exist, return neutral value
        if (!ctx.lakes.IsCreated || ctx.lakes.Length == 0)
            return 9999f;

        float result = 9999f;

        for (int i = 0; i < ctx.lakes.Length; i++)
        {
            var l = ctx.lakes[i];

            // ------------------------------------------------------------
            // Local space relative to lake center
            // ------------------------------------------------------------
            float2 localXZ = p.xz - l.centerXZ;
            float dist = math.length(localXZ);

            // Skip far away points (performance)
            if (dist > l.radius + 25f)
                continue;

            // ------------------------------------------------------------
            // Normalized radial parameter
            // t = 0 at center (deepest)
            // t = 1 at radius (shore)
            // ------------------------------------------------------------
            float t = math.saturate(dist / l.radius);

            // Smooth interpolation for basin shape
            float lakeHeight = math.lerp(l.bottomHeight, l.shoreHeight, t);

            // ------------------------------------------------------------
            // SDF: terrain inside basin => negative; outside => positive
            // ------------------------------------------------------------
            float lakeSdf = p.y - lakeHeight;

            // ------------------------------------------------------------
            // Smooth fade out so the lake blends into terrain
            // ------------------------------------------------------------
            float fade = math.smoothstep(l.radius * 0.9f, l.radius, dist);
            lakeSdf = math.lerp(lakeSdf, 9999f, fade);

            // Combine multiple lakes
            result = math.min(result, lakeSdf);
        }

        return result;
    }
}
