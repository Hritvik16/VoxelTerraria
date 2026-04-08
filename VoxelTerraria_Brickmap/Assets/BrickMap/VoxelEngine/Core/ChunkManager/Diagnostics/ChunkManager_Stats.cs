using UnityEngine;
using System.Text;
using Unity.Collections;
using System.Collections.Generic;
using VoxelEngine.World;

public partial class ChunkManager : MonoBehaviour
{
    public void ReportMemoryUsage()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("<color=#7CFC00><b>[VOXEL ENGINE MEMORY DIAGNOSTICS]</b></color>");
        sb.AppendLine("<color=#AAAAAA>Evaluating current Voxel Architecture: " + currentArchitecture + " (" + UINTS_PER_CHUNK + " uints/chunk)</color>");
        sb.AppendLine("--------------------------------------------------");

        long totalVRAM = 0;
        long totalSystemRAM = 0;

        // --- VRAM BREAKDOWN ---
        sb.AppendLine("<b>--- GPU VRAM (Compute & Graphics Buffers) ---</b>");
        
        void LogBuffer(string name, ComputeBuffer cb)
        {
            if (cb == null) return;
            long size = (long)cb.count * cb.stride;
            totalVRAM += size;
            sb.AppendLine($"{name}: <color=#00BFFF>{FormatBytes(size)}</color>");
        }

        void LogGraphicsBuffer(string name, GraphicsBuffer gb)
        {
            if (gb == null) return;
            long size = (long)gb.count * gb.stride;
            totalVRAM += size;
            sb.AppendLine($"{name}: <color=#00BFFF>{FormatBytes(size)}</color>");
        }

        // Master Pools
        LogBuffer("Chunk Map Buffer", chunkMapBuffer);
        LogBuffer("Macro Mask Pool", macroMaskPoolBuffer);
        LogBuffer("Dense Chunk Pool", denseChunkPoolBuffer);
        LogBuffer("Material Chunk Pool", materialChunkPoolBuffer);
        LogBuffer("Surface Mask Pool", surfaceMaskPoolBuffer);
        LogBuffer("Surface Prefix Pool", surfacePrefixPoolBuffer);
        LogBuffer("Macro Grid Buffer", macroGridBuffer);
        
        // Couriers & Uploads
        for (int i = 0; i < 2; i++)
        {
            LogBuffer($"Upload: Chunk Array [{i}]", tempChunkUploadBuffers[i]);
            LogBuffer($"Upload: Job Queue [{i}]", jobQueueBuffers[i]);
            LogBuffer($"Upload: Mask Array [{i}]", tempMaskUploadBuffers[i]);
            LogBuffer($"Upload: Material Array [{i}]", tempMaterialUploadBuffers[i]);
            LogBuffer($"Upload: Surface Array [{i}]", tempSurfaceUploadBuffers[i]);
            LogBuffer($"Upload: Prefix Array [{i}]", tempPrefixUploadBuffers[i]);
        }

        // Utilities
        LogBuffer("Biome Anchor Buffer", biomeAnchorBuffer);
        LogBuffer("Chunk Height Buffer", chunkHeightBuffer);
        LogBuffer("Material Palette Buffer", materialBuffer);
        LogBuffer("Crosshair Buffer", crosshairBuffer);
        
        // Rendering
        LogGraphicsBuffer("Vertex Buffer (Indirect)", vertexBuffer);
        LogGraphicsBuffer("Args Buffer (Indirect)", argsBuffer);

        // External (WorldManager)
        if (VoxelEngine.WorldManager.Instance != null)
        {
            LogBuffer("Blueprint: Feature Buffer", VoxelEngine.WorldManager.Instance.featureBuffer);
            LogBuffer("Blueprint: Cavern Buffer", VoxelEngine.WorldManager.Instance.cavernBuffer);
            LogBuffer("Blueprint: Tunnel Buffer", VoxelEngine.WorldManager.Instance.tunnelBuffer);
        }

        sb.AppendLine("--------------------------------------------------");

