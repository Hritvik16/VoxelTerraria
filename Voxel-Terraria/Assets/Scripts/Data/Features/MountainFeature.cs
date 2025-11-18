using UnityEngine;

[CreateAssetMenu(
    fileName = "MountainFeature",
    menuName = "VoxelTerraria/Features/Mountain Feature",
    order = 0)]
public class MountainFeature : ScriptableObject
{
    [SerializeField] private bool drawGizmos = true;
    public bool DrawGizmos => drawGizmos;
    [SerializeField] private Vector2 centerXZ = Vector2.zero;

    [Header("Shape")]
    [SerializeField] private float radius = 150f;
    [SerializeField] private float height = 120f;

    [Header("Ridge Noise")]
    [SerializeField] private float ridgeFrequency = 0.05f;
    [SerializeField] private float ridgeAmplitude = 1f;

    [Header("Warp")]
    [SerializeField] private float warpStrength = 30f;

    // Public properties (optional but good practice)
    public Vector2 CenterXZ => centerXZ;
    public float Radius => radius;
    public float Height => height;
    public float RidgeFrequency => ridgeFrequency;
    public float RidgeAmplitude => ridgeAmplitude;
    public float WarpStrength => warpStrength;
}
