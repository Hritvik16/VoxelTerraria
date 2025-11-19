using UnityEngine;
using System;

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

    // private void Awake()
    void OnEnable()
    {
        // Debug.Log("SdfBootstrap.OnEnable()");

        // Debug.Log($"City features array: {cityFeatures}");
        // Debug.Log($"City features length: {(cityFeatures == null ? -1 : cityFeatures.Length)}");

        // foreach (var c in cityFeatures)
        //     Debug.Log($"City entry: {c}");
        

        var mountains = mountainFeatures ?? Array.Empty<MountainFeature>();
        var lakes     = lakeFeatures     ?? Array.Empty<LakeFeature>();
        var forests   = forestFeatures   ?? Array.Empty<ForestFeature>();
        var cities    = cityFeatures     ?? Array.Empty<CityPlateauFeature>();

        SdfRuntime.Context = SdfBootstrapInternal.Build(
            worldSettings,
            mountains,
            lakes,
            forests,
            cities
        );
        // Debug.Log($"Mountains: {SdfRuntime.Context.mountains.Length}");
        // Debug.Log($"Lakes: {SdfRuntime.Context.lakes.Length}");
        // Debug.Log($"Forests: {SdfRuntime.Context.forests.Length}");
        // Debug.Log($"Cities: {SdfRuntime.Context.cities.Length}");
        // Debug.Log($"Cities built: {cities.Length}");
    }

    private void OnDestroy()
    {
        // Dispose when exiting Play Mode
        SdfRuntime.Dispose();
    }
}
