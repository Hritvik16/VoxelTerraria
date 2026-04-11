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
using UnityEngine.InputSystem; // NEW

public partial class ChunkManager : MonoBehaviour, IVoxelWorld
{

    public enum VoxelArchitecture { BrickMap32Bit, DualState1Bit }
    public enum RenderBackend { Raytracer, ComputeRasterizer }
    
    // --- NEW: THE GLOBAL MEMORY CONSTANTS ---
    public const int TICKET_SIZE = 8192;        // Upgraded from 4096! The max compacted surface materials.
    public const int SHADOW_SIZE = 8192;        // 100% fill rate raw materials (32768 bytes / 4 bytes per uint)
    public const int MAX_SURFACE_VOXELS = 32768; // Upgraded from 16384! Prevents packer overflow.
    public const int VOXEL_VOLUME = 32768;      // 32x32x32 chunk volume

    [Header("Engine Architecture")]
    public VoxelArchitecture currentArchitecture = VoxelArchitecture.DualState1Bit;
    public RenderBackend currentRenderer = RenderBackend.Raytracer;

    [Header("Compute Rasterizer (New Pipeline)")]
    public ComputeShader greedyMesherShader;
    public Material rasterVoxelMaterial;
    public int maxVertexCount = 6000000; // Safe ceiling: ~1 Million Quads
    
    // Modern Unity 6 GPU-native memory buffers
    private GraphicsBuffer vertexBuffer;
    private GraphicsBuffer argsBuffer;

    [Header("Clipmap Architecture")]
    public int chunkSize = 32;
    public float voxelScale = 0.2f;
    [Range(1, 8)] public int clipmapLayers = 3;
    public int renderDistanceXZ = 5; 
    public int renderDistanceY = 5;

    public int maxConcurrentJobs = 12; 
    public float gpuUpdateInterval = 0.05f;

    private bool isWorldLoaded = false;

    [Header("Physics & Entities")]
    public List<Transform> worldLoaders = new List<Transform>();
    public ComputeShader raytracerShader;
    public ComputeShader worldGenShader;
    public ComputeShader worldGenUtilityShader;
    public ComputeShader voxelLightingShader;


    [Header("World Editor State")]
    public bool isEditMode = false;
    public int editMode = 0;
    public int brushSize = 0;
    public int brushShape = 0;

    [Header("Debug & Profiling")]
    public bool enableAntFarmSlice = false;
    public bool enableFeatureDebugView = false; // NEW

    [StructLayout(LayoutKind.Sequential)]
    public struct BiomeAnchor {
        public Vector3 position;
        public float radius;
        public int biomeType;
    }
    
    private ComputeBuffer biomeAnchorBuffer;

    // DATA STRUCTURES
    [StructLayout(LayoutKind.Sequential)]
    public struct ChunkData {
        // public Vector4 position;
        // public uint rootNodeIndex;
        // public int currentLOD;
        public uint packedState;
        public uint densePoolIndex;
    }
    
    [StructLayout(LayoutKind.Sequential)]
    public struct ChunkJobData {
        public Vector4 worldPos;
        public int mapIndex;
        public int lodStep; 
        public float layerScale;
        public uint pad1;
        public int editStartIndex; 
        public int editCount;      
        public int pad2;           
        public int pad3;           
    }
    public struct LayerCoord {
        public int layer;
        public Vector3Int coord;
    }

    private class ChunkPriorityComparer : IComparer<LayerCoord> {
        public Vector3Int centerCoord;
        
        public int Compare(LayerCoord a, LayerCoord b) {
            // Pure integer math. Zero floats, zero square roots, zero allocations.
            int dxA = a.coord.x - centerCoord.x;
            int dyA = a.coord.y - centerCoord.y;
            int dzA = a.coord.z - centerCoord.z;
            int sqrDistA = (dxA * dxA) + (dyA * dyA) + (dzA * dzA);

            int dxB = b.coord.x - centerCoord.x;
            int dyB = b.coord.y - centerCoord.y;
            int dzB = b.coord.z - centerCoord.z;
            int sqrDistB = (dxB * dxB) + (dyB * dyB) + (dzB * dzB);

            return sqrDistA.CompareTo(sqrDistB); 
        }
    }
    private ChunkPriorityComparer chunkSorter = new ChunkPriorityComparer();

    public struct ChunkHashKey : System.IEquatable<ChunkHashKey> {
        public int layer;
        public Vector3Int coord;
        public bool Equals(ChunkHashKey other) => layer == other.layer && coord.Equals(other.coord);
        public override int GetHashCode() => System.HashCode.Combine(layer, coord);
    }

