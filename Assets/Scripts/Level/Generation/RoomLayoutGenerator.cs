// Assets/Scripts/Level/Generation/RoomLayoutGenerator.cs
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using CustomDebug;

public class RoomLayoutGenerator
{
    private GridSystem gridSystem;
    private LevelData levelData;
    private List<RoomPlacement> placedRooms;
    private System.Random random;

    // 생성 설정
    private readonly int maxPlacementAttempts = 100;
    private readonly float minRoomSpacing = 1f;

    public RoomLayoutGenerator(GridSystem grid, LevelData data)
    {
        gridSystem = grid;
        levelData = data;
        placedRooms = new List<RoomPlacement>();
        random = new System.Random();
    }
    
    /// <summary>
    /// 시작 방 배치
    /// </summary>
    public RoomPlacement PlaceStartRoom()
    {
        JCDebug.Log("[RoomLayoutGenerator] 시작 방 배치 시작");

        var startRoomData = GetRoomDataByType(RoomData.RoomType.Start);
        if (startRoomData == null)
        {
            JCDebug.Log("시작 방 데이터를 찾을 수 없습니다!", JCDebug.LogLevel.Error);
            return null;
        }

        // 시작 방은 맵 중앙 근처에 배치
        Vector2Int startPosition = CalculateStartRoomPosition();
        var startPlacement = new RoomPlacement(startRoomData, new Vector2Int(10, 10));
        JCDebug.Log($"{startPlacement.gridPosition},gridSystem:{gridSystem.Width},{gridSystem.Height}", JCDebug.LogLevel.Custom);
        if (startPlacement.ValidatePlacement(gridSystem, placedRooms))
        {
            placedRooms.Add(startPlacement);
            MarkGridCellsOccupied(startPlacement);

            JCDebug.Log($"시작 방 배치 완료: {startPosition}");
            return startPlacement;
        }

        JCDebug.Log("시작 방 배치 실패!", JCDebug.LogLevel.Error);
        return null;
    }

    /// <summary>
    /// 일반 방들 배치
    /// </summary>
    public List<RoomPlacement> PlaceNormalRooms()
    {
        JCDebug.Log("[RoomLayoutGenerator] 일반 방들 배치 시작");

        var normalRooms = new List<RoomPlacement>();
        var normalRoomData = GetRoomDatasByType(RoomData.RoomType.Normal);

        if (normalRoomData.Count == 0)
        {
            JCDebug.Log("일반 방 데이터가 없습니다!", JCDebug.LogLevel.Warning);
            return normalRooms;
        }

        int targetRoomCount = Random.Range(levelData.minRooms, levelData.maxRooms + 1);
        int placedCount = 0;
        int attempts = 0;

        while (placedCount < targetRoomCount && attempts < maxPlacementAttempts)
        {
            attempts++;

            // 랜덤한 일반 방 선택
            var roomData = normalRoomData[random.Next(normalRoomData.Count)];

            // 배치 위치 찾기
            Vector2Int position = FindValidPlacementPosition(roomData);

            if (position != Vector2Int.one * -1) // 유효한 위치를 찾았으면
            {
                var placement = new RoomPlacement(roomData, position);

                if (placement.ValidatePlacement(gridSystem, placedRooms))
                {
                    placedRooms.Add(placement);
                    normalRooms.Add(placement);
                    MarkGridCellsOccupied(placement);
                    placedCount++;

                    JCDebug.Log($"일반 방 배치: {roomData.roomName} at {position}");
                }
            }
        }

        JCDebug.Log($"일반 방 배치 완료: {placedCount}/{targetRoomCount}개");
        return normalRooms;
    }

    /// <summary>
    /// 보스 방 배치
    /// </summary>
    public RoomPlacement PlaceBossRoom()
    {
        JCDebug.Log("[RoomLayoutGenerator] 보스 방 배치 시작");

        var bossRoomData = levelData.bossRoom;
        if (bossRoomData == null)
        {
            bossRoomData = GetRoomDataByType(RoomData.RoomType.Boss);
        }

        if (bossRoomData == null)
        {
            JCDebug.Log("보스 방 데이터를 찾을 수 없습니다!", JCDebug.LogLevel.Error);
            return null;
        }

        // 보스 방은 시작방에서 가장 먼 위치에 배치
        Vector2Int bossPosition = FindFarthestPositionFromStart(bossRoomData);

        if (bossPosition != Vector2Int.one * -1)
        {
            var bossPlacement = new RoomPlacement(bossRoomData, bossPosition);

            if (bossPlacement.ValidatePlacement(gridSystem, placedRooms))
            {
                placedRooms.Add(bossPlacement);
                MarkGridCellsOccupied(bossPlacement);

                JCDebug.Log($"보스 방 배치 완료: {bossPosition}");
                return bossPlacement;
            }
        }

        JCDebug.Log("보스 방 배치 실패!", JCDebug.LogLevel.Error);
        return null;
    }

