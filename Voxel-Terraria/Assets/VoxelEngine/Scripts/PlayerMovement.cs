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

    [Header("World Editor Tool")]
    public Material removeHologramMat; // Assign an Unlit Red Material in Inspector
    public Material placeHologramMat;  // Assign an Unlit Green/Blue Material in Inspector
    
    private bool isEditMode = false;
    private enum BuildMode { Remove, Place }
    private BuildMode currentMode = BuildMode.Remove;
    
    private int brushSize = 0;
    private int maxBrushSize = 6; // Radius of 6 = 13x13x13 blocks (Reasonable but fun)
    private int brushShape = 0;   // 0 = Cube, 1 = Sphere
    private uint selectedMaterial = 3; // Default to 3 (Chest) to test entities
    
    [Header("Editor Speed")]
    public float buildCooldown = 0.15f; // Time in seconds between edits (0.15 = ~6 blocks per second)
    private float nextBuildTime = 0f;

    private Transform previewCube;
    private Transform previewSphere;

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
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        // --- BUILD THE HOLOGRAM PRIMITIVES ---
        previewCube = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
        Destroy(previewCube.GetComponent<Collider>());
        previewCube.gameObject.SetActive(false);

        previewSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere).transform;
        Destroy(previewSphere.GetComponent<Collider>());
        previewSphere.gameObject.SetActive(false);
    }

    void Update()
    {
        HandleMouseLook(); // THE FIX: Run rotation before the camera updates
        HandleGravityToggle();
        // Send state to the Compute Shader
        if (ChunkManager.Instance != null) {
            ChunkManager.Instance.isEditMode = isEditMode;
            ChunkManager.Instance.editMode = (int)currentMode;
            ChunkManager.Instance.brushSize = brushSize;
            ChunkManager.Instance.brushShape = brushShape;
        }
        
        if (Keyboard.current != null) {
            if (!rb.useGravity && Keyboard.current.spaceKey.isPressed) FlyUp();
            
            // INTERACT WITH ENTITIES (Chests, Doors, etc.)
            if (Keyboard.current.eKey.wasPressedThisFrame && ChunkManager.Instance != null && ChunkManager.Instance.crosshairData[3] == 1) {
                Vector3Int targetPos = new Vector3Int(ChunkManager.Instance.crosshairData[0], ChunkManager.Instance.crosshairData[1], ChunkManager.Instance.crosshairData[2]);
                
                if (VoxelEngine.MetadataManager.Instance != null && VoxelEngine.MetadataManager.Instance.TryGetEntity(targetPos, out var entity)) {
                    entity.OnInteract();
                }
            }

            // Toggle Edit Mode
            if (Keyboard.current.bKey.wasPressedThisFrame) {
                isEditMode = !isEditMode;
                if (!isEditMode) {
                    previewCube.gameObject.SetActive(false);
                    previewSphere.gameObject.SetActive(false);
                }
            }

            // Toggle Shape
            if (isEditMode && Keyboard.current.tKey.wasPressedThisFrame) {
                brushShape = (brushShape == 0) ? 1 : 0;
            }

            // Material Selection (Keys 1-5)
            if (isEditMode) {
                if (Keyboard.current.digit1Key.wasPressedThisFrame) selectedMaterial = 1;
                if (Keyboard.current.digit2Key.wasPressedThisFrame) selectedMaterial = 2;
                if (Keyboard.current.digit3Key.wasPressedThisFrame) selectedMaterial = 3; // Chest Entity
                if (Keyboard.current.digit4Key.wasPressedThisFrame) selectedMaterial = 4;
                if (Keyboard.current.digit5Key.wasPressedThisFrame) selectedMaterial = 5;
            }
        }

        // Handle Scroll Wheel for Brush Size
        if (isEditMode && Mouse.current != null) {
            float scroll = Mouse.current.scroll.y.ReadValue();
            if (scroll > 0 && brushSize < maxBrushSize) brushSize++;
            if (scroll < 0 && brushSize > 0) brushSize--;
        }

        // --- WORLD EDITOR LOGIC ---
        if (isEditMode && ChunkManager.Instance != null && ChunkManager.Instance.crosshairData[3] == 1) {
            
            Vector3Int gridPos = new Vector3Int(ChunkManager.Instance.crosshairData[0], ChunkManager.Instance.crosshairData[1], ChunkManager.Instance.crosshairData[2]);
            Vector3Int normal = new Vector3Int(ChunkManager.Instance.crosshairData[4], ChunkManager.Instance.crosshairData[5], ChunkManager.Instance.crosshairData[6]);

            // 1. INPUT STATE MACHINE (Continuous Build)
            if (Mouse.current != null) {
                bool leftClick = Mouse.current.leftButton.isPressed;
                bool rightClick = Mouse.current.rightButton.isPressed;

                if (leftClick || rightClick) {
                    BuildMode intendedMode = leftClick ? BuildMode.Remove : BuildMode.Place;
                    
                    // If switching modes, update UI but wait for next frame to build
                    if (currentMode != intendedMode) {
                        currentMode = intendedMode;
                    } 
                    // Only build if enough time has passed since the last action
                    else if (Time.time >= nextBuildTime) {
                        nextBuildTime = Time.time + buildCooldown;
                        
                        if (currentMode == BuildMode.Remove) {
                            // MINING INTERCEPTION: Check if we are mining a C# entity first
                            if (brushSize == 0 && VoxelEngine.MetadataManager.Instance != null && VoxelEngine.MetadataManager.Instance.TryGetEntity(gridPos, out var entity)) {
                                entity.OnDamaged(25);
                            } else {
                                // Normal terrain mining on the GPU
                                ChunkManager.World.DamageVoxel(gridPos, 25, brushSize, brushShape);
                            }
                        } else {
                            // PLACE: Push outward by (brushSize + 1) so the bottom of the brush rests ON the surface
                            ChunkManager.World.EditVoxel(gridPos + (normal * (brushSize + 1)), selectedMaterial, brushSize, brushShape);
                        }
                    }
                } else {
                    // Reset timer when button is released so the next initial click is instant
                    nextBuildTime = 0f;
                }
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
        Vector2 mouseDelta = Mouse.current.delta.ReadValue() * 0.2f;
        
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

   void OnGUI() {
        // GUIStyle crosshairStyle = new GUIStyle();
        // crosshairStyle.normal.textColor = Color.white;
        // crosshairStyle.fontSize = 24;
        // crosshairStyle.alignment = TextAnchor.MiddleCenter;
        // GUI.Label(new Rect(Screen.width / 2f - 10, Screen.height / 2f - 10, 20, 20), "+", crosshairStyle);

        if (isEditMode) {
            GUIStyle hudStyle = new GUIStyle();
            hudStyle.normal.textColor = Color.yellow;
            hudStyle.fontSize = 18;
            hudStyle.fontStyle = FontStyle.Bold;
            
            string shapeName = brushShape == 0 ? "Cube" : "Sphere";
            string modeName = currentMode == BuildMode.Remove ? "REMOVE (Red)" : "PLACE (Green)";
            GUI.Label(new Rect(20, 100, 400, 150), $"--- WORLD EDITOR ---\nToggle Mode: [B]\nToggle Shape: [T] ({shapeName})\nBrush Size: [Scroll] ({brushSize})\nMaterial: {selectedMaterial} (Press 1-5)\nAction: {modeName}", hudStyle);
        }
    }
}