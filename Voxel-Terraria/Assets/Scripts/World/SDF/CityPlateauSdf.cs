using Unity.Mathematics;

public static class CityPlateauSdf
{
    public static float Evaluate(float3 p, float baseHeight, in SdfContext ctx)
    {
        // No city features → no adjustment
        if (!ctx.cities.IsCreated || ctx.cities.Length == 0)
            return baseHeight;

        float modifiedHeight = baseHeight;

        for (int i = 0; i < ctx.cities.Length; i++)
        {
            var c = ctx.cities[i];

            // ------------------------------------------------------------
            // Local XZ to center
            // ------------------------------------------------------------
            float2 localXZ = p.xz - c.centerXZ;
            float dist = math.length(localXZ);

            // Far outside radius → skip
            if (dist > c.radius + 20f)
                continue;

            // ------------------------------------------------------------
            // Normalized distance 0 → center, 1 → edge
            // ------------------------------------------------------------
            float t = math.saturate(dist / c.radius);

            // Plateau mask = 1 in center, fades to 0 at radius
            float mask = 1f - math.smoothstep(0.6f, 1f, t);

            // ------------------------------------------------------------
            // Blend baseHeight → plateauHeight
            // ------------------------------------------------------------
            float blended = math.lerp(baseHeight, c.plateauHeight, mask);

            // If multiple city features overlap, take the minimum height
            modifiedHeight = math.min(modifiedHeight, blended);
        }

        return modifiedHeight;
    }
    public static float DebugEvaluate(float3 p, in SdfContext ctx)
{
    if (!ctx.cities.IsCreated || ctx.cities.Length == 0)
        return 9999f;

    float closest = 9999f;

    for (int i = 0; i < ctx.cities.Length; i++)
    {
        var c = ctx.cities[i];
        float2 localXZ = p.xz - c.centerXZ;
        float dist = math.length(localXZ);

        if (dist > c.radius + 20f)
            continue;

        float t = math.saturate(dist / c.radius);
        float mask = 1f - math.smoothstep(0.6f, 1f, t);

        // THIS LINE is changed: use plateau height always,
        // instead of blending with p.y
        float desiredHeight = c.plateauHeight;

        // Distance to the plateau Y-plane
        float sdf = math.abs(p.y - desiredHeight);

        // distance in XZ also matters — fade out ambience
        sdf += dist;

        closest = math.min(closest, sdf);
    }

    return closest;
}


}