    /// <summary>
    /// 특수 방들 배치 (보물방, 상점 등)
    /// </summary>
    public List<RoomPlacement> PlaceSpecialRooms()
    {
        JCDebug.Log("[RoomLayoutGenerator] 특수 방들 배치 시작");

        var specialRooms = new List<RoomPlacement>();

        // 보물방 배치
        var treasureRoom = PlaceTreasureRoom();
        if (treasureRoom != null)
        {
            specialRooms.Add(treasureRoom);
        }

        // 상점 배치
        var shopRoom = PlaceShopRoom();
        if (shopRoom != null)
        {
            specialRooms.Add(shopRoom);
        }

        // 비밀방 배치 (확률적)
        if (random.NextDouble() < 0.3f) // 30% 확률
        {
            var secretRoom = PlaceSecretRoom();
            if (secretRoom != null)
            {
                specialRooms.Add(secretRoom);
            }
        }

        JCDebug.Log($"특수 방 배치 완료: {specialRooms.Count}개");
        return specialRooms;
    }

    private RoomPlacement PlaceTreasureRoom()
    {
        var treasureRoomData = levelData.treasureRoom ?? GetRoomDataByType(RoomData.RoomType.Treasure);
        if (treasureRoomData == null) return null;

        // 보물방은 막다른 길에 배치
        Vector2Int position = FindDeadEndPosition(treasureRoomData);

        if (position != Vector2Int.one * -1)
        {
            var placement = new RoomPlacement(treasureRoomData, position);
            if (placement.ValidatePlacement(gridSystem, placedRooms))
            {
                placedRooms.Add(placement);
                MarkGridCellsOccupied(placement);
                return placement;
            }
        }

        return null;
    }

    private RoomPlacement PlaceShopRoom()
    {
        var shopRoomData = GetRoomDataByType(RoomData.RoomType.Shop);
        if (shopRoomData == null) return null;

        // 상점은 접근하기 쉬운 위치에 배치
        Vector2Int position = FindAccessiblePosition(shopRoomData);

        if (position != Vector2Int.one * -1)
        {
            var placement = new RoomPlacement(shopRoomData, position);
            if (placement.ValidatePlacement(gridSystem, placedRooms))
            {
                placedRooms.Add(placement);
                MarkGridCellsOccupied(placement);
                return placement;
            }
        }

        return null;
    }

    private RoomPlacement PlaceSecretRoom()
    {
        var secretRoomData = GetRoomDataByType(RoomData.RoomType.Secret);
        if (secretRoomData == null) return null;

        // 비밀방은 숨겨진 위치에 배치
        Vector2Int position = FindHiddenPosition(secretRoomData);

        if (position != Vector2Int.one * -1)
        {
            var placement = new RoomPlacement(secretRoomData, position);
            if (placement.ValidatePlacement(gridSystem, placedRooms))
            {
                placedRooms.Add(placement);
                MarkGridCellsOccupied(placement);
                return placement;
            }
        }

        return null;
    }

    #region 위치 계산 메서드들

    private Vector2Int CalculateStartRoomPosition()
    {
        // 맵 중앙 근처에서 랜덤 위치
        int centerX = gridSystem.Width / 2;
        int centerY = gridSystem.Height / 2;
        int range = Mathf.Min(gridSystem.Width, gridSystem.Height) / 4;

        return new Vector2Int(
            centerX + random.Next(-range, range + 1),
            centerY + random.Next(-range, range + 1)
        );
    }

    private Vector2Int FindValidPlacementPosition(RoomData roomData)
    {
        int attempts = 0;

        while (attempts < maxPlacementAttempts)
        {
            attempts++;

            Vector2Int position = new Vector2Int(
                random.Next(0, gridSystem.Width - roomData.roomSize.x),
                random.Next(0, gridSystem.Height - roomData.roomSize.y)
            );

            if (IsPositionValid(position, roomData.roomSize))
            {
                return position;
            }
        }

        return Vector2Int.one * -1; // 실패
    }

    private Vector2Int FindFarthestPositionFromStart(RoomData roomData)
    {
        var startRoom = placedRooms.FirstOrDefault(r => r.roomData?.roomType == RoomData.RoomType.Start);
        if (startRoom == null) return FindValidPlacementPosition(roomData);

        Vector2Int bestPosition = Vector2Int.one * -1;
        float maxDistance = 0f;

        for (int x = 0; x <= gridSystem.Width - roomData.roomSize.x; x++)
        {
            for (int y = 0; y <= gridSystem.Height - roomData.roomSize.y; y++)
            {
                Vector2Int position = new Vector2Int(x, y);

                if (IsPositionValid(position, roomData.roomSize))
                {
                    float distance = Vector2Int.Distance(position, startRoom.gridPosition);
                    if (distance > maxDistance)
                    {
                        maxDistance = distance;
                        bestPosition = position;
                    }
                }
            }
        }

        return bestPosition;
    }

