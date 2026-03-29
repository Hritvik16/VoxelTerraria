using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.World;
using VoxelEngine.Generation; // Added

namespace VoxelEngine
{
    public enum WorldSize { Small, Medium, Large }

    public class WorldManager : MonoBehaviour
    {
        public static WorldManager Instance;
        
        [Header("World Settings")]
        [Tooltip("Small: 1260m radius (~4,200 blocks wide)\nMedium: 1890m radius (~6,400 blocks wide)\nLarge: 2520m radius (~8,400 blocks wide)")]
        public WorldSize worldSize = WorldSize.Small;
        public int worldSeed = 1337;
        
        [Header("Blueprint State")]
        public List<FeatureAnchor> mapFeatures = new List<FeatureAnchor>();
        public ComputeBuffer featureBuffer;

        [Header("Underground State")]
        public List<CavernNode> cavernNodes = new List<CavernNode>();
        public ComputeBuffer cavernBuffer;
        
        public List<TunnelSpline> tunnelSplines = new List<TunnelSpline>();
        public ComputeBuffer tunnelBuffer;

        public float WorldRadiusXZ {
            get {
                switch (worldSize) {
                    case WorldSize.Medium: return 1890f;
                    case WorldSize.Large: return 2520f;
                    case WorldSize.Small:
                    default: return 1260f;
                }
            }
        }

        void Awake()
        {
            Instance = this;
            GenerateBlueprint();
        }

        public void GenerateBlueprint()
        {
            float sizeMultiplier = (worldSize == WorldSize.Small) ? 1.0f : ((worldSize == WorldSize.Medium) ? 1.5f : 2.0f);
            
            BlueprintGenerator.Generate(
                worldSeed, 
                WorldRadiusXZ, 
                sizeMultiplier, 
                mapFeatures, 
                cavernNodes, 
                tunnelSplines
            );

            if (featureBuffer != null) featureBuffer.Release();
            if (cavernBuffer != null) cavernBuffer.Release();
            if (tunnelBuffer != null) tunnelBuffer.Release();

            featureBuffer = new ComputeBuffer(Mathf.Max(1, mapFeatures.Count), 32);
            if (mapFeatures.Count > 0) featureBuffer.SetData(mapFeatures);
            Shader.SetGlobalBuffer("_FeatureAnchorBuffer", featureBuffer);
            Shader.SetGlobalInt("_FeatureCount", mapFeatures.Count);

            cavernBuffer = new ComputeBuffer(Mathf.Max(1, cavernNodes.Count), 32);
            if (cavernNodes.Count > 0) cavernBuffer.SetData(cavernNodes);
            Shader.SetGlobalBuffer("_CavernNodeBuffer", cavernBuffer);
            Shader.SetGlobalInt("_CavernCount", cavernNodes.Count);

            tunnelBuffer = new ComputeBuffer(Mathf.Max(1, tunnelSplines.Count), 32);
            if (tunnelSplines.Count > 0) tunnelBuffer.SetData(tunnelSplines);
            Shader.SetGlobalBuffer("_TunnelSplineBuffer", tunnelBuffer);
            Shader.SetGlobalInt("_TunnelCount", tunnelSplines.Count);

            Shader.SetGlobalFloat("_WorldRadiusXZ", WorldRadiusXZ);
            Shader.SetGlobalInt("_WorldSeed", worldSeed);
        }

        private void OnDestroy()
        {
            if (featureBuffer != null) featureBuffer.Release();
            if (cavernBuffer != null) cavernBuffer.Release();
            if (tunnelBuffer != null) tunnelBuffer.Release();
        }

        private void OnDrawGizmos()
        {
            if (!Application.isPlaying) return;

            foreach (var node in cavernNodes)
            {
                Gizmos.color = (node.cavernType == 0) ? new Color(0, 1, 0, 0.15f) : new Color(1, 0, 0, 0.4f);
                Gizmos.DrawWireSphere(node.position, node.radius);
            }

            Gizmos.color = Color.yellow;
            foreach (var tunnel in tunnelSplines)
            {
                Gizmos.DrawLine(tunnel.startPoint, tunnel.endPoint);
            }
        }
    }
}