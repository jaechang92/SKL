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

    // ���� ����
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
    /// ���� �� ��ġ
    /// </summary>
    public RoomPlacement PlaceStartRoom()
    {
        JCDebug.Log("[RoomLayoutGenerator] ���� �� ��ġ ����");

        var startRoomData = GetRoomDataByType(RoomData.RoomType.Start);
        if (startRoomData == null)
        {
            JCDebug.Log("���� �� �����͸� ã�� �� �����ϴ�!", JCDebug.LogLevel.Error);
            return null;
        }

        // ���� ���� �� �߾� ��ó�� ��ġ
        Vector2Int startPosition = CalculateStartRoomPosition();
        var startPlacement = new RoomPlacement(startRoomData, startPosition);

        if (startPlacement.ValidatePlacement(gridSystem, placedRooms))
        {
            placedRooms.Add(startPlacement);
            MarkGridCellsOccupied(startPlacement);

            JCDebug.Log($"���� �� ��ġ �Ϸ�: {startPosition}");
            return startPlacement;
        }

        JCDebug.Log("���� �� ��ġ ����!", JCDebug.LogLevel.Error);
        return null;
    }

    /// <summary>
    /// �Ϲ� ��� ��ġ
    /// </summary>
    public List<RoomPlacement> PlaceNormalRooms()
    {
        JCDebug.Log("[RoomLayoutGenerator] �Ϲ� ��� ��ġ ����");

        var normalRooms = new List<RoomPlacement>();
        var normalRoomData = GetRoomDatasByType(RoomData.RoomType.Normal);

        if (normalRoomData.Count == 0)
        {
            JCDebug.Log("�Ϲ� �� �����Ͱ� �����ϴ�!", JCDebug.LogLevel.Warning);
            return normalRooms;
        }

        int targetRoomCount = Random.Range(levelData.minRooms, levelData.maxRooms + 1);
        int placedCount = 0;
        int attempts = 0;

        while (placedCount < targetRoomCount && attempts < maxPlacementAttempts)
        {
            attempts++;

            // ������ �Ϲ� �� ����
            var roomData = normalRoomData[random.Next(normalRoomData.Count)];

            // ��ġ ��ġ ã��
            Vector2Int position = FindValidPlacementPosition(roomData);

            if (position != Vector2Int.one * -1) // ��ȿ�� ��ġ�� ã������
            {
                var placement = new RoomPlacement(roomData, position);

                if (placement.ValidatePlacement(gridSystem, placedRooms))
                {
                    placedRooms.Add(placement);
                    normalRooms.Add(placement);
                    MarkGridCellsOccupied(placement);
                    placedCount++;

                    JCDebug.Log($"�Ϲ� �� ��ġ: {roomData.roomName} at {position}");
                }
            }
        }

        JCDebug.Log($"�Ϲ� �� ��ġ �Ϸ�: {placedCount}/{targetRoomCount}��");
        return normalRooms;
    }

    /// <summary>
    /// ���� �� ��ġ
    /// </summary>
    public RoomPlacement PlaceBossRoom()
    {
        JCDebug.Log("[RoomLayoutGenerator] ���� �� ��ġ ����");

        var bossRoomData = levelData.bossRoom;
        if (bossRoomData == null)
        {
            bossRoomData = GetRoomDataByType(RoomData.RoomType.Boss);
        }

        if (bossRoomData == null)
        {
            JCDebug.Log("���� �� �����͸� ã�� �� �����ϴ�!", JCDebug.LogLevel.Error);
            return null;
        }

        // ���� ���� ���۹濡�� ���� �� ��ġ�� ��ġ
        Vector2Int bossPosition = FindFarthestPositionFromStart(bossRoomData);

        if (bossPosition != Vector2Int.one * -1)
        {
            var bossPlacement = new RoomPlacement(bossRoomData, bossPosition);

            if (bossPlacement.ValidatePlacement(gridSystem, placedRooms))
            {
                placedRooms.Add(bossPlacement);
                MarkGridCellsOccupied(bossPlacement);

                JCDebug.Log($"���� �� ��ġ �Ϸ�: {bossPosition}");
                return bossPlacement;
            }
        }

        JCDebug.Log("���� �� ��ġ ����!", JCDebug.LogLevel.Error);
        return null;
    }

    /// <summary>
    /// Ư�� ��� ��ġ (������, ���� ��)
    /// </summary>
    public List<RoomPlacement> PlaceSpecialRooms()
    {
        JCDebug.Log("[RoomLayoutGenerator] Ư�� ��� ��ġ ����");

        var specialRooms = new List<RoomPlacement>();

        // ������ ��ġ
        var treasureRoom = PlaceTreasureRoom();
        if (treasureRoom != null)
        {
            specialRooms.Add(treasureRoom);
        }

        // ���� ��ġ
        var shopRoom = PlaceShopRoom();
        if (shopRoom != null)
        {
            specialRooms.Add(shopRoom);
        }

        // ��й� ��ġ (Ȯ����)
        if (random.NextDouble() < 0.3f) // 30% Ȯ��
        {
            var secretRoom = PlaceSecretRoom();
            if (secretRoom != null)
            {
                specialRooms.Add(secretRoom);
            }
        }

        JCDebug.Log($"Ư�� �� ��ġ �Ϸ�: {specialRooms.Count}��");
        return specialRooms;
    }

    private RoomPlacement PlaceTreasureRoom()
    {
        var treasureRoomData = levelData.treasureRoom ?? GetRoomDataByType(RoomData.RoomType.Treasure);
        if (treasureRoomData == null) return null;

        // �������� ���ٸ� �濡 ��ġ
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

        // ������ �����ϱ� ���� ��ġ�� ��ġ
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

        // ��й��� ������ ��ġ�� ��ġ
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

    #region ��ġ ��� �޼����

    private Vector2Int CalculateStartRoomPosition()
    {
        // �� �߾� ��ó���� ���� ��ġ
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

        return Vector2Int.one * -1; // ����
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
        // ���ٸ� �� ��ġ ã�� (���� ������ ������ 1���� ��ġ)
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

        return FindValidPlacementPosition(roomData); // ���ٸ� ���� ������ �Ϲ� ��ġ
    }

    private Vector2Int FindAccessiblePosition(RoomData roomData)
    {
        // �����ϱ� ���� ��ġ ã�� (���� ������ ������ 2-3���� ��ġ)
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
        // ������ ��ġ ã�� (�����ڸ� �Ǵ� ���� ��ġ)
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
        // �׸��� ���� Ȯ��
        if (position.x < 0 || position.y < 0 ||
            position.x + size.x > gridSystem.Width ||
            position.y + size.y > gridSystem.Height)
        {
            return false;
        }

        // �ٸ� ����� ��ħ Ȯ��
        Rect newRect = new Rect(position.x, position.y, size.x, size.y);

        foreach (var placement in placedRooms)
        {
            Rect existingRect = new Rect(placement.gridPosition.x, placement.gridPosition.y,
                                       placement.size.x, placement.size.y);

            // �ּ� ���� ����
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
                // �ش� ���⿡ ���� �ְų� ���� �������� Ȯ��
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

    #region ��ƿ��Ƽ �޼����

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