    private Vector2Int FindDeadEndPosition(RoomData roomData)
    {
        // 막다른 길 위치 찾기 (연결 가능한 방향이 1개인 위치)
        for (int x = 0; x <= gridSystem.Width - roomData.roomSize.x; x++)
        {
            for (int y = 0; y <= gridSystem.Height - roomData.roomSize.y; y++)
            {
                Vector2Int position = new Vector2Int(x, y);

                if (IsPositionValid(position, roomData.roomSize))
                {
                    int possibleConnections = CountPossibleConnections(position);
                    if (possibleConnections == 1)
                    {
                        return position;
                    }
                }
            }
        }

        return FindValidPlacementPosition(roomData); // 막다른 길이 없으면 일반 위치
    }

    private Vector2Int FindAccessiblePosition(RoomData roomData)
    {
        // 접근하기 쉬운 위치 찾기 (연결 가능한 방향이 2-3개인 위치)
        for (int x = 0; x <= gridSystem.Width - roomData.roomSize.x; x++)
        {
            for (int y = 0; y <= gridSystem.Height - roomData.roomSize.y; y++)
            {
                Vector2Int position = new Vector2Int(x, y);

                if (IsPositionValid(position, roomData.roomSize))
                {
                    int possibleConnections = CountPossibleConnections(position);
                    if (possibleConnections >= 2 && possibleConnections <= 3)
                    {
                        return position;
                    }
                }
            }
        }

        return FindValidPlacementPosition(roomData);
    }

    private Vector2Int FindHiddenPosition(RoomData roomData)
    {
        // 숨겨진 위치 찾기 (가장자리 또는 고립된 위치)
        var edgePositions = new List<Vector2Int>();

        for (int x = 0; x <= gridSystem.Width - roomData.roomSize.x; x++)
        {
            for (int y = 0; y <= gridSystem.Height - roomData.roomSize.y; y++)
            {
                Vector2Int position = new Vector2Int(x, y);

                if (IsPositionValid(position, roomData.roomSize))
                {
                    bool isOnEdge = x == 0 || y == 0 ||
                                   x + roomData.roomSize.x >= gridSystem.Width ||
                                   y + roomData.roomSize.y >= gridSystem.Height;

                    if (isOnEdge)
                    {
                        edgePositions.Add(position);
                    }
                }
            }
        }

        return edgePositions.Count > 0 ?
               edgePositions[random.Next(edgePositions.Count)] :
               FindValidPlacementPosition(roomData);
    }

    private bool IsPositionValid(Vector2Int position, Vector2Int size)
    {
        // 그리드 범위 확인
        if (position.x < 0 || position.y < 0 ||
            position.x + size.x > gridSystem.Width ||
            position.y + size.y > gridSystem.Height)
        {
            return false;
        }

        // 다른 방과의 겹침 확인
        Rect newRect = new Rect(position.x, position.y, size.x, size.y);

        foreach (var placement in placedRooms)
        {
            Rect existingRect = new Rect(placement.gridPosition.x, placement.gridPosition.y,
                                       placement.size.x, placement.size.y);

            // 최소 간격 적용
            existingRect.x -= minRoomSpacing;
            existingRect.y -= minRoomSpacing;
            existingRect.width += minRoomSpacing * 2;
            existingRect.height += minRoomSpacing * 2;

            if (newRect.Overlaps(existingRect))
            {
                return false;
            }
        }

        return true;
    }

    private int CountPossibleConnections(Vector2Int position)
    {
        int connections = 0;
        Vector2Int[] directions = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

        foreach (var direction in directions)
        {
            Vector2Int checkPos = position + direction;

            if (gridSystem.IsValidPosition(checkPos))
            {
                // 해당 방향에 방이 있거나 연결 가능한지 확인
                bool hasRoomInDirection = placedRooms.Any(r =>
                    Vector2Int.Distance(r.gridPosition, checkPos) < 2f);

                if (hasRoomInDirection || !gridSystem.IsCellOccupied(checkPos))
                {
                    connections++;
                }
            }
        }

        return connections;
    }

    private void MarkGridCellsOccupied(RoomPlacement placement)
    {
        for (int x = 0; x < placement.size.x; x++)
        {
            for (int y = 0; y < placement.size.y; y++)
            {
                Vector2Int cellPos = placement.gridPosition + new Vector2Int(x, y);
                gridSystem.SetCellOccupied(cellPos, true);
            }
        }
    }

    #endregion

    #region 유틸리티 메서드들

    private RoomData GetRoomDataByType(RoomData.RoomType roomType)
    {
        return levelData.possibleRooms?.FirstOrDefault(r => r.roomType == roomType);
    }

    private List<RoomData> GetRoomDatasByType(RoomData.RoomType roomType)
    {
        return levelData.possibleRooms?.Where(r => r.roomType == roomType).ToList() ?? new List<RoomData>();
    }

    #endregion
}