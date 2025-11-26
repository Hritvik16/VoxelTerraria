using Unity.Mathematics;

public enum BlendMode : int
{
    Union = 0,
    Subtract = 1
}

public struct Feature
{
    public FeatureType type;
    public int biomeId;
    public BlendMode blendMode;

    // Packed parameters (expandable anytime)
    public float3 data0;
    public float3 data1;
    public float3 data2;
    public float3 data3;

    // Feature center (XZ) extracted for convenience
    public float2 centerXZ;
}
