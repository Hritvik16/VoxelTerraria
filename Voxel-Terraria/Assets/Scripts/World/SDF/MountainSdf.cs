using Unity.Mathematics;

public static class MountainSdf
{
    /// <summary>
    /// Single-mountain SDF:
    /// negative = inside terrain, positive = air.
    ///
    /// True 3D shape:
    ///   - union of a few ellipsoids (shoulders / bulk)
    ///   - clipped by a tilted plane (cliff / overhang)
    ///   - mid-frequency rock detail stronger on slopes
    /// </summary>
    public static float Evaluate(float3 p, in MountainFeatureData m)
    {
        // --------------------------------------------------------------------
        // 1. Setup & Optimization
        // --------------------------------------------------------------------
        float2 centerXZ = m.centerXZ;
        float2 localXZ  = p.xz - centerXZ;
        float  distXZ   = math.length(localXZ);

        // Relaxed bounds
        if (distXZ > m.radius * 2.5f || p.y > m.height * 1.5f)
            return 9999f;

        // --------------------------------------------------------------------
        // 2. Irregular Footprint (Elliptical + Warped)
        // --------------------------------------------------------------------
        
        // A. Random Rotation & Stretch (Elliptical Base)
        float orientNoise = NoiseUtils.Noise2D(centerXZ * 0.013f + m.seed, 1f, 1f);
        float angle       = orientNoise * math.PI * 2f;
        float sinA = math.sin(angle);
        float cosA = math.cos(angle);
        
        // Rotate
        float2 rotatedXZ = new float2(
            localXZ.x * cosA - localXZ.y * sinA,
            localXZ.x * sinA + localXZ.y * cosA
        );
        
        // Stretch: Make it oblong (Ridge-like)
        // X becomes the long axis, Y becomes the short axis
        float2 stretchedXZ = rotatedXZ;
        stretchedXZ.x *= 0.6f; // Stretch X (make coords smaller -> wider shape)
        stretchedXZ.y *= 1.4f; // Squash Y (make coords bigger -> thinner shape)
        
        // B. Stronger Warp for Organic Outline
        float warpFreq = 0.01f;
        float warpAmp  = m.radius * 0.35f; // Increased from 0.2f

        float2 warpOffset = new float2(
            NoiseUtils.Noise2D(p.xz * warpFreq + m.seed, 1f, 1f),
            NoiseUtils.Noise2D(p.xz * warpFreq + new float2(100, 100) + m.seed, 1f, 1f)
        ) * warpAmp;

        float2 finalLocalXZ = stretchedXZ + warpOffset;
        float finalDist = math.length(finalLocalXZ);

        // --------------------------------------------------------------------
        // 3. Classic Ridge Shape
        // --------------------------------------------------------------------
        
        // Dome shape for overall mass
        float t = math.saturate(finalDist / m.radius);
        float dome = math.smoothstep(1.0f, 0.2f, t); 
        
        // Main Ridge: Very low frequency to create a massive spine
        float ridgeFreq = 0.005f; 
        float mainRidge = NoiseUtils.RidgedNoise2D(p.xz + m.seed * 10f, ridgeFreq, 1.0f);
        
        // Detail: Minimal high-frequency noise for texture
        float detailFreq = 0.05f;
        float detail = NoiseUtils.RidgedNoise2D(p.xz + m.seed * 20f, detailFreq, 1.0f) * 0.1f;
        
        // Combine
        float combinedVal = mainRidge + detail;
        
        // Height Calculation
        float targetHeight = m.height * dome * math.max(0f, combinedVal * 0.9f + 0.1f);

        // --------------------------------------------------------------------
        // 4. SDF Calculation (Pure Heightmap)
        // --------------------------------------------------------------------
        // --------------------------------------------------------------------
        // 4. SDF Calculation (Pure Heightmap)
        // --------------------------------------------------------------------
        // Fix: If we are outside the mountain (targetHeight ~ 0), we must return Air (positive).
        // If we just return p.y, we get a flat floor at y=0.
        // We want to return a large positive value if we are outside the mountain's influence.
        
        if (targetHeight < 0.01f)
        {
             return 9999f; // Force Air
        }

        float sdf = p.y - targetHeight;

        // --------------------------------------------------------------------
        // 5. Arches (Subtractive Only)
        // --------------------------------------------------------------------
        if (m.archThreshold > 0f)
        {
            float archMask = math.smoothstep(10f, 40f, p.y) * math.smoothstep(m.height * 0.8f, m.height * 0.4f, p.y);
            
            if (archMask > 0.01f)
            {
                float archNoise = NoiseUtils.Noise3D(p * 0.03f + m.seed * 5f, 1f, 1f);
                float tunnelDist = math.abs(archNoise);
                float tunnelRadius = 0.15f * m.archThreshold; 
                
                if (tunnelDist < tunnelRadius)
                {
                    float carve = (tunnelRadius - tunnelDist) * 40f;
                    sdf += carve * archMask;
                }
            }
        }

        return sdf * 0.7f;
    }

    /// <summary>
    /// RAW field for biomes: negative inside footprint, positive outside.
    /// Matches the same orientation & warp as Evaluate().
    /// (Kept commented-out as in your previous version; re-enable if you need it.)
    /// </summary>
    // public static float EvaluateRaw(float3 p, in MountainFeatureData m)
    // {
    //     float2 center = m.centerXZ;
    //     float2 local  = p.xz - center;
    //
    //     float orientNoise = NoiseUtils.Noise2D(center * 0.013f, 1f, 1f);
    //     float angle       = orientNoise * math.PI;
    //
    //     float2 dir  = math.normalize(new float2(math.cos(angle), math.sin(angle)));
    //     float2 perp = new float2(-dir.y, dir.x);
    //
    //     float along  = math.dot(local, dir);
    //     float across = math.dot(local, perp);
    //
    //     float2 ridgePos = new float2(along, across);
    //
    //     float warpA = NoiseUtils.Noise2D(ridgePos * 0.05f, 1f, m.warpStrength);
    //     float warpB = NoiseUtils.Noise2D((ridgePos + 200f) * 0.05f, 1f, m.warpStrength);
    //
    //     along  += warpA * 0.5f;
    //     across += warpB * 0.5f;
    //
    //     float longRadius  = m.radius;
    //     float shortRadius = m.radius * 0.4f;
    //
    //     float alongNorm  = along  / longRadius;
    //     float acrossNorm = across / shortRadius;
    //
    //     float radial = math.sqrt(alongNorm * alongNorm + acrossNorm * acrossNorm);
    //
    //     // raw: negative inside ellipse, positive outside
    //     return radial - 1f;
    // }

    // Legacy context-based versions kept commented for reference.
    // Re-enable only if you actually still use them.

    // public static float Evaluate(float3 p, in SdfContext ctx) { ... }
    // public static float EvaluateRaw(float3 p, in SdfContext ctx) { ... }
    // public static float EvaluateRaw3D(float3 p, in SdfContext ctx) { ... }
}