    // --- THE RAM VAULT ---
    public struct CachedChunk {
        public uint[] shape;
        public uint[] mask;
        public uint[] material;
        public uint[] surface; 
        public uint[] prefix; 
    }
    public Dictionary<ChunkHashKey, CachedChunk> modifiedChunks = new Dictionary<ChunkHashKey, CachedChunk>();
    public HashSet<ChunkHashKey> editedChunkCoords = new HashSet<ChunkHashKey>();

    public void SaveToVault(int mapIndex, uint denseIndex, ChunkHashKey key) {
        CachedChunk cache = new CachedChunk { 
            shape = new uint[1024], 
            mask = new uint[19], 
            material = new uint[TICKET_SIZE], 
            surface = new uint[1024],
            prefix = new uint[1024]
        };
        NativeArray<uint>.Copy(cpuDenseChunkPool, (int)denseIndex * 1024, cache.shape, 0, 1024);
        NativeArray<uint>.Copy(cpuMacroMaskPool, (int)denseIndex * 19, cache.mask, 0, 19);
        
        // THE FIX: Use the Sparse Ticket for Materials, not the Dense Index!
        uint ticket = cpuMaterialPointers[mapIndex];
        if (ticket != 0xFFFFFFFF) {
            NativeArray<uint>.Copy(cpuMaterialChunkPool, (int)ticket * TICKET_SIZE, cache.material, 0, TICKET_SIZE);
        } else {
            for(int i = 0; i < TICKET_SIZE; i++) cache.material[i] = 0;
        }

        NativeArray<uint>.Copy(cpuSurfaceMaskPool, (int)denseIndex * 1024, cache.surface, 0, 1024);
        NativeArray<uint>.Copy(cpuSurfacePrefixPool, (int)denseIndex * 1024, cache.prefix, 0, 1024); 
        modifiedChunks[key] = cache;
    }

    public void RestoreFromVault(int mapIndex, uint denseIndex, ChunkHashKey key) {
        CachedChunk cache = modifiedChunks[key];
        NativeArray<uint>.Copy(cache.shape, 0, cpuDenseChunkPool, (int)denseIndex * 1024, 1024);
        NativeArray<uint>.Copy(cache.mask, 0, cpuMacroMaskPool, (int)denseIndex * 19, 19);
        
        // THE FIX: Assign a ticket if restoring a chunk that had materials
        uint ticket = cpuMaterialPointers[mapIndex];
        if (ticket == 0xFFFFFFFF) {
            if (freeMaterialIndices.Count > 0) ticket = freeMaterialIndices.Dequeue();
            else ticket = EvictFurthestMaterialTicket();
            cpuMaterialPointers[mapIndex] = ticket;
            ticketToMapIndex[ticket] = mapIndex;
        }
        NativeArray<uint>.Copy(cache.material, 0, cpuMaterialChunkPool, (int)ticket * TICKET_SIZE, TICKET_SIZE);
        
        NativeArray<uint>.Copy(cache.surface, 0, cpuSurfaceMaskPool, (int)denseIndex * 1024, 1024);
        NativeArray<uint>.Copy(cache.prefix, 0, cpuSurfacePrefixPool, (int)denseIndex * 1024, 1024); 
    }
    
    public ChunkData[] chunkMapArray;
    public Vector3Int[] chunkTargetCoordArray;
    private List<LayerCoord>[] generationQueues = new List<LayerCoord>[8];
    private Vector3Int[] scanOffsets;

    // --- THE LIGHTING ARCHITECTURE (FUTURE PROOFING) ---
    // This illumination buffer is currently UNUSED by the shaders but was setup for future Baked Global Illumination.
    // At high render distances, this buffer exceeds the 4GB Unity ComputeBuffer limit.
    // public NativeArray<uint> cpuIlluminationPool; 
    // private ComputeBuffer illuminationPoolBuffer;
    // private const int UINTS_PER_LIGHT_CHUNK = 16384; 

    // --- THE DIRTY FLAG JANITOR ---
    // private Queue<int> dirtyLightChunks = new Queue<int>();

    // private ComputeBuffer[] chunkMapBuffers = new ComputeBuffer[2]; // FIX: Ring-buffered map!
    // private ComputeBuffer macroGridBuffer;

    // private int kernel_commit;
    
    // // --- THE RING BUFFERS ---
    // private ComputeBuffer[] tempChunkUploadBuffers = new ComputeBuffer[2];
    // private ComputeBuffer[] jobQueueBuffers = new ComputeBuffer[2];
    // private ComputeBuffer[] macroMaskPoolBuffers = new ComputeBuffer[2];
    // private ComputeBuffer[] tempMaskUploadBuffers = new ComputeBuffer[2];

