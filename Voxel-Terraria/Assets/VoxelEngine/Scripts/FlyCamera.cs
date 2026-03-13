using UnityEngine;
using UnityEngine.InputSystem;

public class FlyCamera : MonoBehaviour
{
    [Header("Terraria Speeds (m/s)")]
    public float hermesBootsSpeed = 15f; 
    public float minecartMaxSpeed = 30f; 

    [Header("Controls")]
    [Tooltip("If true, constantly moves forward. You only need to steer with the mouse.")]
    public bool autoDrive = true;
    public float mouseSensitivity = 0.1f;

    private float pitch = 0f;
    private float yaw = 0f;

    void Start()
    {
        // Grab current rotation to prevent snapping on start
        Vector3 angles = transform.eulerAngles;
        pitch = angles.x;
        yaw = angles.y;
    }

    void Update()
    {
        // 1. Smooth Mouse Look (Input System)
        if (Mouse.current != null)
        {
            Vector2 mouseDelta = Mouse.current.delta.ReadValue() * mouseSensitivity;
            yaw += mouseDelta.x;
            pitch -= mouseDelta.y;
            pitch = Mathf.Clamp(pitch, -89f, 89f);
        }
        
        transform.eulerAngles = new Vector3(pitch, yaw, 0f);

        // 2. Speed Selection (Hold Shift for Max Minecart Speed)
        bool isShifting = false;
        if (Keyboard.current != null)
        {
            isShifting = Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed;
        }
        float currentSpeed = isShifting ? minecartMaxSpeed : hermesBootsSpeed;

        // 3. Movement (Pure Transform, Zero Physics)
        Vector3 moveDir = Vector3.zero;

        if (autoDrive)
        {
            moveDir = transform.forward;
        }
        else if (Keyboard.current != null)
        {
            float h = 0;
            float v = 0;
            if (Keyboard.current.wKey.isPressed) v += 1;
            if (Keyboard.current.sKey.isPressed) v -= 1;
            if (Keyboard.current.dKey.isPressed) h += 1;
            if (Keyboard.current.aKey.isPressed) h -= 1;

            moveDir = (transform.forward * v + transform.right * h).normalized;
        }

        // Apply movement smoothly over the frame
        transform.position += moveDir * currentSpeed * Time.deltaTime;
    }
}