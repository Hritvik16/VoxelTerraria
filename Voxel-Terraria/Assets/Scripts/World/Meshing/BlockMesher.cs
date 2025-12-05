using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using VoxelTerraria.World;
using VoxelTerraria.World.SDF;

namespace VoxelTerraria.World.Meshing
{
    public static class BlockMesher
    {
        /// <summary>
        /// Burst-friendly core mesher.
        /// 
        /// - No WorldSettings, no SdfRuntime access.
        /// - All data passed in as blittable structs / NativeArray.
        /// - Suitable for calling from a Burst job.
        /// </summary>
        [BurstCompile]
        public static void BuildMeshCore(
            NativeArray<Voxel> voxels,
            ChunkCoord3 coord,
            int cells,
            int voxelResolution,
            float voxelSize,
            SdfContext ctx,
            ref MeshData meshData)
        {
            // 3 axes: 0=X, 1=Y, 2=Z
            for (int axis = 0; axis < 3; axis++)
            {
                int uAxis = (axis + 1) % 3;
                int vAxis = (axis + 2) % 3;

                // We'll iterate through the chunk in slices along 'axis'
                // For each slice, we build a mask of faces.
                // We need two masks: one for the forward face (+axis) and one for backward (-axis).
                // Mask size is cells * cells.
                
                int sliceSize = cells * cells;
                var maskPos = new NativeArray<ushort>(sliceSize, Allocator.Temp);
                var maskNeg = new NativeArray<ushort>(sliceSize, Allocator.Temp);

                // Iterate through all slices
                for (int i = 0; i < cells; i++)
                {
                    // 1. Populate masks for this slice
                    for (int v = 0; v < cells; v++)
                    {
                        for (int u = 0; u < cells; u++)
                        {
                            // Map (i, u, v) to (x, y, z)
                            int x = 0, y = 0, z = 0;
                            
                            // Axis mapping
                            if (axis == 0) { x = i; y = u; z = v; }
                            else if (axis == 1) { x = v; y = i; z = u; }
                            else { x = u; y = v; z = i; }

                            // Check this voxel
                            bool isSolid = IsCellSolidLocal(x, y, z, cells, voxelResolution, voxels);
                            if (!isSolid) continue; // If current voxel is empty, it has no faces

                            // Calculate world position for SDF checks (needed for IsCellAir)
                            float chunkWorld = voxelSize * cells;
                            float3 chunkOrigin = new float3(
                                coord.x * chunkWorld,
                                coord.y * chunkWorld,
                                coord.z * chunkWorld
                            );

                            // +Axis Face (Forward)
                            // Face is at (i+1) along axis. Check neighbor at (i+1).
                            // Neighbor coords:
                            int nx = x + (axis == 0 ? 1 : 0);
                            int ny = y + (axis == 1 ? 1 : 0);
                            int nz = z + (axis == 2 ? 1 : 0);

                            if (IsCellAir(nx, ny, nz, cells, voxelResolution, voxels, chunkOrigin, voxelSize, ref ctx))
                            {
                                float3 faceCenterLocal = GetFaceCenterLocal(x, y, z, voxelSize, axis, 1);
                                maskPos[v * cells + u] = MaterialForFace(x, y, z, cells, voxelResolution, voxels, chunkOrigin, faceCenterLocal, ref ctx);
                            }

                            // -Axis Face (Backward)
                            // Face is at (i) along axis. Check neighbor at (i-1).
                            // Neighbor coords:
                            nx = x - (axis == 0 ? 1 : 0);
                            ny = y - (axis == 1 ? 1 : 0);
                            nz = z - (axis == 2 ? 1 : 0);

                            if (IsCellAir(nx, ny, nz, cells, voxelResolution, voxels, chunkOrigin, voxelSize, ref ctx))
                            {
                                float3 faceCenterLocal = GetFaceCenterLocal(x, y, z, voxelSize, axis, -1);
                                maskNeg[v * cells + u] = MaterialForFace(x, y, z, cells, voxelResolution, voxels, chunkOrigin, faceCenterLocal, ref ctx);
                            }
                        }
                    }

                    // 2. Greedy Mesh the masks
                    GreedyMeshPlane(maskPos, cells, axis, uAxis, vAxis, i, 1, voxelSize, ref meshData);
                    GreedyMeshPlane(maskNeg, cells, axis, uAxis, vAxis, i, -1, voxelSize, ref meshData);

                    // Clear masks for next slice
                    // Since we are in Burst, a simple loop is vectorized and very fast.
                    for (int k = 0; k < sliceSize; k++)
                    {
                        maskPos[k] = 0;
                        maskNeg[k] = 0;
                    }
                }

                maskPos.Dispose();
                maskNeg.Dispose();
            }
        }

        // Helper to get face center for material calculation
        private static float3 GetFaceCenterLocal(int x, int y, int z, float s, int axis, int dir)
        {
            float3 center = new float3(x + 0.5f, y + 0.5f, z + 0.5f) * s;
            if (axis == 0) center.x += 0.5f * s * dir;
            if (axis == 1) center.y += 0.5f * s * dir;
            if (axis == 2) center.z += 0.5f * s * dir;
            return center;
        }

