using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
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
        
        [Header("Dynamic Updates")]
        public float chunkUpdateDistanceThreshold = 10f;
        public float lodHysteresis = 4.0f;

        [Header("Debug")]
        public bool generateOnStart = true;
        
        [Header("LOD Settings")]
        [Range(10f, 1000f)] public float lod0Distance = 30f;
        [Range(10f, 1000f)] public float lod1Distance = 60f;
        [Range(10f, 1000f)] public float lod2Distance = 120f;

        // Use NativeList for Burst compatibility
        private NativeList<ChunkCoord3> buildList;
        private HashSet<ChunkCoord3> queuedChunks = new HashSet<ChunkCoord3>();
        private Dictionary<ChunkCoord3, GameObject> activeChunks = new Dictionary<ChunkCoord3, GameObject>();
        private Dictionary<ChunkCoord3, int> activeChunkLods = new Dictionary<ChunkCoord3, int>();
        
        private ChunkCoord3 lastPlayerChunk = new ChunkCoord3(int.MinValue, int.MinValue, int.MinValue);
        private Vector3 lastUpdatePos;

        private void OnEnable()
        {
            if (!buildList.IsCreated)
            {
                buildList = new NativeList<ChunkCoord3>(Allocator.Persistent);
            }
        }

        private void OnDisable()
        {
            if (buildList.IsCreated)
            {
                buildList.Dispose();
            }
        }

        private void Start()
        {
            if (settings == null)
            {
                Debug.LogWarning("[ChunkRebuildSystem] WorldSettings not assigned. Attempting to find...");
                var bootstrap = FindObjectOfType<SdfBootstrap>();
                if (bootstrap != null) settings = bootstrap.worldSettings;
            }
            
            if (player != null)
            {
                lastUpdatePos = player.position;
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
            while (processed < chunksPerFrame && buildList.Length > 0)
            {
                int index = buildList.Length - 1;
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

            // Check if player moved enough to trigger update
            float sqrDist = Vector3.SqrMagnitude(player.position - lastUpdatePos);
            bool movedEnough = sqrDist > (chunkUpdateDistanceThreshold * chunkUpdateDistanceThreshold);

            // Only update if player moved to a new chunk OR moved enough distance
            if (movedEnough || playerChunk.x != lastPlayerChunk.x || playerChunk.y != lastPlayerChunk.y || playerChunk.z != lastPlayerChunk.z)
            {
                lastPlayerChunk = playerChunk;
                lastUpdatePos = player.position;
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
                activeChunkLods.Remove(c);
            }

            // 2. Cleanup Queued Chunks (Remove far ones)
            // Filter the existing list to keep only those within range
            // NativeList doesn't support RemoveAll easily, so we rebuild it
            NativeList<ChunkCoord3> kept = new NativeList<ChunkCoord3>(buildList.Length, Allocator.Temp);
            for (int i = 0; i < buildList.Length; i++)
            {
                ChunkCoord3 c = buildList[i];
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
            buildList.Clear();
            buildList.AddRange(kept);
            kept.Dispose();

            // 3. Add new chunks & Check LOD updates
            int px = Mathf.FloorToInt(playerPos.x / chunkWorldSize);
            int py = Mathf.FloorToInt(playerPos.y / chunkWorldSize);
            int pz = Mathf.FloorToInt(playerPos.z / chunkWorldSize);

            // Check existing active chunks for LOD updates
            foreach (var kvp in activeChunks)
            {
                ChunkCoord3 c = kvp.Key;
                if (queuedChunks.Contains(c)) continue; // Already queued for update

                Vector3 chunkCenter = GetChunkCenter(c, chunkWorldSize);
                float dist = Vector3.Distance(chunkCenter, playerPos);
                
                // Use hysteresis for LOD calculation
                // If we are currently at LOD X, we only switch to X+1 if dist > limit + hysteresis
                // or switch to X-1 if dist < limit - hysteresis
                int currentLOD = activeChunkLods[c];
                int targetLOD = GetLodLevelWithHysteresis(dist, currentLOD);

                if (targetLOD != currentLOD)
                {
                    EnqueueChunk(c);
                }
            }

            // Add new chunks
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
                            EnqueueChunk(c);
                        }
                    }
                }
            }

            // 4. Sort buildList using Burst Job
            // We sort Descending by distance, so the End of the list is the Closest.
            if (buildList.Length > 1)
            {
                var sortJob = new ChunkSorterJob
                {
                    chunks = buildList,
                    playerPos = playerPos,
                    chunkWorldSize = chunkWorldSize
                };
                sortJob.Schedule().Complete();
            }
        }

        [BurstCompile]
        struct ChunkSorterJob : IJob
        {
            public NativeList<ChunkCoord3> chunks;
            public float3 playerPos;
            public float chunkWorldSize;

            public void Execute()
            {
                chunks.Sort(new ChunkDistComparer { playerPos = playerPos, chunkWorldSize = chunkWorldSize });
            }
        }

        struct ChunkDistComparer : IComparer<ChunkCoord3>
        {
            public float3 playerPos;
            public float chunkWorldSize;

            public int Compare(ChunkCoord3 a, ChunkCoord3 b)
            {
                float3 centerA = new float3((a.x + 0.5f) * chunkWorldSize, (a.y + 0.5f) * chunkWorldSize, (a.z + 0.5f) * chunkWorldSize);
                float3 centerB = new float3((b.x + 0.5f) * chunkWorldSize, (b.y + 0.5f) * chunkWorldSize, (b.z + 0.5f) * chunkWorldSize);

                float distA = math.distancesq(centerA, playerPos);
                float distB = math.distancesq(centerB, playerPos);

                // Descending sort: B compareTo A
                return distB.CompareTo(distA);
            }
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
            if (!queuedChunks.Contains(coord))
            {
                buildList.Add(coord);
                queuedChunks.Add(coord);
            }
        }

        private int GetLodLevel(float distance)
        {
            if (distance < lod0Distance) return 0;
            if (distance < lod1Distance) return 1;
            if (distance < lod2Distance) return 2;
            return 3;
        }

        private int GetLodLevelWithHysteresis(float distance, int currentLOD)
        {
            // Calculate thresholds with hysteresis
            // To go UP a level (0->1), we need to exceed distance + hysteresis
            // To go DOWN a level (1->0), we need to be below distance - hysteresis
            
            float h = lodHysteresis;

            // Check if we should downgrade (increase LOD level)
            if (currentLOD == 0 && distance > lod0Distance + h) return 1;
            if (currentLOD == 1 && distance > lod1Distance + h) return 2;
            if (currentLOD == 2 && distance > lod2Distance + h) return 3;

            // Check if we should upgrade (decrease LOD level)
            if (currentLOD == 3 && distance < lod2Distance - h) return 2;
            if (currentLOD == 2 && distance < lod1Distance - h) return 1;
            if (currentLOD == 1 && distance < lod0Distance - h) return 0;

            // Otherwise keep current
            // But what if we are way off? e.g. current is 0 but distance is 500?
            // The above logic only handles adjacent transitions. 
            // For robustness, if we are far outside the band, snap to the correct one.
            
            int baseLOD = GetLodLevel(distance);
            if (Mathf.Abs(baseLOD - currentLOD) > 1) return baseLOD;

            return currentLOD;
        }

        private void BuildChunk(ChunkCoord3 coord)
        {
            // Calculate LOD
            float chunkWorldSize = settings.chunkSize * settings.voxelSize;
            Vector3 chunkCenter = GetChunkCenter(coord, chunkWorldSize);
            float dist = Vector3.Distance(chunkCenter, player.position);
            
            // If we are rebuilding an existing chunk, use hysteresis to be stable
            int lodLevel;
            if (activeChunkLods.TryGetValue(coord, out int currentLOD))
            {
                lodLevel = GetLodLevelWithHysteresis(dist, currentLOD);
            }
            else
            {
                lodLevel = GetLodLevel(dist);
            }
            
            // Scale voxel size UP, scale chunk size DOWN to keep world size constant
            float currentVoxelSize = settings.voxelSize * Mathf.Pow(2, lodLevel);
            int lodChunkSize = settings.chunkSize / (1 << lodLevel);
            
            // Ensure we don't go below 1 voxel
            if (lodChunkSize < 1) lodChunkSize = 1;

            // 1. Allocate ChunkData
            ChunkData chunkData = new ChunkData(coord, lodChunkSize, lodLevel, currentVoxelSize, Allocator.TempJob);

            try
            {
                // 2. Run VoxelGenerator (Burst Job)
                VoxelGenerator.GenerateChunkVoxels(ref chunkData, SdfRuntime.Context, settings);

                // 3. Run BlockMesher (Burst Job logic inside)
                MeshData meshData = BlockMesher.BuildMesh(chunkData, settings);

                // 4. Apply Mesh
                ApplyMesh(coord, meshData, lodLevel);
            }
            finally
            {
                // Dispose ChunkData
                chunkData.Dispose();
            }
        }

        private void ApplyMesh(ChunkCoord3 coord, MeshData meshData, int lodLevel)
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

            // Update LOD tracking
            activeChunkLods[coord] = lodLevel;

            // Ensure components
            MeshFilter mf = chunkObj.GetComponent<MeshFilter>();
            if (mf == null) mf = chunkObj.AddComponent<MeshFilter>();

            MeshRenderer mr = chunkObj.GetComponent<MeshRenderer>();
            if (mr == null) mr = chunkObj.AddComponent<MeshRenderer>();

            MeshCollider mc = chunkObj.GetComponent<MeshCollider>();
            if (mc == null) mc = chunkObj.AddComponent<MeshCollider>();

            // Convert MeshData to Unity Mesh
            Mesh mesh = meshData.ToMesh(calculateNormals: false); 
            mesh.name = $"ChunkMesh_{coord.x}_{coord.y}_{coord.z}_LOD{lodLevel}";

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

        private void OnDrawGizmosSelected()
        {
            if (player == null) return;

            Vector3 center = player.position;

            // View Distance
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(center, viewDistance);

            // LOD 0
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(center, lod0Distance);

            // LOD 1
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(center, lod1Distance);

            // LOD 2
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(center, lod2Distance);
        }
    }
}
