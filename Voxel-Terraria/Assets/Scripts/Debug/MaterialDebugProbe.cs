using UnityEngine;
using VoxelTerraria.World;

namespace VoxelTerraria.DebugTools
{
    [ExecuteAlways]
    public class MaterialDebugProbe : MonoBehaviour
    {
        [ContextMenu("Check Materials")]
        public void CheckMaterials()
        {
            Material[] mats = TerrainMaterialLibrary.GetMaterials(8);
            
            Debug.Log("--- Material Library Dump ---");
            for (int i = 0; i < mats.Length; i++)
            {
                Material m = mats[i];
                if (m == null)
                {
                    Debug.Log($"[{i}] NULL (Air)");
                }
                else
                {
                    Color c = m.color; // or m.GetColor("_BaseColor")
                    Debug.Log($"[{i}] {m.name} - Color: {c}");
                }
            }
        }
    }
}