    // --- PERSISTENT MASTER ARRAYS ---
    private ComputeBuffer chunkMapBuffer; 
    private ComputeBuffer macroMaskPoolBuffer; 
    private ComputeBuffer macroGridBuffer;

    private int kernel_commit;
    
    // --- THE UPLOAD COURIERS (These stay ring-buffered!) ---
    private ComputeBuffer[] tempChunkUploadBuffers = new ComputeBuffer[2];
    private ComputeBuffer[] jobQueueBuffers = new ComputeBuffer[2];
    private ComputeBuffer[] tempMaskUploadBuffers = new ComputeBuffer[2];
    
    // private uint[] preallocatedMaskUpload;
    private int ringIndex = 0;
    private int radarIndex = 0;

    // --- NEW: Pre-allocated Managed Arrays (Anti-GC) ---
    // private uint[] preallocatedChunkUpload;
    private ComputeBuffer denseChunkPoolBuffer;
    public ComputeBuffer materialChunkPoolBuffer;
    public ComputeBuffer[] tempMaterialUploadBuffers = new ComputeBuffer[2];
    public NativeArray<uint> nativeMaterialUpload;

    // --- NEW: THE SPARSE MATERIAL CACHE ---
    [Header("Material Cache")]
    public int maxMaterialTickets = 15000; // Halved to exactly offset the 4096 -> 8192 upgrade!
    public Queue<uint> freeMaterialIndices = new Queue<uint>();
    public NativeArray<uint> cpuMaterialPointers; // Maps mapIndex -> Material Ticket
    public ComputeBuffer materialPointersBuffer;  // The GPU Lookup Table
    public int[] ticketToMapIndex; // NEW: Tracks who owns which ticket!

    // --- NEW: TOURNAMENT SELECTION EVICTION ---
    public uint EvictFurthestMaterialTicket() {
        uint bestTicket = 0;
        float maxDistSq = -1f;
        Vector3Int center = primaryAnchorChunks[0];
        
        // Check 32 random tickets. O(1) performance, guarantees we steal from the background!
        for(int i = 0; i < 32; i++) {
            int randomTicket = UnityEngine.Random.Range(0, maxMaterialTickets);
            int mIdx = ticketToMapIndex[randomTicket];
            if (mIdx != -1) {
                Vector3Int coord = chunkTargetCoordArray[mIdx];
                float dx = coord.x - center.x; float dy = coord.y - center.y; float dz = coord.z - center.z;
                float distSq = dx*dx + dy*dy + dz*dz;
                if (distSq > maxDistSq) {
                    maxDistSq = distSq;
                    bestTicket = (uint)randomTicket;
                }
            }
        }
        
        int evictedMapIndex = ticketToMapIndex[bestTicket];
        if (evictedMapIndex != -1) cpuMaterialPointers[evictedMapIndex] = 0xFFFFFFFF;
        ticketToMapIndex[bestTicket] = -1;
        return bestTicket;
    }

    public ComputeBuffer surfaceMaskPoolBuffer; 
    public ComputeBuffer[] tempSurfaceUploadBuffers = new ComputeBuffer[2];
    public NativeArray<uint> nativeSurfaceUpload;

    public ComputeBuffer surfacePrefixPoolBuffer; // NEW: O(1) Math lookup
    public ComputeBuffer[] tempPrefixUploadBuffers = new ComputeBuffer[2];
    public NativeArray<uint> nativePrefixUpload;

    private NativeArray<uint> nativeChunkUpload;
    private NativeArray<uint> nativeMaskUpload;
    
    // NEW: CPU Backing Arrays for Burst Jobs
    [HideInInspector] public int dynamicMaxChunks; 
    public NativeArray<uint> cpuDenseChunkPool;
    public NativeArray<uint> cpuMacroMaskPool; 
    public NativeArray<uint> cpuMaterialChunkPool; 
    public NativeArray<uint> cpuSurfaceMaskPool; 
    public NativeArray<uint> cpuSurfacePrefixPool; 
    
    [Header("Shadow RAM Cache")]
    public int maxShadowTickets = 4000; // ~128MB Strict Cap
    public Queue<uint> freeShadowIndices = new Queue<uint>();
    public Queue<ChunkHashKey> shadowFifoQueue = new Queue<ChunkHashKey>();
    public Dictionary<ChunkHashKey, uint> shadowCoordMap = new Dictionary<ChunkHashKey, uint>();
    public NativeArray<uint> cpuShadowRAMPool; // The Ground Truth
    
