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

    void Update()
    {
        // Blast the colors globally to all compute shaders instantly
        Shader.SetGlobalColor("_ZenithColor", zenithColor);
        Shader.SetGlobalColor("_HorizonColor", horizonColor);
        Shader.SetGlobalColor("_SunHaloColor", sunHaloColor);
        
        Shader.SetGlobalFloat("_HazeDensity", hazeDensity);
        Shader.SetGlobalFloat("_AmbientStrength", ambientStrength);
        Shader.SetGlobalFloat("_ShadowDarkness", shadowDarkness);
        
        // Ensure the sun direction is always synced
        if (directionalSun != null) {
            Vector4 sunDir = new Vector4(-directionalSun.transform.forward.x, -directionalSun.transform.forward.y, -directionalSun.transform.forward.z, 0).normalized;
            Shader.SetGlobalVector("_SunDir", sunDir);
        }
    }
}
