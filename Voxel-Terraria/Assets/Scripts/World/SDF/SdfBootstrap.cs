using UnityEngine;
using System;

namespace VoxelTerraria.World.SDF {
[ExecuteAlways]
public class SdfBootstrap : MonoBehaviour
{
    [Header("World Settings")]
    public WorldSettings worldSettings;

    [Header("Features")]
    public MountainFeature[] mountainFeatures;
    public LakeFeature[] lakeFeatures;
    public ForestFeature[] forestFeatures;
    public CityPlateauFeature[] cityFeatures;

    // Called in Edit Mode and Play Mode whenever the component becomes enabled/active
    private void OnEnable()
    {
        if (worldSettings == null)
        {
            Debug.LogWarning("SdfBootstrap: Missing WorldSettings reference.");
            return;
        }

        var mountains = mountainFeatures ?? Array.Empty<MountainFeature>();
        var lakes     = lakeFeatures     ?? Array.Empty<LakeFeature>();
        var forests   = forestFeatures   ?? Array.Empty<ForestFeature>();
        var cities    = cityFeatures     ?? Array.Empty<CityPlateauFeature>();

        var ctx = SdfBootstrapInternal.Build(
            worldSettings,
            mountains,
            lakes,
            forests,
            cities
        );

        // Important: use SetContext so old arrays are disposed
        SdfRuntime.SetContext(ctx);
    }

    // In the editor, this is what runs when exiting Play Mode or disabling the object
    private void OnDisable()
    {
        SdfRuntime.Dispose();
    }

    // In case the object is explicitly destroyed in Edit Mode
    private void OnDestroy()
    {
        SdfRuntime.Dispose();
    }
}
}