    public NativeArray<float> cpuChunkHeights; 
    public NativeArray<BiomeAnchor> cpuBiomes; // NEW: Burst Biome Math
    private ComputeBuffer chunkHeightBuffer;   // NEW

    // NEW: Async Job Tracking
    private Unity.Jobs.JobHandle activeTerrainJobHandle;
    public bool isTerrainJobRunning = false; // THE FIX: Made public so Physics can read it!
    
    // --- TRUE ZERO ALLOCATION PERSISTENT ARRAYS ---
    private NativeArray<ChunkJobData> persistentJobDataArray;
    private NativeArray<VoxelEngine.World.FeatureAnchor> persistentFeatureArray;
    private NativeArray<VoxelEngine.World.CavernNode> persistentCavernArray;
    private NativeArray<VoxelEngine.World.TunnelSpline> persistentTunnelArray; 
    private int activeDispatches = 0;
    
    [HideInInspector] public ComputeBuffer crosshairBuffer;
    [HideInInspector] public int[] crosshairData = new int[8];
    // Dynamically chooses 1024 ints (1-Bit) or 32768 ints (32-Bit)
    public int UINTS_PER_CHUNK => currentArchitecture == VoxelArchitecture.DualState1Bit ? 1024 : 32768;
    private Queue<uint> freeDenseIndices = new Queue<uint>(); 

    // --- ZERO-ALLOCATION CACHES ---
    private System.Action<UnityEngine.Rendering.AsyncGPUReadbackRequest> crosshairCallback;
    private ChunkJobData[] pendingJobsArray;

    private Vector3Int[] primaryAnchorChunks = new Vector3Int[8];
    private Vector4[] shaderCenterChunks = new Vector4[8]; 

    private Stopwatch totalLoadTimer = new Stopwatch();
    private float deltaTime = 0.0f;
    private int chunksPerLayer, totalMapCapacity;

    [Header("Aesthetics")]
    public VoxelMaterialPalette globalPalette;
    private ComputeBuffer materialBuffer;

    private GUIStyle cachedFpsStyle;

    public static ChunkManager Instance;
    public static IVoxelWorld World => Instance; 
    public float VoxelScale => voxelScale;
    public event System.Action<Vector3Int, uint> OnVoxelChanged;
    public event System.Action<Vector3Int, Vector3Int> OnAreaDestroyed;

    private int kernel_generate;
    int kernel_edit;
    int kernel_damage;
    int kernel_clear;

    void Awake() {
        Instance = this;
        kernel_generate = worldGenShader.FindKernel("GenerateChunk");
        kernel_commit = worldGenUtilityShader.FindKernel("CommitUploadBuffers");
        
        if (worldGenUtilityShader != null) {
            kernel_clear = worldGenUtilityShader.FindKernel("ClearJobState");
            kernel_edit = worldGenUtilityShader.FindKernel("EditDenseVoxel");
            kernel_damage = worldGenUtilityShader.FindKernel("DamageDenseVoxel");
        }
    }


    
    // Zero-allocation callback for the crosshair
    void OnCrosshairReadback(UnityEngine.Rendering.AsyncGPUReadbackRequest req) {
        if (req.hasError || !Application.isPlaying) return;
        req.GetData<int>().CopyTo(crosshairData);
    }

    // void CleanupAndQueue(int layer, Vector3Int center) {
    //     float layerScale = voxelScale * (1 << layer);
        
    //     int activeRadXZ = Mathf.Max(2, renderDistanceXZ - layer); 
    //     int activeRadY = renderDistanceY;   
    //     
    //     if (layer == 0) {
    //         activeRadY = Mathf.Min(4, renderDistanceY); 
    //     } 
    //     else if (layer == 1) {
    //         activeRadY = Mathf.Min(6, renderDistanceY);
    //     } 
    //     else if (layer == 2) {
    //         activeRadY = Mathf.Min(8, renderDistanceY);
    //     }
    //
    //     // int activeRadXZSq = activeRadXZ * activeRadXZ;

    //     for (int i = 0; i < scanOffsets.Length; i++) {
    //         Vector3Int coord = center + scanOffsets[i];
            
    //         // FIX: Zero-Allocation Distance Math! 
    //         // Removed Vector2() allocations to prevent hidden CPU stuttering.
    //         // THE FIX: Zero-Allocation Square Culling Math!
    //         int dx = Mathf.Abs(coord.x - center.x);
    //         int dz = Mathf.Abs(coord.z - center.z);
    //         int distXZ = Mathf.Max(dx, dz);
            
