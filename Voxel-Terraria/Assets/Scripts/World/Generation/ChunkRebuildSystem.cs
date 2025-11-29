using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using VoxelTerraria.World.Meshing;
using VoxelTerraria.World.SDF;

namespace VoxelTerraria.World.Generation
{
    public class ChunkRebuildSystem : MonoBehaviour
    {
        [Header("Settings")]
        public WorldSettings settings;
        public int chunksPerFrame = 4;

        [Header("Infinite Scrolling")]
        public Transform player;
        public float viewDistance = 100f;
        public float destroyDistanceBuffer = 20f; // Extra buffer before destroying chunks

        [Header("Debug")]
        public bool generateOnStart = true;
        
        // Use a List instead of Queue to allow sorting.
        // We will sort Descending by distance, so the End of the list is the Closest.
        // This allows O(1) removal.
        private List<ChunkCoord3> buildList = new List<ChunkCoord3>();
        private HashSet<ChunkCoord3> queuedChunks = new HashSet<ChunkCoord3>();
        private Dictionary<ChunkCoord3, GameObject> activeChunks = new Dictionary<ChunkCoord3, GameObject>();
        private ChunkCoord3 lastPlayerChunk = new ChunkCoord3(int.MinValue, int.MinValue, int.MinValue);

        private void Start()
        {
            if (settings == null)
            {
                Debug.LogWarning("[ChunkRebuildSystem] WorldSettings not assigned. Attempting to find...");
                var bootstrap = FindObjectOfType<SdfBootstrap>();
                if (bootstrap != null) settings = bootstrap.worldSettings;
            }
        }

        private void Update()
        {
            // Safety check for SDF
            if (!SdfRuntime.Initialized || SdfRuntime.Context.chunkSize <= 0)
                return;

            if (settings == null)
                return;

            // Update loading logic
            UpdateChunkLoading();

            // Process list (closest first -> end of list)
            int processed = 0;
            while (processed < chunksPerFrame && buildList.Count > 0)
            {
                int index = buildList.Count - 1;
                ChunkCoord3 coord = buildList[index];
                buildList.RemoveAt(index);
                queuedChunks.Remove(coord);
                
                BuildChunk(coord);
                processed++;
            }
        }

        private void UpdateChunkLoading()
        {
            if (player == null) return;

            float chunkWorldSize = settings.chunkSize * settings.voxelSize;
            int px = Mathf.FloorToInt(player.position.x / chunkWorldSize);
            int py = Mathf.FloorToInt(player.position.y / chunkWorldSize);
            int pz = Mathf.FloorToInt(player.position.z / chunkWorldSize);
            ChunkCoord3 playerChunk = new ChunkCoord3(px, py, pz);

            // Only update if player moved to a new chunk or first run
            if (playerChunk.x != lastPlayerChunk.x || playerChunk.y != lastPlayerChunk.y || playerChunk.z != lastPlayerChunk.z)
            {
                lastPlayerChunk = playerChunk;
                RefreshChunks(player.position, chunkWorldSize);
            }
        }

        private void RefreshChunks(Vector3 playerPos, float chunkWorldSize)
        {
            int radiusChunks = Mathf.CeilToInt(viewDistance / chunkWorldSize);
            float destroyDistSq = (viewDistance + destroyDistanceBuffer) * (viewDistance + destroyDistanceBuffer);
            float viewDistSq = viewDistance * viewDistance;

            // 1. Unload far chunks (Active)
            List<ChunkCoord3> toRemove = new List<ChunkCoord3>();
            foreach (var kvp in activeChunks)
            {
                Vector3 chunkCenter = GetChunkCenter(kvp.Key, chunkWorldSize);
                if (Vector3.SqrMagnitude(chunkCenter - playerPos) > destroyDistSq)
                {
                    toRemove.Add(kvp.Key);
                }
            }

            foreach (var c in toRemove)
            {
                if (activeChunks.TryGetValue(c, out GameObject obj))
                {
                    Destroy(obj);
                }
                activeChunks.Remove(c);
            }

            // 2. Cleanup Queued Chunks (Remove far ones)
            // Filter the existing list to keep only those within range
            List<ChunkCoord3> kept = new List<ChunkCoord3>();
            foreach (var c in buildList)
            {
                Vector3 chunkCenter = GetChunkCenter(c, chunkWorldSize);
                if (Vector3.SqrMagnitude(chunkCenter - playerPos) <= destroyDistSq)
                {
                    kept.Add(c);
                }
                else
                {
                    queuedChunks.Remove(c);
                }
            }
            buildList = kept;

            // 3. Add new chunks
            int px = Mathf.FloorToInt(playerPos.x / chunkWorldSize);
            int py = Mathf.FloorToInt(playerPos.y / chunkWorldSize);
            int pz = Mathf.FloorToInt(playerPos.z / chunkWorldSize);

            for (int x = -radiusChunks; x <= radiusChunks; x++)
            {
                for (int y = -radiusChunks; y <= radiusChunks; y++)
                {
                    for (int z = -radiusChunks; z <= radiusChunks; z++)
                    {
                        ChunkCoord3 c = new ChunkCoord3(px + x, py + y, pz + z);
                        
                        // Check if already active or queued
                        if (activeChunks.ContainsKey(c) || queuedChunks.Contains(c))
                            continue;

                        Vector3 chunkCenter = GetChunkCenter(c, chunkWorldSize);

                        if (Vector3.SqrMagnitude(chunkCenter - playerPos) <= viewDistSq)
                        {
                            buildList.Add(c);
                            queuedChunks.Add(c);
                        }
                    }
                }
            }

            // 4. Sort buildList (Descending distance, so end is closest)
            buildList.Sort((a, b) => {
                float distA = Vector3.SqrMagnitude(GetChunkCenter(a, chunkWorldSize) - playerPos);
                float distB = Vector3.SqrMagnitude(GetChunkCenter(b, chunkWorldSize) - playerPos);
                // Descending sort: B compareTo A
                return distB.CompareTo(distA);
            });
        }

