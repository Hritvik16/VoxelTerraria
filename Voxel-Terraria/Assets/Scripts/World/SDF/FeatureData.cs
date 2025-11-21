using Unity.Mathematics;
using System;

[Serializable]
public struct MountainFeatureData
{
    public float2 centerXZ;       // World-space XZ center
    public float radius;          // Horizontal radius
    public float height;          // Peak height above base

    public float ridgeFrequency;  // Ridged noise frequency
    public float ridgeAmplitude;  // Ridged noise amplitude

    public float warpStrength;    // Domain warp strength
}

[Serializable]
public struct LakeFeatureData
{
    public float2 centerXZ;       // World-space XZ center
    public float radius;          // Lake radius

    public float bottomHeight;    // Lake bed height (y)
    public float shoreHeight;     // Shoreline height (y)
}

[Serializable]
public struct ForestFeatureData
{
    public float2 centerXZ;       // World-space XZ center
    public float radius;          // Forest radius

    public float treeDensity;     // Trees per area (for later)
}

[Serializable]
public struct CityPlateauFeatureData
{
    public float2 centerXZ;       // World-space XZ center
    public float radius;          // Plateau radius

    public float plateauHeight;   // Plateau height (y)
}
