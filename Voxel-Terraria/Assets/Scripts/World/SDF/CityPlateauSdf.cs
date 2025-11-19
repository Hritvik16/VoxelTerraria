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
}
