using UnityEngine;
using Unity.Mathematics;

namespace VoxelTerraria.Data.Features
{
    [CreateAssetMenu(fileName = "New Cave Tunnel", menuName = "Voxel Terraria/Features/Cave Tunnel")]
    public class CaveTunnelFeature : FeatureSO
    {
        [Header("Connection")]
        public CaveRoomFeature StartRoom;
        
        [Tooltip("Direction from Start Room center (e.g., 1,0,0 is East)")]
        public Vector3 StartDirection = Vector3.right;
        
        [Header("Tunnel Settings")]
        public float Length = 50f;
        public float Radius = 8f;

        [Header("Noise Settings")]
        public float NoiseFrequency = 0.1f;
        public float NoiseAmplitude = 2f;
        public float Seed = 0f;

        public override Feature ToFeature(WorldSettings settings)
        {
            Feature f = new Feature();
            f.type = FeatureType.CaveTunnel;
            f.blendMode = BlendMode.Subtract; // Tunnels are holes
            
            // Calculate Start and End points
            Vector3 startPos = Vector3.zero;
            if (StartRoom != null)
            {
                startPos = StartRoom.GetWorldCenter(settings);
            }
            // If no start room, we default to 0,0,0 (or maybe we should have a manual start pos?)
            // For now, let's assume StartRoom is required for this specific implementation, 
            // or we add a manual override later.
            
            Vector3 direction = StartDirection.normalized;
            if (direction == Vector3.zero) direction = Vector3.right;

            Vector3 endPos = startPos + direction * Length;

            // Center of the tunnel (for bounding box)
            Vector3 center = (startPos + endPos) * 0.5f;
            f.centerXZ = new float2(center.x, center.z);

            // data0: Start Point (x, y, z)
            f.data0 = new float3(startPos.x, startPos.y, startPos.z);

            // data1: End Point (x, y, z)
            f.data1 = new float3(endPos.x, endPos.y, endPos.z);

            // data2: Radius, NoiseFreq, NoiseAmp
            f.data2 = new float3(Radius, NoiseFrequency, NoiseAmplitude);
            
            // data3: Seed, unused, unused
            f.data3 = new float3(Seed, 0, 0);

            return f;
        }
        
        public Vector3 GetWorldStart(WorldSettings ws)
        {
            if (StartRoom != null) return StartRoom.GetWorldCenter(ws);
            return Vector3.zero;
        }

        public Vector3 GetWorldEnd(WorldSettings ws)
        {
            Vector3 start = GetWorldStart(ws);
            Vector3 dir = StartDirection.normalized;
            if (dir == Vector3.zero) dir = Vector3.right;
            return start + dir * Length;
        }
    }
}
