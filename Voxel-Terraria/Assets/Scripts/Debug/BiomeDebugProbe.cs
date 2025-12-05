// using UnityEngine;
// using Unity.Mathematics;
// using VoxelTerraria.World.SDF;

// namespace VoxelTerraria.DebugTools
// {
//     [ExecuteAlways]
//     public class BiomeDebugProbe : MonoBehaviour
//     {
//         public bool autoUpdate = true;

//         private void OnDrawGizmos()
//         {
//             if (!autoUpdate) return;
//             Probe();
//         }

//         [ContextMenu("Probe Now")]
//         public void Probe()
//         {
//             if (!SdfRuntime.Initialized) return;

//             float3 pos = transform.position;
//             SdfContext ctx = SdfRuntime.Context;

//             float sdf = CombinedTerrainSdf.Evaluate(pos, ref ctx).distance;
//             int biomeId = BiomeEvaluator.EvaluateDominantBiomeId(pos, ctx);
//             int materialId = MaterialSelector.SelectMaterialId(pos, sdf, ctx);

//             // Find which feature is dominant
//             int bestFeatureIdx = -1;
//             float bestSdf = float.MaxValue;
//             for (int i = 0; i < ctx.featureCount; i++)
//             {
//                 float s = FeatureBounds3DComputer.EvaluateSdf_Fast(pos, ctx.features[i]).distance;
//                 if (s < bestSdf)
//                 {
//                     bestSdf = s;
//                     bestFeatureIdx = i;
//                 }
//             }

//             string featureName = "None";
//             if (bestFeatureIdx >= 0)
//             {
//                 var f = ctx.features[bestFeatureIdx];
//                 featureName = $"{f.type} (Biome {f.biomeId})";
//             }

//             // Draw label
//             string info = $"SDF: {sdf:F2}\nBiome: {biomeId}\nMat: {materialId}\nFeat: {featureName}";
            
//             Gizmos.color = Color.red;
//             Gizmos.DrawWireSphere(transform.position, 0.5f);
            
//             #if UNITY_EDITOR
//             UnityEditor.Handles.Label(transform.position + Vector3.up, info);
//             #endif
//         }
//     }
// }
