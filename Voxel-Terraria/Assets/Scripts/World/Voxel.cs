using System;
using Unity.Mathematics;

namespace VoxelTerraria.World
{
    [Serializable]
    public struct Voxel
    {
        // Signed. >0 = solid, <0 = empty (SDF-compatible convention).
        public short density;

        // Material index (0 = default, others defined by your material table).
        public ushort materialId;

        public Voxel(short density, ushort materialId)
        {
            this.density = density;
            this.materialId = materialId;
        }
    }
}
