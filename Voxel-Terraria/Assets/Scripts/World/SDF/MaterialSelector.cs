using Unity.Mathematics;

public static class MaterialSelector
{
    /// <summary>
    /// Selects a voxel's material based solely on biomeId.
    /// Fully feature-agnostic:
    ///   - Uses BiomeEvaluator to get biomeId
    ///   - No special cases for mountains, islands, forests, etc.
    ///
    /// Material mapping is defined by WorldSettings or a simple lookup table.
    /// </summary>
    public static ushort SelectMaterialId(float3 worldPos, float sdf, in SdfContext ctx)
    {
        // Air above terrain
        if (sdf > 0f)
            return 0;

        // Resolve biome
        int biomeId = BiomeEvaluator.EvaluateDominantBiomeId(worldPos, ctx);

        // No biome feature influences this voxel → default solid
        if (biomeId < 0)
            return 1;

        //------------------------------------------------------------
        // GENERIC BIOME → MATERIAL MAPPING
        //------------------------------------------------------------
        // For now: materialId = biomeId + 1
        //   biomeId 0 → material 1
        //   biomeId 1 → material 2
        //   biomeId 2 → material 3
        //   ...
        //
        // Later you can store mappings inside WorldSettings.
        //------------------------------------------------------------

        return (ushort)(biomeId + 1);
    }
}
