// Put this file in: Assets/Scripts/Editor/World/WorldGenerationWindow.cs

using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

using VoxelTerraria.World;
using VoxelTerraria.World.Meshing;
using VoxelTerraria.World.Generation;
using VoxelTerraria.World.SDF;

namespace VoxelTerraria.EditorTools
{
    public class WorldGenerationWindow : EditorWindow
    {
        [SerializeField, Range(1, 16)]
        private int microBlockDiv = 8;   // default 8

        [SerializeField]
        private MeshMode meshMode = MeshMode.BlockyCubes; // default to block mesher (final art style)

        // ------------------------------------------------------
        // Constants
        // ------------------------------------------------------
        private const string RootName = "EditorGeneratedWorld";

        // ------------------------------------------------------
        // Cached references
        // ------------------------------------------------------
        private WorldSettings cachedWorldSettings;
        private SdfContext cachedSdfContext;
        private bool hasValidContext;

        // ------------------------------------------------------
        // Generation state (progressive)
        // ------------------------------------------------------
        private bool isGenerating = false;
        private Queue<ChunkCoord3> generationQueue;
        private int totalChunks = 0;
        private int processedChunks = 0;

        // How many chunks to process each EditorApplication.update tick
        [SerializeField]
        private int chunksPerStep = 1;

        // Chunk range (from SdfContext)
        private int minChunkX;
        private int maxChunkX;
        private int minChunkZ;
        private int maxChunkZ;

        // Generation timing
        private double generationStartTime;

        // Cached root transform
        private Transform cachedRoot;

        // Shared material for all generated chunks
        private static Material s_sharedChunkMaterial;

        // ------------------------------------------------------
        // Menu
        // ------------------------------------------------------
        [MenuItem("Tools/World Generation Window")]
        public static void ShowWindow()
        {
            var window = GetWindow<WorldGenerationWindow>("World Generation");
            window.minSize = new Vector2(360, 220);
        }

        // ------------------------------------------------------
        // Lifecycle
        // ------------------------------------------------------
        private void OnEnable()
        {
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.update += OnEditorUpdate;
            CacheDependencies();
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;

            if (isGenerating)
            {
                StopGeneration();
            }
        }

        // ------------------------------------------------------
        // Dependency caching
        // ------------------------------------------------------
        private void CacheDependencies()
        {
            // WorldSettings comes from SdfBootstrap (ScriptableObject asset, not a scene component)
            var bootstrap = FindObjectOfType<SdfBootstrap>();
            cachedWorldSettings = bootstrap != null ? bootstrap.worldSettings : null;

            if (cachedWorldSettings == null)
            {
                // Only log once per repaint to avoid spam
                // (the help box in OnGUI will also warn)
                // Debug.LogWarning("WorldGenerationWindow: No WorldSettings found via SdfBootstrap.");
            }

            // SDF context
            cachedSdfContext = SdfRuntime.Context;
            hasValidContext = CheckContextValid(cachedSdfContext);
        }

        private static bool CheckContextValid(in SdfContext ctx)
        {
            // Minimal sanity check:
            //  - chunkSize must be non-zero
            //  - chunk grid must be positive if bounds were computed
            if (ctx.chunkSize <= 0)
                return false;

            // If you haven't run ChunkBoundsAutoComputer yet, chunksX/chunksZ may be 0.
            // We still allow "valid" here so the UI can tell you what's wrong.
            return true;
        }

