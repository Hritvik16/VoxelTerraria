using UnityEngine;

namespace VoxelTerraria.World
{
    public class VoxelChunkView : MonoBehaviour
    {
        [SerializeField] ChunkCoord3 coord3;

        public void SetCoord(ChunkCoord coord)
        {
            coord3 = coord.As3();
        }

        public void SetCoord(ChunkCoord3 c)
        {
            coord3 = c;
        }

        public ChunkCoord3 Coord3 => coord3;

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Vector3 pos = transform.position;
            Gizmos.DrawWireCube(pos + Vector3.one * 0.5f, Vector3.one);
        }
    }
}
