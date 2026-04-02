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

    public enum VoxelArchitecture { BrickMap32Bit, DualState1Bit }
    public enum RenderBackend { Raytracer, ComputeRasterizer }

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

    public struct VoxelEdit {
        public int flatIndex;
        public uint material;
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

    private ComputeBuffer deltaMapBuffer;
    private const int MAX_EDITS_PER_DISPATCH = 16384; 
    private Dictionary<Vector3Int, Dictionary<int, uint>> worldDeltaMap = new Dictionary<Vector3Int, Dictionary<int, uint>>();
    
    
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

    private NativeArray<uint> nativeChunkUpload;
    private NativeArray<uint> nativeMaskUpload;
    
    // NEW: CPU Backing Arrays for Burst Jobs
    [HideInInspector] public int dynamicMaxChunks; 
    public NativeArray<uint> cpuDenseChunkPool;
    public NativeArray<uint> cpuMacroMaskPool; 
    public NativeArray<float> cpuChunkHeights; // NEW
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
                            freeDenseIndices.Enqueue(oldCd.densePoolIndex);
                            oldCd.packedState = 0; oldCd.densePoolIndex = 0xFFFFFFFF; 
                            // oldCd.position = new Vector4(-99999f, -99999f, -99999f, 0f); 
                            chunkMapArray[idx] = oldCd;
                        }
                        chunkTargetCoordArray[idx] = coord; 
                        generationQueues[L].Add(new LayerCoord { layer = L, coord = coord });
                    }
                } else {
                    if (chunkTargetCoordArray[idx] == coord) {
                        ChunkData oldCd = chunkMapArray[idx];
                        if (oldCd.densePoolIndex != 0xFFFFFFFF) {
                            freeDenseIndices.Enqueue(oldCd.densePoolIndex);
                            oldCd.packedState = 0; oldCd.densePoolIndex = 0xFFFFFFFF; 
                            // oldCd.position = new Vector4(-99999f, -99999f, -99999f, 0f); 
                            chunkMapArray[idx] = oldCd;
                        }
                        chunkTargetCoordArray[idx] = new Vector3Int(-99999, -99999, -99999);
                    }
                }
            }
        }

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
                UnityEngine.Debug.LogWarning($"✅ WORLD LOADED IN {totalLoadTimer.ElapsedMilliseconds} ms ✅");            
            }
        }
    // --- COMPUTE RASTERIZER DISPATCH (MUST RUN EVERY FRAME!) ---
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
}