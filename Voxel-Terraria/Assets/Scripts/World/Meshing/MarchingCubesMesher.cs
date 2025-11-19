using Unity.Mathematics;
using UnityEngine;
using VoxelTerraria.World;

namespace VoxelTerraria.World.Meshing
{
    public static class MarchingCubesMesher
    {
        public static MeshData BuildMesh(in ChunkData chunk, WorldSettings settings)
        {
            // cells = number of cubes per axis
            int cells  = chunk.chunkSize;
            int voxRes = chunk.voxelResolution; // cells + 1

            float voxelSize = settings.voxelSize;

            MeshData mesh = new MeshData(1024);

            var voxels = chunk.voxels;

            // Local position of a grid node in chunk-local space
            float3 VoxelPos(int x, int y, int z)
            {
                return new float3(
                    x * voxelSize,
                    y * voxelSize,
                    z * voxelSize
                );
            }

            short Density(int x, int y, int z)
            {
                int idx = x + y * voxRes + z * voxRes * voxRes;
                return voxels[idx].density;
            }

            // Loop over each cell (cube) in index space [0..cells-1]
            for (int z = 0; z < cells; z++)
            {
                for (int y = 0; y < cells; y++)
                {
                    for (int x = 0; x < cells; x++)
                    {
                        // Read 8 corner densities (padded grid)
                        short d0 = Density(x,   y,   z);
                        short d1 = Density(x+1, y,   z);
                        short d2 = Density(x+1, y,   z+1);
                        short d3 = Density(x,   y,   z+1);
                        short d4 = Density(x,   y+1, z);
                        short d5 = Density(x+1, y+1, z);
                        short d6 = Density(x+1, y+1, z+1);
                        short d7 = Density(x,   y+1, z+1);

                        int caseIndex = 0;
                        if (d0 > 0) caseIndex |= 1;
                        if (d1 > 0) caseIndex |= 2;
                        if (d2 > 0) caseIndex |= 4;
                        if (d3 > 0) caseIndex |= 8;
                        if (d4 > 0) caseIndex |= 16;
                        if (d5 > 0) caseIndex |= 32;
                        if (d6 > 0) caseIndex |= 64;
                        if (d7 > 0) caseIndex |= 128;

                        if (caseIndex == 0 || caseIndex == 255)
                            continue; // empty or full

                        int edges = MarchingCubesTables.edgeTable[caseIndex];
                        float3[] vertList = new float3[12];

                        float3 p0 = VoxelPos(x,   y,   z);
                        float3 p1 = VoxelPos(x+1, y,   z);
                        float3 p2 = VoxelPos(x+1, y,   z+1);
                        float3 p3 = VoxelPos(x,   y,   z+1);
                        float3 p4 = VoxelPos(x,   y+1, z);
                        float3 p5 = VoxelPos(x+1, y+1, z);
                        float3 p6 = VoxelPos(x+1, y+1, z+1);
                        float3 p7 = VoxelPos(x,   y+1, z+1);

                        // Interpolate edges where needed
                        float3 Interp(float3 a, float3 b, float da, float db)
                        {
                            float t = da / (da - db);
                            return a + t * (b - a);
                        }

                        if ((edges & 1) != 0)   vertList[0]  = Interp(p0,p1,d0,d1);
                        if ((edges & 2) != 0)   vertList[1]  = Interp(p1,p2,d1,d2);
                        if ((edges & 4) != 0)   vertList[2]  = Interp(p2,p3,d2,d3);
                        if ((edges & 8) != 0)   vertList[3]  = Interp(p3,p0,d3,d0);

                        if ((edges & 16) != 0)  vertList[4]  = Interp(p4,p5,d4,d5);
                        if ((edges & 32) != 0)  vertList[5]  = Interp(p5,p6,d5,d6);
                        if ((edges & 64) != 0)  vertList[6]  = Interp(p6,p7,d6,d7);
                        if ((edges & 128) != 0) vertList[7]  = Interp(p7,p4,d7,d4);

                        if ((edges & 256) != 0) vertList[8]  = Interp(p0,p4,d0,d4);
                        if ((edges & 512) != 0) vertList[9]  = Interp(p1,p5,d1,d5);
                        if ((edges & 1024)!= 0) vertList[10] = Interp(p2,p6,d2,d6);
                        if ((edges & 2048)!= 0) vertList[11] = Interp(p3,p7,d3,d7);

                        // Emit triangles
                        for (int t = 0; t < 16; t += 3)
                        {
                            int a = MarchingCubesTables.triTable[caseIndex, t];
                            if (a == -1)
                                break;

                            int b = MarchingCubesTables.triTable[caseIndex, t+1];
                            int c = MarchingCubesTables.triTable[caseIndex, t+2];

                            float3 vA = vertList[a];
                            float3 vB = vertList[b];
                            float3 vC = vertList[c];

                            // Flat normal per triangle
                            float3 n = math.normalize(math.cross(vB - vA, vC - vA));
                            mesh.AddTriangle(
                                vA, vB, vC,
                                n,  n,  n,
                                new float2(0,0),
                                new float2(1,0),
                                new float2(0,1)
                            );
                        }
                    }
                }
            }

            return mesh;
        }
    }
}