    //         bool isInsideActiveRadius = (distXZ <= activeRadXZ) && (Mathf.Abs(coord.y - center.y) <= activeRadY);
            
    //         int idx = GetMapIndex(layer, coord);
            
    //         if (isInsideActiveRadius) {
    //             if (chunkTargetCoordArray[idx] != coord) {
    //                 ChunkData oldCd = chunkMapArray[idx];
    //                 if (oldCd.densePoolIndex != 0xFFFFFFFF) {
    //                     freeDenseIndices.Enqueue(oldCd.densePoolIndex);
    //                     oldCd.packedState = 0; 
    //                     oldCd.densePoolIndex = 0xFFFFFFFF; 
    //                     oldCd.position = new Vector4(-99999f, -99999f, -99999f, 0f); 
    //                     chunkMapArray[idx] = oldCd;
    //                 }
    //                 chunkTargetCoordArray[idx] = coord; 
    //                 generationQueues[layer].Add(new LayerCoord { layer = layer, coord = coord });
    //             }
    //         } else {
    //             if (chunkTargetCoordArray[idx] == coord) {
    //                 ChunkData oldCd = chunkMapArray[idx];
    //                 if (oldCd.densePoolIndex != 0xFFFFFFFF) {
    //                     freeDenseIndices.Enqueue(oldCd.densePoolIndex);
    //                     oldCd.packedState = 0; 
    //                     oldCd.densePoolIndex = 0xFFFFFFFF; 
    //                     oldCd.position = new Vector4(-99999f, -99999f, -99999f, 0f); 
    //                     chunkMapArray[idx] = oldCd;
    //                 }
    //                 chunkTargetCoordArray[idx] = new Vector3Int(-99999, -99999, -99999);
    //             }
    //         }
    //     }
    // }

    
    
