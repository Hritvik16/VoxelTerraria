using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace VoxelTerraria.World.Meshing
{
    /// <summary>
    /// A simple container for mesh construction.
    /// Works for both BlockMesher and MarchingCubesMesher.
    /// </summary>
    public class MeshData
    {
        public readonly List<float3> vertices;
        public readonly List<float3> normals;
        public readonly List<float2> uvs;
        public readonly List<int> indices;

        public MeshData(int initialCapacity = 256)
        {
            vertices = new List<float3>(initialCapacity);
            normals  = new List<float3>(initialCapacity);
            uvs      = new List<float2>(initialCapacity);
            indices  = new List<int>(initialCapacity * 2);
        }

        /// <summary>
        /// Adds a triangle (3 vertices, 3 normals, 3 uvs).
        /// </summary>
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

        /// <summary>
        /// Adds a quad (two triangles) given four positions and a normal.
        /// </summary>
        public void AddQuad(float3 v0, float3 v1, float3 v2, float3 v3, float3 normal)
        {
            float2 uv0 = new float2(0, 0);
            float2 uv1 = new float2(1, 0);
            float2 uv2 = new float2(1, 1);
            float2 uv3 = new float2(0, 1);

            AddTriangle(v0, v1, v2, normal, normal, normal, uv0, uv1, uv2);
            AddTriangle(v0, v2, v3, normal, normal, normal, uv0, uv2, uv3);
        }

        /// <summary>
        /// Converts MeshData into a UnityEngine.Mesh.
        /// </summary>
        public Mesh ToMesh(bool calculateNormals = false)
        {
            Mesh mesh = new Mesh();
            mesh.indexFormat = IndexFormat.UInt32; // allow large meshes

            // Convert float3 → Vector3
            Vector3[] v = new Vector3[vertices.Count];
            Vector3[] n = new Vector3[normals.Count];
            Vector2[] u = new Vector2[uvs.Count];

            for (int i = 0; i < vertices.Count; i++)
                v[i] = new Vector3(vertices[i].x, vertices[i].y, vertices[i].z);

            for (int i = 0; i < normals.Count; i++)
                n[i] = new Vector3(normals[i].x, normals[i].y, normals[i].z);

            for (int i = 0; i < uvs.Count; i++)
                u[i] = new Vector2(uvs[i].x, uvs[i].y);

            mesh.SetVertices(v);
            mesh.SetNormals(n);
            mesh.SetUVs(0, u);
            mesh.SetTriangles(indices, 0);

            if (calculateNormals)
                mesh.RecalculateNormals();

            mesh.RecalculateBounds();
            return mesh;
        }
        public void AddTriangle(float3 v0, float3 v1, float3 v2)
        {
            // Compute flat triangle normal
            float3 normal = math.normalize(math.cross(v1 - v0, v2 - v0));

            // Basic UVs for now (good enough for terrain)
            float2 uv0 = new float2(0, 0);
            float2 uv1 = new float2(1, 0);
            float2 uv2 = new float2(0, 1);

            AddTriangle(
                v0, v1, v2,
                normal, normal, normal,
                uv0, uv1, uv2
            );
        }

    }
}
