using UnityEngine;

[CreateAssetMenu(
    fileName = "ForestFeature",
    menuName = "VoxelTerraria/Features/Forest Feature",
    order = 2)]
public class ForestFeature : ScriptableObject
{
    [SerializeField] private bool drawGizmos = true;
    public bool DrawGizmos => drawGizmos;

    [SerializeField] private Vector2 centerXZ = Vector2.zero;

    [Header("Forest Shape")]
    [SerializeField] private float radius = 200f;

    [Header("Tree Density")]
    [SerializeField] private float treeDensity = 0.5f; // 0–1 normalized

    public Vector2 CenterXZ => centerXZ;
    public float Radius => radius;
    public float TreeDensity => treeDensity;
}
