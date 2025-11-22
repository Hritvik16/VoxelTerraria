// using UnityEngine;

// /// <summary>
// /// Central place to map material IDs (0..7) to actual Materials.
// /// ID mapping we agreed on:
// /// 0 = air (unused)
// /// 1 = Grass
// /// 2 = ForestFloor
// /// 3 = Stone
// /// 4 = Sand
// /// 5 = CityGround
// /// 6 = Dirt
// /// 7 = LakeBed
// /// </summary>
// public static class TerrainMaterialLibrary
// {
//     private static Material[] cached;

//     public static Material[] GetMaterials(int requiredSubmeshCount)
// {
//     if (cached != null && cached.Length >= requiredSubmeshCount)
//         return cached;

//     cached = new Material[8];

//     cached[0] = null;
//     cached[1] = LoadByName("Grass");
//     cached[2] = LoadByName("ForestFloor");
//     cached[3] = LoadByName("Stone");
//     cached[4] = LoadByName("Sand");
//     cached[5] = LoadByName("CityGround");
//     cached[6] = LoadByName("Dirt");
//     cached[7] = LoadByName("LakeBed");
//     // cached[8] = LoadByName("Water");

//     return cached;
// }


//     private static Material LoadByName(string name)
//     {
//         // Easiest option: keep materials anywhere and assign via Resources folder:
//         // Put your .mat files under: Assets/Resources/Materials/
//         // e.g. Assets/Resources/Materials/Grass.mat
//         Material mat = Resources.Load<Material>("Materials/" + name);
//         if (mat == null)
//         {
//             Debug.LogWarning($"TerrainMaterialLibrary: could not find material 'Materials/{name}.mat' in any Resources folder.");
//         }
//         return mat;
//     }
// }
using UnityEngine;

/// <summary>
/// Central place to map material IDs (0..7) to actual Materials.
/// ID mapping:
/// 0 = air (unused)
/// 1 = Grass
/// 2 = ForestFloor
/// 3 = Stone
/// 4 = Sand
/// 5 = CityGround
/// 6 = Dirt
/// 7 = LakeBed
/// </summary>
public static class TerrainMaterialLibrary
{
    private static Material[] cached;

    public static Material[] GetMaterials(int requiredSubmeshCount)
    {
        // We only ever use IDs 0..7 for now.
        const int kMaterialCount = 8;

        if (cached != null && cached.Length == kMaterialCount)
            return cached;

        cached = new Material[kMaterialCount];

        cached[0] = null; // air
        cached[1] = LoadByName("Grass");
        cached[2] = LoadByName("ForestFloor");
        cached[3] = LoadByName("Stone");
        cached[4] = LoadByName("Sand");
        cached[5] = LoadByName("CityGround");
        cached[6] = LoadByName("Dirt");
        cached[7] = LoadByName("LakeBed");
        // No water yet; we’ll handle that later as a separate mesh/plane.

        return cached;
    }

    private static Material LoadByName(string name)
    {
        // Put materials under: Assets/Resources/Materials/<Name>.mat
        Material mat = Resources.Load<Material>("Materials/" + name);
        if (mat == null)
        {
            Debug.LogWarning(
                $"TerrainMaterialLibrary: could not find material 'Materials/{name}.mat' in any Resources folder.");
        }
        return mat;
    }
}
