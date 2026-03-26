using UnityEngine;
using UnityEngine.UI;

namespace VoxelEngine
{
    public class MinimapGenerator : MonoBehaviour
    {
        public ComputeShader minimapShader;
        public RawImage minimapDisplay;
        
        [Header("Controls")]
        public Vector2 offset = Vector2.zero;

        private RenderTexture renderTexture;
        private int currentWidth = 0;
        private int currentHeight = 0;

        void Update()
        {
            if (minimapShader == null || minimapDisplay == null || WorldManager.Instance == null || WorldManager.Instance.featureBuffer == null) return;

            // 1. Auto-Resolution: Dynamically scale to the UI Rect bounds
            RectTransform rt = minimapDisplay.rectTransform;
            int targetWidth = Mathf.Max(256, Mathf.RoundToInt(rt.rect.width));
            int targetHeight = Mathf.Max(256, Mathf.RoundToInt(rt.rect.height));

            // Rebuild texture only if the UI canvas resizes
            if (renderTexture == null || currentWidth != targetWidth || currentHeight != targetHeight)
            {
                if (renderTexture != null) renderTexture.Release();
                
                currentWidth = targetWidth;
                currentHeight = targetHeight;
                
                renderTexture = new RenderTexture(currentWidth, currentHeight, 0, RenderTextureFormat.ARGB32);
                renderTexture.enableRandomWrite = true;
                renderTexture.Create();
                minimapDisplay.texture = renderTexture;
            }

            // 2. Auto-Zoom: Pad the World Radius by 10% to fit the screen
            float autoZoom = WorldManager.Instance.WorldRadiusXZ * 1.1f;

            int kernel = minimapShader.FindKernel("GenerateMinimap");
            minimapShader.SetTexture(kernel, "Result", renderTexture);
            minimapShader.SetFloat("_Zoom", autoZoom);
            minimapShader.SetVector("_Offset", offset);
            minimapShader.SetFloat("_WorldRadiusXZ", WorldManager.Instance.WorldRadiusXZ);

            minimapShader.SetBuffer(kernel, "_FeatureAnchorBuffer", WorldManager.Instance.featureBuffer);
            minimapShader.SetInt("_FeatureCount", WorldManager.Instance.mapFeatures.Count);

            minimapShader.Dispatch(kernel, Mathf.CeilToInt(currentWidth / 8f), Mathf.CeilToInt(currentHeight / 8f), 1);
        }

        void OnDestroy()
        {
            if (renderTexture != null) renderTexture.Release();
        }
    }
}