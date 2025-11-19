using UnityEngine;

namespace VoxelTerraria.World
{
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    [RequireComponent(typeof(MeshCollider))]
    public class VoxelChunkView : MonoBehaviour
    {
        [Header("Chunk Coordinates")]
        public ChunkCoord coord;

        // Optional runtime reference for debugging
        public ChunkData? chunkDataReference;

        // Cached components
        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;
        private MeshCollider meshCollider;

        // -------------------------------------------------------------
        // Ensure all components exist (Awake does not always run in Editor)
        // -------------------------------------------------------------
        private void EnsureComponents()
        {
            if (!meshFilter)  meshFilter  = GetComponent<MeshFilter>();
            if (!meshRenderer) meshRenderer = GetComponent<MeshRenderer>();
            if (!meshCollider) meshCollider = GetComponent<MeshCollider>();
        }

        private void Awake()
        {
            EnsureComponents();
        }

        // -------------------------------------------------------------
        // Assign Mesh to Renderer + Collider
        // -------------------------------------------------------------
        public void ApplyMesh(Mesh mesh)
        {
            EnsureComponents();

            if (mesh == null)
            {
                meshFilter.sharedMesh = null;
                meshCollider.sharedMesh = null;
                return;
            }

            // Assign mesh to renderer
            meshFilter.sharedMesh = mesh;

            // Refresh collider safely
            meshCollider.sharedMesh = null;
            meshCollider.sharedMesh = mesh;
        }

        // -------------------------------------------------------------
        // Helper for naming GameObjects
        // -------------------------------------------------------------
        public void SetCoord(ChunkCoord newCoord)
        {
            coord = newCoord;
            gameObject.name = $"Chunk_{coord.x}_{coord.z}";
        }
    }
}
