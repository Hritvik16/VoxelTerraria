using UnityEngine;
using UnityEngine.UI;

namespace VoxelEngine.World
{
    [ExecuteAlways]
    public class MinimapGenerator : MonoBehaviour
    {
        [Header("References")]
        public ComputeShader minimapShader;
        public RawImage displayImage; 
        
        [Header("Settings")]
        public int resolution = 512;
        [Range(0.1f, 5f)] public float zoom = 1.0f;
        public Vector2 panOffset = Vector2.zero;

        private RenderTexture renderTexture;
        private int lastSeed;

        void OnEnable()
        {
            InitializeTexture();
        }

        void InitializeTexture()
        {
            if (renderTexture != null) renderTexture.Release();
            
            renderTexture = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.ARGB32);
            renderTexture.enableRandomWrite = true;
            renderTexture.filterMode = FilterMode.Bilinear;
            renderTexture.Create();
            
            if (displayImage != null) displayImage.texture = renderTexture;
        }

        void Update()
        {
            if (minimapShader == null || renderTexture == null || displayImage == null) return;
            if (WorldManager.Instance == null) return;

            // Auto-update if the developer seed changes in the Editor
            if (WorldManager.Instance.developerSeed != lastSeed)
            {
                WorldManager.Instance.ApplySettingsToGPU();
                lastSeed = WorldManager.Instance.developerSeed;
            }

            Generate(); 
        }

        public void Generate()
        {
            int kernel = minimapShader.FindKernel("GenerateMinimap");
            minimapShader.SetTexture(kernel, "Result", renderTexture);
            minimapShader.SetFloat("_Zoom", zoom);
            minimapShader.SetVector("_Offset", panOffset);
            
            // Dispatches 8x8 thread groups
            minimapShader.Dispatch(kernel, Mathf.CeilToInt(resolution / 8f), Mathf.CeilToInt(resolution / 8f), 1);
        }

        private void OnDisable()
        {
            if (renderTexture != null) renderTexture.Release();
        }
    }
}