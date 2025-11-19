using Unity.Mathematics;

public static class BaseIslandSdf
{
    public static float Evaluate(float3 p, in SdfContext ctx)
    {
        float islandRadius = ctx.islandRadius;
        float maxHeight    = ctx.maxBaseHeight;

        float2 xz = p.xz;
        float dist = math.length(xz);

        if (dist > islandRadius + 150f)
            return 999f;

        // ----- STRONG WARP -----
        float warpFreq = 0.006f;   // important change
        float warpAmp  = 80f;      // important change

        float2 warpOffset = new float2(
            NoiseUtils.Noise2D(xz * warpFreq, 1f, 1f),
            NoiseUtils.Noise2D((xz + 200f) * warpFreq, 1f, 1f)
        ) * warpAmp;

        float2 warpedXZ = xz + warpOffset;
        float warpedDist = math.length(warpedXZ);

        // ----- SMOOTHED FALLBACK -----
        float t = 1f - warpedDist / islandRadius;
        t = math.smoothstep(0f, 1f, t);

        float baseHeight = t * maxHeight;

        return p.y - baseHeight;
    }
}