        private static void GreedyMeshPlane(
            NativeArray<ushort> mask,
            int cells,
            int axis,
            int uAxis,
            int vAxis,
            int slice,
            int dir, // +1 or -1
            float voxelSize,
            ref MeshData meshData)
        {
            for (int v = 0; v < cells; v++)
            {
                for (int u = 0; u < cells; u++)
                {
                    ushort matId = mask[v * cells + u];
                    if (matId == 0) continue;

                    // Compute width (u direction)
                    int width = 1;
                    while (u + width < cells && mask[v * cells + (u + width)] == matId)
                    {
                        width++;
                    }

                    // Compute height (v direction)
                    int height = 1;
                    bool done = false;
                    while (v + height < cells)
                    {
                        for (int k = 0; k < width; k++)
                        {
                            if (mask[(v + height) * cells + (u + k)] != matId)
                            {
                                done = true;
                                break;
                            }
                        }
                        if (done) break;
                        height++;
                    }

                    // Add Quad
                    AddGreedyQuad(axis, uAxis, vAxis, slice, u, v, width, height, dir, voxelSize, matId, ref meshData);

                    // Clear mask
                    for (int h = 0; h < height; h++)
                    {
                        for (int w = 0; w < width; w++)
                        {
                            mask[(v + h) * cells + (u + w)] = 0;
                        }
                    }

                    // Increment u
                    u += width - 1;
                }
            }
        }

        private static void AddGreedyQuad(
            int axis, int uAxis, int vAxis,
            int slice, int u, int v,
            int width, int height,
            int dir,
            float s,
            ushort matId,
            ref MeshData meshData)
        {
            // Construct vertices
            // We need 4 points.
            // Start (u, v) in 2D plane.
            // Extent (width, height).
            
            // Coordinates in (axis, uAxis, vAxis) system:
            // P0: (slice + offset, u, v)
            // P1: (slice + offset, u + width, v)
            // P2: (slice + offset, u + width, v + height)
            // P3: (slice + offset, u, v + height)
            
            // Offset: if dir == 1 (+axis), face is at slice + 1 * s? 
            // Wait, slice index 'i' corresponds to voxel 'i'.
            // +Face is at (i+1)*s.
            // -Face is at i*s.
            
            float axisPos = (slice + (dir == 1 ? 1 : 0)) * s;
            
            float3[] verts = new float3[4];
            
            // Local coords on the plane
            float u0 = u * s;
            float u1 = (u + width) * s;
            float v0 = v * s;
            float v1 = (v + height) * s;

            // Map back to x,y,z
            // We need to construct 4 vertices.
            // Order depends on normal direction to ensure CCW winding.
            // Normal:
            float3 normal = new float3(0, 0, 0);
            if (axis == 0) normal.x = dir;
            if (axis == 1) normal.y = dir;
            if (axis == 2) normal.z = dir;

            // Quad vertices in plane coordinates (U, V)
            // 0: (u0, v0), 1: (u1, v0), 2: (u1, v1), 3: (u0, v1)
            
            // If dir is positive: 0->1->2->3 is CCW?
            // Let's check standard axes.
            // Axis X (0), U=Y (1), V=Z (2). Normal +X.
            // Verts on YZ plane.
            // (y, z) -> (y+w, z) -> (y+w, z+h) -> (y, z+h)
            // Right hand rule: Y cross Z = X. So CCW in YZ matches +X.
            
            // If dir is negative: Normal -X.
            // We need CW in YZ to be CCW facing -X?
            // -X normal. We look from -X.
            // Y cross Z = X. So Y->Z is clockwise for -X.
            // So we need Z->Y or just reverse order.
            
            // Let's generate generic points first
            float3 p0 = MapToWorld(axis, uAxis, vAxis, axisPos, u0, v0);
            float3 p1 = MapToWorld(axis, uAxis, vAxis, axisPos, u1, v0);
            float3 p2 = MapToWorld(axis, uAxis, vAxis, axisPos, u1, v1);
            float3 p3 = MapToWorld(axis, uAxis, vAxis, axisPos, u0, v1);

            if (dir == 1)
            {
                // CCW for +Axis
                meshData.AddQuad(p0, p1, p2, p3, normal, matId);
            }
            else
            {
                // CCW for -Axis (reverse of +Axis logic)
                // p0->p3->p2->p1
                meshData.AddQuad(p0, p3, p2, p1, normal, matId);
            }
        }

        private static float3 MapToWorld(int axis, int uAxis, int vAxis, float axisVal, float uVal, float vVal)
        {
            float3 p = new float3();
            // This is slightly tricky because uAxis/vAxis indices are 0,1,2.
            // We can use array indexing on float3 if we cast to pointer or use indexer if available.
            // float3 has [int index] in Unity.Mathematics? Yes.
            
            p[axis] = axisVal;
            p[uAxis] = uVal;
            p[vAxis] = vVal;
            return p;
        }

