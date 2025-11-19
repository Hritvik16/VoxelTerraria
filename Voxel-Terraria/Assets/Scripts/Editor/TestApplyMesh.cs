using UnityEditor;
using UnityEngine;
using VoxelTerraria.World;

public static class TestApplyMesh
{
    [MenuItem("VoxelTerraria/Test/Apply Mesh To Selected Chunk")]
    public static void ApplyMeshToSelected()
    {
        if (Selection.activeGameObject == null)
        {
            Debug.LogWarning("Select a VoxelChunkView in the scene.");
            return;
        }

        VoxelChunkView view = Selection.activeGameObject.GetComponent<VoxelChunkView>();
        if (!view)
        {
            Debug.LogWarning("Selected object does not have VoxelChunkView.");
            return;
        }

        // ------------------------------------------
        // Build a reliable cube mesh (never null)
        // ------------------------------------------
        GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Mesh cube = temp.GetComponent<MeshFilter>().sharedMesh;
        GameObject.DestroyImmediate(temp);

        // Apply
        view.ApplyMesh(cube);

        Debug.Log("Mesh assigned to VoxelChunkView!");
    }
}
