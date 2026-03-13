using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(VoxelPhysicsManager))]
public class VoxelPhysicsDebugger : MonoBehaviour
{
    public bool showDebugColliders = true;
    public Color colliderColor = Color.green;

    [Header("GPU Scanner Bounds")]
    public bool showScannerBounds = true;
    public Color scannerBoxColor = Color.magenta;

    private VoxelPhysicsManager physicsManager;

    void OnEnable() { physicsManager = GetComponent<VoxelPhysicsManager>(); }

    void OnDrawGizmos()
    {
        if (physicsManager == null) return;

        // 1. Draw the Colliders the GPU returns
        if (showDebugColliders)
        {
            Gizmos.color = colliderColor;
            for (int i = 0; i < physicsManager.poolIndex; i++)
            {
                GameObject col = physicsManager.colliderPool[i];
                if (col != null && col.activeInHierarchy) Gizmos.DrawWireCube(col.transform.position, col.transform.localScale);
            }
        }

        // 2. Draw the Request Bounds sent to the GPU
        if (showScannerBounds && physicsManager.chunkManager != null)
        {
            Gizmos.color = scannerBoxColor;
            float scale = physicsManager.chunkManager.voxelScale;

            foreach (var body in physicsManager.trackedBodies)
            {
                if (body == null) continue;
                Vector3 min = (Vector3)body.minGridBound * scale;
                Vector3 max = (Vector3)body.maxGridBound * scale;
                
                // Add 1 voxel scale to max to encompass the full volume
                max += Vector3.one * scale; 
                
                Vector3 center = (min + max) / 2f;
                Vector3 size = max - min;
                Gizmos.DrawWireCube(center, size);
            }
        }
    }
}