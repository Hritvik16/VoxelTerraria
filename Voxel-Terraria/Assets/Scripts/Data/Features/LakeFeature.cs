using UnityEngine;

[CreateAssetMenu(
    fileName = "LakeFeature",
    menuName = "VoxelTerraria/Features/Lake Feature",
    order = 1)]
public class LakeFeature : ScriptableObject
{
    [SerializeField] private bool drawGizmos = true;
    public bool DrawGizmos => drawGizmos;
    [SerializeField] private Vector2 centerXZ = Vector2.zero;

    [Header("Lake Shape")]
    [SerializeField] private float radius = 120f;

    [Header("Heights")]
    [SerializeField] private float bottomHeight = -30f;
    [SerializeField] private float shoreHeight = 5f;

    public Vector2 CenterXZ => centerXZ;
    public float Radius => radius;
    public float BottomHeight => bottomHeight;
    public float ShoreHeight => shoreHeight;
}
