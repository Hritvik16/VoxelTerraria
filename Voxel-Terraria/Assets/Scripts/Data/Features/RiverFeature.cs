using UnityEngine;
using Unity.Mathematics;

namespace VoxelTerraria.Data.Features
{
    [CreateAssetMenu(fileName = "NewRiverFeature", menuName = "VoxelTerraria/Features/River")]
    public class RiverFeature : FeatureSO
    {
        [Header("Position & Size")]
        public float2 centerXZ;
        public float radius = 100f;

        [Header("River Settings")]
        public float width = 10f;
        public float depth = 15f;
        public float meanderFrequency = 0.02f;
        public float meanderAmplitude = 50f;
        
        [Header("Vertical Path")]
        public float startHeight = 150f;
        public float endHeight = 30f;
        
        public float startHeightOffset = 10f; // Default to slightly above base
        public float endHeightOffset = 10f;
        
        public float seed = 0f;
        
        [Header("Automatic Placement")]
        [Tooltip("List of features to connect in order. If empty, and Randomize is on, generates a random river.")]
        public System.Collections.Generic.List<FeatureSO> manualPath;
        
        // Legacy fields removed/ignored
        // public FeatureSO startFeature;
        // public FeatureSO endFeature;

        public override Vector3 GetConnectorPoint(WorldSettings settings)
        {
            // If we are connected, return our end point
            // If we are connected, return our end point
            // if (startFeature != null && endFeature != null) ... REMOVED
            // {
            //     Vector3 p2 = endFeature.GetConnectorPoint(settings);
            //     return p2;
            // }
            // Otherwise return manual end point
            return new Vector3(centerXZ.x, endHeight, centerXZ.y + radius * 0.5f);
        }

        public override Feature ToFeature(WorldSettings settings)
        {
            float s = 0f;
            float c = 1f;

            // Automatic Placement Logic
            // Automatic Placement Logic is now handled by SdfBootstrap
            // which generates multiple segments for a chain.
            // This method now returns a "dummy" or "default" feature if called directly without context,
            // OR we can make it return a specific segment if we pass data in.
            
            // However, ToFeature signature is fixed: ToFeature(WorldSettings).
            // It returns ONE feature.
            // But a RiverChain is MANY features.
            
            // Solution:
            // SdfBootstrap will NOT call ToFeature() on RiverFeature directly for the chain.
            // Instead, SdfBootstrap will read the 'manualPath' and manually construct Feature structs.
            // OR, we add a helper method here: CreateSegment(FeatureSO start, FeatureSO end, WorldSettings settings).
            
            // For now, let's keep ToFeature returning a valid feature based on inspector values (Manual placement),
            // and ignore the automatic logic here.
            
            // if (startFeature != null && endFeature != null) ... REMOVED

            Feature f = new Feature();
            
            f.type = FeatureType.River;
            f.biomeId = 3; 
            f.blendMode = BlendMode.Subtract;

            // Pack data
            f.data0 = new float3(centerXZ.x, centerXZ.y, radius);
            f.data1 = new float3(width, depth, meanderFrequency);
            f.data2 = new float3(meanderAmplitude, startHeight, endHeight);
            
            // data3: x=seed, y=sin, z=cos
            f.data3 = new float3(seed, s, c);

            return f;
        }

        /// <summary>
        /// Helper to create a river segment connecting two points/features.
        /// </summary>
        public static Feature CreateSegment(
            Vector3 startPoint, 
            Vector3 endPoint, 
            RiverFeature template, 
            float seedOffset)
        {
            float width = template.width;
            float depth = template.depth;
            float meanderFreq = template.meanderFrequency;
            float meanderAmp = template.meanderAmplitude;
            
            // Calculate direction and length
            float2 p1 = new float2(startPoint.x, startPoint.z);
            float2 p2 = new float2(endPoint.x, endPoint.z);
            float dist = math.distance(p1, p2);
            float2 mid = (p1 + p2) * 0.5f;
            float2 dir = math.normalize(p2 - p1);
            
            // Angle
            float angle = math.atan2(dir.y, dir.x);
            float rotAngle = (math.PI / 2f) - angle;
            float s = math.sin(rotAngle);
            float c = math.cos(rotAngle);
            
            Feature f = new Feature();
            f.type = FeatureType.River;
            f.biomeId = 3;
            f.blendMode = BlendMode.Subtract;
            
            // Pack data
            f.data0 = new float3(mid.x, mid.y, dist);
            f.data1 = new float3(width, depth, meanderFreq);
            f.data2 = new float3(meanderAmp, startPoint.y, endPoint.y);
            
            // Mix seed
            f.data3 = new float3(template.seed + seedOffset, s, c);
            
            return f;
        }
    }
}
