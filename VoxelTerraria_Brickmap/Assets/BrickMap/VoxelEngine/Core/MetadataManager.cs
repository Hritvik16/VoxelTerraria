using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Entities;

namespace VoxelEngine
{
    public class MetadataManager
    {
        public static MetadataManager Instance { get; private set; }
        private Dictionary<Vector3Int, VoxelEntity> entityGrid = new Dictionary<Vector3Int, VoxelEntity>();

        // This Unity attribute forces this method to run automatically when the game starts.
        // No GameObjects required. Perfect for modular drop-in systems.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoBootstrapper()
        {
            Instance = new MetadataManager();
            
            // Wire up the observer pattern securely
            if (ChunkManager.World != null)
            {
                ChunkManager.World.OnVoxelChanged += Instance.HandleVoxelChanged;
                ChunkManager.World.OnAreaDestroyed += Instance.ProcessAoEDestruction;
                Debug.Log("[MetadataManager] Successfully Booted and Subscribed to IVoxelWorld events.");
            }
        }

        // --- EVENT HANDLERS ---
        private void HandleVoxelChanged(Vector3Int position, uint newMaterial)
        {
            if (newMaterial == 3) {
                RegisterEntity(position, new ChestEntity(position));
            } else if (newMaterial == 0) {
                UnregisterEntity(position);
            }
        }

        public void ProcessAoEDestruction(Vector3Int minGlobal, Vector3Int maxGlobal)
        {
            List<Vector3Int> toDestroy = new List<Vector3Int>();
            foreach (var kvp in entityGrid)
            {
                Vector3Int pos = kvp.Key;
                if (pos.x >= minGlobal.x && pos.x <= maxGlobal.x &&
                    pos.y >= minGlobal.y && pos.y <= maxGlobal.y &&
                    pos.z >= minGlobal.z && pos.z <= maxGlobal.z)
                {
                    toDestroy.Add(pos);
                }
            }
            foreach (var pos in toDestroy) UnregisterEntity(pos);
        }

        // --- DICTIONARY API ---
        public void RegisterEntity(Vector3Int position, VoxelEntity entity)
        {
            if (!entityGrid.ContainsKey(position))
            {
                entityGrid.Add(position, entity);
                Debug.Log($"[MetadataManager] Entity registered at {position}");
            }
        }

        public void UnregisterEntity(Vector3Int position)
        {
            if (entityGrid.TryGetValue(position, out VoxelEntity entity))
            {
                entity.OnDestroyed();
                entityGrid.Remove(position);
            }
        }

        public bool TryGetEntity(Vector3Int position, out VoxelEntity entity)
        {
            return entityGrid.TryGetValue(position, out entity);
        }
    }
}