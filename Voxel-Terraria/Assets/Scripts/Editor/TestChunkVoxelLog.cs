using UnityEngine;
using UnityEditor;
using VoxelTerraria.World;

public static class TestChunkVoxelLog
{
    [MenuItem("VoxelTerraria/Test/Log Chunk (0,0) Voxels")]
    public static void LogChunkVoxels()
    {
        // Find the world in the scene
        var world = GameObject.FindFirstObjectByType<VoxelWorld>();
        if (world == null)
        {
            Debug.LogError("No VoxelWorld found in scene!");
            return;
        }

        // Fetch chunk (0,0)
        if (!world.TryGetChunkData(0, 0, out ChunkData chunk))
        {
            Debug.LogError("Chunk (0,0) not found. Did world generation run?");
            return;
        }

        Debug.Log($"Logging first 20 voxels for Chunk (0,0)...");

        // Log 20 sample voxels
        int size = chunk.chunkSize;
        for (int i = 0; i < 20; i++)
        {
            int x = i % size;
            int y = (i / size) % size;
            int z = (i / (size * size));

            Voxel v = chunk.Get(x, y, z);

            Debug.Log($"Voxel[{x},{y},{z}] → density={v.density}, material={v.materialId}");
        }

        Debug.Log("Finished logging chunk voxels.");
    }
}
