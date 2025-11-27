using UnityEngine;
using VoxelTerraria.Data.Features;

using Unity.Mathematics;
namespace VoxelTerraria.World.SDF
{
    [ExecuteAlways]
    public class SdfBootstrap : MonoBehaviour
    {
        [Header("World Settings")]
        public WorldSettings worldSettings;

        [Header("Features – Typed Lists (Editor-facing)")]
        public BaseIslandFeature baseIsland;
        public MountainFeature[] mountainFeatures;
        public LakeFeature[] lakeFeatures;
        public ForestFeature[] forestFeatures;
        public CityPlateauFeature[] cityFeatures;
        public VolcanoFeature[] volcanoFeatures;
        public RiverFeature[] riverFeatures;
        public CaveRoomFeature[] caveRooms;
        public CaveTunnelFeature[] caveTunnels;


        private void OnEnable()
        {
            if (worldSettings == null)
            {
                Debug.LogWarning("SdfBootstrap: Missing WorldSettings reference.");
                return;
            }

            var ctx = SdfBootstrapInternal.Build(
                worldSettings,
                baseIsland,
                mountainFeatures,
                lakeFeatures,
                forestFeatures,
                cityFeatures,
                volcanoFeatures,
                riverFeatures,
                caveRooms,
                caveTunnels
            );

            SdfRuntime.SetContext(ctx);
        }

        public void Refresh()
        {
            OnEnable();
        }

        private void OnValidate()
        {
            // Auto-refresh when inspector values change
            // This ensures river connections (which update SO fields) are applied immediately
            if (worldSettings != null)
            {
                // We delay the refresh slightly to avoid errors during serialization? 
                // No, direct call is usually fine for data packing.
                // But we should check if we are in play mode or edit mode.
                // In Edit mode, this is fine.
                Refresh();
            }
        }

        private void OnDisable()
        {
            SdfRuntime.Dispose();
        }

        private void OnDrawGizmosSelected()
        {
            if (worldSettings == null) return;

            // Draw Base Island
            if (baseIsland != null && baseIsland.ShowGizmos)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawWireSphere(new Vector3(baseIsland.CenterXZ.x, worldSettings.seaLevel, baseIsland.CenterXZ.y), baseIsland.Radius);
            }

            // Draw Mountains
            if (mountainFeatures != null)
            {
                Gizmos.color = Color.gray;
                foreach (var m in mountainFeatures)
                {
                    if (m == null || !m.ShowGizmos) continue;
                    Vector3 center = new Vector3(m.CenterXZ.x, worldSettings.seaLevel, m.CenterXZ.y);
                    Gizmos.DrawWireSphere(center, m.Radius);
                    Gizmos.DrawLine(center, center + Vector3.up * m.Height);
                }
            }

            // Draw Volcanoes
            if (volcanoFeatures != null)
            {
                Gizmos.color = new Color(1f, 0.5f, 0f); // Orange
                foreach (var v in volcanoFeatures)
                {
                    if (v == null || !v.ShowGizmos) continue;
                    Vector3 center = new Vector3(v.CenterXZ.x, v.BaseHeight, v.CenterXZ.y);
                    Gizmos.DrawWireSphere(center, v.Radius);
                    Gizmos.DrawLine(center, center + Vector3.up * v.Height);
                }
            }

