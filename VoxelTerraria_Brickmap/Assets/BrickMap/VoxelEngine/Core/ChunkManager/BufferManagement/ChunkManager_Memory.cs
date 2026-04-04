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
    void OnEnable() {
        Instance = this;
        // Toggle the Shader Architecture!
        if (currentArchitecture == VoxelArchitecture.DualState1Bit) {
            Shader.EnableKeyword("DUAL_STATE_1BIT");
        } else {
            Shader.DisableKeyword("DUAL_STATE_1BIT");
        }
        if (crosshairData == null || crosshairData.Length != 8) crosshairData = new int[8];
        
        // Initialize the zero-allocation caches
        crosshairCallback = OnCrosshairReadback;
        if (pendingJobsArray == null || pendingJobsArray.Length != maxConcurrentJobs) pendingJobsArray = new ChunkJobData[maxConcurrentJobs];
        
        int sideXZ = 2 * renderDistanceXZ + 1;
        int sideY = 2 * renderDistanceY + 1;
        chunksPerLayer = sideXZ * sideXZ * sideY;
        totalMapCapacity = chunksPerLayer * clipmapLayers; 
        
        // NEW: Dynamic Memory Ceiling (Total active chunks + 20% buffer)
        dynamicMaxChunks = Mathf.CeilToInt(totalMapCapacity * 1.2f);
        
        // Stratified Static Pool: Dynamically scaled for infinite clipmap layers
        int POOL_SIZE = 0;
        for (int L = 0; L < clipmapLayers; L++) {
            int cap = (L == 0) ? 38000 : ((L == 1) ? 8192 : 256);
            POOL_SIZE += chunksPerLayer * cap;
        }

        // THE FIX: Delete the `if (null)` checks. Force Unity to build fresh, correctly sized arrays!
        chunkMapArray = new ChunkData[totalMapCapacity];
        for (int i = 0; i < totalMapCapacity; i++) {
            chunkMapArray[i] = new ChunkData {
                // position = new Vector4(-99999f, -99999f, -99999f, 0f),
                // rootNodeIndex = 0xFFFFFFFF,
                densePoolIndex = 0xFFFFFFFF 
            };
        }
        
        chunkTargetCoordArray = new Vector3Int[totalMapCapacity];
        for(int i = 0; i < totalMapCapacity; i++) chunkTargetCoordArray[i] = new Vector3Int(-99999, -99999, -99999);

        List<Vector3Int> offsets = new List<Vector3Int>();
        for (int y = -renderDistanceY; y <= renderDistanceY; y++) {
            for (int x = -renderDistanceXZ; x <= renderDistanceXZ; x++) {
                for (int z = -renderDistanceXZ; z <= renderDistanceXZ; z++) {
                    Vector3Int off = new Vector3Int(x, y, z);
                    
                    // THE FIX: Chebyshev (Square) Distance instead of Euclidean (Circle)
                    int dXZ = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z));
                    
                    if (dXZ <= renderDistanceXZ) offsets.Add(off);
                }
            }
        }
        offsets.Sort((a, b) => a.sqrMagnitude.CompareTo(b.sqrMagnitude));
        scanOffsets = offsets.ToArray();

        for(int i = 0; i < 8; i++) {
            generationQueues[i] = new List<LayerCoord>();
            primaryAnchorChunks[i] = new Vector3Int(-9999, -9999, -9999);
        }

        // int totalVoxelCapacity = maxConcurrentJobs * VOXELS_PER_CHUNK;
        int totalVoxelCapacity = maxConcurrentJobs * UINTS_PER_CHUNK;
        if (chunkMapBuffer == null) {
            // 1. Build the Singular Master Arrays
            chunkMapBuffer = new ComputeBuffer(totalMapCapacity, 8);
            chunkMapBuffer.SetData(chunkMapArray);
            macroMaskPoolBuffer = new ComputeBuffer(dynamicMaxChunks * 19, sizeof(uint)); // CHANGED to 19
            
            for (int i = 0; i < 2; i++) {
                tempChunkUploadBuffers[i] = new ComputeBuffer(totalVoxelCapacity, sizeof(uint));
                jobQueueBuffers[i] = new ComputeBuffer(maxConcurrentJobs, 48);
                tempMaskUploadBuffers[i] = new ComputeBuffer(maxConcurrentJobs * 19, sizeof(uint)); // CHANGED to 19
            }
            
            // NEW: Native Unmanaged Pointers
            nativeChunkUpload = new NativeArray<uint>(totalVoxelCapacity, Allocator.Persistent);
            nativeMaskUpload = new NativeArray<uint>(maxConcurrentJobs * 19, Allocator.Persistent);
        }
        
        if (deltaMapBuffer == null) {
            deltaMapBuffer = new ComputeBuffer(MAX_EDITS_PER_DISPATCH, 8);
            deltaMapBuffer.SetData(new VoxelEdit[MAX_EDITS_PER_DISPATCH]); 
        }

        if (crosshairBuffer == null) crosshairBuffer = new ComputeBuffer(2, 16);

        if (denseChunkPoolBuffer == null) {
            denseChunkPoolBuffer = new ComputeBuffer(dynamicMaxChunks * UINTS_PER_CHUNK, sizeof(uint));
            cpuDenseChunkPool = new NativeArray<uint>(dynamicMaxChunks * UINTS_PER_CHUNK, Allocator.Persistent);
            // Allocate the 16-Bit RGB Light Array
            // illuminationPoolBuffer = new ComputeBuffer(dynamicMaxChunks * UINTS_PER_LIGHT_CHUNK, sizeof(uint));
            // cpuIlluminationPool = new NativeArray<uint>(dynamicMaxChunks * UINTS_PER_LIGHT_CHUNK, Allocator.Persistent);
            
            // Shader.SetGlobalBuffer("_IlluminationPool", illuminationPoolBuffer);
            cpuMacroMaskPool = new NativeArray<uint>(dynamicMaxChunks * 19, Allocator.Persistent); 

            chunkHeightBuffer = new ComputeBuffer(totalMapCapacity, sizeof(float));
            cpuChunkHeights = new NativeArray<float>(totalMapCapacity, Allocator.Persistent);
            Shader.SetGlobalBuffer("_ChunkHeightMap", chunkHeightBuffer);

            // --- INITIALIZE PERSISTENCE BUFFERS ON STARTUP ---
            // This ensures the Raytracer always has valid buffer references even if no edits exist yet!
            if (deltaMapUploadBuffer == null) deltaMapUploadBuffer = new ComputeBuffer(500000, 8); 
            if (chunkPointersBuffer == null) {
                chunkPointersBuffer = new ComputeBuffer(totalMapCapacity, 8);
                // Clear the pointers so they don't contain garbage VRAM data
                Vector2Int[] zeros = new Vector2Int[totalMapCapacity];
                chunkPointersBuffer.SetData(zeros);
            }
            
            if (!persistentJobDataArray.IsCreated) persistentJobDataArray = new NativeArray<ChunkJobData>(maxConcurrentJobs, Allocator.Persistent);
            if (!persistentFeatureArray.IsCreated) persistentFeatureArray = new NativeArray<VoxelEngine.World.FeatureAnchor>(5000, Allocator.Persistent);
            if (!persistentCavernArray.IsCreated) persistentCavernArray = new NativeArray<VoxelEngine.World.CavernNode>(5000, Allocator.Persistent);
            if (!persistentTunnelArray.IsCreated) persistentTunnelArray = new NativeArray<VoxelEngine.World.TunnelSpline>(5000, Allocator.Persistent); 

            denseChunkPoolBuffer.SetData(cpuDenseChunkPool);


            if (argsBuffer == null) {
                // The "Args" buffer tells the GPU how many triangles to draw.
                // Layout: [VertexCountPerInstance, InstanceCount, StartVertex, StartInstance]
                argsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Structured, 4, sizeof(uint));
                argsBuffer.SetData(new uint[] { 0, 1, 0, 0 });            
            }
            if (vertexBuffer == null) {
                // 16 bytes per vertex: float3 position (12 bytes) + uint packedData (4 bytes)
                vertexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, maxVertexCount, 16);
            }

            // Shader.SetGlobalBuffer("_DenseChunkPool", denseChunkPoolBuffer);
            // Shader.SetGlobalBuffer("_MacroMaskPool", macroMaskPoolBuffers[0]); 
            Shader.SetGlobalBuffer("_DenseChunkPool", denseChunkPoolBuffer);
            Shader.SetGlobalBuffer("_MacroMaskPool", macroMaskPoolBuffer); 

            if (macroGridBuffer == null) {
                macroGridBuffer = new ComputeBuffer(totalMapCapacity, 8);
                Shader.SetGlobalBuffer("_MacroGrid", macroGridBuffer);
            }
            Shader.SetGlobalBuffer("_ChunkMap", chunkMapBuffer); // <--- Removed [0]
            
            freeDenseIndices.Clear();
            for (uint i = 0; i < dynamicMaxChunks; i++) freeDenseIndices.Enqueue(i);
            
            float vramMB = (dynamicMaxChunks * UINTS_PER_CHUNK * 4.0f) / (1024f * 1024f);
            UnityEngine.Debug.Log($"[ChunkManager] Allocated {dynamicMaxChunks} chunks dynamically. Est. VRAM: {vramMB:F2} MB");
        }

        if (macroGridBuffer == null) {
            macroGridBuffer = new ComputeBuffer(totalMapCapacity, 8);
            Shader.SetGlobalBuffer("_MacroGrid", macroGridBuffer);
        }
        Shader.SetGlobalBuffer("_ChunkMap", chunkMapBuffer);

        if (globalPalette != null && materialBuffer == null) {
            materialBuffer = new ComputeBuffer(256, 32);
            materialBuffer.SetData(globalPalette.materials);
            Shader.SetGlobalBuffer("_MaterialPalette", materialBuffer);
        }
        Shader.SetGlobalInt("_PoolSize", (int)POOL_SIZE);
        Shader.SetGlobalInt("_ChunkCount", chunksPerLayer);
        Shader.SetGlobalInt("_ClipmapLayers", clipmapLayers);
        Shader.SetGlobalVector("_RenderBounds", new Vector4(renderDistanceXZ, renderDistanceY, 0, 0));
        Shader.SetGlobalFloat("_VoxelScale", voxelScale);
        totalLoadTimer.Start();
    }

    void OnDisable() {
        if (isTerrainJobRunning) {
            activeTerrainJobHandle.Complete(); 
        }
        if (cpuDenseChunkPool.IsCreated) cpuDenseChunkPool.Dispose();
        if (cpuMacroMaskPool.IsCreated) cpuMacroMaskPool.Dispose(); 
        if (cpuChunkHeights.IsCreated) cpuChunkHeights.Dispose(); 
        chunkHeightBuffer?.Release();

        if (persistentJobDataArray.IsCreated) persistentJobDataArray.Dispose();
        if (persistentFeatureArray.IsCreated) persistentFeatureArray.Dispose();
        if (persistentCavernArray.IsCreated) persistentCavernArray.Dispose();
        if (persistentTunnelArray.IsCreated) persistentTunnelArray.Dispose();
        if (nativeChunkUpload.IsCreated) nativeChunkUpload.Dispose();
        if (nativeMaskUpload.IsCreated) nativeMaskUpload.Dispose();
        // if (cpuIlluminationPool.IsCreated) cpuIlluminationPool.Dispose();

        // illuminationPoolBuffer?.Release();
        
        macroGridBuffer?.Release();
        deltaMapBuffer?.Release();
        materialBuffer?.Release();
        denseChunkPoolBuffer?.Release(); 
        crosshairBuffer?.Release();
        
        for (int i = 0; i < 2; i++) {
            tempChunkUploadBuffers[i]?.Release();
            jobQueueBuffers[i]?.Release();
            tempMaskUploadBuffers[i]?.Release();
        }
        biomeAnchorBuffer?.Release();
        biomeAnchorBuffer = null;

        vertexBuffer?.Release();
        argsBuffer?.Release();
        
        vertexBuffer = null;
        argsBuffer = null;

        macroGridBuffer = null;
        materialBuffer = null;
        denseChunkPoolBuffer = null;
        deltaMapBuffer = null;
        crosshairBuffer = null;
    }

    public void BindPhysicsData(ComputeShader cs, int kernel) {
        if (chunkMapBuffer == null) return;
        if (cs.name == "RayTracer" && crosshairBuffer != null) {
            cs.SetBuffer(kernel, "_CrosshairTarget", crosshairBuffer);
            cs.SetVector("_TargetBlock", new Vector4(crosshairData[0], crosshairData[1], crosshairData[2], crosshairData[3]));
            cs.SetVector("_TargetNormal", new Vector4(crosshairData[4], crosshairData[5], crosshairData[6], 0));
            cs.SetInt("_IsEditMode", isEditMode ? 1 : 0);
            cs.SetInt("_EditMode", editMode);
            cs.SetInt("_BrushSize", brushSize);
            cs.SetInt("_BrushShape", brushShape);
            cs.SetFloat("_VoxelScale", voxelScale);
            
            // THE FIX: Only bind the buffer if the world has actually finished generating!
            if (biomeAnchorBuffer != null) {
                cs.SetBuffer(kernel, "_BiomeAnchors", biomeAnchorBuffer);
            }

            // --- NEW: Bind the Blueprint Features to the Raytracer ---
            if (WorldManager.Instance != null && WorldManager.Instance.featureBuffer != null) {
                cs.SetBuffer(kernel, "_FeatureAnchorBuffer", WorldManager.Instance.featureBuffer);
                cs.SetInt("_FeatureCount", WorldManager.Instance.mapFeatures.Count);
            }

            // --- NEW: Bind the Delta Map Persistence Buffers ---
            if (deltaMapUploadBuffer != null && chunkPointersBuffer != null) {
                cs.SetBuffer(kernel, "_DeltaMapBuffer", deltaMapUploadBuffer);
                cs.SetBuffer(kernel, "_ChunkEditPointers", chunkPointersBuffer);
            }
        }
        if (macroGridBuffer != null) cs.SetBuffer(kernel, "_MacroGrid", macroGridBuffer);
        if (denseChunkPoolBuffer != null) cs.SetBuffer(kernel, "_DenseChunkPool", denseChunkPoolBuffer);
        
        // FIX: Reverted [0] back to [ringIndex] so it reads the Grand Swap!
        if (macroMaskPoolBuffer != null) cs.SetBuffer(kernel, "_MacroMaskPool", macroMaskPoolBuffer);
        cs.SetBuffer(kernel, "_ChunkMap", chunkMapBuffer);
        
        cs.SetVectorArray("_ClipmapCenters", shaderCenterChunks);
        cs.SetInt("_ClipmapLayers", clipmapLayers);
        cs.SetFloat("_VoxelScale", voxelScale);
        cs.SetVector("_RenderBounds", new Vector4(renderDistanceXZ, renderDistanceY, 0, 0));
        cs.SetInt("_ChunkCount", chunksPerLayer);
    }

    public void BindMathData(ComputeShader cs, int kernel) {
        if (deltaMapBuffer != null) cs.SetBuffer(kernel, "_DeltaMapBuffer", deltaMapBuffer);

        if (WorldManager.Instance != null) {
            if (WorldManager.Instance.featureBuffer != null) cs.SetBuffer(kernel, "_FeatureAnchorBuffer", WorldManager.Instance.featureBuffer);
            if (WorldManager.Instance.cavernBuffer != null) cs.SetBuffer(kernel, "_CavernNodeBuffer", WorldManager.Instance.cavernBuffer);
            if (WorldManager.Instance.tunnelBuffer != null) cs.SetBuffer(kernel, "_TunnelSplineBuffer", WorldManager.Instance.tunnelBuffer);
            cs.SetInt("_FeatureCount", WorldManager.Instance.mapFeatures.Count);
            cs.SetInt("_CavernCount", WorldManager.Instance.cavernNodes.Count);
            cs.SetInt("_TunnelCount", WorldManager.Instance.tunnelSplines.Count);
        }
        
        cs.SetInt("_ChunkCount", chunksPerLayer);
        cs.SetInt("_ClipmapLayers", clipmapLayers);
        cs.SetVectorArray("_ClipmapCenters", shaderCenterChunks);
        cs.SetVector("_RenderBounds", new Vector4(renderDistanceXZ, renderDistanceY, 0, 0));
        cs.SetFloat("_VoxelScale", voxelScale);
    }

    void UpdateGPUBuffers() {
        Shader.SetGlobalVectorArray("_ClipmapCenters", shaderCenterChunks);
        Shader.SetGlobalInt("_ClipmapLayers", clipmapLayers);
        Shader.SetGlobalVector("_RenderBounds", new Vector4(renderDistanceXZ, renderDistanceY, 0, 0));
        Shader.SetGlobalInt("_ChunkCount", chunksPerLayer);
    }

    public bool IsReady() {
        // THE FIX: Do not allow the camera to render until Metal is guaranteed to have all its buffers!
        return worldGenShader != null && worldGenUtilityShader != null && 
               chunkMapBuffer != null && denseChunkPoolBuffer != null && 
               biomeAnchorBuffer != null && 
               (VoxelEngine.WorldManager.Instance != null && VoxelEngine.WorldManager.Instance.featureBuffer != null);
    }
}
