using UnityEngine;

public class CelestialManager : MonoBehaviour
{
    public static CelestialManager Instance; // So the Render Feature can find it
    [Range(0, 24)] public float timeOfDay = 12f;
    public float daySpeed = 0.5f;
    public bool isAnimate = true;

    void Awake() { Instance = this; }

    void Update()
    {
        if (isAnimate && Application.isPlaying)
            timeOfDay = (timeOfDay + Time.deltaTime * daySpeed) % 24f;

        // Just calculate the vector, don't set it globally here
    }

    public Vector4 GetCelestialVector()
    {
        float angle = (timeOfDay / 24f) * 360f;
        Vector3 sunDir = Quaternion.Euler(angle - 90, 170, 0) * Vector3.forward;
        return new Vector4(sunDir.x, sunDir.y, sunDir.z, timeOfDay);
    }
}