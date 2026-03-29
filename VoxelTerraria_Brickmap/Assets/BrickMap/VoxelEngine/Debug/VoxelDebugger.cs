using UnityEngine;
using UnityEngine.InputSystem;

public class VoxelDebugger : MonoBehaviour
{
    [Header("Debug Settings")]
    public bool debugModeActive = false;
    public bool showRedWireframe = false;

    private GUIStyle titleStyle;
    private GUIStyle textStyle;
    private int cachedH = -1;

    void Update()
    {
        if (Keyboard.current != null)
        {
            if (Keyboard.current.lKey.wasPressedThisFrame) debugModeActive = !debugModeActive;
            
            if (debugModeActive)
            {
                if (Keyboard.current.aKey.wasPressedThisFrame) showRedWireframe = !showRedWireframe;
            }
        }

        Shader.SetGlobalInt("_ShowRedWireframe", (debugModeActive && showRedWireframe) ? 1 : 0);
    }

    void OnGUI()
    {
        if (!debugModeActive) return;

        int w = Screen.width, h = Screen.height;

        if (h != cachedH) {
            titleStyle = new GUIStyle { alignment = TextAnchor.UpperLeft, fontSize = Mathf.Max(24, h * 3 / 100) };
            titleStyle.normal.textColor = Color.yellow;
            textStyle = new GUIStyle { alignment = TextAnchor.UpperLeft, fontSize = Mathf.Max(16, h * 2 / 100), richText = true };
            textStyle.normal.textColor = Color.white;
            cachedH = h;
        }

        GUI.Label(new Rect(20, 20, w, h * 5 / 100), "<b>*** DEBUG MODE ACTIVE ***</b>", titleStyle);
        
        Rect boxRect = new Rect(20, 20 + (h * 5 / 100), w * 0.38f, h * 0.08f);
        GUI.Box(boxRect, "");

        float startY = boxRect.y + 10, padding = h * 3.0f / 100, leftX = boxRect.x + 10;
        
        string wireStatus = showRedWireframe ? "<color=#00FF00>ON</color>" : "<color=#FF4444>OFF</color>";
        GUI.Label(new Rect(leftX, startY, w, padding), $"<b>[A] Red Wireframe:</b> {wireStatus}", textStyle);
    }
}