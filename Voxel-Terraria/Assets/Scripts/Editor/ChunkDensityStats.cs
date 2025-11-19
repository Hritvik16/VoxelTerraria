using UnityEditor;
using UnityEngine;
using VoxelTerraria.World;

public static class ChunkDensityStats
{
    // Hardcode chunk coords here for now
    private const int TargetChunkX = 40;
    private const int TargetChunkZ = 40;

    [MenuItem("VoxelTerraria/Test/Analyze Chunk Density")]
    public static void AnalyzeChunk()
    {
        var world = Object.FindFirstObjectByType<VoxelWorld>();
        if (world == null)
        {
            Debug.LogError("No VoxelWorld found in scene!");
            return;
        }

        if (!world.TryGetChunkData(TargetChunkX, TargetChunkZ, out var chunk))
        {
            Debug.LogError($"Chunk ({TargetChunkX},{TargetChunkZ}) not found!");
            return;
        }

        int total = chunk.voxels.Length;
        int solid = 0;
        int air = 0;

        for (int i = 0; i < total; i++)
        {
            if (chunk.voxels[i].density < 0) solid++;
            else air++;
        }

        Debug.Log($"===== Chunk Density Stats ({TargetChunkX},{TargetChunkZ}) =====");
        Debug.Log($"Total voxels: {total}");
        Debug.Log($"Solid voxels: {solid}");
        Debug.Log($"Air voxels:   {air}");
        Debug.Log($"Solid %: {(solid / (float)total * 100f):0.00}%");
        Debug.Log($"Air %:   {(air / (float)total * 100f):0.00}%");
        Debug.Log("==========================================================");
    }
}
