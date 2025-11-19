using Unity.Mathematics;

public struct BiomeWeights
{
    public float grass;
    public float forest;
    public float mountain;
    public float lakeshore;
    public float city;

    public void Normalize()
    {
        float sum = grass + forest + mountain + lakeshore + city;
        if (sum > 0)
        {
            float inv = 1f / sum;
            grass     *= inv;
            forest    *= inv;
            mountain  *= inv;
            lakeshore *= inv;
            city      *= inv;
        }
    }
}

using Unity.Mathematics;

public static class BiomeEvaluator
{
    public static BiomeWeights EvaluateBiomeWeights(float3 p, in SdfContext ctx)
    {
        BiomeWeights bw = new BiomeWeights();

        // ------------------------------------------------------------
        // 1. APPROXIMATE SURFACE HEIGHT
        // ------------------------------------------------------------
        // This extracts the height = p.y - sdf(p)
        float sdfValue = CombinedTerrainSdf.Evaluate(p, ctx);
        float height    = p.y - sdfValue;

        // ------------------------------------------------------------
        // 2. APPROXIMATE SLOPE (sample sdf around the point)
        // ------------------------------------------------------------
        float3 dx = new float3(0.5f, 0, 0);
        float3 dz = new float3(0, 0, 0.5f);

        float sdfX  = CombinedTerrainSdf.Evaluate(p + dx, ctx);
        float sdfXn = CombinedTerrainSdf.Evaluate(p - dx, ctx);
        float sdfZ  = CombinedTerrainSdf.Evaluate(p + dz, ctx);
        float sdfZn = CombinedTerrainSdf.Evaluate(p - dz, ctx);

        float slopeX = math.abs(sdfX - sdfXn);
        float slopeZ = math.abs(sdfZ - sdfZn);
        float slope = math.max(slopeX, slopeZ);  // slope estimate

        // ------------------------------------------------------------
        // 3. GRASSLAND (default everywhere except steep or special)
        // ------------------------------------------------------------
        // Grassland prefers:
        // - low slopes
        // - mid heights
        float grassSlopeMask = 1f - math.saturate(slope * 4f);
        float grassHeightMask = math.saturate(1f - math.abs(height) * 0.02f);
        bw.grass = grassSlopeMask * grassHeightMask;

        // ------------------------------------------------------------
        // 4. FOREST (near forest features, low slopes)
        // ------------------------------------------------------------
        if (ctx.forests.IsCreated)
        {
            for (int i = 0; i < ctx.forests.Length; i++)
            {
                var f = ctx.forests[i];

                float dist = math.length(p.xz - f.centerXZ);
                if (dist < f.radius)
                {
                    float t = dist / f.radius;
                    float mask = 1f - math.saturate(t);

                    // forests prefer low slopes
                    float slopeMask = 1f - math.saturate(slope * 5f);

                    bw.forest = math.max(bw.forest, mask * slopeMask);
                }
            }
        }

        // ------------------------------------------------------------
        // 5. MOUNTAIN (high height near mountain footprint)
        // ------------------------------------------------------------
        if (ctx.mountains.IsCreated)
        {
            for (int i = 0; i < ctx.mountains.Length; i++)
            {
                var m = ctx.mountains[i];

                float dist = math.length(p.xz - m.centerXZ);
                if (dist < m.radius)
                {
                    float radial = 1f - math.saturate(dist / m.radius);
                    float heightMask = math.saturate((height - (m.height * 0.3f)) * 0.02f);

                    bw.mountain = math.max(bw.mountain, radial * heightMask);
                }
            }
        }

        // ------------------------------------------------------------
        // 6. LAKE SHORE (near lake edge)
        // ------------------------------------------------------------
        if (ctx.lakes.IsCreated)
        {
            for (int i = 0; i < ctx.lakes.Length; i++)
            {
                var l = ctx.lakes[i];

                float dist = math.length(p.xz - l.centerXZ);
                float edgeDist = math.abs(dist - l.radius);

                // lake shore appears near water level
                float nearWater = math.exp(-math.pow(math.abs(height - ctx.seaLevel), 1.2f));

                float shoreMask = math.exp(-edgeDist * 0.1f) * nearWater;

                bw.lakeshore = math.max(bw.lakeshore, shoreMask);
            }
        }

        // ------------------------------------------------------------
        // 7. CITY (inside plateau radius)
        // ------------------------------------------------------------
        if (ctx.cities.IsCreated)
        {
            for (int i = 0; i < ctx.cities.Length; i++)
            {
                var c = ctx.cities[i];

                float dist = math.length(p.xz - c.centerXZ);
                if (dist < c.radius)
                {
                    float t = dist / c.radius;
                    float mask = 1f - math.saturate(t);
                    bw.city = math.max(bw.city, mask);
                }
            }
        }

        // ------------------------------------------------------------
        // 8. Normalize
        // ------------------------------------------------------------
        bw.Normalize();
        return bw;
    }
}
