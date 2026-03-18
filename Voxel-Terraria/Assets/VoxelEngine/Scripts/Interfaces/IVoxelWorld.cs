using UnityEngine;

namespace VoxelEngine.Interfaces
{
    public interface IVoxelWorld
    {
        float VoxelScale { get; }
        
        // --- THE OBSERVER PATTERN ---
        // Other systems listen to these events; the engine doesn't care who is listening.
        event System.Action<Vector3Int, uint> OnVoxelChanged; 
        event System.Action<Vector3Int, Vector3Int> OnAreaDestroyed;

        void EditVoxel(Vector3Int globalVoxelPos, uint newMaterial, int brushSize = 0, int brushShape = 0);
        void DamageVoxel(Vector3Int globalVoxelPos, int damageAmount, int brushSize = 0, int brushShape = 0);
    }
}