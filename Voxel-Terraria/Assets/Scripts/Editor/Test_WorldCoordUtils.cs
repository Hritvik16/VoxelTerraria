
using UnityEngine;
using UnityEditor;
using Unity.Mathematics;
using VoxelTerraria.World;

public class Test_WorldCoordUtils : EditorWindow
{
    private WorldSettings settings;
    private string result = "";

    [MenuItem("VoxelTerraria/Run Coordinate Test")]
    public static void Open()
    {
        GetWindow<Test_WorldCoordUtils>("Coord Test");
    }

    private void OnGUI()
    {
        settings = EditorGUILayout.ObjectField("World Settings", settings, typeof(WorldSettings), false) as WorldSettings;

        if (settings == null)
        {
            EditorGUILayout.HelpBox("Assign WorldSettings asset.", MessageType.Info);
            return;
        }

        if (GUILayout.Button("Run Random Test"))
        {
            RunTest();
        }

        EditorGUILayout.LabelField("Result:");
        EditorGUILayout.TextArea(result, GUILayout.Height(200));
    }

    private void RunTest()
    {
        float3 p = new float3(
            UnityEngine.Random.Range(-500f, 500f),
            UnityEngine.Random.Range(0f, 200f),
            UnityEngine.Random.Range(-500f, 500f)
        );

        // Convert world → chunk
        ChunkCoord chunk = WorldCoordUtils.WorldToChunk(p, settings);

        // Get chunk origin in world
        float3 chunkOriginWorld = WorldCoordUtils.ChunkOriginWorld(chunk, settings);

        // Compute global voxel index
        VoxelCoord voxel = WorldCoordUtils.WorldToVoxel(p, settings);

        // Compute chunk origin in voxel space
        float v = settings.voxelSize;
        int chunkOriginVoxelX = Mathf.FloorToInt(chunkOriginWorld.x / v);
        int chunkOriginVoxelZ = Mathf.FloorToInt(chunkOriginWorld.z / v);

        // Correct inner voxel index
        VoxelCoord inner = new VoxelCoord(
            voxel.x - chunkOriginVoxelX,
            voxel.y,
            voxel.z - chunkOriginVoxelZ
        );

        // Convert back to world
        float3 reconstructed = WorldCoordUtils.VoxelCenterWorld(chunk, inner, settings);

        float error = math.distance(p, reconstructed);
        float allowed = 0.5f * settings.voxelSize;

        result =
            $"World Pos: {p}\n" +
            $"Chunk: {chunk}\n" +
            $"Global Voxel: {voxel}\n" +
            $"ChunkOrigin World: {chunkOriginWorld}\n" +
            $"Inner Voxel: {inner}\n" +
            $"Reconstructed: {reconstructed}\n" +
            $"Error = {error} (Allowed ≤ {allowed})\n" +
            (error <= allowed ? "\nPASS ✔" : "\nFAIL ❌");
    }
}
