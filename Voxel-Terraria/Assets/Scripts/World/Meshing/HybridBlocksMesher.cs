using Unity.Mathematics;
using VoxelTerraria.World;

namespace VoxelTerraria.World.Meshing
{
    /// <summary>
    /// HybridBlocks:
    /// - Also starts from MarchingCubes mesh
    /// - Stronger quantization for a chunkier, more voxel-like surface
    /// </summary>
    public static class HybridBlocksMesher
    {
        public static MeshData BuildMesh(in ChunkData chunkData, WorldSettings settings)
        {
            MeshData mesh = MarchingCubesMesher.BuildMesh(in chunkData, settings);

            float step = settings.voxelSize; // stronger snap to voxel grid

            for (int i = 0; i < mesh.vertices.Count; i++)
            {
                float3 p = mesh.vertices[i];
                p = math.round(p / step) * step;
                mesh.vertices[i] = p;
            }

            return mesh;
        }
    }
}
