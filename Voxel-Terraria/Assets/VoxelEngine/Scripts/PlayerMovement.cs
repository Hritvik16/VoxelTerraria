using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Movement Settings")]
    public float walkSpeed = 5f;
    public float flySpeed = 10f;
    public float lookSensitivity = 0.5f; // New Input System delta values are different
    
    [Header("Step Logic")]
    public float stepHeight = 0.11f;     // Slightly higher than 0.1 voxel size
    public float stepSharpness = 0.1f;   // How high to nudge the player
    
    public Transform cameraTransform;

    [Header("Gravity Toggle")]
    public float doubleTapTimeThreshold = 0.3f;
    
    private Rigidbody rb;
    private float lastSpacePressTime;
    private float verticalRotation = 0f;

    [Header("Camera Auto-Attach")]
    public Vector3 eyeLevelOffset = new Vector3(0, 0.6f, 0);

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        
        // Find the camera automatically if it is floating loose in the scene
        if (cameraTransform == null && Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }

        // Force the camera to become a child of the player and snap to eye level
        if (cameraTransform != null && cameraTransform.parent != this.transform)
        {
            cameraTransform.SetParent(this.transform);
            cameraTransform.localPosition = eyeLevelOffset;
            cameraTransform.localRotation = Quaternion.identity;
        }
    }

    void Start()
    {
        // CRITICAL: Locks the mouse to the game window and hides it. 
        // This stops OS cursor acceleration from corrupting the delta values.
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        HandleMouseLook(); // THE FIX: Run rotation before the camera updates
        HandleGravityToggle();
        
        // Use Keyboard.current for direct polling (Simple script approach)
        if (Keyboard.current != null)
        {
            if (!rb.useGravity && Keyboard.current.spaceKey.isPressed)
            {
                FlyUp();
            }
        }
    }
    // Removed LateUpdate completely from this script

    void FixedUpdate()
    {
        HandleMovement();
    }

    private void HandleMovement()
    {
        Vector2 moveInput = Vector2.zero;
        if (Keyboard.current != null)
        {
            float x = 0;
            float z = 0;
            if (Keyboard.current.wKey.isPressed) z += 1;
            if (Keyboard.current.sKey.isPressed) z -= 1;
            if (Keyboard.current.aKey.isPressed) x -= 1;
            if (Keyboard.current.dKey.isPressed) x += 1;
            moveInput = new Vector2(x, z);
        }

        Vector3 moveDir = transform.right * moveInput.x + transform.forward * moveInput.y;
        float currentSpeed = rb.useGravity ? walkSpeed : flySpeed;

        if (rb.useGravity)
        {
            Vector3 currentVel = rb.linearVelocity;
            Vector3 targetVelocity = moveDir * currentSpeed;
            
            // CRITICAL FIX: Smoothly blend horizontal movement, but leave 'y' alone!
            // This stops the player from clipping through the spawned colliders.
            rb.linearVelocity = Vector3.Lerp(currentVel, new Vector3(targetVelocity.x, currentVel.y, targetVelocity.z), 0.4f);
            
            if (moveInput.magnitude > 0.1f)
            {
                ApplyStepLogic(moveDir);
            }
        }
        else
        {
            // Flying - No gravity, full 3D control
            rb.linearVelocity = moveDir * currentSpeed;
        }
    }

    private void ApplyStepLogic(Vector3 moveDir)
    {
        // Raycast at foot level to detect small obstacles
        // We check slightly above the base but below stepHeight
        Vector3 footPos = transform.position + Vector3.up * 0.01f;
        Vector3 kneePos = transform.position + Vector3.up * stepHeight;

        // Use a small distance for the check
        float checkDist = 0.4f; // Slightly more than typical collider radius

        bool hitFoot = Physics.Raycast(footPos, moveDir, checkDist);
        bool hitKnee = Physics.Raycast(kneePos, moveDir, checkDist);

        // If we hit something at the feet but NOT at the knee, it's a step!
        if (hitFoot && !hitKnee)
        {
            // Apply a small upward nudge to the position to clear the lip
            // This prevents the "snagging" against the side of the 0.1 voxel
            rb.position += Vector3.up * stepSharpness;
        }
    }

    private void FlyUp()
    {
        // Add vertical velocity when flying
        rb.linearVelocity = new Vector3(rb.linearVelocity.x, flySpeed, rb.linearVelocity.z);
    }

    private void HandleMouseLook()
    {
        if (Mouse.current == null) return;

        // THE FIX: Multiply the raw pixel delta by a base sensitivity scale (0.05f).
        // This scales a 1-pixel movement down to 0.05 degrees, allowing continuous, sub-pixel-feeling rotation.
        Vector2 mouseDelta = Mouse.current.delta.ReadValue() * 0.5f;
        
        float mouseX = mouseDelta.x * lookSensitivity;
        float mouseY = mouseDelta.y * lookSensitivity;

        // Yaw (Horizontal) - Rotates the entire player body
        transform.Rotate(Vector3.up * mouseX);

        // Pitch (Vertical) - Tilts ONLY the camera's local X axis
        verticalRotation -= mouseY;
        verticalRotation = Mathf.Clamp(verticalRotation, -90f, 90f);
        if (cameraTransform != null)
        {
            cameraTransform.localRotation = Quaternion.Euler(verticalRotation, 0, 0);
        }
    }

    private void HandleGravityToggle()
    {
        if (Keyboard.current == null) return;

        if (Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            float timeSinceLastPress = Time.time - lastSpacePressTime;
            
            if (timeSinceLastPress <= doubleTapTimeThreshold)
            {
                // Double tap detected
                ToggleGravity();
                lastSpacePressTime = 0; // Reset
            }
            else
            {
                lastSpacePressTime = Time.time;
            }
        }
    }

    private void ToggleGravity()
    {
        rb.useGravity = !rb.useGravity;
        
        if (!rb.useGravity)
        {
            // Stop falling immediately when enabling fly mode
            rb.linearVelocity = Vector3.zero;
        }
        
        Debug.Log("Gravity Toggled: " + (rb.useGravity ? "On" : "Off"));
    }
}