            // Draw Rivers
            if (riverFeatures != null)
            {
                Gizmos.color = Color.blue;
                foreach (var r in riverFeatures)
                {
                    if (r == null || !r.ShowGizmos) continue;
                    
                    // 1. Draw Manual Chain
                    if (r.manualPath != null && r.manualPath.Count >= 2)
                    {
                        Gizmos.color = Color.blue;
                        for (int i = 0; i < r.manualPath.Count - 1; i++)
                        {
                            var f1 = r.manualPath[i];
                            var f2 = r.manualPath[i+1];
                            if (f1 == null || f2 == null) continue;
                            
                            Vector3 p1 = f1.GetConnectorPoint(worldSettings);
                            Vector3 p2 = f2.GetConnectorPoint(worldSettings);
                            
                            // Use the same offset logic as generation for visualization
                            // (Approximation for Gizmos)
                            Vector2 c1 = f1.GetCenter();
                            Vector2 c2 = f2.GetCenter();
                            float rad1 = f1.GetRadius();
                            float rad2 = f2.GetRadius();
                            
                            Unity.Mathematics.float2 dir = Unity.Mathematics.math.normalize(new Unity.Mathematics.float2(c2.x - c1.x, c2.y - c1.y));
                            Unity.Mathematics.float2 startPos = new Unity.Mathematics.float2(c1.x, c1.y) + dir * (rad1 * 0.8f);
                            Unity.Mathematics.float2 endPos   = new Unity.Mathematics.float2(c2.x, c2.y) - dir * (rad2 * 0.8f);
                            
                            float h1 = f1.GetBaseHeight(worldSettings) + r.startHeightOffset;
                            float h2 = f2.GetBaseHeight(worldSettings) + r.endHeightOffset;
                            
                            Vector3 start = new Vector3(startPos.x, h1, startPos.y);
                            Vector3 end   = new Vector3(endPos.x, h2, endPos.y);
                            
                            Gizmos.DrawLine(start, end);
                            Gizmos.DrawWireSphere(start, 2f);
                            Gizmos.DrawWireSphere(end, 2f);
                        }
                    }
                    else
                    {
                        // Draw legacy/default gizmo
                        // ... (keep existing logic for single segment fallback)
                        // Draw detailed meandering path
                        int segments = 50;
                        Vector3 prevPos = Vector3.zero;
                        
                        for (int i = 0; i <= segments; i++)
                        {
                            float t = (float)i / segments; // 0 to 1
                            float currentDist = t * r.radius; // 0 to length
                            
                            // Calculate position (matching RiverSdf logic)
                            // Z goes from center - length/2 to center + length/2
                            float z = r.centerXZ.y - r.radius * 0.5f + currentDist;
                            
                            // Y lerps from start to end
                            float y = Mathf.Lerp(r.startHeight, r.endHeight, t);
                            
                            // X meanders
                            float noiseVal = NoiseUtils.Noise2D(new Unity.Mathematics.float2(currentDist, 0) * r.meanderFrequency + r.seed, 1f, 1f);
                            float x = r.centerXZ.x + noiseVal * r.meanderAmplitude;
                            
                            Vector3 currentPos = new Vector3(x, y, z);
                            
                            if (i > 0)
                            {
                                Gizmos.DrawLine(prevPos, currentPos);
                            }
                            prevPos = currentPos;
                        }

                        // Draw bounds (approximate)
                        Gizmos.color = new Color(0, 0, 1, 0.3f);
                        Gizmos.DrawWireCube(
                            new Vector3(r.centerXZ.x, (r.startHeight + r.endHeight) * 0.5f, r.centerXZ.y),
                            new Vector3(r.width * 2f + r.meanderAmplitude * 2f, Mathf.Abs(r.startHeight - r.endHeight), r.radius)
                        );
                    }
                }
            }
            // Draw Cave Rooms
            if (caveRooms != null)
            {
                Gizmos.color = Color.magenta;
                foreach (var c in caveRooms)
                {
                    if (c == null || !c.ShowGizmos) continue;
                    Vector3 center = c.GetWorldCenter(worldSettings);
                    Gizmos.DrawWireSphere(center, c.Radius);
                }
            }

            // Draw Cave Tunnels
            if (caveTunnels != null)
            {
                Gizmos.color = Color.cyan;
                foreach (var t in caveTunnels)
                {
                    if (t == null || !t.ShowGizmos) continue;
                    Vector3 start = t.GetWorldStart(worldSettings);
                    Vector3 end = t.GetWorldEnd(worldSettings);
                    Gizmos.DrawLine(start, end);
                    Gizmos.DrawWireSphere(start, t.Radius);
                    Gizmos.DrawWireSphere(end, t.Radius);
                }
            }
        }
}
}