    void Update() {
        // --- THE FIX: DYNAMIC BIOME BUFFER CREATION ---
        // Waits patiently until the WorldManager has successfully generated the features
        if (biomeAnchorBuffer == null && VoxelEngine.WorldManager.Instance != null && VoxelEngine.WorldManager.Instance.mapFeatures != null && VoxelEngine.WorldManager.Instance.mapFeatures.Count > 0) {
            List<BiomeAnchor> dynamicBiomes = new List<BiomeAnchor>();
            foreach (var feature in VoxelEngine.WorldManager.Instance.mapFeatures) {
                if (feature.topologyID == 0) { // It's a Biome!
                    dynamicBiomes.Add(new BiomeAnchor { 
                        position = new Vector3(feature.position.x, 0, feature.position.y), 
                        radius = feature.radius, 
                        biomeType = feature.biomeID 
                    });
                }
            }
            if (dynamicBiomes.Count > 0) {
                biomeAnchorBuffer = new ComputeBuffer(dynamicBiomes.Count, 20); 
                biomeAnchorBuffer.SetData(dynamicBiomes.ToArray());
                Shader.SetGlobalBuffer("_BiomeAnchors", biomeAnchorBuffer);
                Shader.SetGlobalInt("_BiomeAnchorCount", dynamicBiomes.Count);
                
                if (cpuBiomes.IsCreated) cpuBiomes.Dispose();
                cpuBiomes = new NativeArray<BiomeAnchor>(dynamicBiomes.ToArray(), Allocator.Persistent);
            }
        }

        // --- NEW: F3 Debug Toggle (Using New Input System) ---
        if (Keyboard.current != null && Keyboard.current.f3Key.wasPressedThisFrame) {
            enableFeatureDebugView = !enableFeatureDebugView;
            Shader.SetGlobalInt("_FeatureDebugView", enableFeatureDebugView ? 1 : 0);
        }

        deltaTime += (Time.unscaledDeltaTime - deltaTime) * 0.1f; 
        if (worldLoaders.Count == 0 || worldLoaders[0] == null) return;

        // bool mapOrMaskChanged = false;

        // 1. FINISH ACTIVE JOBS
        if (isTerrainJobRunning) {
            // if (!activeTerrainJobHandle.IsCompleted) return;
            if (activeTerrainJobHandle.IsCompleted) {
                CompleteActiveJob(); 
            }
            // mapOrMaskChanged = true;
        }

        // // 2. QUEUE NEW CHUNKS
        // bool anyAnchorChanged = false;
        // for (int L = 0; L < clipmapLayers; L++) {
        //     float layerScale = voxelScale * (1 << L);
        //     float worldChunkSize = chunkSize * layerScale;
        //     Vector3Int currentAnchor = GetChunkCoord(worldLoaders[0].position, worldChunkSize);

        //     if (currentAnchor != primaryAnchorChunks[L]) {
        //         primaryAnchorChunks[L] = currentAnchor;
        //         shaderCenterChunks[L] = new Vector4(currentAnchor.x, currentAnchor.y, currentAnchor.z, layerScale); 
        //         CleanupAndQueue(L, currentAnchor);
        //         anyAnchorChanged = true;
        //     }
        // }

        // if (anyAnchorChanged) mapOrMaskChanged = true;

        // if (VoxelPhysicsManager.Instance != null) {
        //     VoxelPhysicsManager.Instance.SyncPhysics();
        // }

        // 2. CONTINUOUS AMORTIZED RADAR (The 50 m/s Fix)
        bool anyAnchorChanged = false;
        for (int L = 0; L < clipmapLayers; L++) {
            float layerScale = voxelScale * (1 << L);
            Vector3Int currentAnchor = GetChunkCoord(worldLoaders[0].position, chunkSize * layerScale);
            if (currentAnchor != primaryAnchorChunks[L]) {
                primaryAnchorChunks[L] = currentAnchor;
                shaderCenterChunks[L] = new Vector4(currentAnchor.x, currentAnchor.y, currentAnchor.z, layerScale); 
                anyAnchorChanged = true;
            }
        }

        // The Radar Dish: Sweeps 4,000 coordinates per frame (~1.5s for a full 100m scan)
        int radarSpeed = 4000;
        bool unloadedChunks = false; // THE FIX: Track if we recycled any memory this frame
        
        // THE FIX: Pause the radar sweep if the background thread is currently locking the master arrays!
        if (!isTerrainJobRunning) {
            for (int i = 0; i < radarSpeed; i++) {
                if (scanOffsets == null || scanOffsets.Length == 0) break;
                
                radarIndex = (radarIndex + 1) % scanOffsets.Length;
                Vector3Int offset = scanOffsets[radarIndex];

                for (int L = 0; L < clipmapLayers; L++) {
                    Vector3Int center = primaryAnchorChunks[L];
                    Vector3Int coord = center + offset;
                    
                    int activeRadXZ = Mathf.Max(2, renderDistanceXZ - L); 
                    int activeRadY = renderDistanceY;   
                    
                    if (L == 0) { activeRadY = Mathf.Min(4, renderDistanceY); } 
                    else if (L == 1) { activeRadY = Mathf.Min(6, renderDistanceY); } 
                    else if (L == 2) { activeRadY = Mathf.Min(8, renderDistanceY); }

                    int dx = Mathf.Abs(coord.x - center.x);
                    int dz = Mathf.Abs(coord.z - center.z);
                    bool isInsideActiveRadius = (Mathf.Max(dx, dz) <= activeRadXZ) && (Mathf.Abs(coord.y - center.y) <= activeRadY);
                    
                    int idx = GetMapIndex(L, coord);
                    
                    if (isInsideActiveRadius) {
                        if (chunkTargetCoordArray[idx] != coord) {
                            ChunkData oldCd = chunkMapArray[idx];
                            if (oldCd.densePoolIndex != 0xFFFFFFFF) {
                                Vector3Int oldCoord = chunkTargetCoordArray[idx];
                                ChunkHashKey oldKey = new ChunkHashKey { layer = L, coord = oldCoord };
                                if (editedChunkCoords.Contains(oldKey)) SaveToVault(idx, oldCd.densePoolIndex, oldKey);
                                
                                freeDenseIndices.Enqueue(oldCd.densePoolIndex);
                                
                                // --- NEW: RETURN THE TICKET ---
                                if (cpuMaterialPointers[idx] != 0xFFFFFFFF) {
                                    uint t = cpuMaterialPointers[idx];
                                    freeMaterialIndices.Enqueue(t);
                                    ticketToMapIndex[t] = -1; // Clear tracking
                                    cpuMaterialPointers[idx] = 0xFFFFFFFF;
                                }
                                
                                oldCd.packedState = 0; oldCd.densePoolIndex = 0xFFFFFFFF; 
                                // oldCd.position = new Vector4(-99999f, -99999f, -99999f, 0f); 
                                chunkMapArray[idx] = oldCd;
                                unloadedChunks = true; // THE FIX: Flag the GPU for an update!
                            }
                            chunkTargetCoordArray[idx] = coord; 
                            generationQueues[L].Add(new LayerCoord { layer = L, coord = coord });
                        }
                    } else {
                        if (chunkTargetCoordArray[idx] == coord) {
                            ChunkData oldCd = chunkMapArray[idx];
                            if (oldCd.densePoolIndex != 0xFFFFFFFF) {
                                Vector3Int oldCoord = chunkTargetCoordArray[idx];
                                ChunkHashKey oldKey = new ChunkHashKey { layer = L, coord = oldCoord };
                                if (editedChunkCoords.Contains(oldKey)) SaveToVault(idx, oldCd.densePoolIndex, oldKey);
                                
                                freeDenseIndices.Enqueue(oldCd.densePoolIndex);
                                
                                // --- NEW: RETURN THE TICKET ---
                                if (cpuMaterialPointers[idx] != 0xFFFFFFFF) {
                                    uint t = cpuMaterialPointers[idx];
                                    freeMaterialIndices.Enqueue(t);
                                    ticketToMapIndex[t] = -1; // Clear tracking
                                    cpuMaterialPointers[idx] = 0xFFFFFFFF;
                                }
                                
                                oldCd.packedState = 0; oldCd.densePoolIndex = 0xFFFFFFFF; 
                                // oldCd.position = new Vector4(-99999f, -99999f, -99999f, 0f); 
                                chunkMapArray[idx] = oldCd;
                                unloadedChunks = true; // THE FIX: Flag the GPU for an update!
                            }
                            chunkTargetCoordArray[idx] = new Vector3Int(-99999, -99999, -99999);
                        }
                    }
                }
            }
            
            // THE FIX: If we recycled ANY memory, blast the updated map to the GPU instantly!
            // This physically deletes the dangling pointers, curing the memory corruption permanently.
            if (unloadedChunks) chunkMapBuffer.SetData(chunkMapArray);
        } // <-- THE FIX: Closes the radar loop

        // 3. UPDATE SHADER BINDINGS
        if (anyAnchorChanged) {
            UpdateGPUBuffers(); 
            totalLoadTimer.Restart();
            isWorldLoaded = false; 
        }

        // --- THE CHAIN-JOB LOCKOUT FIX ---
        // We are threading the needle! This runs in the exact split-second window 
        // after the previous job completed, but BEFORE the next job locks the arrays!
        if (!isTerrainJobRunning && VoxelPhysicsManager.Instance != null) {
            VoxelPhysicsManager.Instance.SyncPhysics();
        }

        // 4. DISPATCH NEW JOBS
        DispatchNewJobs(); 
        // ProcessDirtyLightChunks();

        if (crosshairBuffer != null && Time.frameCount % 4 == 0) {
            UnityEngine.Rendering.AsyncGPUReadback.Request(crosshairBuffer, crosshairCallback);
        }

        // bool allQueuesEmpty = true;
        // for (int i = 0; i < clipmapLayers; i++) if (generationQueues[i].Count > 0) allQueuesEmpty = false;

        // if (allQueuesEmpty && totalLoadTimer.IsRunning) {
        //     totalLoadTimer.Stop();
        //     UnityEngine.Debug.LogWarning($"✅ CLIPMAPS LOADED IN {totalLoadTimer.ElapsedMilliseconds} ms ✅");
        // }
        // --- ADD THIS AT THE END OF UPDATE ---
        if (!isWorldLoaded) {
            bool queuesEmpty = true;
            for (int i = 0; i < clipmapLayers; i++) {
                if (generationQueues[i].Count > 0) queuesEmpty = false;
            }
            
            if (queuesEmpty && activeDispatches == 0) {
                totalLoadTimer.Stop();
                isWorldLoaded = true;
                UnityEngine.Debug.LogWarning($"✅ CLIPMAPS LOADED IN {totalLoadTimer.Elapsed.TotalSeconds:F2} seconds ✅");            
            }
        }
        if (currentRenderer == RenderBackend.ComputeRasterizer && rasterVoxelMaterial != null) {
            rasterVoxelMaterial.SetBuffer("_VertexBuffer", vertexBuffer);
            Bounds renderBounds = new Bounds(worldLoaders[0].position, new Vector3(4000, 4000, 4000));
            Graphics.DrawProceduralIndirect(rasterVoxelMaterial, renderBounds, MeshTopology.Triangles, argsBuffer, 0, null, null, ShadowCastingMode.On, true, gameObject.layer);
        }
    } 



