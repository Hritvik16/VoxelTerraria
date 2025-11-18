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

    [Header("City Plateau")]
    [SerializeField] private float radius = 180f;
    [SerializeField] private float plateauHeight = 30f;

    public Vector2 CenterXZ => centerXZ;
    public float Radius => radius;
    public float PlateauHeight => plateauHeight;
}
