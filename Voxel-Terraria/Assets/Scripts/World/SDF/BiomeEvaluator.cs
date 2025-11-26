using Unity.Mathematics;
using VoxelTerraria.World.SDF;

public static class BiomeEvaluator
{
    /// <summary>
    /// Evaluate the dominant biomeId at position p using RAW SDF fields.
    /// Feature-agnostic:
    ///   - Does NOT know about mountains or islands.
    ///   - Uses only feature.biomeId and RAW SDF from adapters.
    ///
    /// Returns:
    ///   - biomeId >= 0 if any feature influences this point
    ///   - -1 if no biome applies (outside all features)
    /// </summary>
    public static int EvaluateDominantBiomeId(float3 p, in SdfContext ctx)
    {
        int bestBiomeId = -1;
        float bestSdf = float.MaxValue;

        if (!ctx.features.IsCreated || ctx.featureCount <= 0)
            return -1;

        for (int i = 0; i < ctx.featureCount; i++)
        {
            Feature f = ctx.features[i];

            // Use the TRUE 3D SDF shape to determine the biome.
            // The feature that creates the surface (lowest SDF) owns the biome.
            float sdf = FeatureBounds3DComputer.EvaluateSdf_Fast(p, in f);

            if (sdf < bestSdf)
            {
                bestSdf = sdf;
                bestBiomeId = f.biomeId;
            }
        }

        return bestBiomeId;
    }
}
