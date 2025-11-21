using Unity.Mathematics;

public static class CombinedTerrainSdf
{
    public static float Evaluate(float3 p, in SdfContext ctx)
    {
        if (ctx.mountains.IsCreated && ctx.mountains.Length > 0)
            return MountainSdf.Evaluate(p, ctx);
        // 1. Base island height SDF
        float f = BaseIslandSdf.Evaluate(p, ctx);

        float baseHeight = p.y - f;

        // 2. Mountains
        if (ctx.mountains.IsCreated && ctx.mountains.Length > 0)
        {
            float fm = MountainSdf.Evaluate(p, ctx);
            f = math.min(f, fm);
        }

        return f;
    }
}