    void OnGUI() {
        if (cachedFpsStyle == null) {
            cachedFpsStyle = new GUIStyle();
            cachedFpsStyle.fontSize = 30;
            cachedFpsStyle.normal.textColor = Color.yellow;
        }
        // Use standard ToString() to prevent string interpolation memory leaks
        GUI.Label(new Rect(20, 20, 200, 40), "FPS: " + Mathf.RoundToInt(1.0f / deltaTime).ToString(), cachedFpsStyle);
    }

    // --- THE FIX: DUAL-STATE C# PREDICTION MATH ---
    // Helper Hash function (Translating your HLSL hash to C#)
    private float Hash(Unity.Mathematics.float3 p) {
        p = Unity.Mathematics.math.frac(p * 0.3183099f + 0.1f);
        p *= 17.0f;
        return Unity.Mathematics.math.frac(p.x * p.y * p.z * (p.x + p.y + p.z));
    }

    public bool IsSolid(Vector3Int globalPos) {
        Vector3Int chunkCoord = new Vector3Int(
            Mathf.FloorToInt(globalPos.x / 32f),
            Mathf.FloorToInt(globalPos.y / 32f),
            Mathf.FloorToInt(globalPos.z / 32f)
        );
        int idx = GetMapIndex(0, chunkCoord);
        if (idx < 0 || idx >= totalMapCapacity || chunkTargetCoordArray[idx] != chunkCoord) return false;

        ChunkData cd = chunkMapArray[idx];
        if ((cd.packedState & 1) == 0 || cd.densePoolIndex == 0xFFFFFFFF) return false;

        int localX = globalPos.x - (chunkCoord.x * 32);
        int localY = globalPos.y - (chunkCoord.y * 32);
        int localZ = globalPos.z - (chunkCoord.z * 32);

        if (localX < 0 || localX >= 32 || localY < 0 || localY >= 32 || localZ < 0 || localZ >= 32) return false;

        int flatIdx = localX + (localY << 5) + (localZ << 10);
        int uintIdx = flatIdx >> 5;
        int bitIdx = flatIdx & 31;
        int poolIndex = (int)(cd.densePoolIndex * 1024u) + uintIdx;

        return (cpuDenseChunkPool[poolIndex] & (1u << bitIdx)) != 0;
    }

