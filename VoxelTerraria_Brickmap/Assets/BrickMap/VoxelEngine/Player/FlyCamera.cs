using UnityEngine;
using UnityEngine.InputSystem;

namespace DualStateEngine.Player {
    public class FlyCamera : MonoBehaviour {
        [Header("Movement Settings")]
        public float normalSpeed = 15.0f;
        public float sprintSpeed = 45.0f;
        
        // Lowered sensitivity because the new Input System reads raw mouse deltas
        public float mouseSensitivity = 0.1f; 

        private float rotationX = 0.0f;
        private float rotationY = 0.0f;

        void Start() {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            
            Vector3 angles = transform.eulerAngles;
            rotationX = angles.y;
            rotationY = angles.x;
        }

        void Update() {
            // 1. Mouse Look
            if (Mouse.current != null) {
                Vector2 mouseDelta = Mouse.current.delta.ReadValue();
                rotationX += mouseDelta.x * mouseSensitivity;
                rotationY -= mouseDelta.y * mouseSensitivity;
                rotationY = Mathf.Clamp(rotationY, -90f, 90f); 
                
                transform.localRotation = Quaternion.Euler(rotationY, rotationX, 0);
            }

            // 2. Keyboard Movement
            if (Keyboard.current != null) {
                float currentSpeed = Keyboard.current.leftShiftKey.isPressed ? sprintSpeed : normalSpeed;
                Vector3 direction = Vector3.zero;

                if (Keyboard.current.wKey.isPressed) direction.z += 1;
                if (Keyboard.current.sKey.isPressed) direction.z -= 1;
                if (Keyboard.current.aKey.isPressed) direction.x -= 1;
                if (Keyboard.current.dKey.isPressed) direction.x += 1;
                
                // Vertical movement
                if (Keyboard.current.spaceKey.isPressed) direction.y += 1;
                if (Keyboard.current.leftCtrlKey.isPressed) direction.y -= 1;

                // Apply translation
                transform.Translate(direction * currentSpeed * Time.deltaTime, Space.Self);
            }
        }
    }
}