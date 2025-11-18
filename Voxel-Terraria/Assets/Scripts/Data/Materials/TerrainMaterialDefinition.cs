using UnityEngine;

[System.Serializable]
public class TerrainMaterialDefinition
{
    [Tooltip("Unique ushort ID used in voxel data.")]
    public ushort id;

    [Header("Rendering")]
    public Material unityMaterial;

    [Header("Physical Properties")]
    [Tooltip("How hard the material is to dig through.")]
    public float hardness = 1f;

    [Tooltip("Physics density (used for buoyancy, debris).")]
    public float density = 1f;
}
