using Unity.Collections;
using Unity.Mathematics;

public static class SdfBootstrapInternal
{
    public static SdfContext Build(
        WorldSettings world, 
        MountainFeature[] mountainSOs,
        LakeFeature[] lakeSOs,
        ForestFeature[] forestSOs,
        CityPlateauFeature[] citySOs,
        Allocator allocator = Allocator.Persistent)
    {
        // SAME EXACT BUILD CODE WE ALREADY WROTE
        SdfContext ctx = new SdfContext
        {
            worldWidth = world.worldWidth,
            worldDepth = world.worldDepth,
            worldHeight = world.worldHeight,

            voxelSize = world.voxelSize,
            chunkSize = world.chunkSize,
            seaLevel  = world.seaLevel,

            mountains = new NativeArray<MountainFeatureData>(mountainSOs != null ? mountainSOs.Length : 0, allocator),
            lakes     = new NativeArray<LakeFeatureData>(lakeSOs != null ? lakeSOs.Length : 0, allocator),
            forests   = new NativeArray<ForestFeatureData>(forestSOs != null ? forestSOs.Length : 0, allocator),
            cities    = new NativeArray<CityPlateauFeatureData>(citySOs != null ? citySOs.Length : 0, allocator)
        };

        // Convert arrays — same as before
        if (mountainSOs != null)
        {
            for (int i = 0; i < mountainSOs.Length; i++)
            {
                ctx.mountains[i] = new MountainFeatureData
                {
                    centerXZ = new float2(mountainSOs[i].CenterXZ.x, mountainSOs[i].CenterXZ.y),
                    radius = mountainSOs[i].Radius,
                    height = mountainSOs[i].Height,
                    ridgeFrequency = mountainSOs[i].RidgeFrequency,
                    ridgeAmplitude = mountainSOs[i].RidgeAmplitude,
                    warpStrength   = mountainSOs[i].WarpStrength
                };
            }
        }

        if (lakeSOs != null)
        {
            for (int i = 0; i < lakeSOs.Length; i++)
            {
                ctx.lakes[i] = new LakeFeatureData
                {
                    centerXZ = new float2(lakeSOs[i].CenterXZ.x, lakeSOs[i].CenterXZ.y),
                    radius = lakeSOs[i].Radius,
                    bottomHeight = lakeSOs[i].BottomHeight,
                    shoreHeight  = lakeSOs[i].ShoreHeight
                };
            }
        }

        if (forestSOs != null)
        {
            for (int i = 0; i < forestSOs.Length; i++)
            {
                ctx.forests[i] = new ForestFeatureData
                {
                    centerXZ = new float2(forestSOs[i].CenterXZ.x, forestSOs[i].CenterXZ.y),
                    radius = forestSOs[i].Radius,
                    treeDensity = forestSOs[i].TreeDensity
                };
            }
        }

        if (citySOs != null)
        {
            for (int i = 0; i < citySOs.Length; i++)
            {
                ctx.cities[i] = new CityPlateauFeatureData
                {
                    centerXZ = new float2(citySOs[i].CenterXZ.x, citySOs[i].CenterXZ.y),
                    radius = citySOs[i].Radius,
                    plateauHeight = citySOs[i].PlateauHeight
                };
            }
        }

        return ctx;
    }
}
