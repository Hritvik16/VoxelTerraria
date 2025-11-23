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

    // Public accessors
    public Vector2 CenterXZ => centerXZ;
    public float Radius => radius;
    public float Height => height;
    public float RidgeFrequency => ridgeFrequency;
    public float RidgeAmplitude => ridgeAmplitude;
    public float WarpStrength => warpStrength;

    /// <summary>
    /// Convert this MountainFeature SO into a plain Feature struct.
    /// This is the ONLY place that knows how mountain data maps into Feature.
    /// </summary>
    public override Feature ToFeature()
    {
        Feature f = new Feature();

        f.type    = FeatureType.Mountain;  // enum value (we define this later)
        f.biomeId = 2;                      // mountain biome (can be changed later)

        // Pack all mountain data into float3 slots
        f.data0 = new float3(centerXZ.x, centerXZ.y, radius);
        f.data1 = new float3(height, ridgeFrequency, ridgeAmplitude);
        f.data2 = new float3(warpStrength, 0f, 0f);

        return f;
    }
}
