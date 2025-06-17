// Assets/Scripts/Level/Data/RoomPlacement.cs
using UnityEngine;
using System.Collections.Generic;
using CustomDebug;

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
    public int placementOrder = 0; // 0 = 시작방, 999 = 보스방

    [Header("Connection Requirements")]
    public List<Vector2Int> requiredConnections = new List<Vector2Int>();
    public List<Vector2Int> optionalConnections = new List<Vector2Int>();
    public int minConnections = 1;
    public int maxConnections = 4;

    [Header("Placement Constraints")]
    public bool mustBeIsolated = false; // 특별한 방 (보물방 등)
    public bool preferEdgePosition = false; // 가장자리 선호
    public bool preferCenterPosition = false; // 중앙 선호
    public float minDistanceFromStart = 0f;
    public float maxDistanceFromStart = float.MaxValue;

    [Header("Validation")]
    public bool isValid = true;
    public string invalidReason = "";

    // 생성자
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
    /// 방 타입에 따른 배치 순서 설정
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
    /// 배치가 유효한지 검증
    /// </summary>
    public bool ValidatePlacement(GridSystem grid, List<RoomPlacement> existingPlacements)
    {
        isValid = true;
        invalidReason = "";

        // 그리드 범위 내인지 확인
        if (!IsWithinGridBounds(grid))
        {
            isValid = false;
            invalidReason = "Grid bounds exceeded";
            return false;
        }

        // 다른 방과 겹치는지 확인
        if (HasOverlapWithExisting(existingPlacements))
        {
            isValid = false;
            invalidReason = "Overlaps with existing room";
            return false;
        }

        // 시작방과의 거리 제약 확인
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
        JCDebug.Log($"{gridPosition.x + size.x <= grid.Width} && {gridPosition.y + size.y <= grid.Height}", JCDebug.LogLevel.Custom);

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
        if (startRoom == null) return true; // 시작방이 없으면 제약 없음

        float distanceToStart = Vector2Int.Distance(gridPosition, startRoom.gridPosition);

        return distanceToStart >= minDistanceFromStart && distanceToStart <= maxDistanceFromStart;
    }

    /// <summary>
    /// 연결 포인트 추가
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
    /// 이 방이 특정 위치에 연결 가능한지 확인
    /// </summary>
    public bool CanConnectTo(Vector2Int targetPosition)
    {
        // 인접한 위치인지 확인 (상하좌우)
        int deltaX = Mathf.Abs(gridPosition.x - targetPosition.x);
        int deltaY = Mathf.Abs(gridPosition.y - targetPosition.y);

        return (deltaX == 1 && deltaY == 0) || (deltaX == 0 && deltaY == 1);
    }

    /// <summary>
    /// 배치 점수 계산 (AI 배치 알고리즘용)
    /// </summary>
    public float CalculatePlacementScore(GridSystem grid, List<RoomPlacement> existingPlacements)
    {
        if (!isValid) return -1f;

        float score = placementPriority;

        // 가장자리 선호도
        if (preferEdgePosition)
        {
            bool isOnEdge = gridPosition.x == 0 || gridPosition.y == 0 ||
                           gridPosition.x + size.x >= grid.Width ||
                           gridPosition.y + size.y >= grid.Height;
            score += isOnEdge ? 0.3f : -0.2f;
        }

        // 중앙 선호도
        if (preferCenterPosition)
        {
            Vector2 gridCenter = new Vector2(grid.Width / 2f, grid.Height / 2f);
            float distanceFromCenter = Vector2.Distance(gridPosition, gridCenter);
            float normalizedDistance = distanceFromCenter / (grid.Width + grid.Height);
            score += (1f - normalizedDistance) * 0.3f;
        }

        // 고립 요구사항
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