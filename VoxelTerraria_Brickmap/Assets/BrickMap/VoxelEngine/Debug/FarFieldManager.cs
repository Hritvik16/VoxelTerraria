// using UnityEngine;
// using Unity.Mathematics;
// using Unity.Collections;
// using Unity.Jobs;
// using Unity.Burst;
// using VoxelEngine.World;
// using VoxelEngine.Generation;
// using System.Collections.Generic;
// #if ENABLE_INPUT_SYSTEM
// using UnityEngine.InputSystem;
// #endif

// namespace VoxelEngine.DebugTools
// {
//     public class FarFieldManager : MonoBehaviour
//     {
//         [Header("Resolution Settings")]
//         public int ringResolution = 120; // Vertices across
//         public int backdropResolution = 120;

//         [Header("Aesthetics")]
//         public Material farFieldMaterial;
//         public float verticalOffset = -0.3f; // Avoid z-fighting

//         private GameObject ringObj;
//         private GameObject backdropObj;
//         private bool isVisible = true;

//         [BurstCompile]
//         struct FarFieldHeightJob : IJobParallelFor
//         {
//             [ReadOnly] public NativeArray<Vector3> vertices;
//             [ReadOnly] public NativeArray<FeatureAnchor> features;
//             public int featureCount;
//             public float worldRadius;

//             public NativeArray<Vector3> outVertices;
//             public NativeArray<Color32> outColors;

//             public void Execute(int i)
//             {
//                 Vector3 v = vertices[i];
//                 var data = TerrainNoiseMath.GetSurfaceData(v.x, v.z, features, featureCount, worldRadius);
                
//                 v.y = data.height;
//                 outVertices[i] = v;

//                 Color c1 = GetColor(data.topology1);
//                 Color c2 = GetColor(data.topology2);
//                 outColors[i] = Color.Lerp(c2, c1, data.weight);
//             }

//             private Color GetColor(int id)
//             {
//                 if (id == 0) return new Color(0.2f, 0.6f, 0.2f); // Plains
//                 if (id == 1) return new Color(0.5f, 0.35f, 0.2f); // Steppes
//                 if (id == 2) return new Color(0.4f, 0.4f, 0.45f); // Mountains
//                 if (id == 3) return new Color(0.8f, 0.7f, 0.2f); // Mesa
//                 return Color.black;
//             }
//         }

//         void Start()
//         {
//             // Initial generation happens after a small delay to ensure WorldManager is ready
//             Invoke("GenerateAll", 0.5f);
//         }

//         void Update()
//         {
// #if ENABLE_INPUT_SYSTEM
//             var kb = Keyboard.current;
//             if (kb != null && kb.fKey.wasPressedThisFrame)
//             {
//                 ToggleVisibility();
//             }
// #else
//             if (Input.GetKeyDown(KeyCode.F))
//             {
//                 ToggleVisibility();
//             }
// #endif
//         }

//         private void ToggleVisibility()
//         {
//             isVisible = !isVisible;
//             if (ringObj) ringObj.SetActive(isVisible);
//             if (backdropObj) backdropObj.SetActive(isVisible);
//         }

//         public void GenerateAll()
//         {
//             if (WorldManager.Instance == null || WorldManager.Instance.mapFeatures.Count == 0) return;

//             float worldRadius = WorldManager.Instance.WorldRadiusXZ;
            
//             // Layer 1: Mid-Range Ring (400m to 1200m)
//             ringObj = CreateMeshLayer("FarField_Ring", 400f, 1200f, ringResolution);
            
//             // Layer 2: Distance Backdrop (1200m to 2600m)
//             backdropObj = CreateMeshLayer("FarField_Backdrop", 1200f, 2600f, backdropResolution);
//         }

//         private GameObject CreateMeshLayer(string name, float innerRadius, float outerRadius, int res)
//         {
//             GameObject obj = new GameObject(name);
//             obj.transform.SetParent(this.transform);
//             obj.transform.localPosition = new Vector3(0, verticalOffset, 0);

//             MeshFilter mf = obj.AddComponent<MeshFilter>();
//             MeshRenderer mr = obj.AddComponent<MeshRenderer>();
//             mr.material = farFieldMaterial != null ? farFieldMaterial : new Material(Shader.Find("Unlit/VertexColor"));

//             Mesh mesh = new Mesh();
//             mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

//             // Generate Grid
//             List<Vector3> verts = new List<Vector3>();
//             List<int> tris = new List<int>();

//             float step = (outerRadius * 2f) / (res - 1);
//             float start = -outerRadius;

//             Dictionary<int2, int> gridToIdx = new Dictionary<int2, int>();

//             for (int z = 0; z < res; z++)
//             {
//                 for (int x = 0; x < res; x++)
//                 {
//                     float px = start + x * step;
//                     float pz = start + z * step;
//                     float dist = Mathf.Sqrt(px * px + pz * pz);

//                     if (dist >= innerRadius && dist <= outerRadius + step)
//                     {
//                         gridToIdx[new int2(x, z)] = verts.Count;
//                         verts.Add(new Vector3(px, 0, pz));
//                     }
//                 }
//             }

//             for (int z = 0; z < res - 1; z++)
//             {
//                 for (int x = 0; x < res - 1; x++)
//                 {
//                     int i0, i1, i2, i3;
//                     if (gridToIdx.TryGetValue(new int2(x, z), out i0) &&
//                         gridToIdx.TryGetValue(new int2(x + 1, z), out i1) &&
//                         gridToIdx.TryGetValue(new int2(x, z + 1), out i2) &&
//                         gridToIdx.TryGetValue(new int2(x + 1, z + 1), out i3))
//                     {
//                         tris.Add(i0); tris.Add(i2); tris.Add(i1);
//                         tris.Add(i1); tris.Add(i2); tris.Add(i3);
//                     }
//                 }
//             }

//             // Burst Height Calculation
//             NativeArray<Vector3> nativeVerts = new NativeArray<Vector3>(verts.ToArray(), Allocator.TempJob);
//             NativeArray<Vector3> outVerts = new NativeArray<Vector3>(verts.Count, Allocator.TempJob);
//             NativeArray<Color32> outCols = new NativeArray<Color32>(verts.Count, Allocator.TempJob);
            
//             NativeArray<FeatureAnchor> nativeFeatures = new NativeArray<FeatureAnchor>(WorldManager.Instance.mapFeatures.ToArray(), Allocator.TempJob);

//             FarFieldHeightJob job = new FarFieldHeightJob
//             {
//                 vertices = nativeVerts,
//                 features = nativeFeatures,
//                 featureCount = nativeFeatures.Length,
//                 worldRadius = WorldManager.Instance.WorldRadiusXZ,
//                 outVertices = outVerts,
//                 outColors = outCols
//             };

//             JobHandle handle = job.Schedule(verts.Count, 64);
//             handle.Complete();

//             mesh.vertices = outVerts.ToArray();
//             mesh.colors32 = outCols.ToArray();
//             mesh.triangles = tris.ToArray();
//             mesh.RecalculateNormals();

//             mf.mesh = mesh;

//             nativeVerts.Dispose();
//             outVerts.Dispose();
//             outCols.Dispose();
//             nativeFeatures.Dispose();

//             return obj;
//         }

//         private void OnGUI()
//         {
//             GUI.Label(new Rect(10, Screen.height - 30, 300, 25), "[F] Toggle Far-Field Overview Mesh");
//         }
//     }
// }