    private uint PredictProceduralMaterial(Vector3Int globalPos) {
        // 1. IS IT THE SURFACE?
        bool isSurface = !IsSolid(globalPos + Vector3Int.up);
        
        // 2. IS IT A STEEP CLIFF? 
        float nx = (IsSolid(globalPos + Vector3Int.left) ? 1f : 0f) - (IsSolid(globalPos + Vector3Int.right) ? 1f : 0f);
        float ny = (IsSolid(globalPos + Vector3Int.down) ? 1f : 0f) - (IsSolid(globalPos + Vector3Int.up) ? 1f : 0f);
        float nz = (IsSolid(globalPos + Vector3Int.back) ? 1f : 0f) - (IsSolid(globalPos + Vector3Int.forward) ? 1f : 0f);
        
        Unity.Mathematics.float3 normal = Unity.Mathematics.math.normalize(new Unity.Mathematics.float3(nx, ny, nz));
        bool isMassiveCliff = normal.y < 0.3f;

        // 3. WHICH BIOME IS IT IN?
        int currentBiome = 0;
        float closestDist = float.MaxValue;
        // Assuming voxelScale is accessible here (usually 0.1f)
        // float voxelScale = 0.1f; 
        Unity.Mathematics.float3 worldPos = new Unity.Mathematics.float3(globalPos.x, globalPos.y, globalPos.z) * voxelScale;
        
        float boundaryWarp = (Hash(worldPos * 0.005f) - 0.5f) * 150.0f;

        // Access your Biome Anchors (Update reference if your singleton is different)
        var anchors = VoxelEngine.WorldManager.Instance.mapFeatures;
        foreach (var anchor in anchors) {
            if (anchor.topologyID == 0) { 
                float dist = Unity.Mathematics.math.distance(worldPos, new Unity.Mathematics.float3(anchor.position.x, 0, anchor.position.y)) + boundaryWarp;
                if (dist < closestDist) {
                    closestDist = dist;
                    currentBiome = anchor.biomeID;
                }
            }
        }

        // 4. RETURN PALETTE ID
        switch (currentBiome) {
            case 0: return isSurface ? 1u : (isMassiveCliff ? 3u : 2u);   // FOREST (Grass, Stone, Dirt)
            case 1: return isSurface ? 4u : (isMassiveCliff ? 3u : 13u);  // DESERT (Sand, Stone, Sandstone)
            case 2: return isSurface ? 7u : (isMassiveCliff ? 3u : 8u);   // SNOW (Snow, Stone, Ice)
            case 3: return isSurface ? 10u : (isMassiveCliff ? 3u : 13u); // JUNGLE (Grass, Stone, Mud)
            case 4: return isSurface ? 12u : (isMassiveCliff ? 14u : 11u);// VOLCANIC (Lava, Volcanic Rock, Obsidian)
        }
        return 3u; 
    }
}