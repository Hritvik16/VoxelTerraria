using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using VoxelTerraria.World.Generation;

namespace VoxelTerraria.World.SDF
{
    /// <summary>
    /// Axis-aligned bounding box for a single feature, in world space.
    /// </summary>
    public struct FeatureAabb
    {
        public float3 min;
        public float3 max;
        public bool valid;
    }

    /// <summary>
    /// Computes 3D bounds for a single Feature using a registry of
    /// feature-specific callbacks.
    ///
    /// This class itself is FEATURE-AGNOSTIC in terms of data;
    /// it does not store any SDF delegates (Burst can't handle that).
    ///
    /// Bounds strategy:
    ///   1. Use analytic bounds for a guaranteed safe box (no clipping).
    ///   2. Sample SDF on a small 3D grid inside that box to tighten X/Z.
    ///   3. Restore analytic Y (vertical) to keep a robust height envelope.
    ///   4. Expand by a small voxel-sized margin to avoid voxelization edge issues.
    /// </summary>
    public static class FeatureBounds3DComputer
    {
        // ------------------------------------------------------------
        // Delegate types for registry (bounds only — not used in Burst jobs)
        // ------------------------------------------------------------
        public delegate void AnalyticBoundsFunc(
            in Feature f,
            WorldSettings settings,
            out float3 center,
            out float3 halfExtents
        );

        // Registry: one entry per FeatureType (editor / bootstrap only)
        private static readonly Dictionary<FeatureType, AnalyticBoundsFunc> s_boundsFuncs =
            new Dictionary<FeatureType, AnalyticBoundsFunc>();

        /// <summary>
        /// Register a feature type with its analytic bounds function.
        /// Called from adapters (e.g., BaseIslandFeatureAdapter.EnsureRegistered()).
        /// </summary>
        public static void Register(
            FeatureType type,
            AnalyticBoundsFunc boundsFunc)
        {
            if (boundsFunc != null)
                s_boundsFuncs[type] = boundsFunc;
        }

        /// <summary>
        /// Burst-friendly SDF dispatch for geometry.
        /// No delegates, no dictionaries – just a switch on FeatureType.
        /// </summary>
        public static float EvaluateSdf_Fast(float3 p, in Feature f)
        {
            switch (f.type)
            {
                case FeatureType.BaseIsland:
                    return BaseIslandFeatureAdapter.Evaluate(p, in f);

                case FeatureType.Mountain:
                    return MountainFeatureAdapter.EvaluateShape(p, in f);

                case FeatureType.Volcano:
                    return VolcanoFeatureAdapter.EvaluateShape(p, in f);
                case FeatureType.River:
                    return RiverSdf.Evaluate(p, in f);
                // Add more feature types here as you create adapters.
                default:
                    return 9999f; // air
            }
        }

        /// <summary>
        /// Burst-friendly RAW SDF dispatch for biome logic.
        /// No delegates, no dictionaries – just a switch on FeatureType.
        /// </summary>
        // public static float EvaluateRaw_Fast(float3 p, in Feature f)
        // {
        //     switch (f.type)
        //     {
        //         case FeatureType.BaseIsland:
        //             return BaseIslandFeatureAdapter.EvaluateRaw(p, in f);

        //         case FeatureType.Mountain:
        //             return MountainFeatureAdapter.EvaluateRaw(p, in f);

        //         // Add more feature types here as you create adapters.
        //         default:
        //             return 9999f;
        //     }
        // }

        /// <summary>
        /// Computes an AABB for the given Feature using the registered
        /// analytic bounds + SDF sampling.
        ///
        /// If the feature type is not registered, returns aabb.valid = false.
        /// This is not used inside Burst jobs.
        /// </summary>
        public static FeatureAabb ComputeAabb(in Feature f, WorldSettings settings)
        {
            FeatureAabb aabb;
            aabb.valid = false;
            aabb.min = aabb.max = float3.zero;

            if (settings == null)
                return aabb;

            if (!s_boundsFuncs.TryGetValue(f.type, out var boundsFunc))
            {
                // No registered bounds function for this feature type.
                return aabb;
            }

            // --------------------------------------------------------
            // 1. Analytic safe region (guaranteed to contain feature)
            // --------------------------------------------------------
            boundsFunc(in f, settings, out float3 center, out float3 halfExtents);

            if (math.any(halfExtents <= 0f))
                halfExtents = new float3(8f, 8f, 8f); // fallback

            // Cache analytic Y extents so we can restore them later
            float analyticMinY = center.y - halfExtents.y;
            float analyticMaxY = center.y + halfExtents.y;

            // --------------------------------------------------------
            // 2. Use analytic bounds directly (safe, no sampling)
            // --------------------------------------------------------
            // We previously sampled the SDF to tighten bounds, but sparse sampling
            // can miss thin features or edges, causing cutoff.
            // Analytic bounds are guaranteed to contain the feature.
            aabb.valid = true;
            aabb.min = center - halfExtents;
            aabb.max = center + halfExtents;

            // --------------------------------------------------------
            // 3. Voxel-size safety margin (prevents voxelization gaps)
            // --------------------------------------------------------
            float voxel = settings.voxelSize;
            float3 margin = new float3(voxel * 2f); // two voxels each side

            aabb.min -= margin;
            aabb.max += margin;

            return aabb;
        }
    }
}
