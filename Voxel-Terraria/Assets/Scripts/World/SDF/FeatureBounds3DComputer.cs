using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

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
    /// This class itself is FEATURE-AGNOSTIC:
    ///   • It does NOT know about mountains, islands, lakes, etc.
    ///   • It only calls delegates registered per FeatureType.
    ///
    /// Each feature type registers:
    ///   • An analytic bounds function:   (Feature, WorldSettings) -> center+halfExtents
    ///   • An SDF evaluation function:    (float3, Feature)        -> sdf
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
        // Delegate types for registry
        // ------------------------------------------------------------
        public delegate void AnalyticBoundsFunc(in Feature f, WorldSettings settings,
                                                out float3 center, out float3 halfExtents);

        public delegate float SdfEvalFunc(float3 p, in Feature f);

        // ------------------------------------------------------------
        // Registry: one entry per FeatureType
        // ------------------------------------------------------------
        private static readonly Dictionary<FeatureType, AnalyticBoundsFunc> s_boundsFuncs =
            new Dictionary<FeatureType, AnalyticBoundsFunc>();

        private static readonly Dictionary<FeatureType, SdfEvalFunc> s_sdfFuncs =
            new Dictionary<FeatureType, SdfEvalFunc>();

        /// <summary>
        /// Register a feature type with its analytic bounds and SDF evaluator.
        /// Call this once (e.g., from a static constructor in the feature's adapter).
        /// </summary>
        public static void Register(
            FeatureType type,
            AnalyticBoundsFunc boundsFunc,
            SdfEvalFunc sdfFunc)
        {
            if (boundsFunc != null)
                s_boundsFuncs[type] = boundsFunc;

            if (sdfFunc != null)
                s_sdfFuncs[type] = sdfFunc;
        }

        /// <summary>
        /// Computes an AABB for the given Feature using the registered
        /// analytic bounds + SDF sampling.
        ///
        /// If the feature type is not registered, returns aabb.valid = false.
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
            // 2. SDF sampling to tighten X/Z (but not Y)
            // --------------------------------------------------------
            const int GridResolution = 8; // 8^3 = 512 samples per feature

            for (int iz = 0; iz < GridResolution; iz++)
            {
                float tz = (iz + 0.5f) / GridResolution;
                float z = math.lerp(-halfExtents.z, halfExtents.z, tz);

                for (int iy = 0; iy < GridResolution; iy++)
                {
                    float ty = (iy + 0.5f) / GridResolution;
                    float y = math.lerp(-halfExtents.y, halfExtents.y, ty);

                    for (int ix = 0; ix < GridResolution; ix++)
                    {
                        float tx = (ix + 0.5f) / GridResolution;
                        float x = math.lerp(-halfExtents.x, halfExtents.x, tx);

                        float3 p = center + new float3(x, y, z);
                        float sdf = EvaluateSdf(p, in f);

                        if (sdf < 0f)
                        {
                            if (!aabb.valid)
                            {
                                aabb.valid = true;
                                aabb.min = aabb.max = p;
                            }
                            else
                            {
                                aabb.min = math.min(aabb.min, p);
                                aabb.max = math.max(aabb.max, p);
                            }
                        }
                    }
                }
            }

            // If sampling didn’t hit inside region, fall back to analytic box
            if (!aabb.valid)
            {
                aabb.valid = true;
                aabb.min = center - halfExtents;
                aabb.max = center + halfExtents;
            }
            else
            {
                // Restore vertical extents to analytic envelope:
                //   - sampling tightens X/Z,
                //   - analytic bounds ensure robust height coverage.
                aabb.min.y = analyticMinY;
                aabb.max.y = analyticMaxY;
            }

            // --------------------------------------------------------
            // 3. Voxel-size safety margin (prevents voxelization gaps)
            // --------------------------------------------------------
            float voxel = settings.voxelSize;
            float3 margin = new float3(voxel * 2f); // two voxels each side

            aabb.min -= margin;
            aabb.max += margin;

            return aabb;
        }

        /// <summary>
        /// Evaluate SDF for a single feature via the registry.
        /// If no SDF is registered, returns a large positive value (air).
        /// </summary>
        public static float EvaluateSdf(float3 p, in Feature f)
        {
            if (s_sdfFuncs.TryGetValue(f.type, out var func))
                return func(p, in f);

            return 9999f;
        }
    }
}
