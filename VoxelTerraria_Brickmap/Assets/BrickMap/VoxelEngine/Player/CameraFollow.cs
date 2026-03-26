using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [Header("Target Settings")]
    public Transform target;
    public Vector3 offset = new Vector3(0, 0.6f, 0); // Default eye level offset

    [Header("Follow Settings")]
    public bool useSmoothing = false;
    public float smoothSpeed = 20f;

    void LateUpdate()
    {
        if (target == null) return;

        Vector3 targetPosition = target.position + target.TransformDirection(offset);
        
        if (useSmoothing)
        {
            transform.position = Vector3.Lerp(transform.position, targetPosition, smoothSpeed * Time.deltaTime);
        }
        else
        {
            transform.position = targetPosition;
        }
    }
}
