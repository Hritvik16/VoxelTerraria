using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Burst;

public class LightManager : MonoBehaviour
{
    public static LightManager Instance;

    [Header("Sky-Light Map Settings")]
    public int gridWidth = 128; 
    public int gridHeight = 64;  
    public int gridDepth = 128;  
    public float cellSize = 1.6f; 
    public byte lightFalloff = 40; 
    public int bleedPasses = 6;    

    private Texture3D skyLightTexture;
    private NativeArray<byte> lightDataA;
    private NativeArray<byte> lightDataB;
    private NativeArray<byte> solidityGrid;
    private NativeArray<ChunkManager.ChunkData> nativeChunkMap;

    private float timer = 0f;
    private Vector3 currentMapCenter;

    // --- ASYNC JOB TRACKING ---
    private JobHandle activeLightJobHandle;
    public bool isJobRunning = false;
    private System.Diagnostics.Stopwatch asyncStopwatch = new System.Diagnostics.Stopwatch();

    void Awake()
    {
        Instance = this;
        
        int totalCells = gridWidth * gridHeight * gridDepth;
        lightDataA = new NativeArray<byte>(totalCells, Allocator.Persistent);
        lightDataB = new NativeArray<byte>(totalCells, Allocator.Persistent);
        solidityGrid = new NativeArray<byte>(totalCells, Allocator.Persistent);

        skyLightTexture = new Texture3D(gridWidth, gridHeight, gridDepth, TextureFormat.R8, false);
        skyLightTexture.filterMode = FilterMode.Trilinear; 
        skyLightTexture.wrapMode = TextureWrapMode.Clamp;

        Shader.SetGlobalTexture("_SkyLightMap", skyLightTexture);
        Shader.SetGlobalVector("_LightMapCenter", Vector3.zero);
        Shader.SetGlobalVector("_LightMapParams", new Vector4(gridWidth, gridHeight, gridDepth, cellSize));
    }

    void Update()
    {
        if (ChunkManager.Instance == null || ChunkManager.Instance.worldLoaders.Count == 0 || !ChunkManager.Instance.IsReady()) return;

        if (!nativeChunkMap.IsCreated) {
            nativeChunkMap = new NativeArray<ChunkManager.ChunkData>(ChunkManager.Instance.chunkMapArray.Length, Allocator.Persistent);
        }

        // --- ASYNC COMPLETION CHECK ---
        // If the background job is running, check if it's done without blocking the main thread!
        if (isJobRunning) {
            if (activeLightJobHandle.IsCompleted) {
                activeLightJobHandle.Complete(); // Safely close the job
                
                NativeArray<byte> finalLightData = (bleedPasses % 2 == 0) ? lightDataA : lightDataB;
                skyLightTexture.SetPixelData(finalLightData, 0);
                skyLightTexture.Apply(false); // Push to GPU
                Shader.SetGlobalVector("_LightMapCenter", currentMapCenter);
                
                isJobRunning = false;
                asyncStopwatch.Stop();
                // UnityEngine.Debug.Log($"[Sky-Light] Async Pipeline Completed in: {asyncStopwatch.ElapsedMilliseconds} ms");
            }
            return; // Exit early, let the game keep running at 100+ FPS
        }

        // --- SAFETY LOCK ---
        // If ChunkManager is currently writing to the terrain, do not start a new light job!
        if (ChunkManager.Instance.isTerrainJobRunning) return;

        timer += Time.deltaTime;
        if (timer >= 0.1f) 
        {
            timer = 0f;
            ScheduleSkyLightMap();
        }
    }

