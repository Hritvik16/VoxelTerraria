// #if UNITY_EDITOR
// using UnityEditor;
// using UnityEngine;
// using System.Linq;

// public static class FeatureGizmoDrawer
// {
//     // --------------------------------------------------------------------
//     // Main Gizmo Entry Point
//     // --------------------------------------------------------------------
//     [DrawGizmo(GizmoType.NonSelected | GizmoType.Selected)]
//     private static void DrawFeatureGizmos(MonoBehaviour src, GizmoType gizmoType)
//     {
//         // Only draw gizmos in Scene view, not prefab mode
//         if (!SceneView.currentDrawingSceneView)
//             return;

//         DrawAllFeatures<MountainFeature>(Color.red);
//         DrawAllFeatures<LakeFeature>(Color.cyan);
//         DrawAllFeatures<ForestFeature>(Color.green);
//         DrawAllFeatures<CityPlateauFeature>(Color.yellow);
//     }

//     // --------------------------------------------------------------------
//     // Generic drawing logic for any feature type
//     // --------------------------------------------------------------------
//     private static void DrawAllFeatures<T>(Color color) where T : ScriptableObject
//     {
//         var guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}");
//         foreach (string guid in guids)
//         {
//             string path = AssetDatabase.GUIDToAssetPath(guid);
//             T feature = AssetDatabase.LoadAssetAtPath<T>(path);
//             if (feature == null) continue;

//             DrawFeature(feature, color);
//         }
//     }

//     // --------------------------------------------------------------------
//     // Per-feature draw logic
//     // --------------------------------------------------------------------
//     private static void DrawFeature(ScriptableObject feature, Color color)
//     {
//         // Get fields via reflection so this works for all Feature types
//         var type = feature.GetType();

//         var centerField = type.GetField("centerXZ", 
//             System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
//         var radiusField = type.GetField("radius", 
//             System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
//         var drawField = type.GetField("drawGizmos",
//             System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

//         if (centerField == null || radiusField == null || drawField == null)
//             return;

//         bool draw = (bool)drawField.GetValue(feature);
//         if (!draw) return;

//         Vector2 center = (Vector2)centerField.GetValue(feature);
//         float radius = (float)radiusField.GetValue(feature);

//         // Convert 2D XZ center to world 3D position
//         Vector3 pos = new Vector3(center.x, 0f, center.y);

//         // Draw sphere
//         Gizmos.color = color;
//         Gizmos.DrawSphere(pos, 2f);

//         // Draw wire circle on XZ plane
//         DrawCircleXZ(pos, radius, color);
//     }

//     // --------------------------------------------------------------------
//     // Utility: draw a circle in XZ plane
//     // --------------------------------------------------------------------
//     private static void DrawCircleXZ(Vector3 center, float radius, Color color)
//     {
//         Gizmos.color = color;
//         const int segments = 64;
//         float step = Mathf.PI * 2f / segments;

//         Vector3 prev = center + new Vector3(Mathf.Cos(0), 0, Mathf.Sin(0)) * radius;

//         for (int i = 1; i <= segments; i++)
//         {
//             float theta = i * step;
//             Vector3 next = center + new Vector3(Mathf.Cos(theta), 0, Mathf.Sin(theta)) * radius;
//             Gizmos.DrawLine(prev, next);
//             prev = next;
//         }
//     }
// }
// #endif

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Reflection;

[InitializeOnLoad]
public static class FeatureGizmoDrawer
{
    static FeatureGizmoDrawer()
    {
        SceneView.duringSceneGui += OnSceneGUI;
    }

    // ==================================================================================
    // Scene GUI (Handles for dragging, labels)
    // ==================================================================================
    private static void OnSceneGUI(SceneView sceneView)
    {
        DrawFeaturesWithHandles<MountainFeature>(Color.red, "Mountain");
        DrawFeaturesWithHandles<LakeFeature>(Color.cyan, "Lake");
        DrawFeaturesWithHandles<ForestFeature>(Color.green, "Forest");
        DrawFeaturesWithHandles<CityPlateauFeature>(Color.yellow, "City");
    }

    private static void DrawFeaturesWithHandles<T>(Color color, string label) where T : ScriptableObject
    {
        string[] guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}");
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            ScriptableObject so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
            if (so == null) continue;

            var type = so.GetType();
            var centerField = type.GetField("centerXZ", BindingFlags.NonPublic | BindingFlags.Instance);
            var radiusField = type.GetField("radius", BindingFlags.NonPublic | BindingFlags.Instance);
            var drawField = type.GetField("drawGizmos", BindingFlags.NonPublic | BindingFlags.Instance);

