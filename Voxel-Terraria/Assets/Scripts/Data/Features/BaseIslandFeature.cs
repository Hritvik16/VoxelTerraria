using Unity.Mathematics;
using UnityEngine;

[CreateAssetMenu(
    fileName = "BaseIslandFeature",
    menuName = "VoxelTerraria/Features/Base Island Feature",
    order = -10)]
public class BaseIslandFeature : FeatureSO
{
    [Header("Placement")]
    public Vector2 centerXZ = Vector2.zero;

    [Header("Island Shape")]
    public float radius = 150f;
    public float maxHeight = 80f;

    [Header("Tuning")]
    [Range(0.1f, 3f)]
    public float coastlineRoughness = 1.0f;

    [Range(0.1f, 3f)]
    public float microDetailStrength = 1.0f;

    public bool randomizeEachGeneration = false;

    // Base seed for deterministic islands when randomize is false
    public int seed = 12345;

    // Public accessors to match other features
    public Vector2 CenterXZ => centerXZ;
    public float Radius => radius;

    public override Vector3 GetConnectorPoint(WorldSettings settings)
    {
        // Connect to the center at sea level (or slightly above/below depending on preference)
        // Let's say sea level for now.
        return new Vector3(centerXZ.x, settings.seaLevel, centerXZ.y);
    }

    public override Feature ToFeature(WorldSettings settings)
    {
        Feature f = new Feature();
        f.type = FeatureType.BaseIsland;
        f.biomeId = 0;

        f.centerXZ = centerXZ;

        // Pack:
        // data0 = radius, maxHeight, coastlineRoughness
        f.data0 = new float3(radius, maxHeight, coastlineRoughness);

        // data1 = microDetailStrength, seed, _
        f.data1 = new float3(microDetailStrength, (float)seed, 0f);

        f.data2 = float3.zero;

        return f;
    }
}