        // ------------------------------------------------------
        // GUI
        // ------------------------------------------------------
        private void OnGUI()
        {
            // Make sure we always reflect latest scene state
            CacheDependencies();
            Repaint();

            EditorGUILayout.LabelField("World Generation (Editor)", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            DrawStatusSection();

            EditorGUILayout.Space();

            // Options
            EditorGUILayout.LabelField("Options", EditorStyles.boldLabel);

            meshMode = (MeshMode)EditorGUILayout.EnumPopup(
                new GUIContent("Mesh Mode",
                "Pick which mesher to use for chunk rendering.\n" +
                "BlockyCubes = final art style.\n" +
                "Others = debug / visualization."),
                meshMode
            );

            if (meshMode == MeshMode.MicroBlocks)
            {
                microBlockDiv = EditorGUILayout.IntSlider(
                    new GUIContent("Micro Block Subdivision",
                    "Number of micro cubes per coarse voxel axis (microDiv).\n" +
                    "microDiv=1 → normal cubes\n" +
                    "microDiv=8 → very fine voxel detail (debug/experiments)"),
                    microBlockDiv, 1, 16
                );
            }

            chunksPerStep = EditorGUILayout.IntSlider(
                new GUIContent("Chunks Per Step",
                    "How many chunks to generate per Editor update. Higher = faster but more stutter."),
                Mathf.Max(1, chunksPerStep),
                1, 32
            );

            EditorGUILayout.Space();

            // Buttons
            EditorGUI.BeginDisabledGroup(isGenerating || !CanGenerateWorld());
            if (GUILayout.Button("Generate All Chunks (Editor)", GUILayout.Height(32)))
            {
                StartGeneration();
            }
            EditorGUI.EndDisabledGroup();

            EditorGUI.BeginDisabledGroup(!IsRootPresent() && !isGenerating);
            if (GUILayout.Button("Clear Generated World", GUILayout.Height(24)))
            {
                ClearGeneratedWorld();
            }
            EditorGUI.EndDisabledGroup();

            if (isGenerating)
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(
                    $"Generating chunks... {processedChunks} / {totalChunks}",
                    MessageType.Info
                );

                if (GUILayout.Button("Cancel Generation"))
                {
                    StopGeneration();
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "This tool:\n" +
                "• Uses SdfRuntime.Context from SdfBootstrap (ExecuteAlways)\n" +
                "• Uses chunk bounds baked into SdfContext (ChunkBoundsAutoComputer)\n" +
                "• Calls VoxelGenerator.GenerateChunkVoxels for each chunk\n" +
                "• Uses selected MeshMode for terrain\n" +
                "• Instantiates MeshFilter/MeshRenderer/MeshCollider under '" + RootName + "'.",
                MessageType.Info
            );
        }

        private void DrawStatusSection()
        {
            if (cachedWorldSettings == null)
            {
                EditorGUILayout.HelpBox(
                    "WorldSettings asset not found.\n" +
                    "Add an SdfBootstrap to the scene and assign a WorldSettings asset.",
                    MessageType.Error
                );
            }

            if (!hasValidContext)
            {
                EditorGUILayout.HelpBox(
                    "SdfRuntime.Context appears to be uninitialized.\n" +
                    "Make sure there is an SdfBootstrap in the scene and that it has run (ExecuteAlways).",
                    MessageType.Warning
                );
            }

            var ctx = cachedSdfContext;

            if (hasValidContext && ctx.chunksX > 0 && ctx.chunksZ > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Computed Chunk Grid:", $"{ctx.chunksX} × {ctx.chunksY} × {ctx.chunksZ}");


                EditorGUILayout.LabelField("Chunk Coordinate Range:",
                    $"X: {ctx.minChunkX} → {ctx.maxChunkX},  " +
                    $"Y: {ctx.minChunkY} → {ctx.maxChunkY},  " +
                    $"Z: {ctx.minChunkZ} → {ctx.maxChunkZ}");

                float worldWidth  = ctx.worldMaxXZ.x - ctx.worldMinXZ.x;
                float worldDepth  = ctx.worldMaxXZ.y - ctx.worldMinXZ.y;

                EditorGUILayout.LabelField("World Extents (XZ):",
                    $"Min = ({ctx.worldMinXZ.x:F1}, {ctx.worldMinXZ.y:F1}), " +
                    $"Max = ({ctx.worldMaxXZ.x:F1}, {ctx.worldMaxXZ.y:F1})");

                EditorGUILayout.LabelField("World Height (meters):",
                    $"{ctx.minTerrainHeight:F1} → {ctx.maxTerrainHeight:F1}");

            }
            else
            {
                EditorGUILayout.LabelField("Computed Chunk Grid:", "Not available (bounds not computed).");
            }

            if (isGenerating)
            {
                EditorGUILayout.LabelField("Generation Status:", $"Running ({processedChunks}/{totalChunks})");
            }
            else
            {
                EditorGUILayout.LabelField("Generation Status:", "Idle");
            }
        }

        private bool CanGenerateWorld()
        {
            if (cachedWorldSettings == null) return false;
            if (!hasValidContext) return false;

            var ctx = cachedSdfContext;
            if (ctx.chunksX <= 0 || ctx.chunksZ <= 0) return false;

            return true;
        }

        // ------------------------------------------------------
        // Generation control
        // ------------------------------------------------------
        private void StartGeneration()
        {
            if (!CanGenerateWorld())
            {
                Debug.LogError("WorldGenerationWindow: Cannot generate – missing dependencies or invalid settings/bounds. See window warnings.");
                return;
            }

            // If already generating, stop first
            if (isGenerating)
            {
                StopGeneration();
            }

            var ctx = cachedSdfContext;

            minChunkX = ctx.minChunkX;
            maxChunkX = ctx.maxChunkX;
            minChunkZ = ctx.minChunkZ;
            maxChunkZ = ctx.maxChunkZ;

            int chunksX = maxChunkX - minChunkX + 1;
            int chunksZ = maxChunkZ - minChunkZ + 1;

            if (chunksX <= 0 || chunksZ <= 0)
            {
                Debug.LogError("WorldGenerationWindow: Computed chunk range is invalid.");
                return;
            }

            generationQueue = new Queue<ChunkCoord3>(chunksX * chunksZ);

            for (int z = minChunkZ; z <= maxChunkZ; z++)
            {
                for (int x = minChunkX; x <= maxChunkX; x++)
                {
                    for (int y = cachedSdfContext.minChunkY; y <= cachedSdfContext.maxChunkY; y++)
                        generationQueue.Enqueue(new ChunkCoord3(x, y, z));

                }
            }


            totalChunks = generationQueue.Count;
            processedChunks = 0;

            // Safety cap to avoid locking the editor with absurdly large worlds
            const int MaxChunksSafe = 4096;
            if (totalChunks > MaxChunksSafe)
            {
                Debug.LogError(
                    $"WorldGenerationWindow: Requested {totalChunks} chunks, which exceeds the safe editor limit of {MaxChunksSafe}. " +
                    "Reduce islandRadius, feature extents, voxelSize, or chunkSize."
                );

                generationQueue.Clear();
                generationQueue = null;
                totalChunks = 0;
                processedChunks = 0;
                return;
            }

            isGenerating = true;
            generationStartTime = EditorApplication.timeSinceStartup;

            // Ensure root exists
            cachedRoot = GetOrCreateRoot();

            // *** NEW: Force a refresh of the SDF context to pick up new seeds/settings ***
            var bootstrap = cachedRoot.GetComponent<VoxelTerraria.World.SDF.SdfBootstrap>();
            if (bootstrap != null)
            {
                bootstrap.Refresh();
            }


            Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
            Application.SetStackTraceLogType(LogType.Warning, StackTraceLogType.None);
            Application.SetStackTraceLogType(LogType.Error, StackTraceLogType.None);

            Debug.Log(
                $"WorldGenerationWindow: Starting generation of {totalChunks} chunks " +
                $"using {meshMode}. X: {minChunkX}→{maxChunkX}, Z: {minChunkZ}→{maxChunkZ}"
            );

            // Restore default Unity behavior
            Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.ScriptOnly);
            Application.SetStackTraceLogType(LogType.Warning, StackTraceLogType.ScriptOnly);
            Application.SetStackTraceLogType(LogType.Error, StackTraceLogType.ScriptOnly);
            
        }

