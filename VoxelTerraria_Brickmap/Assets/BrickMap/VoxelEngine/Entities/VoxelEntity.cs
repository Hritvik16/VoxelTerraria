using UnityEngine;

namespace VoxelEngine.Entities
{
    // The base blueprint for any complex block (Chests, Doors, Signs, Machines)
    public abstract class VoxelEntity
    {
        public Vector3Int GlobalPosition { get; private set; }

        public VoxelEntity(Vector3Int position)
        {
            GlobalPosition = position;
        }

        // Fired when the player presses the Interact key ('E') while looking at this block
        public abstract void OnInteract();
        
        // Fired when the block is mined or blown up
        public abstract void OnDestroyed();

        // NEW: Handles continuous mining damage from the player
        public int Health { get; protected set; } = 100;

        public virtual void OnDamaged(int damageAmount)
        {
            Health -= damageAmount;
            
            if (Health <= 0)
            {
                // Tell the GPU to instantly clear the visual block.
                // Because we built the Observer Pattern, this will automatically fire OnVoxelChanged(0), 
                // which tells the MetadataManager to unregister and destroy this entity!
                ChunkManager.World.EditVoxel(GlobalPosition, 0); 
            }
        }
    }

    // --- TEST IMPLEMENTATION ---
    // We will use this to test the system before building full UIs
    public class ChestEntity : VoxelEntity
    {
        public ChestEntity(Vector3Int position) : base(position) {}

        public override void OnInteract()
        {
            Debug.Log($"[CPU SPATIAL HASH] Opened chest at {GlobalPosition}! (UI would open here)");
        }

        public override void OnDestroyed()
        {
            Debug.Log($"[CPU SPATIAL HASH] Chest at {GlobalPosition} was destroyed. Dropping items...");
        }
    }
}