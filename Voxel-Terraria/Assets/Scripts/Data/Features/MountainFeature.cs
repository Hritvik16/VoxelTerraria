using UnityEngine;
using Unity.Mathematics;

/// <summary>
/// Mountain feature definition (ScriptableObject).
/// Now inherits from FeatureSO and implements ToFeature(),
/// converting inspector values into a Burst-safe Feature struct.
/// </summary>
[CreateAssetMenu(
    fileName = "MountainFeature",
    menuName = "VoxelTerraria/Features/Mountain Feature",
    order = 0)]
public class MountainFeature : FeatureSO
{
    [Header("Position")]
    [SerializeField] private Vector2 centerXZ = Vector2.zero;

    [Header("Shape")]
    [SerializeField] private float radius = 150f;
    [SerializeField] private float height = 120f;

    [Header("Ridge Noise")]
    [SerializeField] private float ridgeFrequency = 0.05f;
    [SerializeField] private float ridgeAmplitude = 1f;

    [Header("Warp")]
    [SerializeField] private float warpStrength = 30f;

    [Header("Complex Shape")]
    [SerializeField] private float archThreshold = 0.6f;
    [SerializeField] private float overhangStrength = 1.0f;

    // Public accessors
    public Vector2 CenterXZ => centerXZ;
    public float Radius => radius;
    public float Height => height;
    public float RidgeFrequency => ridgeFrequency;
    public float RidgeAmplitude => ridgeAmplitude;
    public float WarpStrength => warpStrength;
    public float ArchThreshold => archThreshold;
    public float OverhangStrength => overhangStrength;

    /// <summary>
    /// Convert this MountainFeature SO into a plain Feature struct.
    /// This is the ONLY place that knows how mountain data maps into Feature.
    /// </summary>
    public override Vector3 GetConnectorPoint(WorldSettings settings)
    {
        // Connect to the peak of the mountain
        // Peak height is seaLevel + height
        return new Vector3(centerXZ.x, settings.seaLevel + height, centerXZ.y);
    }

    public override float GetRadius() => radius;
    public override Vector2 GetCenter() => centerXZ;

    public override float GetBaseHeight(WorldSettings settings)
    {
        return settings.seaLevel;
    }

    public override Feature ToFeature(WorldSettings settings)
    {
        Feature f = new Feature();

        f.type    = FeatureType.Mountain;  // enum value (we define this later)
        f.biomeId = 2;                      // mountain biome (can be changed later)

        // Pack all mountain data into float3 slots
        f.data0 = new float3(centerXZ.x, centerXZ.y, radius);
        f.data1 = new float3(height, ridgeFrequency, ridgeAmplitude);
        
        // data2: x=warp, y=seed(injected), z=archThreshold
        f.data2 = new float3(warpStrength, 0f, archThreshold);
        
        // data3: x=overhangStrength
        f.data3 = new float3(overhangStrength, 0f, 0f);

        return f;
    }
}