            if (centerField == null || radiusField == null || drawField == null)
                continue;

            bool draw = (bool)drawField.GetValue(so);
            if (!draw) continue;

            Vector2 centerXZ = (Vector2)centerField.GetValue(so);
            float radius = (float)radiusField.GetValue(so);

            Vector3 pos = new Vector3(centerXZ.x, 0f, centerXZ.y);

            // -----------------------------------------------------------------------------
            // Draggable Handle to Move Feature Center
            // -----------------------------------------------------------------------------
            EditorGUI.BeginChangeCheck();
            Vector3 newPos = Handles.PositionHandle(pos, Quaternion.identity);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(so, "Move Feature");
                centerField.SetValue(so, new Vector2(newPos.x, newPos.z));
                EditorUtility.SetDirty(so);
            }

            // -----------------------------------------------------------------------------
            // Label
            // -----------------------------------------------------------------------------
            Handles.color = color;
            GUIStyle labelStyle = new GUIStyle();
            labelStyle.fontSize = 14;
            labelStyle.normal.textColor = color;
            Handles.Label(pos + Vector3.up * 2f, label, labelStyle);

            // -----------------------------------------------------------------------------
            // Height visualization (vertical bars)
            // -----------------------------------------------------------------------------
            DrawHeightVisualization(so, type, pos, color);

            // -----------------------------------------------------------------------------
            // Forest density visualization
            // -----------------------------------------------------------------------------
            if (so is ForestFeature)
                DrawForestDensity(so, type, pos, radius, color);

            // -----------------------------------------------------------------------------
            // Draw circle on XZ plane
            // -----------------------------------------------------------------------------
            DrawCircleXZ(pos, radius, color);
        }
    }

    // ==================================================================================
    // Height visualization for mountains, lakes, city plateau
    // ==================================================================================
    private static void DrawHeightVisualization(ScriptableObject so, System.Type type, Vector3 pos, Color color)
    {
        FieldInfo heightField = type.GetField("height",
            BindingFlags.NonPublic | BindingFlags.Instance);
        FieldInfo bottomField = type.GetField("bottomHeight",
            BindingFlags.NonPublic | BindingFlags.Instance);
        FieldInfo plateauField = type.GetField("plateauHeight",
            BindingFlags.NonPublic | BindingFlags.Instance);

        Handles.color = new Color(color.r, color.g, color.b, 0.4f);

        if (heightField != null)    // Mountain
        {
            float h = (float)heightField.GetValue(so);
            Handles.DrawLine(pos, pos + Vector3.up * h);
        }
        else if (bottomField != null) // Lake
        {
            float bottom = (float)bottomField.GetValue(so);
            Handles.DrawLine(pos, pos + Vector3.up * bottom);
        }
        else if (plateauField != null) // City plateau
        {
            float h = (float)plateauField.GetValue(so);
            Handles.DrawLine(pos, pos + Vector3.up * h);
        }
    }

    // ==================================================================================
    // Forest density visualization (tick marks around circle)
    // ==================================================================================
    private static void DrawForestDensity(ScriptableObject so, System.Type type, Vector3 pos, float radius, Color color)
    {
        FieldInfo densityField = type.GetField("treeDensity",
            BindingFlags.NonPublic | BindingFlags.Instance);
        if (densityField == null) return;

        float density = (float)densityField.GetValue(so);
        int tickCount = Mathf.Max(8, Mathf.RoundToInt(32 * density));

        Handles.color = new Color(color.r, color.g, color.b, 0.8f);

        for (int i = 0; i < tickCount; i++)
        {
            float t = (i / (float)tickCount) * Mathf.PI * 2f;
            Vector3 dir = new Vector3(Mathf.Cos(t), 0, Mathf.Sin(t));
            Vector3 outer = pos + dir * radius;
            Vector3 inner = pos + dir * (radius - 2f);

            Handles.DrawLine(inner, outer);
        }
    }

    // ==================================================================================
    // Utility: XZ Plane Circle
    // ==================================================================================
    private static void DrawCircleXZ(Vector3 center, float radius, Color color)
    {
        Handles.color = new Color(color.r, color.g, color.b, 0.7f);

        const int segments = 64;
        float step = Mathf.PI * 2f / segments;

        Vector3 prev = center + new Vector3(Mathf.Cos(0), 0, Mathf.Sin(0)) * radius;

        for (int i = 1; i <= segments; i++)
        {
            float theta = i * step;
            Vector3 next = center + new Vector3(Mathf.Cos(theta), 0, Mathf.Sin(theta)) * radius;
            Handles.DrawLine(prev, next);
            prev = next;
        }
    }
}
#endif
