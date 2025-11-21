using UnityEditor;
using UnityEngine;

public static class DebugListMountainFeatures
{
    [MenuItem("Tools/Debug/List Mountain Features")]
    public static void ListMountainFeatures()
    {
        Debug.Log("==== Listing ALL MountainFeature assets ====");

        string[] guids = AssetDatabase.FindAssets("t:MountainFeature");

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Debug.Log("FOUND MountainFeature at: " + path);
        }

        Debug.Log("===========================================");
    }
}
