#if UNITY_EDITOR
using Unity.Mathematics;
using UnityEngine;
using VoxelTerraria.World.SDF;

public class Raw3DDebug : MonoBehaviour
{
    public float3 testPos;

    void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;

        // Keep your raw3D helper
        float raw = MountainSdf.EvaluateRaw3D(testPos, SdfRuntime.Context);

        // New ref-based combined SDF call
        float sdf = CombinedTerrainSdf.Evaluate(testPos, ref SdfRuntime.Context);

        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere((Vector3)testPos, 0.2f);

        UnityEditor.Handles.Label((Vector3)testPos + Vector3.up * 0.3f,
            $"raw3D={raw:0.00}\nsdf={sdf:0.00}");
    }
}
#endif
