using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using UnityEngine.Experimental.Rendering;

public class RayTracingRenderFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class RayTracingSettings {
        public ComputeShader computeShader;
        public Shader blitShader; 
        [Range(1, 4)] public int downsampleFactor = 2;
    }

    public RayTracingSettings settings = new RayTracingSettings();
    private RayTracingPass rayTracingPass;
    private Material blitMaterial;

    public override void Create() {
        if (settings.blitShader != null) blitMaterial = CoreUtils.CreateEngineMaterial(settings.blitShader);
        rayTracingPass = new RayTracingPass(settings.computeShader, blitMaterial, settings.downsampleFactor);
        rayTracingPass.renderPassEvent = RenderPassEvent.BeforeRenderingOpaques; 
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData) {
        if (settings.computeShader == null || blitMaterial == null) return;
        renderer.EnqueuePass(rayTracingPass);
    }

    protected override void Dispose(bool disposing) {
        CoreUtils.Destroy(blitMaterial);
    }
}

class RayTracingPass : ScriptableRenderPass
{
    private ComputeShader computeShader;
    private Material blitMaterial;
    private int downsample;

    private class PassData {
        public ComputeShader computeShader;
        public Material blitMaterial;
        public Camera camera;
        public TextureHandle computeOutput;
        public TextureHandle cameraColorTarget;
        public TextureHandle cameraDepthTarget;
        public int dispatchWidth, dispatchHeight;
    }

    public RayTracingPass(ComputeShader shader, Material material, int downsampleFactor) {
        this.computeShader = shader; this.blitMaterial = material; this.downsample = downsampleFactor;
        ConfigureInput(ScriptableRenderPassInput.Color | ScriptableRenderPassInput.Depth); 
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData) {
        UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
        UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
        if (cameraData.cameraType != CameraType.Game) return;

        int rw = cameraData.cameraTargetDescriptor.width / downsample;
        int rh = cameraData.cameraTargetDescriptor.height / downsample;

        TextureDesc texDesc = new TextureDesc(rw, rh);
        texDesc.format = GraphicsFormat.R16G16B16A16_SFloat;
        texDesc.enableRandomWrite = true;
        texDesc.filterMode = FilterMode.Bilinear; // Standard sharp pixels
        
        TextureHandle computeOutput = renderGraph.CreateTexture(texDesc);

        using (var builder = renderGraph.AddUnsafePass<PassData>("VoxelBaseRender", out var passData)) {
            passData.computeShader = computeShader;
            passData.blitMaterial = blitMaterial;
            passData.camera = cameraData.camera;
            passData.computeOutput = computeOutput;
            passData.cameraColorTarget = resourceData.cameraColor;
            passData.cameraDepthTarget = resourceData.cameraDepth;
            passData.dispatchWidth = rw;
            passData.dispatchHeight = rh;

            builder.UseTexture(computeOutput, AccessFlags.Write);
            builder.UseTexture(passData.cameraColorTarget, AccessFlags.Write);
            builder.UseTexture(passData.cameraDepthTarget, AccessFlags.Write);

            builder.SetRenderFunc((PassData data, UnsafeGraphContext context) => {
                CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                int kernel = data.computeShader.FindKernel("CSMain");

                if (ChunkManager.Instance != null) ChunkManager.Instance.BindPhysicsData(data.computeShader, kernel);

                cmd.SetComputeTextureParam(data.computeShader, kernel, "Result", data.computeOutput);
                cmd.SetComputeMatrixParam(data.computeShader, "_CameraToWorld", data.camera.cameraToWorldMatrix);
                cmd.SetComputeMatrixParam(data.computeShader, "_CameraInverseProjection", data.camera.projectionMatrix.inverse);
                cmd.SetComputeVectorParam(data.computeShader, "_CameraForward", data.camera.transform.forward);
                
                Vector4 sunDir = new Vector4(0.5f, 1f, 0.2f, 0f).normalized; 
                cmd.SetComputeVectorParam(data.computeShader, "_SunDir", sunDir);
                cmd.SetComputeIntParam(data.computeShader, "_DebugViewMode", Shader.GetGlobalInt("_DebugViewMode"));

                cmd.DispatchCompute(data.computeShader, kernel, Mathf.CeilToInt(data.dispatchWidth / 8.0f), Mathf.CeilToInt(data.dispatchHeight / 8.0f), 1);

                data.blitMaterial.SetVector("_SunDir", sunDir);
                CoreUtils.SetRenderTarget(cmd, data.cameraColorTarget, data.cameraDepthTarget);
                data.blitMaterial.SetTexture("_VoxelData", data.computeOutput);
                cmd.DrawProcedural(Matrix4x4.identity, data.blitMaterial, 0, MeshTopology.Triangles, 3, 1);
            });
        }
    }
}