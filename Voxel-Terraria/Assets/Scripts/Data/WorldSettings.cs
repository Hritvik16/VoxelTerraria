using UnityEngine;

[CreateAssetMenu(
    fileName = "WorldSettings",
    menuName = "VoxelTerraria/World Settings",
    order = 0)]
public class WorldSettings : ScriptableObject
{
    [Header("World Dimensions")]
    public int worldWidth = 1024;
    public int worldDepth = 1024;
    public int worldHeight = 256;

    [Header("Voxel & Chunk Settings")]
    public float voxelSize = 1f;
    public int chunkSize = 16;  // 16×16×16 or 32 depending on design

    [Header("Base Island Shape")]
    public float islandRadius = 350f;
    public float maxBaseHeight = 40f;

    [Header("Mountain Feature")]
    public float mountainRadius = 150f;
    public float mountainHeight = 120f;

    [Header("Lake Feature")]
    public float lakeRadius = 120f;
    public float lakeBottomHeight = -30f;
    public float lakeShoreHeight = 5f;

    [Header("City Plateau Feature")]
    public float cityRadius = 180f;
    public float cityHeight = 30f;

    [Header("Water Settings")]
    public float seaLevel = 0f;

    [Header("Performance")]
    public int maxChunksToRebuildPerFrame = 2;
    public bool useMarchingCubes = true;
    public bool useJobs = true;
    public bool useBurst = true;
}
