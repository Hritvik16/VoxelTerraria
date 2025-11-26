using UnityEngine;
using VoxelTerraria.Data.Features;

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
                riverFeatures
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
            if (baseIsland != null && baseIsland.DrawGizmos)
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
                    if (m == null || !m.DrawGizmos) continue;
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
                    if (v == null || !v.DrawGizmos) continue;
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
                    if (r == null || !r.DrawGizmos) continue;
                    
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
                        // Note: We need to use the same noise function. 
                        // Since NoiseUtils is static, we can use it here if accessible.
                        // If not, we'll approximate or need to expose it.
                        // Assuming NoiseUtils is available in Editor (it's just a class).
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
}
}
