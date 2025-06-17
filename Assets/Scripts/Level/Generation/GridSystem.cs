// Assets/Scripts/Level/Generation/GridSystem.cs
using UnityEngine;

public class GridSystem
{
    public int Width { get; private set; }
    public int Height { get; private set; }
    public float CellSize { get; private set; } = 10f;

    private bool[,] occupiedCells;

    public GridSystem(int width, int height, float cellSize = 10f)
    {
        Width = width;
        Height = height;
        CellSize = cellSize;
        occupiedCells = new bool[width, height];
    }

    public bool IsValidPosition(Vector2Int position)
    {
        return position.x >= 0 && position.x < Width &&
               position.y >= 0 && position.y < Height;
    }

    public bool IsCellOccupied(Vector2Int position)
    {
        if (!IsValidPosition(position)) return true;
        return occupiedCells[position.x, position.y];
    }

    public void SetCellOccupied(Vector2Int position, bool occupied)
    {
        if (IsValidPosition(position))
        {
            occupiedCells[position.x, position.y] = occupied;
        }
    }

    public Vector3 GridToWorldPosition(Vector2Int gridPosition)
    {
        return new Vector3(gridPosition.x * CellSize, gridPosition.y * CellSize, 0);
    }

    public Vector2Int WorldToGridPosition(Vector3 worldPosition)
    {
        return new Vector2Int(
            Mathf.RoundToInt(worldPosition.x / CellSize),
            Mathf.RoundToInt(worldPosition.y / CellSize)
        );
    }

    public void DrawGizmos()
    {
        Gizmos.color = Color.gray;
        for (int x = 0; x < Width; x++)
        {
            for (int y = 0; y < Height; y++)
            {
                Vector3 pos = GridToWorldPosition(new Vector2Int(x, y));
                Gizmos.DrawWireCube(pos, Vector3.one * CellSize * 0.9f);
            }
        }
    }
}