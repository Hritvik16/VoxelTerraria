using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(
    fileName = "TerrainMaterials",
    menuName = "VoxelTerraria/Terrain Materials",
    order = 0)]
public class TerrainMaterialLibrary : ScriptableObject
{
    [Tooltip("Ordered list of terrain materials mapped by ID.")]
    public List<TerrainMaterialDefinition> materials = new List<TerrainMaterialDefinition>();

    // Lookup dictionary for runtime speed
    private Dictionary<ushort, TerrainMaterialDefinition> lookup;

    public TerrainMaterialDefinition GetMaterial(ushort id)
    {
        if (lookup == null)
            BuildLookup();

        lookup.TryGetValue(id, out var mat);
        return mat;
    }

    private void BuildLookup()
    {
        lookup = new Dictionary<ushort, TerrainMaterialDefinition>();
        foreach (var mat in materials)
        {
            if (!lookup.ContainsKey(mat.id))
                lookup.Add(mat.id, mat);
            else
                Debug.LogWarning($"Duplicate terrain material ID: {mat.id}");
        }
    }
}
