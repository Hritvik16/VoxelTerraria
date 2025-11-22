using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace VoxelTerraria.World.Meshing
{
    /// <summary>
    /// A simple container for mesh construction.
    /// Works for both BlockMesher and MarchingCubesMesher.
    /// Supports:
    ///   • Single-material meshes   (indices list)
    ///   • Multi-material meshes    (submeshTris by materialId)
    /// </summary>
    public class MeshData
    {
        public readonly List<float3> vertices;
        public readonly List<float3> normals;
        public readonly List<float2> uvs;

        // Legacy single-material index buffer (used by MarchingCubes, etc.)
        public readonly List<int> indices;

        // Multi-material: one triangle list per materialId / submesh index
        public readonly List<List<int>> submeshTris;

        // How many material IDs/submeshes we support (0..materialCount-1)
        public int materialCount;

        public MeshData(int initialCapacity = 256, int materialCount = 8)
        {
            this.materialCount = materialCount;

            vertices = new List<float3>(initialCapacity);
            normals  = new List<float3>(initialCapacity);
            uvs      = new List<float2>(initialCapacity);

            indices  = new List<int>(initialCapacity * 2);

            submeshTris = new List<List<int>>(materialCount);
            for (int i = 0; i < materialCount; i++)
                submeshTris.Add(new List<int>(initialCapacity));
        }

        // -------- Single-material helpers (still used by some debug code) --------

        public void AddTriangle(float3 v0, float3 v1, float3 v2,
                                float3 n0, float3 n1, float3 n2,
                                float2 uv0, float2 uv1, float2 uv2)
        {
            int indexStart = vertices.Count;

            vertices.Add(v0);
            vertices.Add(v1);
            vertices.Add(v2);

            normals.Add(n0);
            normals.Add(n1);
            normals.Add(n2);

            uvs.Add(uv0);
            uvs.Add(uv1);
            uvs.Add(uv2);

            indices.Add(indexStart);
            indices.Add(indexStart + 1);
            indices.Add(indexStart + 2);
        }

        public void AddQuad(float3 v0, float3 v1, float3 v2, float3 v3, float3 normal)
        {
            float2 uv0 = new float2(0, 0);
            float2 uv1 = new float2(1, 0);
            float2 uv2 = new float2(1, 1);
            float2 uv3 = new float2(0, 1);

            AddTriangle(v0, v1, v2, normal, normal, normal, uv0, uv1, uv2);
            AddTriangle(v0, v2, v3, normal, normal, normal, uv0, uv2, uv3);
        }

        public void AddTriangle(float3 v0, float3 v1, float3 v2)
        {
            float3 normal = math.normalize(math.cross(v1 - v0, v2 - v0));

            float2 uv0 = new float2(0, 0);
            float2 uv1 = new float2(1, 0);
            float2 uv2 = new float2(0, 1);

            AddTriangle(
                v0, v1, v2,
                normal, normal, normal,
                uv0, uv1, uv2
            );
        }

        // -------- Multi-material helper --------

        public void AddQuad(float3 v0, float3 v1, float3 v2, float3 v3, float3 normal, ushort materialId)
        {
            int matIndex = materialId;
            if (matIndex < 0 || matIndex >= materialCount)
            {
                matIndex = math.clamp(matIndex, 0, materialCount - 1);
            }

            int baseIndex = vertices.Count;

            vertices.Add(v0);
            vertices.Add(v1);
            vertices.Add(v2);
            vertices.Add(v3);

            normals.Add(normal);
            normals.Add(normal);
            normals.Add(normal);
            normals.Add(normal);

            uvs.Add(new float2(0, 0));
            uvs.Add(new float2(1, 0));
            uvs.Add(new float2(1, 1));
            uvs.Add(new float2(0, 1));

            var tris = submeshTris[matIndex];

            tris.Add(baseIndex + 0);
            tris.Add(baseIndex + 1);
            tris.Add(baseIndex + 2);

            tris.Add(baseIndex + 0);
            tris.Add(baseIndex + 2);
            tris.Add(baseIndex + 3);
        }

        // -------- Convert to Unity Mesh --------

        public Mesh ToMesh(bool calculateNormals = false)
        {
            Mesh mesh = new Mesh
            {
                indexFormat = IndexFormat.UInt32
            };

            int vCount = vertices.Count;
            int nCount = normals.Count;
            int uCount = uvs.Count;

            var v = new Vector3[vCount];
            var n = new Vector3[nCount];
            var u = new Vector2[uCount];

            for (int i = 0; i < vCount; i++)
                v[i] = new Vector3(vertices[i].x, vertices[i].y, vertices[i].z);

            for (int i = 0; i < nCount; i++)
                n[i] = new Vector3(normals[i].x, normals[i].y, normals[i].z);

            for (int i = 0; i < uCount; i++)
                u[i] = new Vector2(uvs[i].x, uvs[i].y);

            mesh.SetVertices(v);
            mesh.SetNormals(n);
            mesh.SetUVs(0, u);

            bool hasAnySubmesh = false;
            for (int m = 0; m < materialCount; m++)
            {
                if (submeshTris[m].Count > 0)
                {
                    hasAnySubmesh = true;
                    break;
                }
            }

            if (hasAnySubmesh)
            {
                mesh.subMeshCount = materialCount;

                for (int m = 0; m < materialCount; m++)
                {
                    var tris = submeshTris[m];
                    if (tris.Count > 0)
                    {
                        mesh.SetTriangles(tris, m);
                    }
                    else
                    {
                        mesh.SetTriangles(System.Array.Empty<int>(), m);
                    }
                }
            }
            else
            {
                mesh.subMeshCount = 1;
                mesh.SetTriangles(indices, 0);
            }

            if (calculateNormals)
                mesh.RecalculateNormals();

            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
