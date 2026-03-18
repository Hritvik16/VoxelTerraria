using UnityEngine;

namespace VoxelEngine.Interfaces
{
    public interface IVoxelWorld
    {
        float VoxelScale { get; }
        void EditVoxel(Vector3Int globalVoxelPos, uint newMaterial, int brushSize = 0, int brushShape = 0);
        void DamageVoxel(Vector3Int globalVoxelPos, int damageAmount, int brushSize = 0, int brushShape = 0);
    }
}