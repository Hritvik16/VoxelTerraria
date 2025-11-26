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
        
        public float startHeightOffset = -5f; // Default to slightly below surface
        public float endHeightOffset = -5f;
        
        public float seed = 0f;
        
        [Header("Automatic Placement")]
        public FeatureSO startFeature;
        public FeatureSO endFeature;

        public override Vector3 GetConnectorPoint(WorldSettings settings)
        {
            // If we are connected, return our end point
            if (startFeature != null && endFeature != null)
            {
                Vector3 p2 = endFeature.GetConnectorPoint(settings);
                return p2;
            }
            // Otherwise return manual end point
            return new Vector3(centerXZ.x, endHeight, centerXZ.y + radius * 0.5f);
        }

        public override Feature ToFeature(WorldSettings settings)
        {
            float s = 0f;
            float c = 1f;

            // Automatic Placement Logic
            if (startFeature != null && endFeature != null)
            {
                Vector3 p1 = startFeature.GetConnectorPoint(settings);
                Vector3 p2 = endFeature.GetConnectorPoint(settings);

                // Calculate center and radius (length)
                float3 start = new float3(p1.x, p1.y, p1.z);
                float3 end   = new float3(p2.x, p2.y, p2.z);

                float dist = math.distance(start.xz, end.xz);
                float2 mid = (start.xz + end.xz) * 0.5f;

                // Calculate rotation (angle of the river vector)
                float2 dir = end.xz - start.xz;
                float angle = math.atan2(dir.y, dir.x); // Angle in radians
                
                // Rotation logic:
                // Target is Z axis (0, 1), which is PI/2.
                // So rotation = PI/2 - angle.
                float rotAngle = (math.PI / 2f) - angle;
                s = math.sin(rotAngle);
                c = math.cos(rotAngle);

                centerXZ = mid;
                radius = dist;
                startHeight = p1.y + startHeightOffset;
                endHeight = p2.y + endHeightOffset;
            }

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
    }
}
