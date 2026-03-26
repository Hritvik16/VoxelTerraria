using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.World;

namespace VoxelEngine
{
    public enum WorldSize { Small, Medium, Large }

    public class WorldManager : MonoBehaviour
    {
        public static WorldManager Instance;
        
        [Header("World Settings")]
        [Tooltip("Small: 1260m radius (~4,200 blocks wide)\nMedium: 1890m radius (~6,400 blocks wide)\nLarge: 2520m radius (~8,400 blocks wide)")]
        public WorldSize worldSize = WorldSize.Small;
        public int worldSeed = 1337;
        
        [Header("Blueprint State")]
        public List<FeatureAnchor> mapFeatures = new List<FeatureAnchor>();
        public ComputeBuffer featureBuffer;

        [Header("Underground State")]
        public List<CavernNode> cavernNodes = new List<CavernNode>();
        public ComputeBuffer cavernBuffer;
        
        public List<TunnelSpline> tunnelSplines = new List<TunnelSpline>();
        public ComputeBuffer tunnelBuffer;

        // Hidden from inspector, safely queried by other scripts
        public float WorldRadiusXZ {
            get {
                switch (worldSize) {
                    case WorldSize.Medium: return 1890f;
                    case WorldSize.Large: return 2520f;
                    case WorldSize.Small:
                    default: return 1260f;
                }
            }
        }

        void Awake()
        {
            Instance = this;
            GenerateBlueprint();
        }

