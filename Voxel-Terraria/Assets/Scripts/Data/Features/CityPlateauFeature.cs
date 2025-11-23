using UnityEngine;

[CreateAssetMenu(
    fileName = "CityPlateauFeature",
    menuName = "VoxelTerraria/Features/City Plateau Feature",
    order = 3)]
public class CityPlateauFeature : ScriptableObject
{
    [SerializeField] private bool drawGizmos = true;
    public bool DrawGizmos => drawGizmos;

    [SerializeField] private Vector2 centerXZ = Vector2.zero;

    [Header("Plateau Shape")]
    [SerializeField] private float radius = 150f;

    [Header("Height")]
    [SerializeField] private float plateauHeight = 25f;

    public Vector2 CenterXZ => centerXZ;
    public float Radius => radius;
    public float PlateauHeight => plateauHeight;

    internal CityPlateauFeatureData ToData()
    {
        return new CityPlateauFeatureData
        {
            centerXZ      = centerXZ,
            radius        = radius,
            plateauHeight = plateauHeight
        };
    }
}
