using UnityEngine;

namespace VoxelTerraria.World.SDF
{
    [ExecuteAlways]
    public class SdfBootstrap : MonoBehaviour
    {
        [Header("World Settings")]
        public WorldSettings worldSettings;

        [Header("Features – Typed Lists (Editor-facing)")]
        public BaseIslandFeature baseIsland;
        public MountainFeature[] mountainFeatures;
        public LakeFeature[] lakeFeatures;
        public ForestFeature[] forestFeatures;
        public CityPlateauFeature[] cityFeatures;

        private void OnEnable()
        {
            if (worldSettings == null)
            {
                Debug.LogWarning("SdfBootstrap: Missing WorldSettings reference.");
                return;
            }

            var ctx = SdfBootstrapInternal.Build(
                worldSettings,
                baseIsland,
                mountainFeatures,
                lakeFeatures,
                forestFeatures,
                cityFeatures
            );

            SdfRuntime.SetContext(ctx);
        }

        private void OnDisable()
        {
            SdfRuntime.Dispose();
        }

        private void OnDestroy()
        {
            SdfRuntime.Dispose();
        }
    }
}
