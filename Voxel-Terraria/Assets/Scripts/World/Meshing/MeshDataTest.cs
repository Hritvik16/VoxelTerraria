using UnityEditor;
using UnityEngine;
using VoxelTerraria.World.Meshing;
using Unity.Mathematics;

public class MeshDataTest : MonoBehaviour
{
    [ContextMenu("Generate Test Quad Mesh")]
    void GenerateTest()
    {
        MeshData md = new MeshData();

        float3 a = new float3(0, 0, 0);
        float3 b = new float3(1, 0, 0);
        float3 c = new float3(1, 1, 0);
        float3 d = new float3(0, 1, 0);
        float3 n = new float3(0, 0, -1);

        md.AddQuad(a, b, c, d, n);

        MeshFilter mf = gameObject.GetComponent<MeshFilter>();
        if (!mf) mf = gameObject.AddComponent<MeshFilter>();

        MeshRenderer mr = gameObject.GetComponent<MeshRenderer>();
        if (!mr) mr = gameObject.AddComponent<MeshRenderer>();

        mf.sharedMesh = md.ToMesh();
        mr.sharedMaterial = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat");
    }
}
