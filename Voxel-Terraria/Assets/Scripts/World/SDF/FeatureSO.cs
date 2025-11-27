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
    [SerializeField] private bool showGizmos = true;
    public bool ShowGizmos => showGizmos;

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

    /// <summary>
    /// Returns the base height of the feature (where it meets the ground/water).
    /// Used for river connections.
    /// </summary>
    public virtual float GetBaseHeight(WorldSettings settings)
    {
        return settings.seaLevel;
    }

    public virtual float GetRadius()
    {
        return 0f;
    }

    public virtual Vector2 GetCenter()
    {
        return Vector2.zero;
    }

    public virtual void DrawGizmos(WorldSettings settings)
    {
        // Optional override
    }
}
