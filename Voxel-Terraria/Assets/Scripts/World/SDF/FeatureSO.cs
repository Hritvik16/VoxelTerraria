using UnityEngine;

/// <summary>
/// Base class for ALL feature ScriptableObjects (Mountain, Lake, Forest, City).
/// Each feature type must override ToFeature() and return a fully-packed Feature struct.
/// 
/// NOTE:
///  - This class contains NO managed members inside Feature.
///  - This class is managed and only used at edit-time/bootstrap.
///  - SdfBootstrapInternal calls ToFeature() for every feature SO.
/// </summary>
public abstract class FeatureSO : ScriptableObject
{
    [SerializeField] private bool drawGizmos = true;
    public bool DrawGizmos => drawGizmos;

    /// <summary>
    /// Convert this ScriptableObject into a Burst-friendly unmanaged Feature struct.
    /// Must be implemented by all subclasses.
    /// </summary>
    public abstract Feature ToFeature(WorldSettings settings);

    /// <summary>
    /// Returns a world-space point that other features (like rivers) can connect to.
    /// Default: returns (0,0,0).
    /// </summary>
    public virtual Vector3 GetConnectorPoint(WorldSettings settings)
    {
        return Vector3.zero;
    }
}
