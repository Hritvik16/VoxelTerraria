using UnityEditor;
using UnityEngine;
using Unity.Mathematics;
using VoxelTerraria.World.SDF;

[CustomEditor(typeof(MountainFeature))]
public class MountainFeatureEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        MountainFeature feature = (MountainFeature)target;

        // Try find a world settings in scene
        SdfBootstrap bootstrap = FindObjectOfType<SdfBootstrap>();
        if (bootstrap == null || bootstrap.worldSettings == null)
        {
            EditorGUILayout.HelpBox(
                "No SdfBootstrap or WorldSettings found.\n" +
                "Scene gizmo validation will still work,\n" +
                "but Inspector warnings cannot compute distances.",
                MessageType.Info
            );
            return;
        }

        WorldSettings world = bootstrap.worldSettings;
        float islandRadius = world.islandRadius;

        Vector2 center = feature.CenterXZ;
        float radius = feature.Radius;

        float dist = math.length(center);
        float maxDist = Mathf.Max(0f, islandRadius - radius);

        EditorGUILayout.Space(8);

        // ------------------------------
        // Summary Box
        // ------------------------------
        EditorGUILayout.LabelField("Validation", EditorStyles.boldLabel);

        EditorGUILayout.LabelField($"• Center Distance: {dist:F2}");
        EditorGUILayout.LabelField($"• Max Allowed:    {maxDist:F2}");
        EditorGUILayout.LabelField($"• Island Radius:  {islandRadius:F2}");

        EditorGUILayout.Space(4);

        // ------------------------------
        // Valid or invalid?
        // ------------------------------
        if (dist > maxDist)
        {
            EditorGUILayout.HelpBox(
                "This mountain extends OUTSIDE the island.\n" +
                "Move it closer or reduce the radius.",
                MessageType.Error
            );
        }
        else if (dist > maxDist * 0.85f)
        {
            EditorGUILayout.HelpBox(
                "This mountain is NEAR the island edge.\n" +
                "It will blend with the coastline.",
                MessageType.Warning
            );
        }
        else
        {
            EditorGUILayout.HelpBox(
                "This mountain placement is valid.",
                MessageType.Info
            );
        }
    }
}