        public void GenerateBlueprint()
        {
            Random.InitState(worldSeed);
            mapFeatures.Clear();

            float currentRadius = WorldRadiusXZ;
            
            // 1. Generate Raw Safe Points (250m spacing)
            List<Vector2> rawPoints = GeneratePoissonDisk(currentRadius, 250f, 30);
            
            // 2. Shuffle Points for random distribution
            for (int i = 0; i < rawPoints.Count; i++) {
                Vector2 temp = rawPoints[i];
                int randomIndex = Random.Range(i, rawPoints.Count);
                rawPoints[i] = rawPoints[randomIndex];
                rawPoints[randomIndex] = temp;
            }

            // 3. Dynamic Quota Assignment based on World Size
            int pointIndex = 0;
            float sizeMultiplier = (worldSize == WorldSize.Small) ? 1.0f : ((worldSize == WorldSize.Medium) ? 1.5f : 2.0f);
            
            int mountainCount = Mathf.RoundToInt(2 * sizeMultiplier);
            int plateauCount = Mathf.RoundToInt(2 * sizeMultiplier);
            int ravineCount = Mathf.RoundToInt(2 * sizeMultiplier);
            int craterCount = Mathf.RoundToInt(1 * sizeMultiplier);

            // Mountains
            for (int i = 0; i < mountainCount && pointIndex < rawPoints.Count; i++, pointIndex++)
                mapFeatures.Add(CreateAnchor(rawPoints[pointIndex], 10, 0, Random.Range(150f, 200f), Random.Range(70f, 90f)));

            // Plateaus
            for (int i = 0; i < plateauCount && pointIndex < rawPoints.Count; i++, pointIndex++)
                mapFeatures.Add(CreateAnchor(rawPoints[pointIndex], 11, 0, Random.Range(80f, 120f), Random.Range(25f, 35f)));

            // Ravines
            for (int i = 0; i < ravineCount && pointIndex < rawPoints.Count; i++, pointIndex++)
                mapFeatures.Add(CreateAnchor(rawPoints[pointIndex], 13, 0, Random.Range(40f, 60f), Random.Range(-60f, -40f)));

            // Craters
            for (int i = 0; i < craterCount && pointIndex < rawPoints.Count; i++, pointIndex++)
                mapFeatures.Add(CreateAnchor(rawPoints[pointIndex], 12, 0, Random.Range(50f, 70f), -30f));

            // Biomes (Remaining)
            int[] requiredBiomes = { 1, 2, 3, 4 }; 
            int biomeIdx = 0;
            while (pointIndex < rawPoints.Count)
            {
                int bID = (biomeIdx < requiredBiomes.Length) ? requiredBiomes[biomeIdx] : Random.Range(0, 5);
                mapFeatures.Add(CreateAnchor(rawPoints[pointIndex], 0, bID, Random.Range(300f, 400f), 0));
                pointIndex++;
                biomeIdx++;
            }

            // 5. The "Plumb Bob" Drop Loop
            cavernNodes.Clear();
            foreach (var feature in mapFeatures)
            {
                // Drop Solid Biome Volumes based on the surface biome
                if (feature.topologyID == 0) 
                {
                    int drops = Random.Range(4, 7);
                    for (int i = 0; i < drops; i++)
                    {
                        float jitterX = feature.position.x + Random.Range(-60f, 60f);
                        float jitterZ = feature.position.y + Random.Range(-60f, 60f);
                        float depthY = Random.Range(10f, -250f); // Stretches from crust to underworld
                        
                        cavernNodes.Add(new CavernNode {
                            position = new Vector3(jitterX, depthY, jitterZ),
                            radius = Random.Range(60f, 100f),
                            biomeID = feature.biomeID,
                            cavernType = 0,
                            pad0 = 0, pad1 = 0
                        });
                    }
                }

                // Drop Air Hubs (Empty Rooms) across the depth bands
                int airDrops = Random.Range(1, 4);
                for (int i = 0; i < airDrops; i++)
                {
                    float jitterX = feature.position.x + Random.Range(-100f, 100f);
                    float jitterZ = feature.position.y + Random.Range(-100f, 100f);
                    
                    float depthY = Random.Range(10f, -280f);
                    float rad = 15f;

                    if (depthY > -40f) rad = Random.Range(10f, 20f);         // Crust
                    else if (depthY > -200f) rad = Random.Range(25f, 45f);   // Deep Caverns
                    else rad = Random.Range(50f, 70f);                       // Underworld

                    cavernNodes.Add(new CavernNode {
                        position = new Vector3(jitterX, depthY, jitterZ),
                        radius = rad,
                        biomeID = 0, // Air doesn't need a biome paint
                        cavernType = 1,
                        pad0 = 0, pad1 = 0
                    });
                }
            }

            // 6. Pathfinding Network (Connect the Air Hubs)
            tunnelSplines.Clear();
            List<CavernNode> airHubs = cavernNodes.FindAll(n => n.cavernType == 1);

            int entranceCount = Mathf.RoundToInt(15 * sizeMultiplier);
            for (int i = 0; i < entranceCount; i++) {
                if (airHubs.Count == 0) break;
                
                // Pick a random underground hub to connect to
                CavernNode targetHub = airHubs[Random.Range(0, airHubs.Count)];
                
                // Push the entrance out horizontally, and up to the surface layer (Y = 60 to 110)
                float angle = Random.value * Mathf.PI * 2f;
                float dist = Random.Range(80f, 150f);
                Vector3 surfacePos = targetHub.position + new Vector3(Mathf.Cos(angle) * dist, 0, Mathf.Sin(angle) * dist);
                surfacePos.y = Random.Range(60f, 110f); // Force it into the mountain heights
                
                // Create the physical entrance node
                CavernNode entrance = new CavernNode {
                    position = surfacePos,
                    radius = Random.Range(8f, 15f), // Big, sweeping openings
                    biomeID = 0,
                    cavernType = 2, // Type 2 = Cave Entrance
                    pad0 = 0, pad1 = 0
                };
                cavernNodes.Add(entrance);
                
                // Plunge a tunnel straight from the surface entrance down into the underground hub
                tunnelSplines.Add(new TunnelSpline {
                    startPoint = surfacePos,
                    endPoint = targetHub.position,
                    radius = Random.Range(5f, 10f),
                    noiseIntensity = Random.Range(0.2f, 0.5f)
                });
            }


            for (int i = 0; i < airHubs.Count; i++)
            {
                CavernNode a = airHubs[i];
                int connections = 0;

                for (int j = 0; j < airHubs.Count; j++)
                {
                    if (i == j) continue;
                    CavernNode b = airHubs[j];

                    float dist = Vector3.Distance(a.position, b.position);
                    if (dist > 120f) continue; // Distance check

                    // Angle check (limit to 60 degrees max slope)
                    float deltaY = Mathf.Abs(a.position.y - b.position.y);
                    float distXZ = Vector2.Distance(new Vector2(a.position.x, a.position.z), new Vector2(b.position.x, b.position.z));
                    float slopeAngle = Mathf.Atan2(deltaY, distXZ) * Mathf.Rad2Deg;
                    
                    if (slopeAngle > 60f) continue;

                    // Ocean check (don't tunnel out into the deep sea)
                    Vector2 midPointXZ = new Vector2((a.position.x + b.position.x) * 0.5f, (a.position.z + b.position.z) * 0.5f);
                    if (midPointXZ.magnitude > TerrainMath.GetCoastDistance(midPointXZ, currentRadius)) continue;

                    tunnelSplines.Add(new TunnelSpline {
                        startPoint = a.position,
                        endPoint = b.position,
                        radius = Random.Range(4f, 8f),
                        noiseIntensity = Random.Range(0.2f, 1.0f)
                    });

                    connections++;
                    if (connections >= 2) break; // Limit branches per hub
                }
            }

            // 7. Buffer Upload
            if (featureBuffer != null) featureBuffer.Release();
            if (cavernBuffer != null) cavernBuffer.Release();
            if (tunnelBuffer != null) tunnelBuffer.Release();

            featureBuffer = new ComputeBuffer(Mathf.Max(1, mapFeatures.Count), 32);
            if (mapFeatures.Count > 0) featureBuffer.SetData(mapFeatures);
            Shader.SetGlobalBuffer("_FeatureAnchorBuffer", featureBuffer);
            Shader.SetGlobalInt("_FeatureCount", mapFeatures.Count);

            cavernBuffer = new ComputeBuffer(Mathf.Max(1, cavernNodes.Count), 32);
            if (cavernNodes.Count > 0) cavernBuffer.SetData(cavernNodes);
            Shader.SetGlobalBuffer("_CavernNodeBuffer", cavernBuffer);
            Shader.SetGlobalInt("_CavernCount", cavernNodes.Count);

            tunnelBuffer = new ComputeBuffer(Mathf.Max(1, tunnelSplines.Count), 32);
            if (tunnelSplines.Count > 0) tunnelBuffer.SetData(tunnelSplines);
            Shader.SetGlobalBuffer("_TunnelSplineBuffer", tunnelBuffer);
            Shader.SetGlobalInt("_TunnelCount", tunnelSplines.Count);

            // CRITICAL FIX: Send the World Math constraints to the GPU so the island actually forms!
            Shader.SetGlobalFloat("_WorldRadiusXZ", WorldRadiusXZ);
            Shader.SetGlobalInt("_WorldSeed", worldSeed);

            // Debug.Log($"[WorldManager] Blueprint Generated: {mapFeatures.Count} Features, {cavernNodes.Count} Caverns, {tunnelSplines.Count} Tunnels.");
        }

