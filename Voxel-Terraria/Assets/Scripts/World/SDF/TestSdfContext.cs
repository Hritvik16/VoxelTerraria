using UnityEngine;

public class TestSdfContext : MonoBehaviour
{
    public WorldSettings world;
    public MountainFeature[] mountains;
    public LakeFeature[] lakes;
    public ForestFeature[] forests;
    public CityPlateauFeature[] cities;

    void Start()
    {
        // void Start()
// {
    // Ensure arrays are not null
    // mountains ??= new MountainFeature[0];
    // lakes     ??= new LakeFeature[0];
    // forests   ??= new ForestFeature[0];
    // cities    ??= new CityPlateauFeature[0];

    var ctx = SdfBootstrap.Build(world, mountains, lakes, forests, cities);
    Debug.Log("SdfContext built successfully!");
    ctx.Dispose();
// }

    }
}
