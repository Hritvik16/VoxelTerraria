using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.World;

namespace VoxelEngine.Generation
{
    public static class PoissonDiskSampler
    {
        public static List<Vector2> GeneratePoissonDisk(float maxRadius, float radius, int k)
        {
            List<Vector2> points = new List<Vector2>();
            List<Vector2> active = new List<Vector2>();

            float cellSize = radius / Mathf.Sqrt(2);
            int gridSize = Mathf.CeilToInt((maxRadius * 2) / cellSize);
            int[,] grid = new int[gridSize, gridSize];
            for (int x = 0; x < gridSize; x++)
                for (int y = 0; y < gridSize; y++)
                    grid[x, y] = -1;

            Vector2 centerStart = Vector2.zero; 
            points.Add(centerStart);
            active.Add(centerStart);
            grid[gridSize / 2, gridSize / 2] = 0;

            while (active.Count > 0)
            {
                int spawnIndex = Random.Range(0, active.Count);
                Vector2 spawnCenter = active[spawnIndex];
                bool accepted = false;

                for (int i = 0; i < k; i++)
                {
                    float angle = Random.value * Mathf.PI * 2;
                    Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    Vector2 candidate = spawnCenter + dir * Random.Range(radius, 2 * radius);

                    if (IsValid(candidate, maxRadius, radius, cellSize, gridSize, grid, points))
                    {
                        points.Add(candidate);
                        active.Add(candidate);
                        
                        int cellX = Mathf.FloorToInt((candidate.x + maxRadius) / cellSize);
                        int cellY = Mathf.FloorToInt((candidate.y + maxRadius) / cellSize);
                        grid[cellX, cellY] = points.Count - 1;
                        accepted = true;
                        break;
                    }
                }
                if (!accepted) active.RemoveAt(spawnIndex);
            }
            return points;
        }

        private static bool IsValid(Vector2 candidate, float maxRadius, float radius, float cellSize, int gridSize, int[,] grid, List<Vector2> points)
        {
            float distFromCenter = candidate.magnitude;
            float coastDist = TerrainMath.GetCoastDistance(candidate, maxRadius);
            
            if (distFromCenter > coastDist - 150f) return false;

            int cellX = Mathf.FloorToInt((candidate.x + maxRadius) / cellSize);
            int cellY = Mathf.FloorToInt((candidate.y + maxRadius) / cellSize);

            if (cellX >= 0 && cellX < gridSize && cellY >= 0 && cellY < gridSize)
            {
                int searchStartX = Mathf.Max(0, cellX - 2);
                int searchEndX = Mathf.Min(cellX + 2, gridSize - 1);
                int searchStartY = Mathf.Max(0, cellY - 2);
                int searchEndY = Mathf.Min(cellY + 2, gridSize - 1);

                for (int x = searchStartX; x <= searchEndX; x++)
                {
                    for (int y = searchStartY; y <= searchEndY; y++)
                    {
                        int pointIndex = grid[x, y];
                        if (pointIndex != -1)
                        {
                            float sqrDst = (candidate - points[pointIndex]).sqrMagnitude;
                            if (sqrDst < radius * radius) return false;
                        }
                    }
                }
                return true;
            }
            return false;
        }
    }
}
