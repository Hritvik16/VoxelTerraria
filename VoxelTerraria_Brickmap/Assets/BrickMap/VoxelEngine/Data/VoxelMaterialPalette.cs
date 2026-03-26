using UnityEngine;

[System.Serializable]
public struct VoxelMaterial 
{
    public Color albedo;    // 16 bytes
    public float roughness; // 4 bytes
    public float metallic;  // 4 bytes
    public float emission;  // 4 bytes
    public float padding;   // 4 bytes (Brings total to 32 bytes)
}

[CreateAssetMenu(fileName = "NewMaterialPalette", menuName = "Voxel Engine/Material Palette")]
public class VoxelMaterialPalette : ScriptableObject
{
    [Tooltip("Index 0 is always Air! Start defining solid materials at Index 1.")]
    public VoxelMaterial[] materials = new VoxelMaterial[256]; 
}