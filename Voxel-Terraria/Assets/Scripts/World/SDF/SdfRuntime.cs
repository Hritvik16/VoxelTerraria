using Unity.Collections;
using Unity.Mathematics;
using VoxelTerraria.World;
using VoxelTerraria.World.Generation;
using VoxelTerraria.World.SDF;

public static class SdfRuntime
{
    public static SdfContext Context;
    public static bool Initialized { get; private set; } = false;

    public static bool FastRejectChunk(ChunkCoord3 coord, WorldSettings settings)
    {
        float3 origin = WorldCoordUtils.ChunkOriginWorld(coord, settings);
        float size = settings.chunkSize * settings.voxelSize;

        // Sample center
        float3 center = origin + new float3(size * 0.5f, size * 0.5f, size * 0.5f);
        float sdf = CombinedTerrainSdf.Evaluate(center, ref Context);

        // If center is > margin above terrain → skip entirely
        return sdf > size * 0.75f;
    }

    public static void SetContext(SdfContext ctx)
    {
        // If old context exists, dispose it first
        if (Initialized)
        {
            Context.Dispose();
        }

        Context = ctx;
        Initialized = true;
    }

    public static void Dispose()
    {
        if (!Initialized)
            return;

        Context.Dispose();
        Initialized = false;
    }
}
