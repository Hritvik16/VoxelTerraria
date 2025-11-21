using UnityEngine;

[CreateAssetMenu(
    fileName = "WorldSettings",
    menuName = "VoxelTerraria/World Settings",
    order = 0)]
public class WorldSettings : ScriptableObject
{
    [Header("Voxel & Chunk Settings")]
    [Tooltip("Size of one voxel in world units (meters). Smaller = more detail.")]
    public float voxelSize = 0.25f;

    [Tooltip("Number of voxels along one chunk edge (chunk = NxNxN voxels).")]
    public int chunkSize = 32;

    [Header("Base Island Shape")]
    [Tooltip("Radius of the island footprint (meters).")]
    public float islandRadius = 350f;

    [Tooltip("Maximum height of the island dome (meters).")]
    public float maxBaseHeight = 40f;

    [Header("Height Quantization (optional)")]
    [Tooltip("Snap heights to steps (0 = disabled).")]
    public float stepHeight = 0f;

    [Header("Water")]
    [Tooltip("Sea level height in world units.")]
    public float seaLevel = 0f;

    [Header("Debug")]
    [Tooltip("Use jobs when generating voxels.")]
    public bool useJobs = true;

    [Tooltip("Use burst compilation for SDF jobs.")]
    public bool useBurst = true;

    [Tooltip("Use marching cubes mesh (if false, block mesher).")]
    public bool useMarchingCubes = false;
}