    private void ScheduleSkyLightMap()
    {
        asyncStopwatch.Restart();
        isJobRunning = true;

        Transform player = ChunkManager.Instance.worldLoaders[0];
        // THE FIX: Snap the grid to the 1.6m cell size, NOT the 6.4m chunk size. 
        // This stops the massive "popping" shadows.
        currentMapCenter = new Vector3(
            Mathf.Floor(player.position.x / cellSize) * cellSize,
            Mathf.Floor(player.position.y / cellSize) * cellSize,
            Mathf.Floor(player.position.z / cellSize) * cellSize
        );

        Vector3 mapMin = currentMapCenter - new Vector3(gridWidth, gridHeight, gridDepth) * (cellSize * 0.5f);

        NativeArray<ChunkManager.ChunkData>.Copy(ChunkManager.Instance.chunkMapArray, nativeChunkMap);

        SkyCastJob skyJob = new SkyCastJob {
            gridWidth = gridWidth, gridHeight = gridHeight, gridDepth = gridDepth,
            cellSize = cellSize, voxelScale = ChunkManager.Instance.voxelScale,
            mapMin = mapMin,
            renderDistanceXZ = ChunkManager.Instance.renderDistanceXZ,
            renderDistanceY = ChunkManager.Instance.renderDistanceY,
            denseChunkPool = ChunkManager.Instance.cpuDenseChunkPool,
            chunkMap = nativeChunkMap,
            solidityGrid = solidityGrid,
            lightGrid = lightDataA 
        };
        
        activeLightJobHandle = skyJob.Schedule(gridWidth * gridDepth, 32);

        // JOB 2: Terraria Flood Fill
        for (int i = 0; i < bleedPasses; i++) {
            bool aToB = (i % 2 == 0);
            LightBleedJob bleedJob = new LightBleedJob {
                gridWidth = gridWidth, gridHeight = gridHeight, gridDepth = gridDepth,
                falloff = lightFalloff,
                solidityGrid = solidityGrid,
                lightIn = aToB ? lightDataA : lightDataB,
                lightOut = aToB ? lightDataB : lightDataA
            };
            activeLightJobHandle = bleedJob.Schedule(gridWidth * gridHeight * gridDepth, 128, activeLightJobHandle);
        }

        // DO NOT CALL .Complete() HERE! Let the Update() loop catch it naturally next frame.
    }

    void OnDestroy()
    {
        if (isJobRunning) activeLightJobHandle.Complete();
        if (lightDataA.IsCreated) lightDataA.Dispose();
        if (lightDataB.IsCreated) lightDataB.Dispose();
        if (solidityGrid.IsCreated) solidityGrid.Dispose();
        if (nativeChunkMap.IsCreated) nativeChunkMap.Dispose();
        if (skyLightTexture != null) Destroy(skyLightTexture);
    }

    // =====================================================================
    // BURST JOBS
    // =====================================================================

    [BurstCompile(CompileSynchronously = true, FloatMode = FloatMode.Fast, DisableSafetyChecks = true)]
    public struct SkyCastJob : IJobParallelFor
    {
        public int gridWidth, gridHeight, gridDepth;
        public float cellSize, voxelScale;
        public Vector3 mapMin;
        public int renderDistanceXZ, renderDistanceY;

        [ReadOnly] public NativeArray<uint> denseChunkPool;
        [ReadOnly] public NativeArray<ChunkManager.ChunkData> chunkMap;

        [WriteOnly] public NativeArray<byte> solidityGrid;
        [WriteOnly] public NativeArray<byte> lightGrid;

