using Unity.Mathematics;

public static class MaterialSelector
{
    /// <summary>
    /// Returns the material ID for a voxel based on world position,
    /// SDF value, and biome weights from the SdfContext.
    /// </summary>
    public static ushort SelectMaterialId(float3 worldPos, float sdf, in SdfContext ctx)
    {
        // Air
        if (sdf > 0f)
            return 0;  // ID 0 = air (must exist in TerrainMaterials.asset)

        // Ground material logic – now raw-SDF driven biomes
        BiomeWeights bw = BiomeEvaluator.EvaluateBiomeWeights(worldPos, ctx);

        // If everything is zero (shouldn't happen for solid voxels), default to grass
        float best = bw.grass;
        ushort id = 1;   // 1 = grass

        if (bw.forest > best)
        {
            best = bw.forest;
            id = 2;   // forest floor
        }

        if (bw.mountain > best)
        {
            best = bw.mountain;
            id = 3;   // stone
        }

        if (bw.lakeshore > best)
        {
            best = bw.lakeshore;
            id = 4;   // sand
        }

        if (bw.city > best)
        {
            best = bw.city;
            id = 5;   // city ground
        }

        return id;
    }
}
