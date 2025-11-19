using UnityEngine;
using Unity.Collections;
using VoxelTerraria.World;

public class TestChunkData : MonoBehaviour
{
    public int chunkSize = 32;

    
    void Start()
    {
        Debug.Log("Testing ChunkData allocation...");

        ChunkCoord coord = new ChunkCoord(0, 0);
        ChunkData chunk = new ChunkData(coord, chunkSize, Allocator.Temp);

        // Write some voxels
        chunk.Set(0, 0, 0, new Voxel(10, 1));
        chunk.Set(chunkSize - 1, chunkSize - 1, chunkSize - 1, new Voxel(20, 2));

        // Read them
        Voxel a = chunk.Get(0, 0, 0);
        Voxel b = chunk.Get(chunkSize - 1, chunkSize - 1, chunkSize - 1);

        Debug.Log($"Voxel A: density={a.density}, material={a.materialId}");
        Debug.Log($"Voxel B: density={b.density}, material={b.materialId}");

        // Cleanup
        chunk.Dispose();

        Debug.Log("ChunkData test completed successfully!");
    }
}
