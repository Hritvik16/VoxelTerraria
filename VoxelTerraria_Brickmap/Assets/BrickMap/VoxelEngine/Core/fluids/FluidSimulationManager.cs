using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using VoxelEngine.World;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

public class FluidSimulationManager : MonoBehaviour
{
    public static FluidSimulationManager Instance { get; private set; }

    [Header("Engine Links")]
    public ChunkManager chunkManager;
    public ComputeShader fluidCompute;

    [Header("Data Architecture")]
    public const int FLUID_TICKET_SIZE = 16384; 
    public int maxDynamicFluidTickets = 4000;

    [Header("Mailbox Architecture")]
    public int maxRequestsPerFrame = 4000; 
    public int maxAutoThawsPerFrame = 2; // The throttle
    public const int MAX_THAWS_STAGING = 10; // The hardware buffer limit

    // --- NEW: STAGING BUFFERS ---
    private ComputeBuffer thawStagingBuffer;
    private int currentThawIndex = 0;
    private int kernel_Clear;
    private int kernel_Thaw;

    [Header("Physics Simulation")]
    public float fluidTickRate = 0.05f; // 20 Ticks Per Second
    private float fluidTimer = 0f;
    private int currentTick = 0;

    // --- PHASE 1: VRAM ---
    private ComputeBuffer dynamicFluidBuffer;       
    private ComputeBuffer chunkFluidPointersBuffer; 
    private Queue<uint> freeFluidTickets;
    private uint[] cpuFluidPointers;                
    private int totalMapCapacity;

    // --- PHASE 2: THE EVENT BRIDGE ---
    private ComputeBuffer spatialDirtyMaskBuffer; 
    private int[] cpuDirtyMask;                   
    private ComputeBuffer gpuToCpuRequestsBuffer; 
    private ComputeBuffer gpuToCpuCountBuffer;    
    private int[] cpuRequestCount = new int[1];
    private ComputeBuffer cpuToGpuEditsBuffer;    
    private uint[] cpuToGpuEditsArray;

    // --- NEW: THE FLUID VAULT & POOL ---
    private Dictionary<ChunkManager.ChunkHashKey, uint[]> frozenFluidVault = new Dictionary<ChunkManager.ChunkHashKey, uint[]>();
    private Stack<uint[]> fluidArrayPool = new Stack<uint[]>();

    // --- PHASE 3: PHYSICS TRACKING ---
    private ComputeBuffer chunkAwakeFlagsBuffer;
    private ComputeBuffer activeTicketsBuffer;
    private int4[] cpuActiveTickets;

    private int kernel_WakeDirty;
    private int kernel_CSMain;
    private int kernel_Janitor;
    private int kernel_Inject;
    private int kernel_Domino;

    void OnEnable() { Instance = this; }

    public void InitializeBuffers(int mapCapacity) {
        totalMapCapacity = mapCapacity;

        dynamicFluidBuffer = new ComputeBuffer(maxDynamicFluidTickets * FLUID_TICKET_SIZE, sizeof(uint));
        chunkFluidPointersBuffer = new ComputeBuffer(totalMapCapacity, sizeof(uint));

        cpuFluidPointers = new uint[totalMapCapacity];
        freeFluidTickets = new Queue<uint>(maxDynamicFluidTickets);
        for (int i = 0; i < totalMapCapacity; i++) cpuFluidPointers[i] = 0xFFFFFFFF;
        for (uint i = 0; i < maxDynamicFluidTickets; i++) freeFluidTickets.Enqueue(i);
        chunkFluidPointersBuffer.SetData(cpuFluidPointers);

        spatialDirtyMaskBuffer = new ComputeBuffer(totalMapCapacity, sizeof(int));
        cpuDirtyMask = new int[totalMapCapacity];
        for(int i = 0; i < totalMapCapacity; i++) cpuDirtyMask[i] = 0;
        spatialDirtyMaskBuffer.SetData(cpuDirtyMask);

        // 12 bytes = int3 (The size of your HLSL Mailbox Append buffer)
        gpuToCpuRequestsBuffer = new ComputeBuffer(maxRequestsPerFrame, 12, ComputeBufferType.Append);
        gpuToCpuCountBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.Raw);
        
