using UnityEngine;

public class VoxelBody : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("How many voxels out from the collider's edge should we check?")]
    public int voxelBuffer = 2; 
    
    [HideInInspector] public Vector3Int minGridBound;
    [HideInInspector] public Vector3Int maxGridBound;

    // Track the last footprint
    [HideInInspector] public Vector3Int lastMinBound = new Vector3Int(-999,-999,-999);
    [HideInInspector] public Vector3Int lastMaxBound = new Vector3Int(-999,-999,-999);

    [HideInInspector] public Vector3 lastBuildPosition = new Vector3(-999, -999, -999);
    
    // --> ADDED THIS ONE LINE to fix the compiler error <--
    [HideInInspector] public Vector3Int lastGridPos = new Vector3Int(-999, -999, -999);
    
    private Collider col;

    void Start()
    {
        col = GetComponent<Collider>();
        if (VoxelPhysicsManager.Instance != null)
            VoxelPhysicsManager.Instance.RegisterBody(this);
    }

    // Called by the physics manager every frame to get the exact grid area
    public void UpdateBounds(float voxelScale)
    {
        if (col == null) return;

        // Get the true world-space bounds of the collider (handles offsets perfectly)
        Bounds b = col.bounds;
        
        // Expand the bounds by our voxel buffer
        b.Expand(voxelBuffer * voxelScale * 2f);

        // Convert world bounds to exact voxel grid min/max coordinates
        minGridBound = new Vector3Int(
            Mathf.FloorToInt(b.min.x / voxelScale),
            Mathf.FloorToInt(b.min.y / voxelScale),
            Mathf.FloorToInt(b.min.z / voxelScale)
        );
        
        maxGridBound = new Vector3Int(
            Mathf.CeilToInt(b.max.x / voxelScale),
            Mathf.CeilToInt(b.max.y / voxelScale),
            Mathf.CeilToInt(b.max.z / voxelScale)
        );
    }
}