        private FeatureAnchor CreateAnchor(Vector2 pos, int topID, int bioID, float rad, float hMod)
        {
            return new FeatureAnchor {
                position = pos, topologyID = topID, biomeID = bioID, radius = rad, heightMod = hMod, pad0 = 0, pad1 = 0
            };
        }

        private List<Vector2> GeneratePoissonDisk(float maxRadius, float radius, int k)
        {
            List<Vector2> points = new List<Vector2>();
            List<Vector2> active = new List<Vector2>();

            float cellSize = radius / Mathf.Sqrt(2);
            int gridSize = Mathf.CeilToInt((maxRadius * 2) / cellSize);
            int[,] grid = new int[gridSize, gridSize];
            for (int x = 0; x < gridSize; x++)
                for (int y = 0; y < gridSize; y++)
                    grid[x, y] = -1;

            Vector2 centerStart = Vector2.zero; 
            points.Add(centerStart);
            active.Add(centerStart);
            grid[gridSize / 2, gridSize / 2] = 0;

            while (active.Count > 0)
            {
                int spawnIndex = Random.Range(0, active.Count);
                Vector2 spawnCenter = active[spawnIndex];
                bool accepted = false;

                for (int i = 0; i < k; i++)
                {
                    float angle = Random.value * Mathf.PI * 2;
                    Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    Vector2 candidate = spawnCenter + dir * Random.Range(radius, 2 * radius);

                    if (IsValid(candidate, maxRadius, radius, cellSize, gridSize, grid, points))
                    {
                        points.Add(candidate);
                        active.Add(candidate);
                        
                        int cellX = Mathf.FloorToInt((candidate.x + maxRadius) / cellSize);
                        int cellY = Mathf.FloorToInt((candidate.y + maxRadius) / cellSize);
                        grid[cellX, cellY] = points.Count - 1;
                        accepted = true;
                        break;
                    }
                }
                if (!accepted) active.RemoveAt(spawnIndex);
            }
            return points;
        }

        private bool IsValid(Vector2 candidate, float maxRadius, float radius, float cellSize, int gridSize, int[,] grid, List<Vector2> points)
        {
            float distFromCenter = candidate.magnitude;
            float coastDist = TerrainMath.GetCoastDistance(candidate, maxRadius);
            
            if (distFromCenter > coastDist - 150f) return false;

            int cellX = Mathf.FloorToInt((candidate.x + maxRadius) / cellSize);
            int cellY = Mathf.FloorToInt((candidate.y + maxRadius) / cellSize);

            if (cellX >= 0 && cellX < gridSize && cellY >= 0 && cellY < gridSize)
            {
                int searchStartX = Mathf.Max(0, cellX - 2);
                int searchEndX = Mathf.Min(cellX + 2, gridSize - 1);
                int searchStartY = Mathf.Max(0, cellY - 2);
                int searchEndY = Mathf.Min(cellY + 2, gridSize - 1);

                for (int x = searchStartX; x <= searchEndX; x++)
                {
                    for (int y = searchStartY; y <= searchEndY; y++)
                    {
                        int pointIndex = grid[x, y];
                        if (pointIndex != -1)
                        {
                            float sqrDst = (candidate - points[pointIndex]).sqrMagnitude;
                            if (sqrDst < radius * radius) return false;
                        }
                    }
                }
                return true;
            }
            return false;
        }

        private void OnDestroy()
        {
            if (featureBuffer != null) featureBuffer.Release();
            if (cavernBuffer != null) cavernBuffer.Release();
            if (tunnelBuffer != null) tunnelBuffer.Release();
        }

        // Phase 1.5 Visual Validation
        private void OnDrawGizmos()
        {
            if (!Application.isPlaying) return;

            // Draw Caverns
            foreach (var node in cavernNodes)
            {
                Gizmos.color = (node.cavernType == 0) ? new Color(0, 1, 0, 0.15f) : new Color(1, 0, 0, 0.4f);
                Gizmos.DrawWireSphere(node.position, node.radius);
            }

            // Draw Tunnels
            Gizmos.color = Color.yellow;
            foreach (var tunnel in tunnelSplines)
            {
                Gizmos.DrawLine(tunnel.startPoint, tunnel.endPoint);
            }
        }
    }
}