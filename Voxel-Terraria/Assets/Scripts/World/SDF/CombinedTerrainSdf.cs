using Unity.Mathematics;
using VoxelTerraria.World.SDF;

public static class CombinedTerrainSdf
{
    public static float Evaluate(float3 p, ref SdfContext ctx)
    {
        float sdf = 9999f;

        // Temporary: everything below sea level is air.
        // You can relax this later when you add caves / underwater.
        if (p.y < ctx.seaLevel)
            return 1f;  // positive → air

        // 1. Find Base Island Mask (if any)
        // We want to clip everything to the island's footprint.
        float islandMask = -9999f;
        bool hasIsland = false;

        for (int i = 0; i < ctx.featureCount; i++)
        {
            if (ctx.features[i].type == FeatureType.BaseIsland)
            {
                islandMask = BaseIslandFeatureAdapter.EvaluateRaw(p, ctx.features[i]);
                hasIsland = true;
                break;
            }
        }

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

        // Apply Island Mask
        // If we are outside the island footprint (mask > 0), force air.
        // if (hasIsland)
        // {
        //     sdf = math.max(sdf, islandMask);
        // }

        return sdf;
    }
}
