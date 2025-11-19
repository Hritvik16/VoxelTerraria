using UnityEditor;
using UnityEngine;
using Unity.Collections;
using VoxelTerraria.World;

public class VoxelDebugTools : EditorWindow
{
    WorldSettings settings;
    int testChunkSize = 32;

    [MenuItem("VoxelTerraria/Tools")]
    public static void ShowWindow()
    {
        GetWindow<VoxelDebugTools>("VoxelTerraria Tools");
    }

    private void OnGUI()
    {
        settings = EditorGUILayout.ObjectField("World Settings", settings, typeof(WorldSettings), false) as WorldSettings;

        GUILayout.Space(10);

        if (GUILayout.Button("Test ChunkData Allocation"))
            TestChunkData();
    }

    private void TestChunkData()
    {
        if (settings == null)
        {
            Debug.LogWarning("Assign a WorldSettings asset.");
            return;
        }

        ChunkCoord coord = new ChunkCoord(0, 0);
        ChunkData chunk = new ChunkData(coord, settings.chunkSize, Allocator.Temp);

        chunk.Set(0, 0, 0, new Voxel(10, 1));
        chunk.Set(31, 31, 31, new Voxel(99, 5));

        Debug.Log($"Voxel[0,0,0] = {chunk.Get(0,0,0).density}, {chunk.Get(0,0,0).materialId}");
        Debug.Log($"Voxel[31,31,31] = {chunk.Get(31,31,31).density}, {chunk.Get(31,31,31).materialId}");

        chunk.Dispose();
        Debug.Log("Test completed!");
    }
}
