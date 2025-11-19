// Put this file in: Assets/Scripts/Editor/Debug/ChunkTestWindow.cs

using System.Reflection;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

using VoxelTerraria.World;
using VoxelTerraria.World.Meshing; // MeshData, BlockMesher

namespace VoxelTerraria.EditorTools
{
    public class ChunkTestWindow : EditorWindow
    {
        // ------------------------------------------------------
        // UI state
        // ------------------------------------------------------
        private int chunkX = 0;
        private int chunkZ = 0;

        // Cached references
        private VoxelWorld cachedVoxelWorld;
        private MethodInfo generateChunkVoxelsMethod;

        private const string RootName = "EditorTestChunks";

        // ------------------------------------------------------
        // Menu
        // ------------------------------------------------------
        [MenuItem("Tools/Chunk Test Window")]
        public static void ShowWindow()
        {
            var window = GetWindow<ChunkTestWindow>("Chunk Test Window");
            window.minSize = new Vector2(320, 150);
        }

        // ------------------------------------------------------
        // OnEnable – cache references if possible
        // ------------------------------------------------------
        private void OnEnable()
        {
            CacheVoxelWorldAndMethod();
        }

        private void CacheVoxelWorldAndMethod()
        {
            if (cachedVoxelWorld == null)
            {
                cachedVoxelWorld = FindObjectOfType<VoxelWorld>();
            }

            if (cachedVoxelWorld != null && generateChunkVoxelsMethod == null)
            {
                // private void GenerateChunkVoxels(ref ChunkData chunkData, in SdfContext ctx)
                generateChunkVoxelsMethod = typeof(VoxelWorld).GetMethod(
                    "GenerateChunkVoxels",
                    BindingFlags.Instance | BindingFlags.NonPublic
                );
            }
        }

        // ------------------------------------------------------
        // GUI
        // ------------------------------------------------------
        private void OnGUI()
        {
            EditorGUILayout.LabelField("Chunk Coordinates", EditorStyles.boldLabel);

            chunkX = EditorGUILayout.IntField("Chunk X", chunkX);
            chunkZ = EditorGUILayout.IntField("Chunk Z", chunkZ);

            EditorGUILayout.Space();

            // Dependency status
            DrawStatus();

            EditorGUI.BeginDisabledGroup(!CanGenerate());
            if (GUILayout.Button("Generate Chunk Mesh", GUILayout.Height(32)))
            {
                GenerateChunkMesh();
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "This tool:\n" +
                "• Uses SdfRuntime.Context from SdfBootstrap (ExecuteAlways)\n" +
                "• Calls VoxelWorld.GenerateChunkVoxels via reflection\n" +
                "• Uses BlockMesher to build a blocky mesh and parents it under '" + RootName + "'.",
                MessageType.Info
            );
        }

        private void DrawStatus()
        {
            CacheVoxelWorldAndMethod();

            if (cachedVoxelWorld == null)
            {
                EditorGUILayout.HelpBox(
                    "No VoxelWorld found in the active scene.\n" +
                    "Add a VoxelWorld component and assign WorldSettings + mappings.",
                    MessageType.Warning
                );
            }
            else if (cachedVoxelWorld.worldSettings == null)
            {
                EditorGUILayout.HelpBox(
                    "VoxelWorld is missing WorldSettings reference.",
                    MessageType.Error
                );
            }

            if (generateChunkVoxelsMethod == null)
            {
                EditorGUILayout.HelpBox(
                    "Could not find VoxelWorld.GenerateChunkVoxels (private) method.\n" +
                    "Check that its name and signature are unchanged.",
                    MessageType.Error
                );
            }

            // Check SdfRuntime.Context
            var ctx = SdfRuntime.Context;
            bool hasContext = ctx.chunkSize != 0 || ctx.mountains.IsCreated ||
                              ctx.lakes.IsCreated || ctx.forests.IsCreated || ctx.cities.IsCreated;

            if (!hasContext)
            {
                EditorGUILayout.HelpBox(
                    "SdfRuntime.Context appears to be uninitialized.\n" +
                    "Make sure there is an SdfBootstrap in the scene and it has run (ExecuteAlways).",
                    MessageType.Warning
                );
            }
        }