        // --- SYSTEM RAM BREAKDOWN ---
        sb.AppendLine("<b>--- SYSTEM RAM (NativeArrays & Managed Pools) ---</b>");

        // NativeArray sizes
        void LogNative<T>(string name, NativeArray<T> arr, int elementSize) where T : struct
        {
            if (!arr.IsCreated) return;
            long size = (long)arr.Length * elementSize;
            totalSystemRAM += size;
            sb.AppendLine($"{name}: <color=#FFA500>{FormatBytes(size)}</color>");
        }

        LogNative("Burst: Dense Chunk Pool", cpuDenseChunkPool, 4);
        LogNative("Burst: Macro Mask Pool", cpuMacroMaskPool, 4);
        LogNative("Burst: Material Pool", cpuMaterialChunkPool, 4);
        LogNative("Burst: Surface Mask Pool", cpuSurfaceMaskPool, 4);
        LogNative("Burst: Surface Prefix Pool", cpuSurfacePrefixPool, 4);
        LogNative("Burst: Shadow RAM (Ground Truth)", cpuShadowRAMPool, 4);
        LogNative("Burst: Chunk Height Cache", cpuChunkHeights, 4);
        LogNative("Burst: Biome Cache", cpuBiomes, 20); // 12 + 4 + 4
        
        LogNative("Courier: Chunk Upload", nativeChunkUpload, 4);
        LogNative("Courier: Mask Upload", nativeMaskUpload, 4);
        LogNative("Courier: Material Upload", nativeMaterialUpload, 4);
        LogNative("Courier: Surface Upload", nativeSurfaceUpload, 4);
        LogNative("Courier: Prefix Upload", nativePrefixUpload, 4);

        LogNative("Burst State: Jobs Array", persistentJobDataArray, 48);
        LogNative("Burst State: Feature Array", persistentFeatureArray, 32);
        LogNative("Burst State: Cavern Array", persistentCavernArray, 32);
        LogNative("Burst State: Tunnel Array", persistentTunnelArray, 32);

        // Managed Arrays
        if (chunkMapArray != null)
        {
            long size = (long)chunkMapArray.Length * 8; // ChunkData
            totalSystemRAM += size;
            sb.AppendLine($"Managed: Master Chunk Map: {FormatBytes(size)}");
        }
        if (chunkTargetCoordArray != null)
        {
            long size = (long)chunkTargetCoordArray.Length * 12; // Vector3Int
            totalSystemRAM += size;
            sb.AppendLine($"Managed: Target Coordinate Cache: {FormatBytes(size)}");
        }

        // Vault Usage
        if (modifiedChunks != null)
        {
            // Per CachedChunk: shape(4k) + mask(76) + mat(16k) + surface(4k) + prefix(4k) + shadow(32k) = ~62kB
            long vaultSize = modifiedChunks.Count * 61520L;
            totalSystemRAM += vaultSize;
            sb.AppendLine($"RAM Vault: Modified Chunks ({modifiedChunks.Count}): <color=#FFA500>{FormatBytes(vaultSize)}</color>");
        }

        sb.AppendLine("--------------------------------------------------");
        sb.AppendLine($"<b>TOTAL VRAM: <color=#00FFFF>{FormatBytes(totalVRAM)}</color></b>");
        sb.AppendLine($"<b>TOTAL SYSTEM RAM: <color=#FFFF00>{FormatBytes(totalSystemRAM)}</color></b>");
        sb.AppendLine($"<b>GRAND TOTAL CONSUMED: <color=#FFFFFF>{FormatBytes(totalVRAM + totalSystemRAM)}</color></b>");
        sb.AppendLine("--------------------------------------------------");

        Debug.Log(sb.ToString());
    }

    private string FormatBytes(long bytes)
    {
        double mb = bytes / (1024.0 * 1024.0);
        if (mb >= 1024.0)
            return $"{(mb / 1024.0):F2} GB";
        return $"{mb:F2} MB";
    }
}
