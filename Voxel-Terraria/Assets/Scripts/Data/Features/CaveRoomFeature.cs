using UnityEngine;
using Unity.Mathematics;

namespace VoxelTerraria.Data.Features
{
    [CreateAssetMenu(fileName = "New Cave Room", menuName = "Voxel Terraria/Features/Cave Room")]
    public class CaveRoomFeature : FeatureSO
    {
        [Header("Room Settings")]
        public Vector2 CenterXZ;
        public float Radius = 20f;

        [Header("Noise Settings")]
        public float NoiseFrequency = 0.05f;
        public float NoiseAmplitude = 5f;
        public float Seed = 0f;



        public override Feature ToFeature(WorldSettings settings)
        {
            Feature f = new Feature();
            f.type = FeatureType.CaveRoom;
            f.blendMode = BlendMode.Subtract; // Rooms are holes
            f.centerXZ = new float2(CenterXZ.x, CenterXZ.y);
            
            // data0: Position (x, y, z)
            float y = GetBaseHeight(settings);
            f.data0 = new float3(CenterXZ.x, y, CenterXZ.y);

            // data1: Radius, NoiseFreq, NoiseAmp
            f.data1 = new float3(Radius, NoiseFrequency, NoiseAmplitude);

            // data2: Seed, unused, unused
            f.data2 = new float3(Seed, 0, 0);

            return f;
        }

        /// <summary>
        /// Helper to get a point on the surface of this room (approximate sphere).
        /// </summary>
        public Vector3 GetSurfacePoint(Vector3 direction)
        {
            float y = 0; // We don't have WorldSettings here easily, so this might be tricky if we need absolute Y.
            // Usually this is used for relative positioning or we need to pass settings.
            // For now, let's return relative to center (0,0,0) + radius.
            // The caller will add the center position.
            return direction.normalized * Radius;
        }
        
        public Vector3 GetWorldCenter(WorldSettings ws)
        {
             return new Vector3(CenterXZ.x, GetBaseHeight(ws), CenterXZ.y);
        }
    }
}
