using UnityEngine;
using Unity.Mathematics;
using VoxelTerraria.World.Generation;
using VoxelTerraria.World.SDF;

[CreateAssetMenu(
    fileName = "VolcanoFeature",
    menuName = "VoxelTerraria/Features/Volcano")]
public class VolcanoFeature : FeatureSO
{
    [Header("Placement")]
    public Vector2 CenterXZ = Vector2.zero;
    public float Radius      = 40f;
    public float Height      = 30f;
    public float BaseHeight  = 0f;   // world-space height where volcano base meets terrain

    [Header("Crater")]
    public float CraterRadius = 18f;
    public float CraterDepth  = 15f;

    [Header("Lava Paths")]
    [Tooltip("Width of each lava channel in world units.")]
    public float PathWidth      = 4f;
    [Tooltip("Depth of each lava channel below the surrounding slope.")]
    public float PathDepth      = 2f;
    [Tooltip("Frequency for noise that wiggles the paths.")]
    public float PathNoiseFreq  = 0.05f;
    [Tooltip("Amplitude for noise that wiggles the paths.")]
    public float PathNoiseAmp   = 3f;

    [Header("Biome")]
    [Tooltip("Biome id used by biome evaluator for volcanic rock.")]
    public int BiomeId = 2;

    public override Vector3 GetConnectorPoint(WorldSettings settings)
    {
        // Connect to the peak of the volcano
        return new Vector3(CenterXZ.x, BaseHeight + Height, CenterXZ.y);
    }

    public override Feature ToFeature(WorldSettings settings)
    {
        Feature f = new Feature();
        f.type = FeatureType.Volcano;
        f.biomeId = 5; // Volcano biome

        f.centerXZ = new float2(CenterXZ.x, CenterXZ.y);

        // Pack data
        // data0: x=radius, y=height, z=baseHeight
        f.data0 = new float3(Radius, Height, BaseHeight);
        
        // data1: x=craterRadius, y=craterDepth, z=pathWidth
        f.data1 = new float3(CraterRadius, CraterDepth, PathWidth);
        
        // data2: x=pathDepth, y=pathNoiseFreq, z=pathNoiseAmp
        f.data2 = new float3(PathDepth, PathNoiseFreq, PathNoiseAmp);
        
        // data3: x=seed (injected later), y=unused, z=unused
        f.data3 = new float3(0f, 0f, 0f);

        return f;
    }
}
