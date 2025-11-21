using Unity.Mathematics;
using VoxelTerraria.World;

namespace VoxelTerraria.World.Meshing
{
    /// <summary>
    /// SmoothedBlocks:
    /// - Starts from MarchingCubes mesh
    /// - Quantizes vertices to a sub-voxel grid (slight blockiness, mostly smooth)
    /// </summary>
    public static class SmoothedBlocksMesher
    {
        public static MeshData BuildMesh(in ChunkData chunkData, WorldSettings settings)
        {
            MeshData mesh = MarchingCubesMesher.BuildMesh(in chunkData, settings);

            float step = settings.voxelSize * 0.5f; // small quantization

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
