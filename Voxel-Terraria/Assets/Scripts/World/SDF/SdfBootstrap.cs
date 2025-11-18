using UnityEngine;

public class SdfBootstrap : MonoBehaviour
{
    [Header("World Settings")]
    public WorldSettings worldSettings;

    [Header("Features")]
    public MountainFeature[] mountainFeatures;
    public LakeFeature[] lakeFeatures;
    public ForestFeature[] forestFeatures;
    public CityPlateauFeature[] cityFeatures;

    private void Awake()
    {
        if (worldSettings == null)
        {
            Debug.LogError("SdfBootstrap: WorldSettings is not assigned!");
            return;
        }

        // Null-safe initialization
        mountainFeatures ??= new MountainFeature[0];
        lakeFeatures     ??= new LakeFeature[0];
        forestFeatures   ??= new ForestFeature[0];
        cityFeatures     ??= new CityPlateauFeature[0];

        // Build the Burst-friendly context
        var ctx = SdfBootstrapInternal.Build(
            worldSettings,
            mountainFeatures,
            lakeFeatures,
            forestFeatures,
            cityFeatures
        );

        SdfRuntime.SetContext(ctx);

        Debug.Log("SDF Context successfully built and stored in SdfRuntime.");
    }

    private void OnDestroy()
    {
        // Dispose when exiting Play Mode
        SdfRuntime.Dispose();
    }
}
