using Unity.Mathematics;

public static class CombinedTerrainSdf
{
    public static float Evaluate(float3 p, in SdfContext ctx)
    {
        float f = 9999f;

        // 1. Base island
        float baseSdf = BaseIslandSdf.Evaluate(p, ctx);
        f = math.min(f, baseSdf);

        // 2. Mountains
        if (ctx.mountains.IsCreated && ctx.mountains.Length > 0)
        {
            float m = MountainSdf.Evaluate(p, ctx);
            f = math.min(f, m);
        }

        // (later you'll add lakes, forest, city here)
        
        return f;
    }
}