        private Vector3 GetChunkCenter(ChunkCoord3 c, float chunkWorldSize)
        {
            return new Vector3(
                (c.x + 0.5f) * chunkWorldSize,
                (c.y + 0.5f) * chunkWorldSize,
                (c.z + 0.5f) * chunkWorldSize
            );
        }

        public void EnqueueChunk(ChunkCoord3 coord)
        {
            if (!queuedChunks.Contains(coord) && !activeChunks.ContainsKey(coord))
            {
                buildList.Add(coord);
                queuedChunks.Add(coord);
                // Note: If manually enqueued, we might want to trigger a sort, 
                // but for now we rely on the next RefreshChunks or just process it eventually.
                // If strictly needed, we could sort here, but it might be expensive.
            }
        }

        private void BuildChunk(ChunkCoord3 coord)
        {
            // 1. Allocate ChunkData
            ChunkData chunkData = new ChunkData(coord, settings.chunkSize, Allocator.TempJob);

            try
            {
                // 2. Run VoxelGenerator (Burst Job)
                VoxelGenerator.GenerateChunkVoxels(ref chunkData, SdfRuntime.Context, settings);

                // 3. Run BlockMesher (Burst Job logic inside)
                MeshData meshData = BlockMesher.BuildMesh(chunkData, settings);

                // 4. Apply Mesh
                ApplyMesh(coord, meshData);
            }
            finally
            {
                // Dispose ChunkData
                chunkData.Dispose();
            }
        }

        private void ApplyMesh(ChunkCoord3 coord, MeshData meshData)
        {
            // Create or get GameObject
            GameObject chunkObj;
            if (!activeChunks.TryGetValue(coord, out chunkObj))
            {
                chunkObj = new GameObject($"Chunk_{coord.x}_{coord.y}_{coord.z}");
                chunkObj.transform.parent = transform;
                
                // Position the chunk
                float chunkWorldSize = settings.chunkSize * settings.voxelSize;
                chunkObj.transform.position = new Vector3(
                    coord.x * chunkWorldSize,
                    coord.y * chunkWorldSize,
                    coord.z * chunkWorldSize
                );

                activeChunks[coord] = chunkObj;
            }

            // Ensure components
            MeshFilter mf = chunkObj.GetComponent<MeshFilter>();
            if (mf == null) mf = chunkObj.AddComponent<MeshFilter>();

            MeshRenderer mr = chunkObj.GetComponent<MeshRenderer>();
            if (mr == null) mr = chunkObj.AddComponent<MeshRenderer>();

            MeshCollider mc = chunkObj.GetComponent<MeshCollider>();
            if (mc == null) mc = chunkObj.AddComponent<MeshCollider>();

            // Convert MeshData to Unity Mesh
            Mesh mesh = meshData.ToMesh(calculateNormals: false); 

            mf.sharedMesh = mesh;
            mc.sharedMesh = mesh;

            // Assign materials using TerrainMaterialLibrary
            if (mesh.subMeshCount > 1)
            {
                mr.sharedMaterials = TerrainMaterialLibrary.GetMaterials(mesh.subMeshCount);
            }
            else
            {
                // Fallback or single material
                Material[] mats = TerrainMaterialLibrary.GetMaterials(8);
                if (mats.Length > 1 && mats[1] != null)
                    mr.sharedMaterial = mats[1]; // Default to Grass (ID 1)
            }
        }
    }
}
