using Unity.Mathematics;

public static class MaterialSelector
{
    // ------------------------------------------------------------
    // Material IDs — adjust if your library uses different ones
    // ------------------------------------------------------------
    private const ushort GrassID       = 1;
    private const ushort DirtID        = 2;
    private const ushort StoneID       = 3;
    private const ushort SandID        = 4;
    private const ushort ForestFloorID = 5;
    private const ushort CityGroundID  = 6;
    private const ushort LakeBedID     = 7;

    public static ushort ChooseMaterialId(
        float3 p,
        float density,
        BiomeWeights weights,
        in SdfContext ctx)
    {
        // --------------------------------------------------------
        // 1. HEIGHT & SLOPE ESTIMATION
        // --------------------------------------------------------
        float sdfValue = CombinedTerrainSdf.Evaluate(p, ctx);

        // height of terrain surface at this point
        float surfaceHeight = p.y - sdfValue;

        // depth relative to ground (negative when inside terrain)
        float depth = -density;

        // slope detection (expensive but reasonably cached in caller)
        float3 dx = new float3(0.5f, 0, 0);
        float3 dz = new float3(0, 0, 0.5f);

        float slope = math.max(
            math.abs(CombinedTerrainSdf.Evaluate(p + dx, ctx) - CombinedTerrainSdf.Evaluate(p - dx, ctx)),
            math.abs(CombinedTerrainSdf.Evaluate(p + dz, ctx) - CombinedTerrainSdf.Evaluate(p - dz, ctx))
        );

        // --------------------------------------------------------
        // 2. LAKEBED & SAND NEAR WATER
        // --------------------------------------------------------
        if (p.y <= ctx.seaLevel + 0.5f)
        {
            // deeper below water → lake bed
            if (depth > 2f)
                return LakeBedID;

            // near shoreline → sand
            return SandID;
        }

        // --------------------------------------------------------
        // 3. CITY GROUND
        // --------------------------------------------------------
        if (weights.city > 0.5f)
            return CityGroundID;

        // --------------------------------------------------------
        // 4. MOUNTAIN (steep slopes → stone)
        // --------------------------------------------------------
        if (weights.mountain > 0.4f || slope > 0.7f)
            return StoneID;

        // --------------------------------------------------------
        // 5. FOREST REGION
        // --------------------------------------------------------
        if (weights.forest > 0.5f)
        {
            if (depth < 1f)
                return ForestFloorID;   // leaf litter, humus
            else if (depth < 4f)
                return DirtID;
            else
                return StoneID;
        }

        // --------------------------------------------------------
        // 6. GRASSLAND
        // --------------------------------------------------------
        if (weights.grass > 0.3f)
        {
            if (depth < 1.2f)
                return GrassID;
            else if (depth < 6f)
                return DirtID;
            else
                return StoneID;
        }

        // --------------------------------------------------------
        // 7. LAKE SHORE BLEND ABOVE WATER
        // --------------------------------------------------------
        if (weights.lakeshore > 0.4f)
            return SandID;

        // --------------------------------------------------------
        // 8. DEFAULT: STONE UNDER EVERYTHING ELSE
        // --------------------------------------------------------
        return StoneID;
    }
}