        private void StopGeneration()
        {
            isGenerating = false;

            if (generationQueue != null)
            {
                generationQueue.Clear();
                generationQueue = null;
            }

            EditorUtility.ClearProgressBar();
            Repaint();

            Debug.Log("WorldGenerationWindow: Generation cancelled/stopped.");
        }

        private void FinishGeneration()
        {
            CreateOrUpdateWaterPlane(cachedWorldSettings);

            isGenerating = false;

            if (generationQueue != null)
            {
                generationQueue.Clear();
                generationQueue = null;
            }

            EditorUtility.ClearProgressBar();
            Repaint();

            double elapsed = EditorApplication.timeSinceStartup - generationStartTime;
            Debug.Log($"WorldGenerationWindow: Generation complete. {totalChunks} chunks in {elapsed:F2} seconds.");
        }
        private void CreateOrUpdateWaterPlane(WorldSettings settings)
{
    Transform root = GetOrCreateRoot();

    Transform existing = root.Find("WaterPlane");
    GameObject go;

    if (existing != null)
    {
        go = existing.gameObject;
    }
    else
    {
        go = GameObject.CreatePrimitive(PrimitiveType.Plane);
        go.name = "WaterPlane";
        go.transform.parent = root;

        var renderer = go.GetComponent<MeshRenderer>();
        // assign water material (lakebed placeholder is fine)
        renderer.sharedMaterial = TerrainMaterialLibrary.GetMaterials(6)[0]; 
    }

    float radius = settings.islandRadius;
    float planeScale = radius * 0.1f; // plane is 10x10 units → convert to radius scale

    go.transform.localScale = new Vector3(planeScale, 1f, planeScale);
    go.transform.position = new Vector3(0f, settings.seaLevel, 0f);
}

