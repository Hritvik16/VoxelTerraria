using Unity.Mathematics;

public struct Feature
{
    public FeatureType type;
    public int biomeId;

    // Packed parameters (expandable anytime)
    public float3 data0;
    public float3 data1;
    public float3 data2;

    // Feature center (XZ) extracted for convenience
    public float2 centerXZ;
}
