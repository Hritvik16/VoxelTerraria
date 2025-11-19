using Unity.Mathematics;

public static class CombinedTerrainSdf
{
    public static float Evaluate(float3 p, in SdfContext ctx)
    {
        // ---------------------------------------------------------------------
        // 1. BASE ISLAND SHAPE
        // ---------------------------------------------------------------------
        float f = BaseIslandSdf.Evaluate(p, ctx);

        // ---------------------------------------------------------------------
        // 2. MOUNTAINS (modify f using min)
        // broad region check: only evaluate if mountain exists
        // ---------------------------------------------------------------------
        if (ctx.mountains.IsCreated && ctx.mountains.Length > 0)
        {
            float mountainF = MountainSdf.Evaluate(p, ctx);
            f = math.min(f, mountainF);
        }

        // ---------------------------------------------------------------------
        // 3. LAKES (cut terrain down)
        // ---------------------------------------------------------------------
        if (ctx.lakes.IsCreated && ctx.lakes.Length > 0)
        {
            float lakeF = LakeSdf.Evaluate(p, ctx);
            f = math.min(f, lakeF);
        }

        // ---------------------------------------------------------------------
        // 4. CITY PLATEAU (height modifier — not an SDF on its own)
        //
        // Plateau modifies baseHeight BEFORE converting to SDF:
        //
        //    finalHeight = CityPlateauSdf.Evaluate(p, baseHeight, ctx)
        //    f = p.y - finalHeight
        //
        // ---------------------------------------------------------------------
        if (ctx.cities.IsCreated && ctx.cities.Length > 0)
        {
            // Re-extract the baseHeight from the existing SDF:
            // f = p.y - baseHeight  => baseHeight = p.y - f
            float baseHeight = p.y - f;

            float plateauHeight = CityPlateauSdf.Evaluate(p, baseHeight, ctx);

            // Convert modified height back to SDF
            float plateauF = p.y - plateauHeight;

            // Combine with the main field using min()
            f = math.min(f, plateauF);
        }

        return f;
    }
}
