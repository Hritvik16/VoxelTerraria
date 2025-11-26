using Unity.Mathematics;

public static class VolcanoSdf
{
    public static float Evaluate(float3 p, in VolcanoFeatureData v)
    {
        // --------------------------------------------------------------------
        // 1. Setup & Optimization
        // --------------------------------------------------------------------
        float peakY = v.baseHeight + v.height;

        if (p.y > peakY + 10.0f)
            return p.y - peakY;

        float2 localXZ = p.xz - v.centerXZ;
        float  distXZ  = math.length(localXZ);

        float rejectRadius = v.radius * 1.4f;
        if (distXZ > rejectRadius)
        {
            return 9999f; 
        }

        // --------------------------------------------------------------------
        // 2. Irregularity (Domain Warping)
        // --------------------------------------------------------------------
        // Lower frequency warp for broader silhouette changes, less "lumpy"
        float warpFreq = 1.5f / v.radius; 
        float warpAmp  = v.radius * 0.2f; 

        float2 warpOffset = new float2(
            NoiseUtils.Noise2D(localXZ * warpFreq + v.seed, 1f, 1f),
            NoiseUtils.Noise2D((localXZ + 100f) * warpFreq - v.seed, 1f, 1f)
        ) * warpAmp;

        float2 warpedXZ   = localXZ + warpOffset;
        float  warpedDist = math.length(warpedXZ);

        // --------------------------------------------------------------------
        // 3. Base Volcano Shape (Sharper Cone)
        // --------------------------------------------------------------------
        float t = warpedDist / v.radius;

        // Use a slightly sharper power for the main slope to look more stable
        float coneProfile = math.pow(math.max(0f, 1.0f - t), 1.2f);
        
        float terrainHeight = v.baseHeight + (v.height * coneProfile);

        // --------------------------------------------------------------------
        // 4. Crater (Bowl Shape)
        // --------------------------------------------------------------------
        float cRadius = v.craterRadius;
        float craterDist = warpedDist; 
        
        // Sharper rim transition
        float craterT = math.smoothstep(cRadius * 1.1f, cRadius * 0.5f, craterDist);
        
        // Flatten the bottom of the crater for a boss arena
        // Using pow(craterT, 0.5) makes the drop-off steeper near the rim and flatter at the bottom
        float bowlShape = math.pow(craterT, 0.5f);
        
        terrainHeight -= bowlShape * v.craterDepth;

        // Distinct rim lip
        float rimWidth = cRadius * 0.3f;
        float rimDist = math.abs(warpedDist - cRadius);
        float rimProfile = 1.0f - math.smoothstep(0f, rimWidth, rimDist);
        terrainHeight += rimProfile * (v.height * 0.08f);

        // --------------------------------------------------------------------
        // 5. Lava Paths (Veins)
        // --------------------------------------------------------------------
        float angle = math.atan2(warpedXZ.y, warpedXZ.x);
        float radiusNorm = warpedDist / v.radius;

        float twistFreq = math.max(v.pathNoiseFreq, 0.01f);
        float twistAmp  = v.pathNoiseAmp;

        float twist = NoiseUtils.Noise2D(warpedXZ * twistFreq + v.seed * 3f, 1f, 1f) * twistAmp;
        float twistedAngle = angle + twist * radiusNorm;

        float pathSignal = math.sin(twistedAngle * 5.0f + v.seed * 133.7f); 
        pathSignal += NoiseUtils.Noise2D(warpedXZ * 0.1f, 1f, 1f) * 0.5f;

        float pathCut = math.smoothstep(0.5f, 0.9f, pathSignal);

        float flowMask = math.smoothstep(0.15f, 0.3f, radiusNorm) * 
                         math.smoothstep(0.9f, 0.6f, radiusNorm);

        terrainHeight -= pathCut * v.pathDepth * flowMask;

        // --------------------------------------------------------------------
        // 6. Surface Detail (Ridged Rock)
        // --------------------------------------------------------------------
        // Replaced blobby noise with Ridged noise for a sharp, rocky look.
        // Higher frequency, lower amplitude to avoid "cones sticking out".
        float detail = NoiseUtils.RidgedNoise3D(
            p * 0.25f + v.seed, 
            1f,     // frequency
            1.0f    // gain
        ) * 2.5f;   // amplitude

        // Mask detail so it fades out at the bottom (blends with island)
        float detailMask = math.smoothstep(v.baseHeight, v.baseHeight + v.height * 0.8f, terrainHeight);
        terrainHeight += detail * detailMask;

        // --------------------------------------------------------------------
        // 7. Final SDF
        // --------------------------------------------------------------------
        return p.y - terrainHeight;
    }
}
