using UnityEngine;

[CreateAssetMenu(
    fileName = "BiomeDefinition",
    menuName = "VoxelTerraria/Biomes/Biome Definition",
    order = 0)]
public class BiomeDefinition : ScriptableObject
{
    // ----------------------------------------------------------
    // Biome ID
    // ----------------------------------------------------------
    public enum BiomeID
    {
        Grassland,
        Forest,
        Mountain,
        LakeShore,
        City
    }

    [Header("Biome Identity")]
    [SerializeField] private BiomeID biomeId = BiomeID.Grassland;
    public BiomeID ID => biomeId;

    // ----------------------------------------------------------
    // Conditions
    // ----------------------------------------------------------

    [Header("Conditions")]
    [Tooltip("Min and max world height (post-SDF) where this biome is allowed.")]
    [SerializeField] private Vector2 heightRange = new Vector2(0f, 100f);

    [Tooltip("Min and max surface slope this biome can appear on.")]
    [SerializeField] private Vector2 slopeRange = new Vector2(0f, 45f);

    public Vector2 HeightRange => heightRange;
    public Vector2 SlopeRange  => slopeRange;

    // ----------------------------------------------------------
    // Color Profile
    // ----------------------------------------------------------

    [System.Serializable]
    public struct BiomeColorProfile
    {
        public Color baseColor;
        public Color detailColor;
        [Range(0f, 1f)]
        public float blendStrength;
    }

    [Header("Color Profile")]
    [SerializeField] private BiomeColorProfile colorProfile;

    public BiomeColorProfile Colors => colorProfile;

    // ----------------------------------------------------------
    // Atmosphere Profile
    // ----------------------------------------------------------

    [System.Serializable]
    public struct AtmosphereProfile
    {
        public Color fogColor;
        public float fogDensity;

        public Color ambientColor;
        [Range(0f, 5f)]
        public float ambientIntensity;

        public Color sunTint;
        [Range(0f, 3f)]
        public float sunIntensityMultiplier;
    }

    [Header("Atmosphere Profile")]
    [SerializeField] private AtmosphereProfile atmosphereProfile;

    public AtmosphereProfile Atmosphere => atmosphereProfile;

    // ----------------------------------------------------------
    // Future Ambient Audio
    // ----------------------------------------------------------

    [Header("Audio (Future)")]
    [SerializeField] private AudioClip ambientLoop;

    public AudioClip AmbientLoop => ambientLoop;
}
