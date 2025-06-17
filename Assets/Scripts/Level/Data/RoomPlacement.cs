// Assets/Scripts/Level/Data/RoomPlacement.cs
using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public class RoomPlacement
{
    [Header("Placement Info")]
    public RoomData roomData;
    public Vector2Int gridPosition;
    public Vector2Int size;
    public float rotation = 0f;

    [Header("Priority & Weight")]
    [Range(0f, 1f)]
    public float placementPriority = 0.5f;
    public int placementOrder = 0; // 0 = ���۹�, 999 = ������

    [Header("Connection Requirements")]
    public List<Vector2Int> requiredConnections = new List<Vector2Int>();
    public List<Vector2Int> optionalConnections = new List<Vector2Int>();
    public int minConnections = 1;
    public int maxConnections = 4;

    [Header("Placement Constraints")]
    public bool mustBeIsolated = false; // Ư���� �� (������ ��)
    public bool preferEdgePosition = false; // �����ڸ� ��ȣ
    public bool preferCenterPosition = false; // �߾� ��ȣ
    public float minDistanceFromStart = 0f;
    public float maxDistanceFromStart = float.MaxValue;

    [Header("Validation")]
    public bool isValid = true;
    public string invalidReason = "";

    // ������
    public RoomPlacement()
    {
        requiredConnections = new List<Vector2Int>();
        optionalConnections = new List<Vector2Int>();
    }

    public RoomPlacement(RoomData data, Vector2Int position) : this()
    {
        roomData = data;
        gridPosition = position;
        size = data != null ? data.roomSize : Vector2Int.one;

        SetPlacementOrderFromRoomType();
    }

    /// <summary>
    /// �� Ÿ�Կ� ���� ��ġ ���� ����
    /// </summary>
    private void SetPlacementOrderFromRoomType()
    {
        if (roomData == null) return;

        switch (roomData.roomType)
        {
            case RoomData.RoomType.Start:
                placementOrder = 0;
                placementPriority = 1f;
                minConnections = 1;
                maxConnections = 2;
                break;

            case RoomData.RoomType.Boss:
                placementOrder = 999;
                placementPriority = 1f;
                minConnections = 1;
                maxConnections = 1;
                preferEdgePosition = true;
                minDistanceFromStart = 5f;
                break;

            case RoomData.RoomType.Treasure:
                placementOrder = 800;
                placementPriority = 0.8f;
                minConnections = 1;
                maxConnections = 1;
                mustBeIsolated = true;
                minDistanceFromStart = 3f;
                break;

            case RoomData.RoomType.Shop:
                placementOrder = 600;
                placementPriority = 0.7f;
                minConnections = 1;
                maxConnections = 2;
                minDistanceFromStart = 2f;
                break;

            case RoomData.RoomType.Secret:
                placementOrder = 700;
                placementPriority = 0.3f;
                minConnections = 1;
                maxConnections = 1;
                mustBeIsolated = true;
                break;

            case RoomData.RoomType.Normal:
            default:
                placementOrder = 100 + Random.Range(0, 500);
                placementPriority = 0.5f;
                minConnections = 1;
                maxConnections = 4;
                break;
        }
    }

    /// <summary>
    /// ��ġ�� ��ȿ���� ����
    /// </summary>
    public bool ValidatePlacement(GridSystem grid, List<RoomPlacement> existingPlacements)
    {
        isValid = true;
        invalidReason = "";

        // �׸��� ���� ������ Ȯ��
        if (!IsWithinGridBounds(grid))
        {
            isValid = false;
            invalidReason = "Grid bounds exceeded";
            return false;
        }

        // �ٸ� ��� ��ġ���� Ȯ��
        if (HasOverlapWithExisting(existingPlacements))
        {
            isValid = false;
            invalidReason = "Overlaps with existing room";
            return false;
        }

        // ���۹���� �Ÿ� ���� Ȯ��
        if (!ValidateDistanceConstraints(existingPlacements))
        {
            isValid = false;
            invalidReason = "Distance constraints not met";
            return false;
        }

        return isValid;
    }

    private bool IsWithinGridBounds(GridSystem grid)
    {
        return gridPosition.x >= 0 && gridPosition.y >= 0 &&
               gridPosition.x + size.x <= grid.Width &&
               gridPosition.y + size.y <= grid.Height;
    }

    private bool HasOverlapWithExisting(List<RoomPlacement> existingPlacements)
    {
        Rect thisRect = new Rect(gridPosition.x, gridPosition.y, size.x, size.y);

        foreach (var placement in existingPlacements)
        {
            if (placement == this) continue;

            Rect otherRect = new Rect(placement.gridPosition.x, placement.gridPosition.y,
                                    placement.size.x, placement.size.y);

            if (thisRect.Overlaps(otherRect))
            {
                return true;
            }
        }

        return false;
    }

    private bool ValidateDistanceConstraints(List<RoomPlacement> existingPlacements)
    {
        var startRoom = existingPlacements.Find(p => p.roomData?.roomType == RoomData.RoomType.Start);
        if (startRoom == null) return true; // ���۹��� ������ ���� ����

        float distanceToStart = Vector2Int.Distance(gridPosition, startRoom.gridPosition);

        return distanceToStart >= minDistanceFromStart && distanceToStart <= maxDistanceFromStart;
    }

    /// <summary>
    /// ���� ����Ʈ �߰�
    /// </summary>
    public void AddRequiredConnection(Vector2Int connectionPoint)
    {
        if (!requiredConnections.Contains(connectionPoint))
        {
            requiredConnections.Add(connectionPoint);
        }
    }

    public void AddOptionalConnection(Vector2Int connectionPoint)
    {
        if (!optionalConnections.Contains(connectionPoint))
        {
            optionalConnections.Add(connectionPoint);
        }
    }

    /// <summary>
    /// �� ���� Ư�� ��ġ�� ���� �������� Ȯ��
    /// </summary>
    public bool CanConnectTo(Vector2Int targetPosition)
    {
        // ������ ��ġ���� Ȯ�� (�����¿�)
        int deltaX = Mathf.Abs(gridPosition.x - targetPosition.x);
        int deltaY = Mathf.Abs(gridPosition.y - targetPosition.y);

        return (deltaX == 1 && deltaY == 0) || (deltaX == 0 && deltaY == 1);
    }

    /// <summary>
    /// ��ġ ���� ��� (AI ��ġ �˰����)
    /// </summary>
    public float CalculatePlacementScore(GridSystem grid, List<RoomPlacement> existingPlacements)
    {
        if (!isValid) return -1f;

        float score = placementPriority;

        // �����ڸ� ��ȣ��
        if (preferEdgePosition)
        {
            bool isOnEdge = gridPosition.x == 0 || gridPosition.y == 0 ||
                           gridPosition.x + size.x >= grid.Width ||
                           gridPosition.y + size.y >= grid.Height;
            score += isOnEdge ? 0.3f : -0.2f;
        }

        // �߾� ��ȣ��
        if (preferCenterPosition)
        {
            Vector2 gridCenter = new Vector2(grid.Width / 2f, grid.Height / 2f);
            float distanceFromCenter = Vector2.Distance(gridPosition, gridCenter);
            float normalizedDistance = distanceFromCenter / (grid.Width + grid.Height);
            score += (1f - normalizedDistance) * 0.3f;
        }

        // �� �䱸����
        if (mustBeIsolated)
        {
            int nearbyRooms = CountNearbyRooms(existingPlacements, 2);
            score += nearbyRooms == 0 ? 0.4f : -0.5f;
        }

        return score;
    }

    private int CountNearbyRooms(List<RoomPlacement> placements, int radius)
    {
        int count = 0;
        foreach (var placement in placements)
        {
            if (placement == this) continue;

            float distance = Vector2Int.Distance(gridPosition, placement.gridPosition);
            if (distance <= radius)
            {
                count++;
            }
        }
        return count;
    }

    public override string ToString()
    {
        return $"RoomPlacement[{roomData?.roomName}] at ({gridPosition.x},{gridPosition.y}) " +
               $"Order:{placementOrder} Priority:{placementPriority:F2} Valid:{isValid}";
    }
}