        private bool CanGenerate()
        {
            if (cachedVoxelWorld == null) return false;
            if (cachedVoxelWorld.worldSettings == null) return false;
            if (generateChunkVoxelsMethod == null) return false;

            var ctx = SdfRuntime.Context;
            bool hasContext = ctx.chunkSize != 0 || ctx.mountains.IsCreated ||
                              ctx.lakes.IsCreated || ctx.forests.IsCreated || ctx.cities.IsCreated;

            return hasContext;
        }

        // ------------------------------------------------------
        // Main generation entry
        // ------------------------------------------------------
        private void GenerateChunkMesh()
        {
            CacheVoxelWorldAndMethod();
            if (!CanGenerate())
            {
                Debug.LogError("ChunkTestWindow: Cannot generate – missing dependencies. See window warnings.");
                return;
            }

            WorldSettings settings = cachedVoxelWorld.worldSettings;
            SdfContext ctx = SdfRuntime.Context;

            int chunkSize = settings.chunkSize;
            ChunkCoord coord = new ChunkCoord(chunkX, chunkZ);

            // 1) Allocate temporary ChunkData (TempJob is fine in EditMode as long as we Dispose)
            ChunkData chunkData = new ChunkData(coord, chunkSize, Allocator.TempJob);

            try
            {
                // 2) Call VoxelWorld.GenerateChunkVoxels(ref ChunkData, in SdfContext) via reflection
                object[] parameters = new object[] { chunkData, ctx }; // ref param will be updated in this array

                generateChunkVoxelsMethod.Invoke(cachedVoxelWorld, parameters);

                // Retrieve back the (possibly) modified struct
                chunkData = (ChunkData)parameters[0];

                // 3) Build mesh from chunk voxels using BlockMesher + MeshData
                MeshData meshData = BlockMesher.BuildMesh(in chunkData, settings);
                Mesh mesh = meshData.ToMesh(false);

                // 4) Create / replace GameObject in scene
                CreateOrReplaceChunkGO(coord, settings, mesh);

                Debug.Log($"ChunkTestWindow: Generated chunk mesh at ({chunkX}, {chunkZ}).");
            }
            finally
            {
                // 5) Ensure no NativeArray leaks
                chunkData.Dispose();
            }
        }

        // ------------------------------------------------------
        // Scene object management
        // ------------------------------------------------------
        private Transform GetOrCreateRoot()
        {
            GameObject root = GameObject.Find(RootName);
            if (root == null)
            {
                root = new GameObject(RootName);
            }
            return root.transform;
        }

        private void CreateOrReplaceChunkGO(ChunkCoord coord, WorldSettings settings, Mesh mesh)
        {
            Transform root = GetOrCreateRoot();

            string chunkName = $"Chunk_{coord.x}_{coord.z}";

            Transform existing = root.Find(chunkName);
            if (existing != null)
            {
                // In editor, destroy immediately
                if (Application.isEditor)
                    Object.DestroyImmediate(existing.gameObject);
                else
                    Object.Destroy(existing.gameObject);
            }

            GameObject go = new GameObject(chunkName);
            go.transform.parent = root;
            go.transform.position = WorldCoordUtils.ChunkOriginWorld(coord, settings);
            go.transform.rotation = Quaternion.identity;

            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            var mc = go.AddComponent<MeshCollider>();

            mf.sharedMesh = mesh;
            mc.sharedMesh = mesh;

            // Try URP Lit first, then fallback to Standard
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Standard");

            if (shader != null)
            {
                var mat = new Material(shader);
                mr.sharedMaterial = mat;
            }
            else
            {
                Debug.LogWarning("ChunkTestWindow: Could not find a suitable shader. Assign a material manually.");
            }
        }
    }
}
