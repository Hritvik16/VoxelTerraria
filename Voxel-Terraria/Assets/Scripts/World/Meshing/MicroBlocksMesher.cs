using Unity.Mathematics;
using VoxelTerraria.World;

namespace VoxelTerraria.World.Meshing
{
    /// <summary>
    /// Micro-block mesher:
    /// - Uses the existing padded density grid (no extra SDF calls)
    /// - Subdivides each coarse cell into microDiv^3 sub-cubes
    /// - Samples density at each sub-cube *corner* via trilinear interpolation
    /// - Emits cubes whenever at least one corner is solid (density > 0)
    ///
    /// This removes the "random holes" on slopes that you were seeing.
    /// Editor-only, not optimized for runtime.
    /// </summary>
    
    public static class MicroBlocksMesher
    {
        public static MeshData BuildMesh(in ChunkData chunkData, WorldSettings settings, int microDiv = 2)
        {
            int cells = chunkData.chunkSize;          // number of coarse cells per axis
            int r     = chunkData.voxelResolution;    // = cells + 1
            float voxelSize = settings.voxelSize;

            MeshData meshData = new MeshData(cells * cells * cells * microDiv * microDiv);

            var voxels = chunkData.voxels;

            // Node density helper (short -> float)
            float NodeDensity(int x, int y, int z)
            {
                int idx = x + y * r + z * r * r;
                return voxels[idx].density;
            }

            // Trilinear interpolation of densities in [0,1]^3
            float Trilinear(
                float d000, float d100, float d010, float d110,
                float d001, float d101, float d011, float d111,
                float fx, float fy, float fz)
            {
                float c00 = math.lerp(d000, d100, fx);
                float c10 = math.lerp(d010, d110, fx);
                float c01 = math.lerp(d001, d101, fx);
                float c11 = math.lerp(d011, d111, fx);

                float c0 = math.lerp(c00, c10, fy);
                float c1 = math.lerp(c01, c11, fy);

                return math.lerp(c0, c1, fz);
            }

            float microSize = voxelSize / microDiv;
            float invDiv    = 1f / microDiv;

            // For each coarse cell
            for (int cz = 0; cz < cells; cz++)
            {
                for (int cy = 0; cy < cells; cy++)
                {
                    for (int cx = 0; cx < cells; cx++)
                    {
                        // Fetch 8 corner densities for this coarse cell (node grid)
                        int nx = cx;
                        int ny = cy;
                        int nz = cz;

                        float d000 = NodeDensity(nx,     ny,     nz);
                        float d100 = NodeDensity(nx + 1, ny,     nz);
                        float d010 = NodeDensity(nx,     ny + 1, nz);
                        float d110 = NodeDensity(nx + 1, ny + 1, nz);
                        float d001 = NodeDensity(nx,     ny,     nz + 1);
                        float d101 = NodeDensity(nx + 1, ny,     nz + 1);
                        float d011 = NodeDensity(nx,     ny + 1, nz + 1);
                        float d111 = NodeDensity(nx + 1, ny + 1, nz + 1);

                        // If the whole cell is air, skip it
                        if (d000 <= 0 && d100 <= 0 && d010 <= 0 && d110 <= 0 &&
                            d001 <= 0 && d101 <= 0 && d011 <= 0 && d111 <= 0)
                        {
                            continue;
                        }

                        float3 coarseBase = new float3(cx * voxelSize, cy * voxelSize, cz * voxelSize);
                        
                        bool MicroSolid(float fx, float fy, float fz)
                        {
                            float d = Trilinear(
                                d000, d100, d010, d110,
                                d001, d101, d011, d111,
                                fx, fy, fz
                            );
                            return d > 0;
                        }

                        for (int mz = 0; mz < microDiv; mz++)
                        {
                            for (int my = 0; my < microDiv; my++)
                            {
                                for (int mx = 0; mx < microDiv; mx++)
                                {
                                    
                                    float fx = (mx + 0.5f) * invDiv;
                                    float fy = (my + 0.5f) * invDiv;
                                    float fz = (mz + 0.5f) * invDiv;

                                    bool solid = MicroSolid(fx, fy, fz);
                                    if (!solid) continue;

                                    // ========== NEIGHBOR CHECK ==========
                                    bool airXPos = (mx + 1 >= microDiv) ||
                                        !MicroSolid((mx + 1 + 0.5f) * invDiv, fy, fz);

                                    bool airXNeg = (mx - 1 < 0) ||
                                        !MicroSolid((mx - 1 + 0.5f) * invDiv, fy, fz);

                                    bool airYPos = (my + 1 >= microDiv) ||
                                        !MicroSolid(fx, (my + 1 + 0.5f) * invDiv, fz);

                                    bool airYNeg = (my - 1 < 0) ||
                                        !MicroSolid(fx, (my - 1 + 0.5f) * invDiv, fz);

                                    bool airZPos = (mz + 1 >= microDiv) ||
                                        !MicroSolid(fx, fy, (mz + 1 + 0.5f) * invDiv);

                                    bool airZNeg = (mz - 1 < 0) ||
                                        !MicroSolid(fx, fy, (mz - 1 + 0.5f) * invDiv);

                                    // If all neighbors solid → skip (internal)
                                    if (!airXPos && !airXNeg &&
                                        !airYPos && !airYNeg &&
                                        !airZPos && !airZNeg)
                                    {
                                        continue;
                                    }

                                    // ========== EMIT CUBE ==========
                                    float3 basePos = coarseBase + new float3(
                                        mx * microSize,
                                        my * microSize,
                                        mz * microSize
                                    );
                                    float s = microSize;

                                    if (airXPos)
                                        meshData.AddQuad(
                                            basePos + new float3(s, 0, 0),
                                            basePos + new float3(s, s, 0),
                                            basePos + new float3(s, s, s),
                                            basePos + new float3(s, 0, s),
                                            new float3(1, 0, 0));

                                    if (airXNeg)
                                        meshData.AddQuad(
                                            basePos + new float3(0, 0, s),
                                            basePos + new float3(0, s, s),
                                            basePos + new float3(0, s, 0),
                                            basePos + new float3(0, 0, 0),
                                            new float3(-1, 0, 0));

                                    if (airYPos)
                                        meshData.AddQuad(
                                            basePos + new float3(0, s, 0),
                                            basePos + new float3(0, s, s),
                                            basePos + new float3(s, s, s),
                                            basePos + new float3(s, s, 0),
                                            new float3(0, 1, 0));

                                    if (airYNeg)
                                        meshData.AddQuad(
                                            basePos + new float3(0, 0, s),
                                            basePos + new float3(0, 0, 0),
                                            basePos + new float3(s, 0, 0),
                                            basePos + new float3(s, 0, s),
                                            new float3(0, -1, 0));

                                    if (airZPos)
                                        meshData.AddQuad(
                                            basePos + new float3(s, 0, s),
                                            basePos + new float3(s, s, s),
                                            basePos + new float3(0, s, s),
                                            basePos + new float3(0, 0, s),
                                            new float3(0, 0, 1));

                                    if (airZNeg)
                                        meshData.AddQuad(
                                            basePos + new float3(0, 0, 0),
                                            basePos + new float3(0, s, 0),
                                            basePos + new float3(s, s, 0),
                                            basePos + new float3(s, 0, 0),
                                            new float3(0, 0, -1));
                                }
                            }
                        }


                        // Subdivide into microDiv^3 micro-cubes
                        // for (int mz = 0; mz < microDiv; mz++)
                        // {
                        //     for (int my = 0; my < microDiv; my++)
                        //     {
                        //         for (int mx = 0; mx < microDiv; mx++)
                        //         {
                        //             // Corner coords of this micro cube in cell-local [0,1]^3
                        //             float fx0 = mx * invDiv;
                        //             float fx1 = (mx + 1) * invDiv;
                        //             float fy0 = my * invDiv;
                        //             float fy1 = (my + 1) * invDiv;
                        //             float fz0 = mz * invDiv;
                        //             float fz1 = (mz + 1) * invDiv;

                        //             // Sample 8 corners of the micro cube
                        //             float c000 = Trilinear(d000, d100, d010, d110,
                        //                                    d001, d101, d011, d111,
                        //                                    fx0, fy0, fz0);
                        //             float c100 = Trilinear(d000, d100, d010, d110,
                        //                                    d001, d101, d011, d111,
                        //                                    fx1, fy0, fz0);
                        //             float c010 = Trilinear(d000, d100, d010, d110,
                        //                                    d001, d101, d011, d111,
                        //                                    fx0, fy1, fz0);
                        //             float c110 = Trilinear(d000, d100, d010, d110,
                        //                                    d001, d101, d011, d111,
                        //                                    fx1, fy1, fz0);

                        //             float c001 = Trilinear(d000, d100, d010, d110,
                        //                                    d001, d101, d011, d111,
                        //                                    fx0, fy0, fz1);
                        //             float c101 = Trilinear(d000, d100, d010, d110,
                        //                                    d001, d101, d011, d111,
                        //                                    fx1, fy0, fz1);
                        //             float c011 = Trilinear(d000, d100, d010, d110,
                        //                                    d001, d101, d011, d111,
                        //                                    fx0, fy1, fz1);
                        //             float c111 = Trilinear(d000, d100, d010, d110,
                        //                                    d001, d101, d011, d111,
                        //                                    fx1, fy1, fz1);

                        //             // If *all* corners are air, skip this micro cube
                        //             if (c000 <= 0 && c100 <= 0 && c010 <= 0 && c110 <= 0 &&
                        //                 c001 <= 0 && c101 <= 0 && c011 <= 0 && c111 <= 0)
                        //             {
                        //                 continue;
                        //             }

                        //             // Otherwise, emit a micro cube
                        //             float3 localOffset = new float3(
                        //                 mx * microSize,
                        //                 my * microSize,
                        //                 mz * microSize
                        //             );

                        //             float3 basePos = coarseBase + localOffset;
                        //             float s = microSize;

                        //             // +X
                        //             {
                        //                 float3 normal = new float3(1, 0, 0);
                        //                 float3 v0 = basePos + new float3(s, 0, 0);
                        //                 float3 v1 = basePos + new float3(s, s, 0);
                        //                 float3 v2 = basePos + new float3(s, s, s);
                        //                 float3 v3 = basePos + new float3(s, 0, s);
                        //                 meshData.AddQuad(v0, v1, v2, v3, normal);
                        //             }

                        //             // -X
                        //             {
                        //                 float3 normal = new float3(-1, 0, 0);
                        //                 float3 v0 = basePos + new float3(0, 0, s);
                        //                 float3 v1 = basePos + new float3(0, s, s);
                        //                 float3 v2 = basePos + new float3(0, s, 0);
                        //                 float3 v3 = basePos + new float3(0, 0, 0);
                        //                 meshData.AddQuad(v0, v1, v2, v3, normal);
                        //             }

                        //             // +Y
                        //             {
                        //                 float3 normal = new float3(0, 1, 0);
                        //                 float3 v0 = basePos + new float3(0, s, 0);
                        //                 float3 v1 = basePos + new float3(0, s, s);
                        //                 float3 v2 = basePos + new float3(s, s, s);
                        //                 float3 v3 = basePos + new float3(s, s, 0);
                        //                 meshData.AddQuad(v0, v1, v2, v3, normal);
                        //             }

                        //             // -Y
                        //             {
                        //                 float3 normal = new float3(0, -1, 0);
                        //                 float3 v0 = basePos + new float3(0, 0, s);
                        //                 float3 v1 = basePos + new float3(0, 0, 0);
                        //                 float3 v2 = basePos + new float3(s, 0, 0);
                        //                 float3 v3 = basePos + new float3(s, 0, s);
                        //                 meshData.AddQuad(v0, v1, v2, v3, normal);
                        //             }

                        //             // +Z
                        //             {
                        //                 float3 normal = new float3(0, 0, 1);
                        //                 float3 v0 = basePos + new float3(s, 0, s);
                        //                 float3 v1 = basePos + new float3(s, s, s);
                        //                 float3 v2 = basePos + new float3(0, s, s);
                        //                 float3 v3 = basePos + new float3(0, 0, s);
                        //                 meshData.AddQuad(v0, v1, v2, v3, normal);
                        //             }

                        //             // -Z
                        //             {
                        //                 float3 normal = new float3(0, 0, -1);
                        //                 float3 v0 = basePos + new float3(0, 0, 0);
                        //                 float3 v1 = basePos + new float3(0, s, 0);
                        //                 float3 v2 = basePos + new float3(s, s, 0);
                        //                 float3 v3 = basePos + new float3(s, 0, 0);
                        //                 meshData.AddQuad(v0, v1, v2, v3, normal);
                        //             }
                        //         }
                        //     }
                        // }
                    }
                }
            }

            return meshData;
        }
        
    }
    
}