        // ------------------------------------------------------
        // Editor update loop (progressive generation)
        // ------------------------------------------------------
        private void OnEditorUpdate()
        {
            if (!isGenerating || generationQueue == null || generationQueue.Count == 0)
                return;

            if (cachedWorldSettings == null || !hasValidContext)
            {
                Debug.LogError("WorldGenerationWindow: Lost dependencies during generation. Stopping.");
                StopGeneration();
                return;
            }

            cachedRoot = GetOrCreateRoot();

            int steps = Mathf.Max(1, chunksPerStep);

            for (int i = 0; i < steps; i++)
            {
                if (generationQueue == null || generationQueue.Count == 0)
                    break;

                ChunkCoord3 coord = generationQueue.Dequeue();
                GenerateSingleChunk(coord);

                processedChunks++;

                float progress = (totalChunks > 0) ? (processedChunks / (float)totalChunks) : 0f;
                EditorUtility.DisplayProgressBar(
                    "World Generation",
                    $"Generating chunk {processedChunks}/{totalChunks} ( {coord.x}, {coord.y}, {coord.z} )",

                    progress
                );
            }

            if (generationQueue == null || generationQueue.Count == 0)
            {
                
                FinishGeneration();
            }
        }

        // ------------------------------------------------------
        // Per-chunk generation
        // ------------------------------------------------------
        private void GenerateSingleChunk(ChunkCoord3 coord)
        {
            
            WorldSettings settings = cachedWorldSettings;
            // Fast check: skip chunk if entire chunk is above terrain (air)
            // if (SdfRuntime.FastRejectChunk(coord, settings))
            //     return; // don’t allocate voxels, don’t mesh, skip entirely

            SdfContext ctx = cachedSdfContext;

            int chunkSize = settings.chunkSize;

            // Allocate temporary ChunkData (TempJob is fine in EditMode as long as we Dispose)
            ChunkData chunkData = new ChunkData(coord, chunkSize, Allocator.TempJob);

            try
            {
                // Fill voxel data from SDF + materials
                VoxelGenerator.GenerateChunkVoxels(ref chunkData, ctx, settings);

                MeshData meshData;

                switch (meshMode)
                {
                    case MeshMode.BlockyCubes:
                        meshData = BlockMesher.BuildMesh(in chunkData, settings);
                        break;

                    case MeshMode.MicroBlocks:
                        meshData = MicroBlocksMesher.BuildMesh(
                            in chunkData,
                            settings,
                            microBlockDiv
                        );
                        break;

                    case MeshMode.SmoothedBlocks:
                        meshData = SmoothedBlocksMesher.BuildMesh(in chunkData, settings);
                        break;

                    case MeshMode.HybridBlocks:
                        meshData = HybridBlocksMesher.BuildMesh(in chunkData, settings);
                        break;

                    case MeshMode.MarchingCubes:
                    default:
                        meshData = MarchingCubesMesher.BuildMesh(in chunkData, settings);
                        break;
                }

                Mesh mesh = meshData.ToMesh();

                // Create / replace GameObject in scene
                CreateOrReplaceChunkGO(coord, settings, mesh);
            }
            finally
            {
                // Ensure no NativeArray leaks
                chunkData.Dispose();
            }
        }

