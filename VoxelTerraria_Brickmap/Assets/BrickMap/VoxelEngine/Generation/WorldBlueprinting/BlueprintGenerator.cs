using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.World;

namespace VoxelEngine.Generation
{
    public static class BlueprintGenerator
    {
        public static void Generate(
            int worldSeed, 
            float worldRadiusXZ, 
            float sizeMultiplier,
            List<FeatureAnchor> mapFeatures, 
            List<CavernNode> cavernNodes, 
            List<TunnelSpline> tunnelSplines)
        {
            Random.InitState(worldSeed);
            mapFeatures.Clear();

            // 1. Generate Raw Safe Points (250m spacing)
            List<Vector2> rawPoints = PoissonDiskSampler.GeneratePoissonDisk(worldRadiusXZ, 250f, 30);
            
            // 2. Shuffle Points
            for (int i = 0; i < rawPoints.Count; i++) {
                Vector2 temp = rawPoints[i];
                int randomIndex = Random.Range(i, rawPoints.Count);
                rawPoints[i] = rawPoints[randomIndex];
                rawPoints[randomIndex] = temp;
            }

            // 3. Dynamic Quota Assignment based on World Size
            int pointIndex = 0;
            
            int mountainRanges = Mathf.RoundToInt(2 * sizeMultiplier);
            int plateauClusters = Mathf.RoundToInt(2 * sizeMultiplier);
            int steppeLines = Mathf.RoundToInt(2 * sizeMultiplier);
            int duneFields = Mathf.RoundToInt(1 * sizeMultiplier);

            // Mountains (ID 10) - Connected peaks forming a range
            for (int i = 0; i < mountainRanges && pointIndex < rawPoints.Count; i++, pointIndex++)
                GenerateSpine(mapFeatures, rawPoints[pointIndex], 10, Random.Range(4, 7), 50f, 40f, 70f, 60f, 100f);

            // Plateaus (ID 11) - Connected flat tops
            for (int i = 0; i < plateauClusters && pointIndex < rawPoints.Count; i++, pointIndex++)
                GenerateSpine(mapFeatures, rawPoints[pointIndex], 11, Random.Range(2, 4), 40f, 30f, 50f, 40f, 60f);

            // Dunes (ID 12) - Winding fields
            for (int i = 0; i < duneFields && pointIndex < rawPoints.Count; i++, pointIndex++)
                GenerateSpine(mapFeatures, rawPoints[pointIndex], 12, Random.Range(5, 9), 40f, 25f, 40f, 0f, 0f);

            // Steppes (ID 13) - Overlapping broad tiers
            for (int i = 0; i < steppeLines && pointIndex < rawPoints.Count; i++, pointIndex++)
                GenerateSpine(mapFeatures, rawPoints[pointIndex], 13, Random.Range(3, 6), 50f, 40f, 60f, 0f, 0f);

            // --- THE FIX: DEDICATED VORONOI BIOME SEEDS ---
            // Generate a fresh, widely-spaced set of points purely for Biomes (no gaps!)
            List<Vector2> biomePoints = PoissonDiskSampler.GeneratePoissonDisk(worldRadiusXZ, 500f, 20);
            
            int[] requiredBiomes = { 0, 1, 2, 3, 4 }; // Ensure at least one of every biome exists
            for (int i = 0; i < biomePoints.Count; i++)
            {
                int bID = (i < requiredBiomes.Length) ? requiredBiomes[i] : Random.Range(0, 5);
                
                // Pack into mapFeatures to transport it, but give it a massive radius
                mapFeatures.Add(CreateAnchor(biomePoints[i], 0, bID, 9999f, 0f));
            }

            // --- THE FIX: DECOUPLED & SUNK CAVE GENERATION ---
            // Caves now use the original 30 scattered Poisson points, NOT the hundreds of tight spine anchors.
            // Air caves are forced safely underground so they stop blowing the tops off the mountains!
            cavernNodes.Clear();
            foreach (var point in rawPoints)
            {
                int drops = Random.Range(1, 3);
                for (int i = 0; i < drops; i++)
                {
                    float jitterX = point.x + Random.Range(-40f, 40f);
                    float jitterZ = point.y + Random.Range(-40f, 40f);
                    float depthY = Random.Range(-20f, -250f); 
                    
                    cavernNodes.Add(new CavernNode {
                        position = new Vector3(jitterX, depthY, jitterZ),
                        radius = Random.Range(40f, 80f),
                        biomeID = 0,
                        cavernType = 0,
                        pad0 = 0, pad1 = 0
                    });
                }

                int airDrops = Random.Range(1, 3);
                for (int i = 0; i < airDrops; i++)
                {
                    float jitterX = point.x + Random.Range(-60f, 60f);
                    float jitterZ = point.y + Random.Range(-60f, 60f);
                    
                    // THE FIX: Highest possible air cave is at Y: -20 (safely below the base terrain)
                    float depthY = Random.Range(-20f, -280f); 
                    float rad = 15f;

                    if (depthY > -60f) rad = Random.Range(10f, 20f);         // Crust
                    else if (depthY > -200f) rad = Random.Range(25f, 45f);   // Deep Caverns
                    else rad = Random.Range(50f, 70f);                       // Underworld

                    cavernNodes.Add(new CavernNode {
                        position = new Vector3(jitterX, depthY, jitterZ),
                        radius = rad,
                        biomeID = 0, 
                        cavernType = 1,
                        pad0 = 0, pad1 = 0
                    });
                }
            }

            // 5. Pathfinding Network (Connect the Air Hubs)
            tunnelSplines.Clear();
            List<CavernNode> airHubs = cavernNodes.FindAll(n => n.cavernType == 1);

            int entranceCount = Mathf.RoundToInt(15 * sizeMultiplier);
            for (int i = 0; i < entranceCount; i++) {
                if (airHubs.Count == 0) break;
                
                CavernNode targetHub = airHubs[Random.Range(0, airHubs.Count)];
                
                float angle = Random.value * Mathf.PI * 2f;
                float dist = Random.Range(80f, 150f);
                Vector3 surfacePos = targetHub.position + new Vector3(Mathf.Cos(angle) * dist, 0, Mathf.Sin(angle) * dist);
                surfacePos.y = Random.Range(60f, 110f); // Force it into the mountain heights
                
                CavernNode entrance = new CavernNode {
                    position = surfacePos,
                    radius = Random.Range(8f, 15f), 
                    biomeID = 0,
                    cavernType = 2, 
                    pad0 = 0, pad1 = 0
                };
                cavernNodes.Add(entrance);
                
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
                    if (dist > 120f) continue; 

                    float deltaY = Mathf.Abs(a.position.y - b.position.y);
                    float distXZ = Vector2.Distance(new Vector2(a.position.x, a.position.z), new Vector2(b.position.x, b.position.z));
                    float slopeAngle = Mathf.Atan2(deltaY, distXZ) * Mathf.Rad2Deg;
                    
                    if (slopeAngle > 60f) continue;

                    Vector2 midPointXZ = new Vector2((a.position.x + b.position.x) * 0.5f, (a.position.z + b.position.z) * 0.5f);
                    if (midPointXZ.magnitude > TerrainMath.GetCoastDistance(midPointXZ, worldRadiusXZ)) continue;

                    tunnelSplines.Add(new TunnelSpline {
                        startPoint = a.position,
                        endPoint = b.position,
                        radius = Random.Range(4f, 8f),
                        noiseIntensity = Random.Range(0.2f, 1.0f)
                    });

                    connections++;
                    if (connections >= 2) break;
                }
            }
        }

        private static FeatureAnchor CreateAnchor(Vector2 pos, int topID, int bioID, float rad, float hMod)
        {
            return new FeatureAnchor {
                position = pos, topologyID = topID, biomeID = bioID, radius = rad, heightMod = hMod, pad0 = 0, pad1 = 0
            };
        }

        // --- NEW: The Spine Generator ---
        private static void GenerateSpine(List<FeatureAnchor> mapFeatures, Vector2 startPoint, int topologyID, int nodeCount, float stepDistance, float minRad, float maxRad, float minHeight, float maxHeight)
        {
            Vector2 currentPoint = startPoint;
            float currentAngle = Random.value * Mathf.PI * 2f; 

            for (int i = 0; i < nodeCount; i++)
            {
                float rad = Random.Range(minRad, maxRad);
                float hMod = Random.Range(minHeight, maxHeight);
                
                mapFeatures.Add(CreateAnchor(currentPoint, topologyID, 0, rad, hMod));

                // Wander the angle for a natural curve
                currentAngle += Random.Range(-Mathf.PI / 4f, Mathf.PI / 4f);
                currentPoint += new Vector2(Mathf.Cos(currentAngle), Mathf.Sin(currentAngle)) * stepDistance;
            }
        }
    }
}
