using UnityEngine;
using UnityEditor;
using Unity.Mathematics;

public class VoxelPreviewWindow : EditorWindow
{
    enum ViewMode { DensityXZ, DensityXY, BiomeXZ, BiomeXY, MountainHeatmapXZ, 
    LakeHeatmapXZ,
    CityHeatmapXZ,
    CombinedSdfXZ }

    ViewMode mode = ViewMode.DensityXZ;

    float sliceHeight = 0f;      // Y for XZ slice, Z for XY slice
    int resolution = 128;        // user adjustable

    Texture2D previewTex;

    // cached access
    static SdfContext? cachedCtx = null;
    static WorldSettings cachedWorld;

    [MenuItem("Window/World/Voxel Preview")]
    public static void ShowWindow()
    {
        GetWindow<VoxelPreviewWindow>("Voxel Preview");
    }

    void OnGUI()
    {
        GUILayout.Label("Voxel Preview", EditorStyles.boldLabel);

        mode = (ViewMode)EditorGUILayout.EnumPopup("Mode", mode);

        sliceHeight = EditorGUILayout.FloatField("Slice", sliceHeight);
        resolution = EditorGUILayout.IntSlider("Resolution", resolution, 32, 512);

        if (GUILayout.Button("Generate Slice"))
        {
            GenerateSlice();
        }

        if (previewTex != null)
        {
            GUILayout.Space(10);
            Rect rect = GUILayoutUtility.GetAspectRect(1f);
            GUI.DrawTexture(rect, previewTex, ScaleMode.StretchToFill);
        }
    }

    // --------------------------------------------------------------------
    // Generate Slice
    // --------------------------------------------------------------------
    void GenerateSlice()
    {
        // Ensure SDF Context exists (get it from an SdfBootstrap in scene)
        EnsureContext();

        if (cachedCtx == null)
        {
            Debug.LogError("VoxelPreviewWindow: No SdfContext found. Add an SdfBootstrap to your scene.");
            return;
        }

        var ctx = cachedCtx.Value;
        // Debug.Log($"CTX: radius={ctx.islandRadius}, height={ctx.maxBaseHeight}");
        // Debug.Log($"Mountains: {ctx.mountains.Length}");
        // Debug.Log($"Cities: {ctx.cities.Length}");
        // if (ctx.cities.Length > 0)
            // Debug.Log($"City Center = {ctx.cities[0].centerXZ}");


        if (previewTex == null || previewTex.width != resolution)
        {
            previewTex = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false);
        }

        float w = cachedWorld.worldWidth;
        float d = cachedWorld.worldDepth;

        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float u = (float)x / (resolution - 1);
                float v = (float)y / (resolution - 1);

                float3 p = float3.zero;

                // -----------------------------------------------------------
                // Mapping world coords based on view mode
                // -----------------------------------------------------------
                switch (mode)
                {
                    case ViewMode.DensityXZ:
                    case ViewMode.BiomeXZ:
                    case ViewMode.CombinedSdfXZ:
                        p = new float3(
                            u * w - w * 0.5f,
                            sliceHeight,
                            v * d - d * 0.5f
                        );
                        break;

                    case ViewMode.DensityXY:
                    case ViewMode.BiomeXY:
                        p = new float3(
                            u * w - w * 0.5f,
                            v * cachedWorld.worldHeight,
                            sliceHeight
                        );
                        break;
                    case ViewMode.MountainHeatmapXZ:
                        p = new float3(
                            u * w - w * 0.5f,
                            sliceHeight,
                            v * d - d * 0.5f
                        );
                        break;
                    case ViewMode.LakeHeatmapXZ:
                    case ViewMode.CityHeatmapXZ:
                        p = new float3(
                            u * w - w * 0.5f,
                            sliceHeight,
                            v * d - d * 0.5f
                        );
                        break;

                }

                // -----------------------------------------------------------
                // Evaluate SDF + Biome
                // -----------------------------------------------------------
                float sdf = CombinedTerrainSdf.Evaluate(p, ctx);
                float density = -sdf; // inside = positive

                Color c = Color.magenta;

                if (mode == ViewMode.MountainHeatmapXZ)
                {
                    float mountainSdf = MountainSdf.Evaluate(p, ctx);

                    // Positive = above mountain, Negative = inside mountain
                    // So let’s transform it into a smooth heatmap:

                    float h = math.saturate(-mountainSdf / 100f);  
                    // if mountain height ~120, dividing by ~100 gives nice contrast

                    // Heatmap gradient: black → green → yellow → red
                    if (h < 0.33f)
                        c = Color.Lerp(Color.black, Color.green, h * 3f);
                    else if (h < 0.66f)
                        c = Color.Lerp(Color.green, Color.yellow, (h - 0.33f) * 3f);
                    else
                        c = Color.Lerp(Color.yellow, Color.red, (h - 0.66f) * 3f);

                    previewTex.SetPixel(x, y, c);
                    continue;
                }
                if (mode == ViewMode.LakeHeatmapXZ)
                {
                    float lakeSdf = LakeSdf.Evaluate(p, ctx);

                    if (x == resolution/2 && y == resolution/2)
                        Debug.Log($"LAKE DEBUG: center lakeSdf={lakeSdf}, p={p}");
                    // float lakeSdf = LakeSdf.Evaluate(p, ctx);

                    // Inside lake = negative
                    // float depth = math.saturate(-lakeSdf / 20f);
                    float depth = math.saturate(-lakeSdf / 5f);

                    // Gradient: black → cyan → blue → deep blue
                    if (depth <= 0f)
                        c = Color.black;
                    else
                        c = Color.Lerp(Color.cyan, Color.blue, depth);

                    previewTex.SetPixel(x, y, c);
                    continue;
                }

