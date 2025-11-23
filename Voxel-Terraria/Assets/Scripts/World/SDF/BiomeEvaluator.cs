using Unity.Mathematics;
using UnityEngine;

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
        if (sum > 0f)
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

public static class BiomeEvaluator
{
    /// <summary>
    /// Biome weights driven by RAW SDF fields only.
    /// - BaseIslandSdf.EvaluateRaw: island footprint
    /// - MountainSdf.EvaluateRaw: mountain footprint
    ///
    /// For now:
    ///   • grass = island but not mountain
    ///   • mountain = mountain footprint
    ///   • forest/lakeshore/city = 0 (not wired yet)
    /// </summary>
    public static BiomeWeights EvaluateBiomeWeights(float3 p, in SdfContext ctx)
    {
        BiomeWeights bw = new BiomeWeights();

        //------------------------------------------------------
        // 1. Island mask from BaseIslandSdf.EvaluateRaw
        //    raw < 0 => inside island, raw > 0 => outside
        //------------------------------------------------------
        float islandRaw = BaseIslandSdf.EvaluateRaw(p, ctx);
        float islandMask = islandRaw < 0f ? 1f : 0f;

        //------------------------------------------------------
        // 2. Mountain mask from MountainSdf.EvaluateRaw
        //    raw < 0 => inside mountain footprint
        //------------------------------------------------------
        float mountainMask = 0f;
        if (ctx.mountains.IsCreated && ctx.mountains.Length > 0)
        {
            // float mountainRaw = MountainSdf.EvaluateRaw(p, ctx);
            float mountainRaw = MountainSdf.EvaluateRaw3D(p, ctx);
            mountainMask = mountainRaw < 0f ? 1f : 0f;
        }

        //------------------------------------------------------
        // 3. Assign biomes
        //------------------------------------------------------
        // Mountain overrides grass where present
        bw.mountain = mountainMask;

        // Grass = island but not mountain
        bw.grass = math.max(0f, islandMask * (1f - mountainMask));

        // Others not used yet – leave at 0 for now
        bw.forest    = 0f;
        bw.lakeshore = 0f;
        bw.city      = 0f;

        //------------------------------------------------------
        // 4. Fallback: outside island, just return all zeros.
        //    MaterialSelector will only be called when sdf < 0 anyway.
        //------------------------------------------------------
        float sum = bw.grass + bw.forest + bw.mountain + bw.lakeshore + bw.city;
        if (sum < 0.0001f)
        {
            // No biome; leave as zero (air / outside island)
            return bw;
        }

        bw.Normalize();
        return bw;
    }
}
