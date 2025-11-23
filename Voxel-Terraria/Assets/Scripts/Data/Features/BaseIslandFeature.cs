using Unity.Mathematics;
using UnityEngine;

[CreateAssetMenu(
    fileName = "BaseIslandFeature",
    menuName = "VoxelTerraria/Features/Base Island Feature",
    order = -10)]
public class BaseIslandFeature : ScriptableObject
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

    public Feature ToFeature()
    {
        Feature f = new Feature();
        f.type = FeatureType.BaseIsland;
        f.biomeId = 0;

        f.centerXZ = centerXZ;

        // Pack:
        // data0 = radius, maxHeight, coastlineRoughness
        f.data0 = new float3(radius, maxHeight, coastlineRoughness);

        // data1 = microDetailStrength, _, _
        f.data1 = new float3(microDetailStrength, 0f, 0f);

        f.data2 = float3.zero;

        return f;
    }
}
