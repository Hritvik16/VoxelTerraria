using Unity.Mathematics;

public static class BaseIslandSdf
{
//     public static float Evaluate(float3 p, in SdfContext ctx)
//     {
//         // Pull values from SdfContext
//         float islandRadius   = ctx.islandRadius;      // e.g., 75
//         float maxBaseHeight  = ctx.maxBaseHeight;     // e.g., 10

//         float2 xz = p.xz;
//         float dist = math.length(xz);

//         //------------------------------------------------------
//         // 0. Hard cutoff (outside island → air)
//         //------------------------------------------------------
//         if (dist > islandRadius * 1.5f)
//             return 999f;   // positive = above surface = air

//         //------------------------------------------------------
//         // 1. Compute normalized radial factor:
//         //    - t = 1 at center
//         //    - t = 0 at islandRadius
//         //------------------------------------------------------
//         float t = math.saturate(1f - dist / islandRadius);

// // VERY gentle ease — soft dunes
// t = math.pow(t, 1.5f);

// // Scale height down near center to avoid steep cliffs
// float baseHeight = t * maxBaseHeight * 0.6f;


//         //------------------------------------------------------
//         // 3. SAFE, DYNAMIC WARP based on world scale
//         //------------------------------------------------------
//         // warp amplitude scales with maxBaseHeight + radius
//         // float warpAmp = math.min(islandRadius * 0.15f, maxBaseHeight * 0.5f);
//         // float warpFreq = 1f / math.max(islandRadius * 4f, 1f);

//         float warpFreq = 2f / islandRadius;         // frequency relative to radius
//         float warpAmp  = islandRadius * 0.35f;      // 35% of radius → strong coastline shape


//         float2 warpOffset = new float2(
//             NoiseUtils.Noise2D(xz * warpFreq, 1f, 1f),
//             NoiseUtils.Noise2D((xz + 200f) * warpFreq, 1f, 1f)
//         ) * warpAmp;

//         // Clamp warp so it never exceeds dome curvature
//         float warpLen = math.length(warpOffset);
//         float warpMax = islandRadius * 0.2f;

//         if (warpLen > warpMax)
//             warpOffset = warpOffset * (warpMax / warpLen);

//         //------------------------------------------------------
//         // 4. Recompute warped distance
//         //------------------------------------------------------
//         float2 warpedXZ = xz + warpOffset;
//         float warpedDist = math.length(warpedXZ);

//         float tw = math.saturate(1f - warpedDist / islandRadius);
//         tw = tw * tw * (3f - 2f * tw);

//         //------------------------------------------------------
//         // 5. Final base island height
//         //------------------------------------------------------
//         // float finalHeight = tw * maxBaseHeight;
//         float finalHeight = tw * maxBaseHeight;
//         //------------------------------------------------------
//         // 5.5 MICRO-TERRAIN (adds sub-voxel granularity)
//         //------------------------------------------------------
//         // float microFreq = 1.5f / ctx.voxelSize;     // increases detail as voxelSize decreases
//         // float microAmp  = ctx.voxelSize * 0.5f;     // small variation (half voxel)

//         // float micro = NoiseUtils.Noise2D(p.xz * microFreq, 2f, 0.5f) * microAmp;
//         // finalHeight += micro;


//         // // === QUANTIZATION ===
//         // float step = ctx.stepHeight;
//         // if (step > 0.0001f)
//         // {
//         //     finalHeight = math.floor(finalHeight / step) * step;
//         // }


//         //------------------------------------------------------
//         // 6. Return standard height SDF
//         //    Negative = inside terrain
//         //    Zero     = surface
//         //    Positive = above terrain
//         //------------------------------------------------------
//         return p.y - finalHeight;
//     }

    public static float Evaluate(float3 p, in SdfContext ctx)
{
    float islandRadius   = ctx.islandRadius;
    float maxBaseHeight  = ctx.maxBaseHeight;

    float2 xz = p.xz;
    float dist = math.length(xz);

    // Outside radius → air
    if (dist > islandRadius * 1.5f)
        return 999f;

    //------------------------------------------------------
    // 1. Initial radial factor (before warp)
    //------------------------------------------------------
    float t = math.saturate(1f - dist / islandRadius);

    // Gentle shape for dome base — helps before warp is applied
    t = math.pow(t, 1.5f);
    float baseHeight = t * maxBaseHeight * 0.6f;

    //------------------------------------------------------
    // 2. Coastline warp (kept as-is)
    //------------------------------------------------------
    float warpFreq = 2f / islandRadius;
    float warpAmp  = islandRadius * 0.35f;

    float2 warpOffset = new float2(
        NoiseUtils.Noise2D(xz * warpFreq, 1f, 1f),
        NoiseUtils.Noise2D((xz + 200f) * warpFreq, 1f, 1f)
    ) * warpAmp;

    float warpLen = math.length(warpOffset);
    float warpMax = islandRadius * 0.2f;

    if (warpLen > warpMax)
        warpOffset *= warpMax / warpLen;

    //------------------------------------------------------
    // 3. Recompute distance after warp
    //------------------------------------------------------
    float2 warpedXZ   = xz + warpOffset;
    float warpedDist  = math.length(warpedXZ);

    //------------------------------------------------------
    // 4. Apply GENTLE falloff to warped distance
    //------------------------------------------------------
    float tw = math.saturate(1f - warpedDist / islandRadius);
    tw = math.pow(tw, 1.5f);


    float finalHeight = tw * maxBaseHeight * 0.6f;


    float microFreq = 1f / islandRadius;
    float microAmp  = ctx.voxelSize * 0.15f;

    finalHeight += NoiseUtils.Noise2D(p.xz * microFreq, 2f, 0.5f) * microAmp;
    //------------------------------------------------------
    // 5. Return SDF (terrain is below the height)
    //------------------------------------------------------
    return p.y - finalHeight;
}

}
