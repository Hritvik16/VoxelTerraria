using UnityEngine;

[CreateAssetMenu(
    fileName = "WorldSettings",
    menuName = "VoxelTerraria/World Settings",
    order = 0)]
public class WorldSettings : ScriptableObject
{
    [Header("Voxel & Chunk Settings")]
    [Tooltip("Size of one voxel in world units (meters). Smaller = more detail.")]
    public float voxelSize = 0.1f;

    [Tooltip("Number of voxels along one chunk edge (chunk = NxNxN voxels).")]
    public int chunkSize = 64;

    [Header("Water")]
    [Tooltip("Sea level height in world units.")]
    public float seaLevel = 0f;

    [Header("Randomization")]
    [Tooltip("If true, features will be randomized on every generation.")]
    public bool randomizeFeatures = false;

    [Tooltip("Global seed for feature generation (used if Randomize is false).")]
    public int globalSeed = 12345;
}
