using UnityEngine;

namespace VoxelEngine.World
{
    [ExecuteAlways] // Allows the seed to update in the Editor for the upcoming minimap
    public class WorldManager : MonoBehaviour
    {
        public static WorldManager Instance;

        public enum WorldSize { Small, Medium, Large }
        
        [Header("World Settings")]
        public int developerSeed = 1337;
        public WorldSize worldSize = WorldSize.Small;
        
        [HideInInspector] public float currentRadiusXZ;

        void Awake()
        {
            Instance = this;
            ApplySettingsToGPU();
        }

        private void OnValidate()
        {
            ApplySettingsToGPU();
        }

        public void ApplySettingsToGPU()
        {
            // Using the 3x adjusted radius for 3D traversal (0.2m voxel scale)
            switch (worldSize)
            {
                case WorldSize.Small: currentRadiusXZ = 1260f; break;
                case WorldSize.Medium: currentRadiusXZ = 1920f; break;
                case WorldSize.Large: currentRadiusXZ = 2520f; break;
            }

            Shader.SetGlobalFloat("_WorldRadiusXZ", currentRadiusXZ);
            Shader.SetGlobalInt("_WorldSeed", developerSeed);
        }
    }
}