using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace VoxelTerraria.World
{
    public class VoxelWorld : MonoBehaviour
    {
        [Header("Debug")]
        public bool debugSdf = false;
        public int debugChunkX = 0;
        public int debugChunkZ = 0;

        [Header("References")]
        public WorldSettings worldSettings;
        public VoxelChunkView chunkPrefab;

        [Header("Generation")]
        public bool generateOnStart = true;

        // For now, don't spawn full 320x320 chunks while testing
        public bool limitChunksForDebug = true;
        [Min(1)] public int debugChunksX = 4;
        [Min(1)] public int debugChunksZ = 4;

        [Header("Biome → Material Mapping")]
        [Tooltip("Map biome IDs to material IDs used in Voxel.density/materialId.")]
        public BiomeMaterialMapping[] biomeMaterialMappings;

        // Internal storage of chunk data
        private Dictionary<(int x, int z), ChunkData> chunkMap;

        private int chunksX;
        private int chunksZ;

        [System.Serializable]
        public struct BiomeMaterialMapping
        {
            public BiomeDefinition.BiomeID biome;
            public ushort materialId;
        }

        private void Awake()
        {
            if (generateOnStart)
            {
                InitializeWorld();
            }
        }

        // ------------------------------------------------------------------
        // Initialize world: compute chunk grid, allocate ChunkData, generate voxels,
        // and instantiate VoxelChunkView gameobjects.
        // ------------------------------------------------------------------
        public void InitializeWorld()
        {
            if (worldSettings == null)
            {
                Debug.LogError("VoxelWorld: Missing WorldSettings.");
                return;
            }

            if (chunkPrefab == null)
            {
                Debug.LogError("VoxelWorld: Missing VoxelChunkView prefab.");
                return;
            }

            // Make sure SdfBootstrap has run and built the context
            SdfContext ctx = SdfRuntime.Context; // SdfBootstrap.OnEnable must have set this

            // Clean up previous world if re-initializing
            if (chunkMap != null)
            {
                foreach (var kv in chunkMap)
                {
                    var data = kv.Value;
                    data.Dispose();
                }
            }

            chunkMap = new Dictionary<(int, int), ChunkData>();

            int chunkSize = worldSettings.chunkSize;
            float voxelSize = worldSettings.voxelSize;

            // worldWidth/worldDepth are in world units
            float worldWidthWorldUnits = worldSettings.worldWidth;
            float worldDepthWorldUnits = worldSettings.worldDepth;

            chunksX = Mathf.CeilToInt(worldWidthWorldUnits / (chunkSize * voxelSize));
            chunksZ = Mathf.CeilToInt(worldDepthWorldUnits / (chunkSize * voxelSize));

            if (limitChunksForDebug)
            {
                chunksX = Mathf.Min(chunksX, debugChunksX);
                chunksZ = Mathf.Min(chunksZ, debugChunksZ);
            }

            Debug.Log($"VoxelWorld: Allocating {chunksX}×{chunksZ} chunks...");

            if (debugSdf)
            {
                // Only generate ONE chunk: (debugChunkX, debugChunkZ)
                ChunkCoord coord = new ChunkCoord(debugChunkX, debugChunkZ);

                ChunkData chunkData = new ChunkData(
                    coord,
                    chunkSize,
                    Allocator.Persistent
                );

                GenerateChunkVoxels(ref chunkData, ctx);
                chunkMap[(debugChunkX, debugChunkZ)] = chunkData;

                VoxelChunkView view = Instantiate(
                    chunkPrefab,
                    WorldCoordUtils.ChunkOriginWorld(coord, worldSettings),
                    Quaternion.identity,
                    transform
                );
                view.SetCoord(coord);

                Debug.Log($"Generated ONLY debug chunk ({debugChunkX},{debugChunkZ})");
                return; // Do NOT generate entire world
            }

            for (int cz = 0; cz < chunksZ; cz++)
            {
                for (int cx = 0; cx < chunksX; cx++)
                {
                    // Chunk coords in chunk-space
                    ChunkCoord coord = new ChunkCoord(cx, cz);

                    // Allocate chunk data
                    ChunkData chunkData = new ChunkData(
                        coord,
                        chunkSize,
                        Allocator.Persistent
                    );

                    // Fill voxel data using SDF + biome evaluation
                    GenerateChunkVoxels(ref chunkData, ctx);

                    // Store in map keyed by (cx, cz)
                    chunkMap[(cx, cz)] = chunkData;

                    // Instantiate chunk view at proper world origin
                    VoxelChunkView view = Instantiate(
                        chunkPrefab,
                        WorldCoordUtils.ChunkOriginWorld(coord, worldSettings),
                        Quaternion.identity,
                        transform
                    );

                    view.SetCoord(coord);
                }
            }

            Debug.Log("VoxelWorld: Initialization complete.");
        }

        // ------------------------------------------------------------------
        // Generate voxel densities + material IDs for a single chunk (sync)
        // NOW: padded grid (chunkSize+1 samples per axis).
        // ------------------------------------------------------------------
        private void GenerateChunkVoxels(ref ChunkData chunkData, in SdfContext ctx)
        {
            int cells   = chunkData.chunkSize;
            int voxRes  = chunkData.voxelResolution; // cells + 1
            ChunkCoord coord = chunkData.coord;

            for (int z = 0; z < voxRes; z++)
            {
                for (int y = 0; y < voxRes; y++)
                {
                    for (int x = 0; x < voxRes; x++)
                    {
                        VoxelCoord index = new VoxelCoord(x, y, z);

                        // Node sample position (aligned grid, shared across chunks)
                        float3 worldPos = WorldCoordUtils.VoxelSampleWorld(coord, index, worldSettings);

                        const float DensityScale = 64f;  // tweak later if needed

                        float sdf = CombinedTerrainSdf.Evaluate(worldPos, ctx);

                        // Convention: density > 0 => solid
                        short densityShort = (short)math.clamp(-sdf * DensityScale, short.MinValue, short.MaxValue);

                        // Optional debug sample at first node
                        if (debugSdf &&
                            coord.x == debugChunkX &&
                            coord.z == debugChunkZ &&
                            x == 0 && y == 0 && z == 0)
                        {
                            Debug.Log(
                                $"[DEBUG] Chunk ({coord.x},{coord.z}) SDF at (0,0,0): sdf={sdf}, worldPos={worldPos}"
                            );
                        }

                        // 2) Evaluate biome + pick material ID (still uses worldPos)
                        ushort materialId = SelectMaterialId(worldPos, sdf, ctx);

                        Voxel voxel = new Voxel(densityShort, materialId);
                        chunkData.Set(x, y, z, voxel);
                    }
                }
            }

            chunkData.isGenerated = true;
            chunkData.isDirty = true;
        }

        // ------------------------------------------------------------------
        // Simple Biome → material ID mapping using BiomeEvaluator + weights.
        // ------------------------------------------------------------------
        private ushort SelectMaterialId(float3 worldPos, float sdf, in SdfContext ctx)
        {
            BiomeWeights bw = BiomeEvaluator.EvaluateBiomeWeights(worldPos, ctx);

            // Pick the dominant biome
            BiomeDefinition.BiomeID dominant = BiomeDefinition.BiomeID.Grassland;
            float best = bw.grass;

            if (bw.forest > best)
            {
                best = bw.forest;
                dominant = BiomeDefinition.BiomeID.Forest;
            }

            if (bw.mountain > best)
            {
                best = bw.mountain;
                dominant = BiomeDefinition.BiomeID.Mountain;
            }

            if (bw.lakeshore > best)
            {
                best = bw.lakeshore;
                dominant = BiomeDefinition.BiomeID.LakeShore;
            }

            if (bw.city > best)
            {
                best = bw.city;
                dominant = BiomeDefinition.BiomeID.City;
            }

            // Map biome to materialId using inspector mapping
            for (int i = 0; i < biomeMaterialMappings.Length; i++)
            {
                if (biomeMaterialMappings[i].biome == dominant)
                    return biomeMaterialMappings[i].materialId;
            }

            // Fallback: default material ID 0
            return 0;
        }

        // ------------------------------------------------------------------
        // Cleanup native memory
        // ------------------------------------------------------------------
        private void OnDestroy()
        {
            if (chunkMap == null)
                return;

            foreach (var kv in chunkMap)
            {
                var data = kv.Value;
                data.Dispose();
            }

            chunkMap.Clear();
        }

        // (Optional) Helper to inspect chunk data from editor/debug code
        public bool TryGetChunkData(int x, int z, out ChunkData chunk)
        {
            return chunkMap.TryGetValue((x, z), out chunk);
        }

        // ------------------------------------------------------------------
        // SDF debug helper – only runs when debugSdf is true
        // ------------------------------------------------------------------
        private void DebugSdfSample(float3 worldPos, float sdf, in SdfContext ctx)
        {
            Debug.Log("<color=cyan>===== SDF DEBUG SAMPLE =====</color>");
            Debug.Log($"WorldPos = {worldPos}");
            Debug.Log($"Raw SDF = {sdf}");

            float v = worldSettings.voxelSize;
            float3 px = worldPos + new float3(v, 0, 0);
            float3 nx = worldPos - new float3(v, 0, 0);
            float3 pz = worldPos + new float3(0, 0, v);
            float3 nz = worldPos - new float3(0, 0, v);

            Debug.Log($"SDF neighbors: px={CombinedTerrainSdf.Evaluate(px, ctx)}, " +
                      $"nx={CombinedTerrainSdf.Evaluate(nx, ctx)}, " +
                      $"pz={CombinedTerrainSdf.Evaluate(pz, ctx)}, " +
                      $"nz={CombinedTerrainSdf.Evaluate(nz, ctx)}");

            var bw = BiomeEvaluator.EvaluateBiomeWeights(worldPos, ctx);
            Debug.Log($"Biome Weights: grass={bw.grass:F2}, forest={bw.forest:F2}, " +
                      $"mountain={bw.mountain:F2}, lake={bw.lakeshore:F2}, city={bw.city:F2}");

            ushort mat = SelectMaterialId(worldPos, sdf, ctx);
            Debug.Log($"Material = {mat}");

            Debug.Log("<color=cyan>===== END SAMPLE =====</color>");
        }
    }
}
