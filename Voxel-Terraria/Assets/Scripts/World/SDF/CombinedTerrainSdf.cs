using Unity.Mathematics;

public static class CombinedTerrainSdf
{
    public static float Evaluate(float3 p, ref SdfContext ctx)
    {
        float sdf = 9999f;

        for (int i = 0; i < ctx.featureCount; i++)
        {
            Feature f = ctx.features[i];

            switch (f.type)
            {
                case FeatureType.BaseIsland:
                    sdf = math.min(sdf, BaseIslandFeatureAdapter.Evaluate(p, f));
                    break;

                case FeatureType.Mountain:
                    sdf = math.min(sdf, MountainFeatureAdapter.EvaluateShape(p, f));
                    break;

                // Later: lakes, forests, cities
            }
        }

        return sdf;
    }
}
