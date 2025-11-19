using Unity.Mathematics;

public static class CombinedTerrainSdf
{
    public static float Evaluate(float3 p, in SdfContext ctx)
    {
        // 1. Base island SDF
        float f = BaseIslandSdf.Evaluate(p, ctx);

        // 2. Mountains
        if (ctx.mountains.IsCreated && ctx.mountains.Length > 0)
        {
            float mountainF = MountainSdf.Evaluate(p, ctx);
            f = math.min(f, mountainF);
        }

        // 3. Lakes (cut terrain downward)
        if (ctx.lakes.IsCreated && ctx.lakes.Length > 0)
        {
            float lakeF = LakeSdf.Evaluate(p, ctx);
            f = math.min(f, lakeF);
        }

        // 4. City plateau (height modifier)
        if (ctx.cities.IsCreated && ctx.cities.Length > 0)
        {
            // Extract current terrain height from the current f:
            // p.y - height = f  → height = p.y - f
            float baseHeight = p.y - f;

            // Plateau modifies this height
            float plateauHeight = CityPlateauSdf.Evaluate(p, baseHeight, ctx);

            // Convert back to SDF
            float plateauF = p.y - plateauHeight;

            // Combine
            f = math.min(f, plateauF);
        }

        return f;
    }
}
