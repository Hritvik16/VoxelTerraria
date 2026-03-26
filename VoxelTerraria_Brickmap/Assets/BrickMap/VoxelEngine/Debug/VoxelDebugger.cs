using UnityEngine;
using UnityEngine.InputSystem;

public class VoxelDebugger : MonoBehaviour
{
    public enum RenderMode { Standard = 0, RayStepsHeatmap = 1, DepthBuffer = 2, SurfaceNormals = 3, RawAlbedo = 4, LightingMask = 5 }
    
    [Header("Screen-Space Debug Modes (F1-F6)")]
    public RenderMode currentRenderMode = RenderMode.Standard;

    [Header("Wireframe Overlays (F7)")]
    public bool showLODWireframes = false;
    public Material wireframeMaterial;

    private Bounds infiniteBounds = new Bounds(Vector3.zero, Vector3.one * 1000000f);
    private bool showLegend = true; 

    void Update()
    {
        if (Keyboard.current != null)
        {
            if (Keyboard.current.f1Key.wasPressedThisFrame) currentRenderMode = RenderMode.Standard;
            if (Keyboard.current.f2Key.wasPressedThisFrame) currentRenderMode = RenderMode.RayStepsHeatmap;
            if (Keyboard.current.f3Key.wasPressedThisFrame) currentRenderMode = RenderMode.DepthBuffer;
            if (Keyboard.current.f4Key.wasPressedThisFrame) currentRenderMode = RenderMode.SurfaceNormals;
            if (Keyboard.current.f5Key.wasPressedThisFrame) currentRenderMode = RenderMode.RawAlbedo;
            if (Keyboard.current.f6Key.wasPressedThisFrame) currentRenderMode = RenderMode.LightingMask;
            
            if (Keyboard.current.f7Key.wasPressedThisFrame) showLODWireframes = !showLODWireframes;
            if (Keyboard.current.f8Key.wasPressedThisFrame) showLegend = !showLegend; 
        }

        Shader.SetGlobalInt("_DebugViewMode", (int)currentRenderMode);

        if (showLODWireframes && wireframeMaterial != null)
        {
            int chunkCount = Shader.GetGlobalInt("_ChunkCount");
            if (chunkCount > 0) Graphics.DrawProcedural(wireframeMaterial, infiniteBounds, MeshTopology.Lines, 24, chunkCount);
        }
    }

    private GUIStyle titleStyle;
    private GUIStyle textStyle;
    private int cachedH = -1;

    void OnGUI()
    {
        int w = Screen.width, h = Screen.height;

        // Only recreate styles if the screen height changes (resolution change)
        if (h != cachedH) {
            titleStyle = new GUIStyle { alignment = TextAnchor.UpperLeft, fontSize = Mathf.Max(24, h * 3 / 100) };
            titleStyle.normal.textColor = Color.yellow;
            textStyle = new GUIStyle { alignment = TextAnchor.UpperLeft, fontSize = Mathf.Max(16, h * 2 / 100), richText = true };
            textStyle.normal.textColor = Color.white;
            cachedH = h;
        }

        GUI.Label(new Rect(20, 60, w, h * 5 / 100), $"<b>Current View:</b> {currentRenderMode.ToString()}", titleStyle);
        GUI.Label(new Rect(20, 60 + (h * 4 / 100), w, h * 3 / 100), "[F8] Toggle Debug Legend", textStyle);

        if (showLegend)
        {
            Rect boxRect = new Rect(20, 100 + (h * 4 / 100), w * 0.38f, h * 0.24f);
            GUI.Box(boxRect, "");

            float startY = boxRect.y + 10, padding = h * 2.8f / 100, leftX = boxRect.x + 10;

            GUI.Label(new Rect(leftX, startY, w, padding), "<b>[F1] Standard:</b> Normal ACES Render", textStyle);
            GUI.Label(new Rect(leftX, startY + padding*1, w, padding), "<b>[F2] Heatmap:</b> Blue = Fast, Red = 150+ steps", textStyle);
            GUI.Label(new Rect(leftX, startY + padding*2, w, padding), "<b>[F3] Depth:</b> White = Near, Black = Far", textStyle);
            GUI.Label(new Rect(leftX, startY + padding*3, w, padding), "<b>[F4] Normals:</b> RGB = XYZ Directions", textStyle);
            GUI.Label(new Rect(leftX, startY + padding*4, w, padding), "<b>[F5] Albedo:</b> Raw Block Textures (Unlit)", textStyle);
            GUI.Label(new Rect(leftX, startY + padding*5, w, padding), "<b>[F6] Light Mask:</b> Voxel Illumination Values", textStyle);
            
            string wireStatus = showLODWireframes ? "<color=#00FF00>ON</color>" : "<color=#FF4444>OFF</color>";
            GUI.Label(new Rect(leftX, startY + padding*6, w, padding), $"<b>[F7] LOD Bounds:</b> {wireStatus}", textStyle);
        }
    }
}