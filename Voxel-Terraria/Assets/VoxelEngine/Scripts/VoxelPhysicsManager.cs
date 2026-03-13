using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Rendering;

public class VoxelPhysicsManager : MonoBehaviour
{
    public static VoxelPhysicsManager Instance;
    public ChunkManager chunkManager;
    public GameObject colliderPrefab;
    public ComputeShader physicsScanner; 

    [HideInInspector] public List<GameObject> colliderPool = new List<GameObject>();
    [HideInInspector] public int poolIndex = 0;

    public List<VoxelBody> trackedBodies = new List<VoxelBody>(); // Changed from private to public
    private bool forceRebuild = true; 

    private ComputeBuffer solidVoxelsBuffer;
    private bool isScanning = false;

    private struct Int4 { public int x, y, z, w; }
    private Int4[] resetData = new Int4[1]; // A pre-allocated 16-byte chunk of zeroes

    void Awake() { Instance = this; }

    void Start()
    {
        for (int i = 0; i < 1500; i++)
        {
            GameObject go = Instantiate(colliderPrefab, transform);
            go.SetActive(false);
            colliderPool.Add(go);
        }
        // Allocating a tiny 32KB buffer instead of 500MB
        solidVoxelsBuffer = new ComputeBuffer(2000, sizeof(int) * 4);
    }

    void OnDisable()
    {
        solidVoxelsBuffer?.Release();
    }

    public void RegisterBody(VoxelBody body) => trackedBodies.Add(body);

    void Update()
    {
        if (chunkManager == null || physicsScanner == null || isScanning) return;
        float scale = chunkManager.voxelScale;

        bool needsUpdate = forceRebuild;

        foreach (var body in trackedBodies)
        {
            // CRITICAL FIX 1: Increase scan frequency to 0.2 meters
            // This stops the player from falling past the bounds before the GPU returns the data
            if (Vector3.Distance(body.transform.position, body.lastBuildPosition) > 0.2f || forceRebuild)
            {
                needsUpdate = true;
                body.lastBuildPosition = body.transform.position;
                body.UpdateBounds(scale);
            }
        }

        if (needsUpdate)
        {
            DispatchPhysicsScanner();
            forceRebuild = false;
        }
    }

    void DispatchPhysicsScanner()
    {
        isScanning = true;
        
        solidVoxelsBuffer.SetData(resetData, 0, 0, 1);

        int kernel = physicsScanner.FindKernel("ScanPhysics");
        chunkManager.BindPhysicsData(physicsScanner, kernel);
        physicsScanner.SetBuffer(kernel, "_SolidVoxels", solidVoxelsBuffer);

        foreach (var body in trackedBodies)
        {
            // CRITICAL FIX 2: Use SetVector for absolute memory safety
            physicsScanner.SetVector("_MinBounds", new Vector4(body.minGridBound.x, body.minGridBound.y, body.minGridBound.z, 0));
            physicsScanner.SetVector("_MaxBounds", new Vector4(body.maxGridBound.x, body.maxGridBound.y, body.maxGridBound.z, 0));

            int dx = (body.maxGridBound.x - body.minGridBound.x) + 1;
            int dy = (body.maxGridBound.y - body.minGridBound.y) + 1;
            int dz = (body.maxGridBound.z - body.minGridBound.z) + 1;

            physicsScanner.Dispatch(kernel, Mathf.CeilToInt(dx / 8f), Mathf.CeilToInt(dy / 8f), Mathf.CeilToInt(dz / 8f));
        }

        // Non-blocking tiny readback
        AsyncGPUReadback.Request(solidVoxelsBuffer, (request) => {
            // ... (The rest of your readback remains perfectly the same) ...
            if (request.hasError || !Application.isPlaying) { isScanning = false; return; }

            // Read the int4 data as a flat integer array
            var rawData = request.GetData<int>();
            int count = rawData[0]; 
            int limit = Mathf.Min(count, 1999);

            for (int i = 0; i < poolIndex; i++) colliderPool[i].SetActive(false);
            poolIndex = 0;

            float scale = chunkManager.voxelScale;

            // Spawn the simple colliders directly from the GPU coordinates
            for (int i = 0; i < limit; i++)
            {
                if (poolIndex >= colliderPool.Count) break;

                int dataIndex = (i + 1) * 4;
                int x = rawData[dataIndex];
                int y = rawData[dataIndex + 1];
                int z = rawData[dataIndex + 2];

                Vector3 spawnPos = new Vector3(x, y, z) * scale + (Vector3.one * scale * 0.5f);

                GameObject col = colliderPool[poolIndex++];
                col.transform.position = spawnPos;
                col.transform.localScale = Vector3.one * scale;
                col.SetActive(true);
            }

            isScanning = false;
        });
    }
}