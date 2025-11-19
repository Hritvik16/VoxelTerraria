using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;

namespace VoxelTerraria.World
{
    public class VoxelWorld : MonoBehaviour
    {
        [Header("References")]
        public WorldSettings worldSettings;
        public VoxelChunkView chunkPrefab;

        [Header("Debug Options")]
        public bool generateOnStart = true;
        public bool renameHierarchy = true;

        // Internal storage of ChunkData
        private Dictionary<(int x, int z), ChunkData> chunkMap;

        // Dimensions of world in chunks
        private int chunksX;
        private int chunksZ;

        private void Awake()
        {
            if (generateOnStart)
                InitializeWorld();
        }

        // ------------------------------------------------------------------
        // Initialize world → allocate ChunkData + instantiate chunk GameObjects
        // ------------------------------------------------------------------
        public void InitializeWorld()
        {
            if (worldSettings == null)
            {
                Debug.LogError("VoxelWorld: Missing WorldSettings.");
                return;
            }

            if (chunkPrefab == null)
            {
                Debug.LogError("VoxelWorld: Missing chunkPrefab (VoxelChunkView).");
                return;
            }

            chunkMap = new Dictionary<(int, int), ChunkData>();

            int chunkSize = worldSettings.chunkSize;
            float voxelSize = worldSettings.voxelSize;

            // Compute number of chunks along each axis
            chunksX = Mathf.CeilToInt(worldSettings.worldWidth / (chunkSize * voxelSize));
            chunksZ = Mathf.CeilToInt(worldSettings.worldDepth / (chunkSize * voxelSize));

            Debug.Log($"VoxelWorld: Allocating {chunksX}×{chunksZ} chunks...");

            for (int cz = 0; cz < chunksZ; cz++)
            {
                for (int cx = 0; cx < chunksX; cx++)
                {
                    ChunkCoord coord = new ChunkCoord(cx, cz);

                    // Allocate chunk data
                    ChunkData chunkData = new ChunkData(
                        coord,
                        chunkSize,
                        Unity.Collections.Allocator.Persistent
                    );

                    chunkMap[(cx, cz)] = chunkData;

                    // Instantiate chunk view
                    VoxelChunkView view = Instantiate(
                        chunkPrefab,
                        transform
                    );

                    view.SetCoord(coord);

                    // Compute actual world position
                    float3 origin = WorldCoordUtils.ChunkOriginWorld(coord, worldSettings);
                    view.transform.position = origin;

                    // Optional: store reference in view
                    view.chunkDataReference = chunkData;
                }
            }

            Debug.Log("VoxelWorld: Initialization complete.");
        }

        // ------------------------------------------------------------------
        // Cleanup native memory (ChunkData)
        // ------------------------------------------------------------------
        private void OnDestroy()
        {
            if (chunkMap == null)
                return;

            foreach (var kvp in chunkMap)
            {
                var data = kvp.Value;
                data.Dispose();
            }

            chunkMap.Clear();
        }
    }
}