        // ------------------------------------------------------
        // Scene object management
        // ------------------------------------------------------
        private Transform GetOrCreateRoot()
        {
            if (cachedRoot != null)
                return cachedRoot;

            GameObject root = GameObject.Find(RootName);
            if (root == null)
            {
                root = new GameObject(RootName);
            }

            cachedRoot = root.transform;
            return cachedRoot;
        }

        private bool IsRootPresent()
        {
            if (cachedRoot != null) return true;
            GameObject root = GameObject.Find(RootName);
            return root != null;
        }

        private void ClearGeneratedWorld()
        {
            // If we're in the middle of generation, stop it first
            if (isGenerating)
            {
                StopGeneration();
            }

            GameObject root = GameObject.Find(RootName);
            if (root != null)
            {
                if (Application.isEditor)
                    DestroyImmediate(root);
                else
                    Destroy(root);

                cachedRoot = null;
                Debug.Log("WorldGenerationWindow: Cleared generated world.");
            }
        }

        private void CreateOrReplaceChunkGO(ChunkCoord3 coord, WorldSettings settings, Mesh mesh)
        {
            Transform root = GetOrCreateRoot();

            string chunkName = $"Chunk_{coord.x}_{coord.y}_{coord.z}";


            Transform existing = root.Find(chunkName);
            if (existing != null)
            {
                if (Application.isEditor)
                    DestroyImmediate(existing.gameObject);
                else
                    Destroy(existing.gameObject);
            }

            GameObject go = new GameObject(chunkName);
            go.transform.parent = root;
            go.transform.position = WorldCoordUtils.ChunkOriginWorld(coord, settings);
            go.transform.rotation = Quaternion.identity;

            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            var mc = go.AddComponent<MeshCollider>();

            // mf.sharedMesh = mesh;
            // mc.sharedMesh = mesh;

            // // Reuse a single material for all chunks
            // if (s_sharedChunkMaterial == null)
            // {
            //     Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            //     if (shader == null)
            //         shader = Shader.Find("Standard");

            //     if (shader != null)
            //     {
            //         s_sharedChunkMaterial = new Material(shader);
            //     }
            //     else
            //     {
            //         Debug.LogWarning("WorldGenerationWindow: Could not find a suitable shader. Assign a material manually.");
            //     }
            // }
            // //---------------------------------------------------
            // // Assign materials based on terrain material IDs
            // //---------------------------------------------------
            // mr.sharedMaterials = TerrainMaterialLibrary.GetMaterials(); 

            // // if (s_sharedChunkMaterial != null)
            // // {
            // //     mr.sharedMaterial = s_sharedChunkMaterial;
            // // }
            mf.sharedMesh = mesh;
mc.sharedMesh = mesh;

// If we have multiple submeshes, use terrain material array
if (mesh.subMeshCount > 1)
{
    mr.sharedMaterials = TerrainMaterialLibrary.GetMaterials(mesh.subMeshCount);
}
else
{
    // Fallback: reuse a single material for all chunks (legacy path)
    if (s_sharedChunkMaterial == null)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Standard");

        if (shader != null)
        {
            s_sharedChunkMaterial = new Material(shader);
        }
        else
        {
            Debug.LogWarning("WorldGenerationWindow: Could not find a suitable shader. Assign a material manually.");
        }
    }

    if (s_sharedChunkMaterial != null)
    {
        mr.sharedMaterial = s_sharedChunkMaterial;
    }
}

        }
    }
}
