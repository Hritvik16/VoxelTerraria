using UnityEngine;

namespace VoxelTerraria.World
{
    /// <summary>
    /// 3D chunk coordinate.
    /// </summary>
    [System.Serializable]
    public struct ChunkCoord3
    {
        public int x, y, z;

        public ChunkCoord3(int x, int y, int z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public override string ToString() => $"({x},{y},{z})";
    }

    /// <summary>
    /// TEMPORARY COMPATIBILITY:
    /// Treat old ChunkCoord as "XZ-only chunk" at Y = 0.
    /// </summary>
    [System.Serializable]
    public struct ChunkCoord
    {
        public int x, z;
        public ChunkCoord(int x, int z)
        {
            this.x = x;
            this.z = z;
        }

        public ChunkCoord3 As3() => new ChunkCoord3(x, 0, z);
        public override string ToString() => $"({x},{z})";
    }
}
