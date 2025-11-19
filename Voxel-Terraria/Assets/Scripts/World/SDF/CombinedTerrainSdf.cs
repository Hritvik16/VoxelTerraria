using Unity.Mathematics;

public static class CombinedTerrainSdf
{
    public static float Evaluate(float3 p, in SdfContext ctx)
    {
        // 1. Base island
        float f = BaseIslandSdf.Evaluate(p, ctx);

        // Extract the current terrain height before features
        float baseHeight = p.y - f;

        // 2. Mountains
        if (ctx.mountains.IsCreated && ctx.mountains.Length > 0)
        {
            float mountainF = MountainSdf.Evaluate(p, ctx);
            f = math.min(f, mountainF);
        }

        // 3. Lakes relative to base height
        if (ctx.lakes.IsCreated && ctx.lakes.Length > 0)
        {
            float lakeF = LakeSdfRelative.Evaluate(p, baseHeight, ctx);
            f = math.min(f, lakeF);
        }

        // 4. City plateau
        if (ctx.cities.IsCreated && ctx.cities.Length > 0)
        {
            float plateauHeight = CityPlateauSdf.Evaluate(p, baseHeight, ctx);
            float plateauF = p.y - plateauHeight;
            f = math.min(f, plateauF);
        }

        return f;
    }
}
