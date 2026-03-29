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

            // 4. The "Plumb Bob" Drop Loop
            cavernNodes.Clear();
            foreach (var feature in mapFeatures)
            {
                if (feature.topologyID == 0) 
                {
                    int drops = Random.Range(4, 7);
                    for (int i = 0; i < drops; i++)
                    {
                        float jitterX = feature.position.x + Random.Range(-60f, 60f);
                        float jitterZ = feature.position.y + Random.Range(-60f, 60f);
                        float depthY = Random.Range(10f, -250f); 
                        
                        cavernNodes.Add(new CavernNode {
                            position = new Vector3(jitterX, depthY, jitterZ),
                            radius = Random.Range(60f, 100f),
                            biomeID = feature.biomeID,
                            cavernType = 0,
                            pad0 = 0, pad1 = 0
                        });
                    }
                }

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
    }
}
