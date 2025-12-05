using Unity.Mathematics;

namespace VoxelTerraria.World.SDF
{
    public struct SdfResult
    {
        public float distance;
        public ushort materialId;

        public SdfResult(float distance, ushort materialId)
        {
            this.distance = distance;
            this.materialId = materialId;
        }
    }
}
