using UnityEngine;
using UnityEditor;
using Unity.Mathematics;
using VoxelTerraria.World.SDF;

[CustomEditor(typeof(SdfBootstrap))]
public class FeatureGizmoDrawer : Editor
{
    private SdfBootstrap Bootstrap => (SdfBootstrap)target;

    private void OnSceneGUI()
    {
        var bootstrap = Bootstrap;
        if (bootstrap == null)
            return;

        var settings = bootstrap.worldSettings;
        if (settings == null)
            return;

        float islandRadius = settings.islandRadius;
        float seaLevel     = settings.seaLevel;

        // ---------------------------------------------------------
        // Draw Island Radius (cyan)
        // ---------------------------------------------------------
        Handles.color = new Color(0f, 1f, 1f, 0.75f);
        Handles.DrawWireDisc(Vector3.zero, Vector3.up, islandRadius);

        Handles.Label(
            Vector3.up * (seaLevel + 2f),
            $"Island Radius = {islandRadius:F1}",
            EditorStyles.boldLabel
        );

        // ---------------------------------------------------------
        // Mountains
        // ---------------------------------------------------------
        var mountains = bootstrap.mountainFeatures;
        if (mountains == null || mountains.Length == 0)
            return;

        foreach (var mf in mountains)
        {
            if (mf == null)
                continue;

            Vector2 centerXZ = mf.CenterXZ;
            float radius     = mf.Radius;

            float dist    = math.length(centerXZ);
            float maxDist = Mathf.Max(0f, islandRadius - radius);

            bool outOfBounds = dist > maxDist;

            Vector3 worldPos = new Vector3(centerXZ.x, seaLevel, centerXZ.y);

            // Gizmo color
            Handles.color = outOfBounds ? new Color(1f, 0.2f, 0.2f, 0.9f)
                                        : new Color(0.3f, 1f, 0.3f, 0.9f);

            // Draw mountain influence radius
            Handles.DrawWireDisc(worldPos, Vector3.up, radius);

            // Draw line to island center
            Handles.DrawLine(Vector3.zero, worldPos);

            // Draw sphere at the center
            Handles.SphereHandleCap(
                0,
                worldPos,
                Quaternion.identity,
                settings.voxelSize * 2f,
                EventType.Repaint
            );

            // Label
            string label =
                outOfBounds
                ? $"Mountain (OUT OF BOUNDS)\ncenterDist={dist:F1}, max={maxDist:F1}"
                : $"Mountain\ncenterDist={dist:F1}, max={maxDist:F1}";

            Handles.Label(
                worldPos + Vector3.up * (settings.voxelSize * 3f),
                label,
                outOfBounds ? EditorStyles.boldLabel : EditorStyles.label
            );
        }
    }
}
