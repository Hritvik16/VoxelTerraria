using Unity.Mathematics;

namespace VoxelTerraria.World
{
    /// <summary>
    /// Integer voxel coordinate inside a chunk.
    /// (x,y,z) range: 0..voxelResolution-1
    /// </summary>
    public struct VoxelCoord
    {
        public readonly int x;
        public readonly int y;
        public readonly int z;

        public VoxelCoord(int x, int y, int z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        // public int3 ToInt3() => new int3(x, y, z);

        public override string ToString() => $"({x},{y},{z})";
    }
}
