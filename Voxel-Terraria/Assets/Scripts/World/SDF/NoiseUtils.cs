using Unity.Mathematics;

public static class NoiseUtils
{
    // --------------------------------------------------------------------
    // 2D Noise
    // --------------------------------------------------------------------
    public static float Noise2D(float2 p, float frequency, float amplitude)
    {
        p *= frequency;
        float n = noise.snoise(p);          // returns -1 to +1
        return n * amplitude;
    }

    // --------------------------------------------------------------------
    // 3D Noise
    // --------------------------------------------------------------------
    public static float Noise3D(float3 p, float frequency, float amplitude)
    {
        p *= frequency;
        float n = noise.snoise(p);          // -1 to +1
        return n * amplitude;
    }

    // --------------------------------------------------------------------
    // Ridged Noise (good for sharp mountains)
    // --------------------------------------------------------------------
    public static float RidgedNoise3D(float3 p, float frequency, float amplitude)
    {
        p *= frequency;

        // Ridged: fold the noise to create sharp ridges
        float n = noise.snoise(p);
        n = math.abs(n);            // make valleys into peaks
        n = 1f - n;                 // invert shape
        n *= n;                     // emphasize ridges
        return n * amplitude;
    }

    public static float RidgedNoise2D(float2 p, float frequency, float amplitude)
    {
        p *= frequency;
        float n = noise.snoise(p);
        n = math.abs(n);
        n = 1f - n;
        n *= n;
        return n * amplitude;
    }

    // --------------------------------------------------------------------
    // Optional helpers (fractal noise layers)
    // --------------------------------------------------------------------
    // public static float FractalNoise3D(float3 p, float baseFreq, float baseAmp, int octaves)
    // {
    //     float total = 0;
    //     float freq = baseFreq;
    //     float amp = baseAmp;

    //     for (int i = 0; i < octaves; i++)
    //     {
    //         total += Noise3D(p, freq, amp);
    //         freq *= 2f;
    //         amp *= 0.5f;
    //     }

    //     return total;
    // }
}