        cpuToGpuEditsBuffer = new ComputeBuffer(maxRequestsPerFrame, sizeof(uint) * 2);
        cpuToGpuEditsArray = new uint[maxRequestsPerFrame * 2];

        // --- NEW: PHASE 3 BUFFERS ---
        chunkAwakeFlagsBuffer = new ComputeBuffer(maxDynamicFluidTickets, sizeof(int));
        chunkAwakeFlagsBuffer.SetData(new int[maxDynamicFluidTickets]); // Initialize to 0

        activeTicketsBuffer = new ComputeBuffer(maxDynamicFluidTickets, 16); // 16 bytes = int4
        cpuActiveTickets = new int4[maxDynamicFluidTickets];

        kernel_WakeDirty = fluidCompute.FindKernel("WakeDirtyChunks");
        kernel_CSMain = fluidCompute.FindKernel("CSMain");
        kernel_Janitor = fluidCompute.FindKernel("ManageSleepStates");
        kernel_Inject = fluidCompute.FindKernel("InjectFluid");
        kernel_Domino = fluidCompute.FindKernel("PropagateWakeStates");
        kernel_Clear = fluidCompute.FindKernel("ClearFluidChunk");
        kernel_Thaw = fluidCompute.FindKernel("ThawFluidChunk");

        thawStagingBuffer = new ComputeBuffer(MAX_THAWS_STAGING * FLUID_TICKET_SIZE, sizeof(uint));

