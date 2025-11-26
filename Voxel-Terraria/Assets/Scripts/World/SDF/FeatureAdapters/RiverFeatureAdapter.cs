using Unity.Mathematics;
using VoxelTerraria.Data.Features;

namespace VoxelTerraria.World.SDF.FeatureAdapters
{
    public static class RiverFeatureAdapter
    {
        private static bool s_registered;

        public static void EnsureRegistered()
        {
            if (s_registered) return;

            FeatureBounds3DComputer.Register(
                FeatureType.River,
                ComputeAnalyticBounds
            );

            s_registered = true;
        }

        public static void Unpack(in Feature f, out RiverFeatureData data)
        {
            data = new RiverFeatureData
            {
                centerXZ       = new float2(f.data0.x, f.data0.y),
                length         = f.data0.z,
                
                width          = f.data1.x,
                depth          = f.data1.y,
                meanderFreq    = f.data1.z,
                
                meanderAmp     = f.data2.x,
                startHeight    = f.data2.y,
                endHeight      = f.data2.z,
                
                seed           = f.data3.x
            };
        }

        private static void ComputeAnalyticBounds(
            in Feature f,
            WorldSettings settings,
            out float3 center,
            out float3 halfExtents)
        {
            RiverFeatureData r;
            Unpack(in f, out r);

            // Unpack rotation (sin, cos) from data3
            // data3: x=seed, y=sin, z=cos
            float sinRot = f.data3.y;
            float cosRot = f.data3.z;

            // River in LOCAL space (aligned with Z):
            // Length is along Z. Width/Meander is along X.
            // Extents:
            float localExtX = r.meanderAmp + r.width * 2f;
            float localExtZ = r.length * 0.5f + r.width * 2f; // Add buffer
            
            // We need to rotate the 4 corners of the XZ rectangle and find the new AABB.
            // Corners: (+X, +Z), (+X, -Z), (-X, +Z), (-X, -Z)
            float2 c1 = new float2(localExtX, localExtZ);
            float2 c2 = new float2(localExtX, -localExtZ);
            float2 c3 = new float2(-localExtX, localExtZ);
            float2 c4 = new float2(-localExtX, -localExtZ);
            
            // Rotate function:
            // x' = x*cos - y*sin
            // y' = x*sin + y*cos
            // Wait, the rotation in RiverSdf was:
            // rotP.x = relP.x * cos - relP.y * sin
            // This rotates the POINT by -angle.
            // To rotate the SHAPE by +angle, we use the inverse rotation?
            // Or rather: The river is defined in a space that is rotated by 'angle' relative to world.
            // So to go from Local -> World, we rotate by 'angle'.
            // The matrix for rotating by 'angle' is:
            // x = x'*cos - y'*sin  (Wait, standard rot matrix is x cos - y sin, x sin + y cos)
            // Let's verify RiverSdf logic.
            // RiverSdf: rotatedP.x = relP.x * cos - relP.y * sin.
            // This is a rotation by -angle (if sin/cos are of angle).
            // Yes, we rotate World Point BACK to Local Frame.
            // So Local Frame is rotated by +angle relative to World.
            // So to get World Bounds from Local Bounds, we rotate Local corners by +angle.
            // Rotation by +angle:
            // x_world = x_local * cos - y_local * sin
            // z_world = x_local * sin + y_local * cos
            // Wait, RiverSdf used:
            // s = sin(PI/2 - angle) = cos(angle)
            // c = cos(PI/2 - angle) = sin(angle)
            // This is confusing. Let's stick to the stored s/c values.
            // In RiverSdf:
            // rot.x = p.x * c - p.y * s
            // rot.y = p.x * s + p.y * c
            // This transforms P_world to P_local.
            // So P_local = R * P_world.
            // So P_world = R_inv * P_local.
            // R = [ c  -s ]
            //     [ s   c ]
            // Inverse of rotation matrix is transpose:
            // R_inv = [ c   s ]
            //         [ -s  c ]
            // So:
            // x_world = x_local * c + z_local * s
            // z_world = x_local * -s + z_local * c
            // Let's use this to rotate the 4 corners.
            
            float3 min = new float3(float.MaxValue);
            float3 max = new float3(float.MinValue);
            
            float2[] corners = new float2[] { c1, c2, c3, c4 };
            foreach (var corner in corners)
            {
                // Rotate
                float x_world = corner.x * cosRot + corner.y * sinRot;
                float z_world = corner.x * -sinRot + corner.y * cosRot;
                
                min.x = math.min(min.x, x_world);
                min.z = math.min(min.z, z_world);
                max.x = math.max(max.x, x_world);
                max.z = math.max(max.z, z_world);
            }
            
            // Add centerXZ offset
            min.x += r.centerXZ.x;
            min.z += r.centerXZ.y;
            max.x += r.centerXZ.x;
            max.z += r.centerXZ.y;
            
            // Y bounds
            float minY = math.min(r.startHeight, r.endHeight);
            float maxY = math.max(r.startHeight, r.endHeight);
            float height = maxY - minY;
            float centerY = minY + height * 0.5f;
            float extY = height * 0.5f + r.depth * 2f;
            
            min.y = centerY - extY;
            max.y = centerY + extY;
            
            // Convert min/max to center/extents
            center = (min + max) * 0.5f;
            halfExtents = (max - min) * 0.5f;
        }
    }

    public struct RiverFeatureData
    {
        public float2 centerXZ;
        public float length;
        
        public float width;
        public float depth;
        public float meanderFreq;
        
        public float meanderAmp;
        public float startHeight;
        public float endHeight;
        
        public float seed;
    }
}