        public void Execute(int index)
        {
            int x = index % gridWidth;
            int z = index / gridWidth;
            int sideXZ = 2 * renderDistanceXZ + 1;
            int sideY = 2 * renderDistanceY + 1;

            byte currentLight = 255;

            // FIX 1: Removed the broken chunkHeights validation. 
            // Light naturally falls from the top of the local grid.

            for (int y = gridHeight - 1; y >= 0; y--)
            {
                int flatIdx = x + (y * gridWidth) + (z * gridWidth * gridHeight);

                float3 worldPos = (float3)mapMin + new float3(x, y, z) * cellSize;
                int3 voxelGlobalPos = new int3(
                    (int)math.floor(worldPos.x / voxelScale),
                    (int)math.floor(worldPos.y / voxelScale),
                    (int)math.floor(worldPos.z / voxelScale)
                );

                int chunkX = (int)math.floor(voxelGlobalPos.x / 32f);
                int chunkY = (int)math.floor(voxelGlobalPos.y / 32f);
                int chunkZ = (int)math.floor(voxelGlobalPos.z / 32f);

                int mx = (int)(((uint)(chunkX + 400000 * sideXZ)) % (uint)sideXZ);
                int my = (int)(((uint)(chunkY + 400000 * sideY)) % (uint)sideY);
                int mz = (int)(((uint)(chunkZ + 400000 * sideXZ)) % (uint)sideXZ);
                int mapIndex = mx + mz * sideXZ + my * sideXZ * sideXZ;

                bool isSolid = false;
                
                if (mapIndex >= 0 && mapIndex < chunkMap.Length) {
                    ChunkManager.ChunkData cd = chunkMap[mapIndex];
                    if ((cd.packedState & 1) != 0 && cd.densePoolIndex != 0xFFFFFFFF) {
                        int localX = voxelGlobalPos.x - (chunkX * 32);
                        int localY = voxelGlobalPos.y - (chunkY * 32);
                        int localZ = voxelGlobalPos.z - (chunkZ * 32);

                        if (localX >= 0 && localX < 32 && localY >= 0 && localY < 32 && localZ >= 0 && localZ < 32) {
                            int bitFlatIdx = localX + (localY << 5) + (localZ << 10);
                            int uintIdx = bitFlatIdx >> 5;
                            int bitIdx = bitFlatIdx & 31;
                            
                            uint matData = denseChunkPool[(int)(cd.densePoolIndex * 1024u) + uintIdx];
                            if ((matData & (1u << bitIdx)) != 0) isSolid = true;
                        }
                    }
                }

                if (isSolid) {
                    currentLight = 0; 
                    solidityGrid[flatIdx] = 1;
                } else {
                    solidityGrid[flatIdx] = 0;
                }
                
                lightGrid[flatIdx] = currentLight;
            }
        }
    }

    [BurstCompile(CompileSynchronously = true, DisableSafetyChecks = true)]
    public struct LightBleedJob : IJobParallelFor
    {
        public int gridWidth, gridHeight, gridDepth;
        public byte falloff;

        [ReadOnly] public NativeArray<byte> solidityGrid;
        [ReadOnly] public NativeArray<byte> lightIn;
        [WriteOnly] public NativeArray<byte> lightOut;

        public void Execute(int index)
        {
            int x = index % gridWidth;
            int y = (index / gridWidth) % gridHeight;
            int z = index / (gridWidth * gridHeight);

            int maxLight = lightIn[index];

            // FIX 2: THE TRILINEAR POISONING FIX
            // Solid blocks are no longer forced to 0. They are allowed to absorb light from Air.
            // BUT, we only ever pull light FROM neighboring Air blocks. Solid blocks do not pass light.

            if (x > 0 && solidityGrid[index - 1] == 0) maxLight = math.max(maxLight, lightIn[index - 1] - falloff);
            if (x < gridWidth - 1 && solidityGrid[index + 1] == 0) maxLight = math.max(maxLight, lightIn[index + 1] - falloff);
            
            if (y > 0 && solidityGrid[index - gridWidth] == 0) maxLight = math.max(maxLight, lightIn[index - gridWidth] - falloff);
            if (y < gridHeight - 1 && solidityGrid[index + gridWidth] == 0) maxLight = math.max(maxLight, lightIn[index + gridWidth] - falloff);
            
            if (z > 0 && solidityGrid[index - (gridWidth * gridHeight)] == 0) maxLight = math.max(maxLight, lightIn[index - (gridWidth * gridHeight)] - falloff);
            if (z < gridDepth - 1 && solidityGrid[index + (gridWidth * gridHeight)] == 0) maxLight = math.max(maxLight, lightIn[index + (gridWidth * gridHeight)] - falloff);

            lightOut[index] = (byte)maxLight;
        }
    }
}