using UnityEngine;
using System.Collections.Generic;
using System.Linq;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using VoxelEngine;
using VoxelEngine.World;

namespace VoxelEngine.DebugTools
{
    /// <summary>
    /// Attach this to your Player GameObject. 
    /// Press keys 1-5 to cycle through features grouped by type.
    /// </summary>
    public class TeleportToFeature : MonoBehaviour
    {
        [Header("Settings")]
        [Tooltip("Extra height added above the detected surface.")]
        public float verticalOffset = 5.0f;

        [Header("Debug Info")]
        public int lastTeleportFullIndex = -1;
        private int[] categoryIndices = new int[5]; // Stores current index for each category (0-3 for types, 4 for random)

        private string[] featureNames = { "Rolling Plains", "Terraced Steppes", "Mountain Ranges", "The Mesa" };

        void Update()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb == null) return;

            if (kb.digit1Key.wasPressedThisFrame) CycleFeatureByType(0);      // Plains
            else if (kb.digit2Key.wasPressedThisFrame) CycleFeatureByType(1); // Steppes
            else if (kb.digit3Key.wasPressedThisFrame) CycleFeatureByType(2); // Mountains
            else if (kb.digit4Key.wasPressedThisFrame) CycleFeatureByType(3); // Mesa
            else if (kb.digit5Key.wasPressedThisFrame) CycleRandom();         // Random
#else
            if (Input.GetKeyDown(KeyCode.Alpha1)) CycleFeatureByType(0);
            else if (Input.GetKeyDown(KeyCode.Alpha2)) CycleFeatureByType(1);
            else if (Input.GetKeyDown(KeyCode.Alpha3)) CycleFeatureByType(2);
            else if (Input.GetKeyDown(KeyCode.Alpha4)) CycleFeatureByType(3);
            else if (Input.GetKeyDown(KeyCode.Alpha5)) CycleRandom();
#endif
        }

        private void CycleFeatureByType(int typeID)
        {
            if (WorldManager.Instance == null) return;

            var features = WorldManager.Instance.mapFeatures;
            var filtered = features.Select((f, i) => new { Feature = f, Index = i })
                                   .Where(x => x.Feature.topologyID == typeID)
                                   .ToList();

            if (filtered.Count == 0) return;

            categoryIndices[typeID] = (categoryIndices[typeID] + 1) % filtered.Count;
            TeleportTo(filtered[categoryIndices[typeID]].Index);
        }

        private void CycleRandom()
        {
            if (WorldManager.Instance == null || WorldManager.Instance.mapFeatures.Count == 0) return;

            categoryIndices[4] = (categoryIndices[4] + 1) % WorldManager.Instance.mapFeatures.Count;
            TeleportTo(categoryIndices[4]);
        }

        public void TeleportTo(int index)
        {
            if (WorldManager.Instance == null) return;

            if (index < 0 || index >= WorldManager.Instance.mapFeatures.Count) return;

            FeatureAnchor anchor = WorldManager.Instance.mapFeatures[index];
            Vector3 targetPos = new Vector3(anchor.position.x, 300f, anchor.position.y);

            RaycastHit hit;
            if (Physics.Raycast(new Vector3(targetPos.x, 500f, targetPos.z), Vector3.down, out hit, 1000f))
            {
                targetPos.y = hit.point.y + verticalOffset;
            }
            else
            {
                targetPos.y = 150f;
            }

            CharacterController cc = GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            transform.position = targetPos;
            if (cc != null) cc.enabled = true;

            lastTeleportFullIndex = index;
        }

        private void OnGUI()
        {
            if (WorldManager.Instance == null) return;

            GUIStyle style = new GUIStyle();
            style.fontSize = 18;
            style.normal.textColor = Color.white;
            float yOffset = 10;

            GUI.Box(new Rect(5, 5, 350, 140), "Feature Teleport Control");

            for (int i = 0; i < 4; i++)
            {
                var filteredCount = WorldManager.Instance.mapFeatures.Count(f => f.topologyID == i);
                string text = $"[{i + 1}] {featureNames[i]}: {filteredCount} Seeds (Current: {categoryIndices[i] + 1})";
                
                // Show coordinates of the CURRENTLY selected feature in this category
                var filtered = WorldManager.Instance.mapFeatures.Where(f => f.topologyID == i).ToList();
                if (filtered.Count > 0)
                {
                    var target = filtered[categoryIndices[i]];
                    text += $" @ ({target.position.x:F0}, {target.position.y:F0})";
                }

                GUI.Label(new Rect(10, yOffset + 25, 400, 30), text, style);
                yOffset += 22;
            }

            GUI.Label(new Rect(10, yOffset + 25, 400, 30), $"[5] Random Feature (Total: {WorldManager.Instance.mapFeatures.Count})", style);
        }
    }
}