                if (mode == ViewMode.CityHeatmapXZ)
                {
                    // Simple 2D footprint: just show where the city plateau lives in XZ
                    c = Color.black;

                    if (ctx.cities.IsCreated && ctx.cities.Length > 0)
                    {
                        float maxMask = 0f;

                        for (int ci = 0; ci < ctx.cities.Length; ci++)
                        {
                            var city = ctx.cities[ci];

                            float2 localXZ = p.xz - city.centerXZ;
                            float dist = math.length(localXZ);

                            // 1 at center, 0 at radius and beyond
                            float mask = math.saturate(1f - dist / city.radius);

                            maxMask = math.max(maxMask, mask);
                        }

                        if (maxMask > 0f)
                        {
                            // Yellow at edge → red at center
                            c = Color.Lerp(Color.yellow, Color.red, maxMask);
                        }
                    }

                    previewTex.SetPixel(x, y, c);
                    continue;
                }

                // if (mode == ViewMode.CombinedSdfXZ)
                // {
                //     float f = CombinedTerrainSdf.Evaluate(p, ctx);

                //     float norm = math.saturate(( -f ) / 50f); 
                //     // f < 0 = solid → white
                //     // f > 0 = air → black

                //     c = new Color(norm, norm, norm);

                //     previewTex.SetPixel(x, y, c);
                //     continue;
                // }

                // if (mode == ViewMode.CombinedSdfXZ)
                // {
                //     float f = CombinedTerrainSdf.Evaluate(p, ctx);

                //     if (f < 0)
                //         c = Color.Lerp(Color.white, Color.red, math.saturate(-f / 50f));   // DEEP solid
                //     else
                //         c = Color.Lerp(Color.black, Color.blue, math.saturate(f / 50f));  // air above terrain

                //     previewTex.SetPixel(x, y, c);
                //     continue;
                // }
                if (mode == ViewMode.CombinedSdfXZ)
                {
                    float f = CombinedTerrainSdf.Evaluate(p, ctx);

                    // 3-color heatmap:
                    // blue = air (positive f)
                    // yellow = near surface
                    // red = deep inside terrain

                    float t = math.clamp(f / 80f, -1f, 1f);

                    if (t > 0)
                    {
                        // air: blue → black
                        c = Color.Lerp(Color.blue, Color.black, t);
                    }
                    else
                    {
                        // inside terrain: red → yellow → white
                        float i = -t;
                        if (i < 0.33f)
                            c = Color.Lerp(Color.yellow, Color.red, i * 3f);
                        else if (i < 0.66f)
                            c = Color.Lerp(Color.red, Color.magenta, (i - 0.33f) * 3f);
                        else
                            c = Color.Lerp(Color.magenta, Color.white, (i - 0.66f) * 3f);
                    }

                    previewTex.SetPixel(x, y, c);
                    continue;
                }



                if (mode == ViewMode.DensityXZ || mode == ViewMode.DensityXY)
                {
                    // black = air, white = solid
                    float val = math.saturate(density * 0.1f);
                    c = new Color(val, val, val);
                }
                else  // biome modes
                {
                    var weights = BiomeEvaluator.EvaluateBiomeWeights(p, ctx);

                    // choose dominant biome
                    float maxW = math.max(math.max(math.max(math.max(
                        weights.grass, weights.forest), weights.mountain),
                        weights.lakeshore), weights.city);

                    if (maxW == weights.grass)      c = GrassColor;
                    else if (maxW == weights.forest)    c = ForestColor;
                    else if (maxW == weights.mountain)  c = MountainColor;
                    else if (maxW == weights.lakeshore) c = LakeShoreColor;
                    else if (maxW == weights.city)      c = CityColor;
                }

                previewTex.SetPixel(x, y, c);
            }
        }

        previewTex.Apply();
    }

    // --------------------------------------------------------------------
    // Load context from scene’s SdfBootstrap
    // --------------------------------------------------------------------
    void EnsureContext()
    {
        var bootstrap = FindFirstObjectByType<SdfBootstrap>();
        if (bootstrap == null)
        {
            Debug.LogWarning("VoxelPreviewWindow: No SdfBootstrap in scene.");
            return;
        }

        cachedCtx = SdfRuntime.Context;
        cachedWorld = bootstrap.worldSettings;
    }

    // Biome debug colors
    static readonly Color GrassColor     = new Color(0.2f, 0.8f, 0.2f);
    static readonly Color ForestColor    = new Color(0.1f, 0.5f, 0.1f);
    static readonly Color MountainColor  = new Color(0.5f, 0.5f, 0.5f);
    static readonly Color LakeShoreColor = new Color(0.9f, 0.8f, 0.5f);
    static readonly Color CityColor      = new Color(0.7f, 0.2f, 0.2f);
}
