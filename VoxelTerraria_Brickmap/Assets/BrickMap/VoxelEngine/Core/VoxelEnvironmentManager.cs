using UnityEngine;

[ExecuteAlways]
public class VoxelEnvironmentManager : MonoBehaviour
{
    [Header("Atmosphere Colors")]
    public Color zenithColor = new Color(0.2f, 0.4f, 0.8f);
    public Color horizonColor = new Color(0.6f, 0.8f, 1.0f);
    public Color sunHaloColor = new Color(1.0f, 0.9f, 0.7f);
    
    [Header("Fog & Distance")]
    [Range(0.01f, 10.0f)] public float hazeDensity = 2.5f;

    [Header("Lighting & Shadows")]
    public Light directionalSun;
    [Range(0.0f, 1.0f)] public float ambientStrength = 0.3f;
    [Range(0.0f, 1.0f)] public float shadowDarkness = 0.75f; 

    // void Update()
    // {
    //     // Blast the colors globally to all compute shaders instantly
    //     Shader.SetGlobalColor("_ZenithColor", zenithColor);
    //     Shader.SetGlobalColor("_HorizonColor", horizonColor);
    //     Shader.SetGlobalColor("_SunHaloColor", sunHaloColor);
        
    //     Shader.SetGlobalFloat("_HazeDensity", hazeDensity);
    //     Shader.SetGlobalFloat("_AmbientStrength", ambientStrength);
    //     Shader.SetGlobalFloat("_ShadowDarkness", shadowDarkness);
        
    //     // Ensure the sun direction is always synced
    //     if (directionalSun != null) {
    //         Vector4 sunDir = new Vector4(-directionalSun.transform.forward.x, -directionalSun.transform.forward.y, -directionalSun.transform.forward.z, 0).normalized;
    //         Shader.SetGlobalVector("_SunDir", sunDir);
    //     }
    // }
    void Update()
    {
        bool isSunOn = directionalSun != null && directionalSun.isActiveAndEnabled;

        // Default to night time
        float sunIntensity = 0.0f;
        Vector4 sunDir = new Vector4(0, -1, 0, 0);

        if (isSunOn) {
            // Unity's transform.forward is the direction the light travels.
            // If Y is negative, the sun is pointing down from the sky (Day).
            // If Y is positive, the sun is pointing up from under the ground (Night).
            float sunAngleY = -directionalSun.transform.forward.y; 
            
            // This calculates a fade multiplier. It is 1.0 at high noon, 
            // and smoothly fades to 0.0 as the sun hits the horizon.
            // The * 4.0f makes the sunset happen a bit faster right at the horizon line.
            sunIntensity = Mathf.Clamp01(sunAngleY * 4.0f);

            sunDir = new Vector4(-directionalSun.transform.forward.x, -directionalSun.transform.forward.y, -directionalSun.transform.forward.z, 0).normalized;
        }

        // --- DYNAMIC DAY/NIGHT LERPING ---
        // Smoothly transition from Pitch Black (Night) to your Inspector Colors (Day)
        Color currentZenith = Color.Lerp(new Color(0.01f, 0.01f, 0.03f), zenithColor, sunIntensity);
        Color currentHorizon = Color.Lerp(Color.black, horizonColor, sunIntensity);
        Color currentHalo = Color.Lerp(Color.black, sunHaloColor, sunIntensity);
        
        // Fade the ambient light down to almost zero at night
        float currentAmbient = Mathf.Lerp(0.0f, ambientStrength, sunIntensity);

        // Blast the dynamically calculated colors to the compute shader
        Shader.SetGlobalColor("_ZenithColor", currentZenith);
        Shader.SetGlobalColor("_HorizonColor", currentHorizon);
        Shader.SetGlobalColor("_SunHaloColor", currentHalo);
        
        Shader.SetGlobalFloat("_HazeDensity", hazeDensity);
        Shader.SetGlobalFloat("_AmbientStrength", currentAmbient);
        Shader.SetGlobalFloat("_ShadowDarkness", shadowDarkness);
        Shader.SetGlobalVector("_SunDir", sunDir);
    }
}
