using Unity.Collections;
using Unity.Jobs;          // <-- ADDED: For JobHandle and .Schedule()
using Unity.Mathematics;   // <-- ADDED: For high-performance math types
using System.Diagnostics;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using VoxelEngine;
using VoxelEngine.Interfaces;
using VoxelEngine.World; // FIX: Tell ChunkManager to look inside the World namespace!
using System.Runtime.InteropServices;

public partial class ChunkManager : MonoBehaviour, IVoxelWorld
{
    public int GetMapIndex(int layer, Vector3Int coord) {
        int sideXZ = 2 * renderDistanceXZ + 1;
        int sideY = 2 * renderDistanceY + 1;
        int mx = ((coord.x % sideXZ) + sideXZ) % sideXZ;
        int my = ((coord.y % sideY) + sideY) % sideY;
        int mz = ((coord.z % sideXZ) + sideXZ) % sideXZ;
        return (layer * chunksPerLayer) + (mx + (mz * sideXZ) + (my * sideXZ * sideXZ));
    }

    Vector3Int GetChunkCoord(Vector3 pos, float worldChunkSize) => 
        new Vector3Int(Mathf.FloorToInt(pos.x / worldChunkSize), Mathf.FloorToInt(pos.y / worldChunkSize), Mathf.FloorToInt(pos.z / worldChunkSize));

}