        /// <summary>
        /// Old API: Build mesh from ChunkData + WorldSettings.
        /// This stays as a thin wrapper around the Burst-safe core.
        /// </summary>
        public static MeshData BuildMesh(in ChunkData chunkData, WorldSettings settings)
        {
            int cells      = chunkData.chunkSize;
            int r          = chunkData.voxelResolution;
            float voxelSize = chunkData.currentVoxelSize;

            MeshData meshData = new MeshData(cells * cells * cells, 8);

            var voxels = chunkData.voxels;
            var coord  = chunkData.coord3;

            // Get SdfContext from SdfRuntime on the main thread.
            SdfContext ctx = SdfRuntime.Context;

            BuildMeshCore(
                voxels,
                coord,
                cells,
                r,
                voxelSize,
                ctx,
                ref meshData
            );

            return meshData;
        }

        // --------------------------------------------------------------------
        // Helper methods (Burst-friendly: no captures, no managed state)
        // --------------------------------------------------------------------

        private static int IndexNode(int x, int y, int z, int r)
        {
            return x + y * r + z * r * r;
        }

        private static bool IsCellSolidLocal(
            int cx, int cy, int cz,
            int cells,
            int r,
            NativeArray<Voxel> voxels)
        {
            if (cx < 0 || cy < 0 || cz < 0 ||
                cx >= cells || cy >= cells || cz >= cells)
            {
                return false;
            }

            int solidCount = 0;

            for (int dz = 0; dz <= 1; dz++)
            for (int dy = 0; dy <= 1; dy++)
            for (int dx = 0; dx <= 1; dx++)
            {
                int nx = cx + dx;
                int ny = cy + dy;
                int nz = cz + dz;

                int idx = IndexNode(nx, ny, nz, r);
                if (voxels[idx].density > 0)
                    solidCount++;
            }

            return solidCount > 0;
        }

        /// <summary>
        /// Returns true if the cell should be treated as air.
        /// Inside-chunk: just !IsCellSolidLocal.
        /// Outside-chunk: uses SDF if context is valid; otherwise treated as solid.
        /// </summary>
        private static bool IsCellAir(
            int cx, int cy, int cz,
            int cells,
            int r,
            NativeArray<Voxel> voxels,
            float3 chunkOrigin,
            float voxelSize,
            ref SdfContext ctx)
        {
            // Inside this chunk: rely on local voxel data.
            if (cx >= 0 && cy >= 0 && cz >= 0 &&
                cx <  cells && cy <  cells && cz <  cells)
            {
                return !IsCellSolidLocal(cx, cy, cz, cells, r, voxels);
            }

            // If SdfContext isn't valid, treat outside as solid (no gaps).
            if (ctx.chunkSize <= 0 || ctx.voxelSize <= 0f)
            {
                return false;
            }

            // Outside the chunk: query SDF at the cell center.
            float3 cellCenter = new float3(
                chunkOrigin.x + (cx + 0.5f) * voxelSize,
                chunkOrigin.y + (cy + 0.5f) * voxelSize,
                chunkOrigin.z + (cz + 0.5f) * voxelSize
            );

            float sdf = CombinedTerrainSdf.Evaluate(cellCenter, ref ctx);
            // Solid if sdf < 0 → air if sdf >= 0
            return sdf >= 0f;
        }

        private static ushort GetCellMaterialIdLocal(
            int cx, int cy, int cz,
            int cells,
            int r,
            NativeArray<Voxel> voxels)
        {
            // Default mat (fallback if outside chunk)
            ushort mat = 1;

            if (cx < 0 || cy < 0 || cz < 0 ||
                cx >= cells || cy >= cells || cz >= cells)
            {
                return mat;
            }

            for (int dz = 0; dz <= 1; dz++)
            for (int dy = 0; dy <= 1; dy++)
            for (int dx = 0; dx <= 1; dx++)
            {
                int nx = cx + dx;
                int ny = cy + dy;
                int nz = cz + dz;

                int idx = IndexNode(nx, ny, nz, r);
                Voxel v = voxels[idx];

                if (v.density > 0)
                {
                    mat = v.materialId;
                    break;
                }
            }

            return mat;
        }

        /// <summary>
        /// Computes the material for a face, including shoreline override.
        /// </summary>
        private static ushort MaterialForFace(
            int cx, int cy, int cz,
            int cells,
            int r,
            NativeArray<Voxel> voxels,
            float3 chunkOrigin,
            float3 faceCenterLocal,
            ref SdfContext ctx)
        {
            ushort matId = GetCellMaterialIdLocal(cx, cy, cz, cells, r, voxels);
            if (matId == 0)
                return 0;

            float3 worldFaceCenter = chunkOrigin + faceCenterLocal;
            float distFromSea = math.abs(worldFaceCenter.y - ctx.seaLevel);
            float nearCoast = math.exp(-distFromSea * 0.6f);

            // Coastline tint: if close enough to sea level, switch to coastal material.
            if (nearCoast > 0.35f && matId != 3)
                matId = 4;

            return matId;
        }
    }
}
