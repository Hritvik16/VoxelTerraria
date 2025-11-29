using Unity.Mathematics;
using VoxelTerraria.World.SDF;

public static class CombinedTerrainSdf
{
    public static float Evaluate(float3 p, ref SdfContext ctx)
    {
        float sdf = 9999f;

        // Temporary: everything below sea level is air.
        // You can relax this later when you add caves / underwater.
        // if (p.y < ctx.seaLevel)
        //     return 1f;  // positive → air

        // 1. Combine all features
        // We iterate through all features (BaseIsland, Mountains, etc.)
        // and combine them using their blend mode.
        // BaseIsland is just another feature (Union) that provides the base shape.
        
        for (int i = 0; i < ctx.featureCount; i++)
        {
            Feature f = ctx.features[i];
            float s = FeatureBounds3DComputer.EvaluateSdf_Fast(p, in f);
            
            if (f.blendMode == BlendMode.Subtract)
            {
                // Subtraction: max(sdf, -featureSdf)
                // This carves the feature out of the existing terrain
                sdf = math.max(sdf, -s);
            }
            else
            {
                // Union: min(sdf, featureSdf)
                // This adds the feature to the terrain
                sdf = math.min(sdf, s);
            }
        }

        return sdf;
    }
}
