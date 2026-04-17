using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Movement Settings")]
    public float walkSpeed = 5f;
    public float flySpeed = 10f;
    public float lookSensitivity = 0.5f; 
    
    [Header("Step Logic")]
    public float stepHeight = 0.25f;     
    public float stepSharpness = 0.15f;   
    
    public Transform cameraTransform;

    [Header("World Editor Tool")]
    public Material removeHologramMat; 
    public Material placeHologramMat;  
    
    private bool isEditMode = false;
    private enum BuildMode { Remove, Place }
    private BuildMode currentMode = BuildMode.Remove;
    
    private int brushSize = 0;
    private int maxBrushSize = 6; 
    private int brushShape = 0;   
    private uint selectedMaterial = 3; 
    
    [Header("Editor Speed")]
    public float buildCooldown = 0.15f; 
    private float nextBuildTime = 0f;

    private Transform previewCube;
    private Transform previewSphere;

    [Header("Gravity Toggle")]
    public float doubleTapTimeThreshold = 0.3f;
    
    private Rigidbody rb;
    private float lastSpacePressTime;
    
    // --- GC-FREE DEVICE CACHING ---
    private Keyboard kb;
    private Mouse mouse;
    
    // --- THE CAMERA DECOUPLING FIX ---
    private float yaw = 0f;
    private float pitch = 0f;

    [Header("Camera Auto-Attach")]
    public Vector3 eyeLevelOffset = new Vector3(0, 0.6f, 0);

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        
        if (cameraTransform == null && Camera.main != null) {
            cameraTransform = Camera.main.transform;
        }

        if (cameraTransform != null && cameraTransform.parent != this.transform) {
            cameraTransform.SetParent(this.transform);
            cameraTransform.localPosition = eyeLevelOffset;
            cameraTransform.localRotation = Quaternion.identity;
        }
    }

    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        yaw = transform.eulerAngles.y;

        // Build Holograms
        previewCube = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
        Destroy(previewCube.GetComponent<Collider>());
        previewCube.gameObject.SetActive(false);

        previewSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere).transform;
        Destroy(previewSphere.GetComponent<Collider>());
        previewSphere.gameObject.SetActive(false);
    }

    void Update()
    {
        // 1. CACHE THE DEVICES (Reduces 20+ hidden allocations down to 2!)
        kb = Keyboard.current;
        mouse = Mouse.current;
        
        HandleMouseLook(); 
        HandleGravityToggle();

        if (ChunkManager.Instance != null) {
            ChunkManager.Instance.isEditMode = isEditMode;
            ChunkManager.Instance.editMode = (int)currentMode;
            ChunkManager.Instance.brushSize = brushSize;
            ChunkManager.Instance.brushShape = brushShape;
        }
        
        if (kb != null) {
            if (!rb.useGravity && kb.spaceKey.isPressed) FlyUp();
            
            if (kb.eKey.wasPressedThisFrame && ChunkManager.Instance != null && ChunkManager.Instance.crosshairData[3] == 1) {
                Vector3Int targetPos = new Vector3Int(ChunkManager.Instance.crosshairData[0], ChunkManager.Instance.crosshairData[1], ChunkManager.Instance.crosshairData[2]);
                if (VoxelEngine.MetadataManager.Instance != null && VoxelEngine.MetadataManager.Instance.TryGetEntity(targetPos, out var entity)) {
                    entity.OnInteract();
                }
            }

            if (kb.bKey.wasPressedThisFrame) {
                isEditMode = !isEditMode;
                if (!isEditMode) {
                    previewCube.gameObject.SetActive(false);
                    previewSphere.gameObject.SetActive(false);
                }
            }

            if (isEditMode && kb.tKey.wasPressedThisFrame) brushShape = (brushShape == 0) ? 1 : 0;

            if (isEditMode) {
                if (kb.digit1Key.wasPressedThisFrame) selectedMaterial = 1;
                if (kb.digit2Key.wasPressedThisFrame) selectedMaterial = 2;
                if (kb.digit3Key.wasPressedThisFrame) selectedMaterial = 3; 
                if (kb.digit4Key.wasPressedThisFrame) selectedMaterial = 4;
                if (kb.digit5Key.wasPressedThisFrame) selectedMaterial = 5;
                if (kb.digit6Key.wasPressedThisFrame) selectedMaterial = 6;
                if (kb.digit7Key.wasPressedThisFrame) selectedMaterial = 7;
                if (kb.digit8Key.wasPressedThisFrame) selectedMaterial = 8;
                if (kb.digit9Key.wasPressedThisFrame) selectedMaterial = 9;  // WATER
                if (kb.digit0Key.wasPressedThisFrame) selectedMaterial = 12; // LAVA
            }
        }

        if (isEditMode && mouse != null) {
            float scroll = mouse.scroll.y.ReadValue();
            if (scroll > 0 && brushSize < maxBrushSize) brushSize++;
            if (scroll < 0 && brushSize > 0) brushSize--;
        }

        // --- WORLD EDITOR LOGIC ---
        if (isEditMode && ChunkManager.Instance != null && ChunkManager.Instance.crosshairData[3] == 1) {
            Vector3Int gridPos = new Vector3Int(ChunkManager.Instance.crosshairData[0], ChunkManager.Instance.crosshairData[1], ChunkManager.Instance.crosshairData[2]);
            Vector3Int normal = new Vector3Int(ChunkManager.Instance.crosshairData[4], ChunkManager.Instance.crosshairData[5], ChunkManager.Instance.crosshairData[6]);

            if (mouse != null) {
                bool leftClick = mouse.leftButton.isPressed;
                bool rightClick = mouse.rightButton.isPressed;

                if (leftClick || rightClick) {
                    BuildMode intendedMode = leftClick ? BuildMode.Remove : BuildMode.Place;
                    if (currentMode != intendedMode) currentMode = intendedMode;
                    else if (Time.time >= nextBuildTime) {
                        nextBuildTime = Time.time + buildCooldown;
                        if (currentMode == BuildMode.Remove) {
                            if (brushSize == 0 && VoxelEngine.MetadataManager.Instance != null && VoxelEngine.MetadataManager.Instance.TryGetEntity(gridPos, out var entity)) {
                                entity.OnDamaged(25);
                            } else {
                                ChunkManager.World.DamageVoxel(gridPos - (normal * brushSize), 25, brushSize, brushShape);
                            }
                        } else {
                            ChunkManager.World.EditVoxel(gridPos + (normal * (brushSize + 1)), selectedMaterial, brushSize, brushShape);
                        }
                    }
                } else {
                    nextBuildTime = 0f;
                }
            }
        }
    }

    void FixedUpdate()
    {
        // Must cache here too because FixedUpdate runs on a different timestep!
        kb = Keyboard.current;
        HandleMovement();
    }

    private void HandleMovement()
    {
        Vector2 moveInput = Vector2.zero;
        if (kb != null) {
            float x = 0; float z = 0;
            if (kb.wKey.isPressed) z += 1;
            if (kb.sKey.isPressed) z -= 1;
            if (kb.aKey.isPressed) x -= 1;
            if (kb.dKey.isPressed) x += 1;
            moveInput = new Vector2(x, z).normalized; // Normalized to prevent double-speed diagonal movement
        }

        // Calculate move direction purely based on the Camera's Yaw!
        Quaternion moveRotation = Quaternion.Euler(0, yaw, 0);
        Vector3 moveDir = moveRotation * Vector3.right * moveInput.x + moveRotation * Vector3.forward * moveInput.y;
        
        float currentSpeed = rb.useGravity ? walkSpeed : flySpeed;

        if (rb.isKinematic) return; 

        if (rb.useGravity) {
            Vector3 targetVelocity = moveDir * currentSpeed;
            rb.linearVelocity = new Vector3(targetVelocity.x, rb.linearVelocity.y, targetVelocity.z);
        } else {
            rb.linearVelocity = moveDir * currentSpeed;
        }
    }

    private void FlyUp()
    {
        if (rb.isKinematic) return; 
        rb.linearVelocity = new Vector3(rb.linearVelocity.x, flySpeed, rb.linearVelocity.z);
    }

    private void HandleMouseLook()
    {
        if (mouse == null) return;

        Vector2 mouseDelta = mouse.delta.ReadValue() * 0.2f;
        yaw += mouseDelta.x * lookSensitivity;
        pitch -= mouseDelta.y * lookSensitivity;
        pitch = Mathf.Clamp(pitch, -90f, 90f);

        if (cameraTransform != null) {
            cameraTransform.localRotation = Quaternion.Euler(pitch, yaw, 0);
        }
    }

    private void HandleGravityToggle()
    {
        if (kb == null) return;
        if (kb.spaceKey.wasPressedThisFrame) {
            float timeSinceLastPress = Time.time - lastSpacePressTime;
            if (timeSinceLastPress <= doubleTapTimeThreshold) {
                ToggleGravity();
                lastSpacePressTime = 0; 
            } else {
                lastSpacePressTime = Time.time;
            }
        }
    }

    private void ToggleGravity()
    {
        rb.useGravity = !rb.useGravity;
        if (!rb.useGravity) rb.linearVelocity = Vector3.zero;
    }
}