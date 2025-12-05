using UnityEngine;

namespace VoxelTerraria.DebugTools
{
    public class MeshMaterialInspector : MonoBehaviour
    {
        [ContextMenu("Inspect Materials")]
        public void Inspect()
        {
            var mr = GetComponent<MeshRenderer>();
            if (mr == null)
            {
                Debug.LogError("No MeshRenderer found!");
                return;
            }

            var mats = mr.sharedMaterials;
            Debug.Log($"MeshRenderer has {mats.Length} materials:");
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (m == null)
                {
                    Debug.Log($"[{i}] NULL");
                }
                else
                {
                    Debug.Log($"[{i}] {m.name} - Color: {m.color} - Shader: {m.shader.name}");
                }
            }
        }
    }
}