        Debug.Log($"[FluidSidecar] Physics Engine Online. Tick Rate: {fluidTickRate}s.");
    }

    void OnDisable() {
        dynamicFluidBuffer?.Release();
        chunkFluidPointersBuffer?.Release();
        spatialDirtyMaskBuffer?.Release();
        gpuToCpuRequestsBuffer?.Release();
        gpuToCpuCountBuffer?.Release();
        cpuToGpuEditsBuffer?.Release();
        chunkAwakeFlagsBuffer?.Release();
        activeTicketsBuffer?.Release();
        thawStagingBuffer?.Release();
    }

    // --- PHASE 1 & 2 METHODS (FREEZE & THAW) ---
    
    private uint[] GetArrayFromPool() {
        return fluidArrayPool.Count > 0 ? fluidArrayPool.Pop() : new uint[FLUID_TICKET_SIZE];
    }

    public uint AllocateFluidTicket(int mapIndex, ChunkManager.ChunkHashKey key) {
        if (freeFluidTickets.Count > 0) {
            uint ticket = freeFluidTickets.Dequeue();
            cpuFluidPointers[mapIndex] = ticket;

            // --- THE THAW ---
            if (frozenFluidVault.TryGetValue(key, out uint[] savedData)) {
                if (currentThawIndex < MAX_THAWS_STAGING) {
                    // 1. Upload to the lightweight staging buffer (No CPU/GPU stall!)
                    thawStagingBuffer.SetData(savedData, 0, currentThawIndex * FLUID_TICKET_SIZE, FLUID_TICKET_SIZE);
                    
                    // 2. Command the GPU to natively copy the data
                    fluidCompute.SetInt("_ThawTicket", (int)ticket);
                    fluidCompute.SetInt("_ThawOffset", currentThawIndex);
                    fluidCompute.SetBuffer(kernel_Thaw, "_ThawStaging", thawStagingBuffer);
                    fluidCompute.SetBuffer(kernel_Thaw, "_FluidWrite", dynamicFluidBuffer);
                    fluidCompute.SetBuffer(kernel_Thaw, "_ChunkAwakeFlags", chunkAwakeFlagsBuffer);
                    fluidCompute.Dispatch(kernel_Thaw, Mathf.CeilToInt(FLUID_TICKET_SIZE / 64f), 1, 1);
                    
                    currentThawIndex++;
                } else {
                    // Failsafe: Revert to blocking sync if the pipeline is somehow overwhelmed
                    dynamicFluidBuffer.SetData(savedData, 0, (int)ticket * FLUID_TICKET_SIZE, FLUID_TICKET_SIZE);
                    chunkAwakeFlagsBuffer.SetData(new int[] { 2 }, 0, (int)ticket, 1);
                }

                fluidArrayPool.Push(savedData);
                frozenFluidVault.Remove(key);
            } else {
                // THE NATIVE ZERO-BANDWIDTH CLEAR
                // No arrays pushed across PCIe! The GPU just zeroes the memory natively.
                fluidCompute.SetInt("_ClearTicket", (int)ticket);
                fluidCompute.SetBuffer(kernel_Clear, "_FluidWrite", dynamicFluidBuffer);
                fluidCompute.SetBuffer(kernel_Clear, "_ChunkAwakeFlags", chunkAwakeFlagsBuffer);
                fluidCompute.Dispatch(kernel_Clear, Mathf.CeilToInt(FLUID_TICKET_SIZE / 64f), 1, 1);
            }

            chunkFluidPointersBuffer.SetData(cpuFluidPointers, mapIndex, mapIndex, 1);
            return ticket;
        }
        return 0xFFFFFFFF;
    }

    // --- THE FREEZE ---
    public void FreezeAndFreeTicket(int mapIndex, ChunkManager.ChunkHashKey key) {
        uint ticket = cpuFluidPointers[mapIndex];
        if (ticket == 0xFFFFFFFF) return;

        int offset = (int)ticket * FLUID_TICKET_SIZE * 4; // uint is 4 bytes
        int size = FLUID_TICKET_SIZE * 4;

        // Safely extract the VRAM to C# RAM without stalling the main thread
        AsyncGPUReadback.Request(dynamicFluidBuffer, size, offset, (request) => {
            if (request.hasError || !Application.isPlaying) return;
            uint[] data = GetArrayFromPool();
            request.GetData<uint>().CopyTo(data);
            frozenFluidVault[key] = data;
        });

        // Instantly free the ticket for reuse, stopping the memory leak
        cpuFluidPointers[mapIndex] = 0xFFFFFFFF;
        chunkFluidPointersBuffer.SetData(cpuFluidPointers, mapIndex, mapIndex, 1);
        freeFluidTickets.Enqueue(ticket);
    }

    public void SetChunkDirty(int mapIndex) {
        if (mapIndex >= 0 && mapIndex < totalMapCapacity) {
            cpuDirtyMask[mapIndex] = 1;
            
            // FIX 2A: WAKE UP NEIGHBORS!
            Vector3Int coord = chunkManager.chunkTargetCoordArray[mapIndex];
            Vector3Int[] neighbors = new Vector3Int[] {
                new Vector3Int(0, 1, 0), new Vector3Int(0, -1, 0),
                new Vector3Int(1, 0, 0), new Vector3Int(-1, 0, 0),
                new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1)
            };
            
            for (int i = 0; i < 6; i++) {
                int nIdx = chunkManager.GetMapIndex(0, coord + neighbors[i]);
                if (nIdx >= 0 && nIdx < totalMapCapacity && chunkManager.chunkTargetCoordArray[nIdx] == coord + neighbors[i]) {
                    cpuDirtyMask[nIdx] = 1;
                }
            }
        }
    }

    // --- NEW: THE AUTO-THAW RADAR (TIME-SLICED) ---
    private float autoThawTimer = 0f;
    public int maxThawsPerFrame = 2; // Cap the PCIe upload bandwidth per frame!
    
    private Queue<ChunkManager.ChunkHashKey> thawQueue = new Queue<ChunkManager.ChunkHashKey>();
    private HashSet<ChunkManager.ChunkHashKey> queuedForThaw = new HashSet<ChunkManager.ChunkHashKey>();

    private void ProcessAutoThaw() {
        if (frozenFluidVault.Count == 0) return;

        autoThawTimer += Time.deltaTime;
        if (autoThawTimer >= 0.5f) { 
            autoThawTimer = 0f;

            // 1. Scan the freezer for valid chunks and add them to the waiting list
            foreach (var key in frozenFluidVault.Keys) {
                if (key.layer == 0 && !queuedForThaw.Contains(key)) {
                    int mapIndex = chunkManager.GetMapIndex(0, key.coord);
                    
                    if (mapIndex >= 0 && mapIndex < totalMapCapacity && chunkManager.chunkTargetCoordArray[mapIndex] == key.coord) {
                        if (cpuFluidPointers[mapIndex] == 0xFFFFFFFF) {
                            thawQueue.Enqueue(key);
                            queuedForThaw.Add(key); // Prevent adding the same chunk twice
                        }
                    }
                }
            }
        }
    }

    // --- PHASE 3: THE HEARTBEAT ---
    void Update() {
        if (chunkManager == null || !chunkManager.IsReady()) return;

        currentThawIndex = 0; // Reset the Staging Offset
        ProcessAutoThaw();

        // --- THE TIME-SLICED DISPATCHER ---
        // Bleed off the queue slowly to maintain a flawless 60 FPS
        int thawsThisFrame = 0;
        while (thawQueue.Count > 0 && thawsThisFrame < maxThawsPerFrame) {
            var key = thawQueue.Dequeue();
            queuedForThaw.Remove(key);
            
            // Double check validity in case the player moved at supersonic speeds
            int mapIndex = chunkManager.GetMapIndex(0, key.coord);
            if (mapIndex >= 0 && mapIndex < totalMapCapacity && chunkManager.chunkTargetCoordArray[mapIndex] == key.coord) {
                if (cpuFluidPointers[mapIndex] == 0xFFFFFFFF) {
                    AllocateFluidTicket(mapIndex, key);
                    thawsThisFrame++;
                }
            }
        }

        fluidTimer += Time.deltaTime;
        int ticksThisFrame = 0;
        
        while (fluidTimer >= fluidTickRate && ticksThisFrame < 2) {
            TickPhysics();
            fluidTimer -= fluidTickRate;
            ticksThisFrame++;
        }
        if (fluidTimer > fluidTickRate) fluidTimer = fluidTickRate;
    }

    private int[] requestData = new int[4000 * 3]; // Mailbox read array

    void TickPhysics() {
        // --- 1. READ THE INBOX (GPU -> CPU) ---
        ComputeBuffer.CopyCount(gpuToCpuRequestsBuffer, gpuToCpuCountBuffer, 0);
        gpuToCpuCountBuffer.GetData(cpuRequestCount);
        int reqCount = Mathf.Min(cpuRequestCount[0], maxRequestsPerFrame);

        if (reqCount > 0) {
            gpuToCpuRequestsBuffer.GetData(requestData, 0, 0, reqCount * 3);
            int ticketsCreated = 0;
            
            for (int i = 0; i < reqCount; i++) {
                int cx = requestData[i * 3];
                int cy = requestData[i * 3 + 1];
                int cz = requestData[i * 3 + 2];
                Vector3Int coord = new Vector3Int(cx, cy, cz);
                
                int mapIndex = chunkManager.GetMapIndex(0, coord);
                if (mapIndex >= 0 && mapIndex < totalMapCapacity && chunkManager.chunkTargetCoordArray[mapIndex] == coord) {
                    
                    // The GPU wants to flow into this chunk. Give it a ticket!
                    if (cpuFluidPointers[mapIndex] == 0xFFFFFFFF) {
                        ChunkManager.ChunkHashKey key = new ChunkManager.ChunkHashKey { layer = 0, coord = coord };
                        AllocateFluidTicket(mapIndex, key);
                        SetChunkDirty(mapIndex); 
                        ticketsCreated++;
                    }
                }
            }
            if (ticketsCreated > 0) Debug.Log($"[Fluid Mailbox] Water expanded! C# allocated {ticketsCreated} new fluid chunks.");
        }
        gpuToCpuRequestsBuffer.SetCounterValue(0); 

        // --- 2. BUILD ACTIVE TICKETS LIST ---
        int activeCount = 0;
        for (int i = 0; i < totalMapCapacity; i++) {
            uint ticket = cpuFluidPointers[i];
            if (ticket != 0xFFFFFFFF) {
                Vector3Int coord = chunkManager.chunkTargetCoordArray[i];
                cpuActiveTickets[activeCount] = new int4((int)ticket, coord.x, coord.y, coord.z);
                activeCount++;
            }
        }
        if (activeCount == 0) return; 

        activeTicketsBuffer.SetData(cpuActiveTickets, 0, 0, activeCount);

        // --- 3. WAKE DIRTY CHUNKS ---
        spatialDirtyMaskBuffer.SetData(cpuDirtyMask); // Sync C# edits to GPU
        fluidCompute.SetInt("_TotalMapCapacity", totalMapCapacity);
        fluidCompute.SetBuffer(kernel_WakeDirty, "_SpatialDirtyMask", spatialDirtyMaskBuffer);
        fluidCompute.SetBuffer(kernel_WakeDirty, "_ChunkFluidPointers", chunkFluidPointersBuffer);
        fluidCompute.SetBuffer(kernel_WakeDirty, "_ChunkAwakeFlags", chunkAwakeFlagsBuffer);
        fluidCompute.Dispatch(kernel_WakeDirty, Mathf.CeilToInt(totalMapCapacity / 64f), 1, 1);

        // Clear C# mask now that GPU has processed it
        for(int i=0; i<totalMapCapacity; i++) cpuDirtyMask[i] = 0; 

        // --- 4. SIMULATE CA (8-Pass Checkerboard) ---
        fluidCompute.SetInt("_DispatchCount", activeCount);
        fluidCompute.SetInt("_Tick", currentTick);
        fluidCompute.SetVector("_RenderBounds", new Vector4(chunkManager.renderDistanceXZ, chunkManager.renderDistanceY, 0, 0));
        fluidCompute.SetInt("_ChunkCount", chunkManager.chunksPerLayer); 
        
        fluidCompute.SetBuffer(kernel_CSMain, "_ActiveTickets", activeTicketsBuffer);
        fluidCompute.SetBuffer(kernel_CSMain, "_FluidWrite", dynamicFluidBuffer);
        fluidCompute.SetBuffer(kernel_CSMain, "_ChunkAwakeFlags", chunkAwakeFlagsBuffer);
        fluidCompute.SetBuffer(kernel_CSMain, "_ChunkFluidPointers", chunkFluidPointersBuffer);
        fluidCompute.SetBuffer(kernel_CSMain, "_TicketRequestsInbox", gpuToCpuRequestsBuffer);
        
        Vector4[] checkerOffsets = new Vector4[8] {
            new Vector4(0,0,0,0), new Vector4(1,0,0,0), new Vector4(0,1,0,0), new Vector4(1,1,0,0),
            new Vector4(0,0,1,0), new Vector4(1,0,1,0), new Vector4(0,1,1,0), new Vector4(1,1,1,0)
        };

        // Dispatch perfectly mathematically sized thread groups
        for (int pass = 0; pass < 8; pass++) {
            fluidCompute.SetVector("_CheckerOffset", checkerOffsets[pass]);
            fluidCompute.Dispatch(kernel_CSMain, activeCount * 2, 2, 2);
        }

        // --- 4.5: THE DOMINO WAKE-UP ---
        fluidCompute.SetBuffer(kernel_Domino, "_ActiveTickets", activeTicketsBuffer);
        fluidCompute.SetBuffer(kernel_Domino, "_ChunkAwakeFlags", chunkAwakeFlagsBuffer);
        // Bind the pointers & data because CheckSpace needs to read them!
        fluidCompute.SetBuffer(kernel_Domino, "_ChunkFluidPointers", chunkFluidPointersBuffer);
        fluidCompute.SetBuffer(kernel_Domino, "_FluidWrite", dynamicFluidBuffer); 
        fluidCompute.SetInt("_DispatchCount", activeCount);
        
        fluidCompute.Dispatch(kernel_Domino, Mathf.CeilToInt(activeCount / 64f), 1, 1);

        // --- 5. THE JANITOR ---
        fluidCompute.SetBuffer(kernel_Janitor, "_ActiveTickets", activeTicketsBuffer);
        fluidCompute.SetBuffer(kernel_Janitor, "_ChunkAwakeFlags", chunkAwakeFlagsBuffer);
        fluidCompute.Dispatch(kernel_Janitor, Mathf.CeilToInt(activeCount / 64f), 1, 1);

        currentTick++;
    }

    // --- NEW: THE BULK FLUID BRUSH (8-PASS CHECKERBOARD) ---
    public void SpawnFluidBulk(List<Vector3Int> positions, uint matID) {
        int count = Mathf.Min(positions.Count, maxRequestsPerFrame);
        int validEdits = 0;

        // Sync the newly spawned fluid to the exact Phase the GPU is currently running!
        uint currentPhase = (uint)(currentTick & 1);
        uint packedData = (matID & 0x3F) | (7 << 6) | (0 << 10) | (1 << 13) | (currentPhase << 14);

        for (int i = 0; i < count; i++) {
            Vector3Int globalPos = positions[i];
            Vector3Int chunkCoord = new Vector3Int(globalPos.x >> 5, globalPos.y >> 5, globalPos.z >> 5);
            int mapIndex = chunkManager.GetMapIndex(0, chunkCoord);
            if (mapIndex < 0) continue;

            uint ticket = cpuFluidPointers[mapIndex];
            if (ticket == 0xFFFFFFFF) {
                ChunkManager.ChunkHashKey key = new ChunkManager.ChunkHashKey { layer = 0, coord = chunkCoord };
                ticket = AllocateFluidTicket(mapIndex, key);
            }
            if (ticket == 0xFFFFFFFF) continue;

            Vector3Int local = new Vector3Int(globalPos.x & 31, globalPos.y & 31, globalPos.z & 31);
            int flatIdx = local.x + (local.y << 5) + (local.z << 10);
            
            cpuToGpuEditsArray[validEdits * 2] = (ticket << 15) | (uint)flatIdx;
            cpuToGpuEditsArray[validEdits * 2 + 1] = packedData;
            validEdits++;
            
            SetChunkDirty(mapIndex); 
        }

        if (validEdits > 0) {
            cpuToGpuEditsBuffer.SetData(cpuToGpuEditsArray, 0, 0, validEdits * 2);
            fluidCompute.SetInt("_EditCount", validEdits);
            fluidCompute.SetBuffer(kernel_Inject, "_FluidEdits", cpuToGpuEditsBuffer);
            fluidCompute.SetBuffer(kernel_Inject, "_FluidWrite", dynamicFluidBuffer);
            fluidCompute.SetBuffer(kernel_Inject, "_ChunkAwakeFlags", chunkAwakeFlagsBuffer);
            
            // THE FIX: Fire the brush stroke in a single pass. 
            // The Interlocked shader handles the thread management safely.
            fluidCompute.Dispatch(kernel_Inject, Mathf.CeilToInt(validEdits / 64f), 1, 1);
        }
    }

    // --- PHASE 4: VISUAL OPTIC NERVE ---
    public void BindToRaytracer(ComputeShader raytracer, int kernel) {
        if (dynamicFluidBuffer == null || chunkFluidPointersBuffer == null) return;
        raytracer.SetBuffer(kernel, "_DynamicFluids", dynamicFluidBuffer);
        raytracer.SetBuffer(kernel, "_ChunkFluidPointers", chunkFluidPointersBuffer);
    }
}
