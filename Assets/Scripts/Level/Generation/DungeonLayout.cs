// Assets/Scripts/Level/Generation/DungeonLayout.cs ����
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using CustomDebug;

[System.Serializable]
public class DungeonLayout : MonoBehaviour
{
    [Header("Dungeon Info")]
    [SerializeField] private string dungeonId;
    [SerializeField] private string dungeonName;
    [SerializeField] private Vector2Int dungeonSize;
    [SerializeField] private int totalRoomCount;

    [Header("Room Collections")]
    [SerializeField] private List<GeneratedRoom> allRooms = new List<GeneratedRoom>();
    [SerializeField] private List<GeneratedRoom> visitedRooms = new List<GeneratedRoom>();
    [SerializeField] private List<GeneratedRoom> clearedRooms = new List<GeneratedRoom>();

    [Header("Special Rooms")]
    [SerializeField] private GeneratedRoom startRoom;
    [SerializeField] private GeneratedRoom bossRoom;
    [SerializeField] private List<GeneratedRoom> treasureRooms = new List<GeneratedRoom>();
    [SerializeField] private List<GeneratedRoom> shopRooms = new List<GeneratedRoom>();
    [SerializeField] private List<GeneratedRoom> secretRooms = new List<GeneratedRoom>();

    [Header("Layout State")]
    [SerializeField] private bool isCompleted = false;
    [SerializeField] private float completionPercentage = 0f;
    [SerializeField] private GeneratedRoom currentPlayerRoom;

    [Header("Navigation Data")]
    [SerializeField] private List<Corridor> allCorridors = new List<Corridor>();
    private Dictionary<GeneratedRoom, List<GeneratedRoom>> roomConnections;
    private Dictionary<Vector2Int, GeneratedRoom> gridToRoomMap;

    // �̺�Ʈ
    public System.Action<DungeonLayout> OnLayoutInitialized;
    public System.Action<GeneratedRoom> OnRoomDiscovered;
    public System.Action<GeneratedRoom> OnRoomCleared;
    public System.Action<DungeonLayout> OnDungeonCompleted;
    public System.Action<float> OnCompletionPercentageChanged;

    // Properties
    public int TotalRoomCount => totalRoomCount;
    public int VisitedRoomCount => visitedRooms.Count;
    public int ClearedRoomCount => clearedRooms.Count;
    public float CompletionPercentage => completionPercentage;
    public bool IsCompleted => isCompleted;
    public GeneratedRoom CurrentPlayerRoom => currentPlayerRoom;
    public GeneratedRoom StartRoom => startRoom;
    public GeneratedRoom BossRoom => bossRoom;

    private void Awake()
    {
        roomConnections = new Dictionary<GeneratedRoom, List<GeneratedRoom>>();
        gridToRoomMap = new Dictionary<Vector2Int, GeneratedRoom>();
    }

    /// <summary>
    /// ���� ���̾ƿ� �ʱ�ȭ
    /// </summary>
    public void Initialize(List<GeneratedRoom> rooms, GridSystem gridSystem)
    {
        allRooms = new List<GeneratedRoom>(rooms);
        totalRoomCount = allRooms.Count;
        dungeonSize = new Vector2Int(gridSystem.Width, gridSystem.Height);

        // Ư�� ��� �з�
        CategorizeRooms();

        // ���� ���� ����
        BuildConnectionMap();

        // �׸��� �� ����
        BuildGridMap();

        // �� �̺�Ʈ ����
        SubscribeToRoomEvents();

        // �ʱ� �Ϸ��� ���
        UpdateCompletionPercentage();

        OnLayoutInitialized?.Invoke(this);

        JCDebug.Log($"[DungeonLayout] ���� ���̾ƿ� �ʱ�ȭ �Ϸ�: {totalRoomCount}�� ��");
    }

    /// <summary>
    /// ����� Ÿ�Ժ��� �з�
    /// </summary>
    private void CategorizeRooms()
    {
        treasureRooms.Clear();
        shopRooms.Clear();
        secretRooms.Clear();

        foreach (var room in allRooms)
        {
            if (room.roomData == null) continue;

            switch (room.roomData.roomType)
            {
                case RoomData.RoomType.Start:
                    startRoom = room;
                    break;
                case RoomData.RoomType.Boss:
                    bossRoom = room;
                    break;
                case RoomData.RoomType.Treasure:
                    treasureRooms.Add(room);
                    break;
                case RoomData.RoomType.Shop:
                    shopRooms.Add(room);
                    break;
                case RoomData.RoomType.Secret:
                    secretRooms.Add(room);
                    break;
            }
        }

        JCDebug.Log($"[DungeonLayout] �� �з� �Ϸ� - ������:{treasureRooms.Count}, ����:{shopRooms.Count}, ��й�:{secretRooms.Count}");
    }

    /// <summary>
    /// �� �� ���� ���� ����
    /// </summary>
    private void BuildConnectionMap()
    {
        roomConnections.Clear();

        foreach (var room in allRooms)
        {
            roomConnections[room] = new List<GeneratedRoom>(room.connectedRooms);
        }

        // ���� ���� ����
        allCorridors.Clear();
        foreach (var room in allRooms)
        {
            foreach (var corridor in room.corridors)
            {
                if (!allCorridors.Contains(corridor))
                {
                    allCorridors.Add(corridor);
                }
            }
        }

        JCDebug.Log($"[DungeonLayout] ���� �� ���� �Ϸ�: {allCorridors.Count}�� ����");
    }

    /// <summary>
    /// �׸��� ��ġ�� �� ���� ����
    /// </summary>
    private void BuildGridMap()
    {
        gridToRoomMap.Clear();

        foreach (var room in allRooms)
        {
            // ���� �����ϴ� ��� �׸��� ���� ���� ����
            for (int x = 0; x < room.roomData.roomSize.x; x++)
            {
                for (int y = 0; y < room.roomData.roomSize.y; y++)
                {
                    Vector2Int gridPos = room.gridPosition + new Vector2Int(x, y);
                    gridToRoomMap[gridPos] = room;
                }
            }
        }
    }

    /// <summary>
    /// �� �̺�Ʈ�鿡 ����
    /// </summary>
    private void SubscribeToRoomEvents()
    {
        foreach (var room in allRooms)
        {
            if (room.roomComponent != null)
            {
                room.roomComponent.OnRoomEntered += HandleRoomEntered;
                room.roomComponent.OnRoomCleared += HandleRoomCleared;
                room.roomComponent.OnRoomExited += HandleRoomExited;
            }
        }
    }

    /// <summary>
    /// �� ���� ó��
    /// </summary>
    private void HandleRoomEntered(Room roomComponent)
    {
        var generatedRoom = allRooms.FirstOrDefault(r => r.roomComponent == roomComponent);
        if (generatedRoom == null) return;

        // ���� �÷��̾� �� ������Ʈ
        currentPlayerRoom = generatedRoom;

        // ó�� �湮�ϴ� ������ Ȯ��
        if (!generatedRoom.isVisited)
        {
            generatedRoom.isVisited = true;
            visitedRooms.Add(generatedRoom);
            OnRoomDiscovered?.Invoke(generatedRoom);

            JCDebug.Log($"[DungeonLayout] ���ο� �� �߰�: {generatedRoom.roomData?.roomName}");
        }

        UpdateCompletionPercentage();
    }

    /// <summary>
    /// �� Ŭ���� ó��
    /// </summary>
    private void HandleRoomCleared(Room roomComponent)
    {
        var generatedRoom = allRooms.FirstOrDefault(r => r.roomComponent == roomComponent);
        if (generatedRoom == null) return;

        if (!generatedRoom.isCleared)
        {
            generatedRoom.isCleared = true;
            clearedRooms.Add(generatedRoom);
            OnRoomCleared?.Invoke(generatedRoom);

            JCDebug.Log($"[DungeonLayout] �� Ŭ����: {generatedRoom.roomData?.roomName}");

            // ���� �� Ŭ���� �� ���� �Ϸ�
            if (generatedRoom == bossRoom)
            {
                CompleteDungeon();
            }
        }

        UpdateCompletionPercentage();
    }

    /// <summary>
    /// �� ���� ó��
    /// </summary>
    private void HandleRoomExited(Room roomComponent)
    {
        // ����� Ư���� ó�� ����
        // �ʿ�� �߰� ���� ����
    }

    /// <summary>
    /// �Ϸ��� ������Ʈ
    /// </summary>
    private void UpdateCompletionPercentage()
    {
        if (totalRoomCount == 0)
        {
            completionPercentage = 0f;
            return;
        }

        // �湮�� �� 50% + Ŭ������ �� 50%
        float visitedWeight = 0.3f;
        float clearedWeight = 0.7f;

        float visitedPercent = (float)visitedRooms.Count / totalRoomCount;
        float clearedPercent = (float)clearedRooms.Count / totalRoomCount;

        float newPercentage = (visitedPercent * visitedWeight + clearedPercent * clearedWeight) * 100f;

        if (!Mathf.Approximately(completionPercentage, newPercentage))
        {
            completionPercentage = newPercentage;
            OnCompletionPercentageChanged?.Invoke(completionPercentage);
        }
    }

    /// <summary>
    /// ���� �Ϸ� ó��
    /// </summary>
    private void CompleteDungeon()
    {
        isCompleted = true;
        completionPercentage = 100f;
        OnDungeonCompleted?.Invoke(this);

        JCDebug.Log("[DungeonLayout] ���� �Ϸ�!");
    }

    #region Public Query Methods

    public GeneratedRoom GetStartRoom()
    {
        return startRoom;
    }

    /// <summary>
    /// ��� �� ��� ��ȯ
    /// </summary>
    public List<GeneratedRoom> GetAllRooms()
    {
        return new List<GeneratedRoom>(allRooms);
    }

    /// <summary>
    /// Ư�� Ÿ���� ��� ��ȯ
    /// </summary>
    public List<GeneratedRoom> GetRoomsByType(RoomData.RoomType roomType)
    {
        return allRooms.Where(r => r.roomData?.roomType == roomType).ToList();
    }

    /// <summary>
    /// �׸��� ��ġ�� �� ��ȯ
    /// </summary>
    public GeneratedRoom GetRoomAtPosition(Vector2Int gridPosition)
    {
        return gridToRoomMap.TryGetValue(gridPosition, out GeneratedRoom room) ? room : null;
    }

    /// <summary>
    /// Ư�� ��� ����� ��� ��ȯ
    /// </summary>
    public List<GeneratedRoom> GetConnectedRooms(GeneratedRoom room)
    {
        return roomConnections.TryGetValue(room, out List<GeneratedRoom> connections) ?
               new List<GeneratedRoom>(connections) : new List<GeneratedRoom>();
    }

    /// <summary>
    /// �� �� ��� ã��
    /// </summary>
    public List<GeneratedRoom> FindPath(GeneratedRoom from, GeneratedRoom to)
    {
        if (from == null || to == null) return new List<GeneratedRoom>();

        var visited = new HashSet<GeneratedRoom>();
        var queue = new Queue<(GeneratedRoom room, List<GeneratedRoom> path)>();
        queue.Enqueue((from, new List<GeneratedRoom> { from }));

        while (queue.Count > 0)
        {
            var (current, path) = queue.Dequeue();

            if (current == to)
            {
                return path;
            }

            if (visited.Contains(current)) continue;
            visited.Add(current);

            foreach (var neighbor in GetConnectedRooms(current))
            {
                if (!visited.Contains(neighbor))
                {
                    var newPath = new List<GeneratedRoom>(path) { neighbor };
                    queue.Enqueue((neighbor, newPath));
                }
            }
        }

        return new List<GeneratedRoom>(); // ��� ����
    }

    /// <summary>
    /// ���۹濡�� Ư�� ������� �Ÿ�
    /// </summary>
    public int GetDistanceFromStart(GeneratedRoom targetRoom)
    {
        if (startRoom == null || targetRoom == null) return -1;

        var path = FindPath(startRoom, targetRoom);
        return path.Count > 0 ? path.Count - 1 : -1;
    }

    /// <summary>
    /// ���� �湮���� ���� ���
    /// </summary>
    public List<GeneratedRoom> GetUnvisitedRooms()
    {
        return allRooms.Where(r => !r.isVisited).ToList();
    }

    /// <summary>
    /// �湮������ Ŭ�������� ���� ���
    /// </summary>
    public List<GeneratedRoom> GetUnclearedRooms()
    {
        return allRooms.Where(r => r.isVisited && !r.isCleared).ToList();
    }

    /// <summary>
    /// ������� ��ȯ
    /// </summary>
    public List<GeneratedRoom> GetTreasureRooms()
    {
        return new List<GeneratedRoom>(treasureRooms);
    }

    /// <summary>
    /// ������ ��ȯ
    /// </summary>
    public List<GeneratedRoom> GetShopRooms()
    {
        return new List<GeneratedRoom>(shopRooms);
    }

    /// <summary>
    /// ��й�� ��ȯ
    /// </summary>
    public List<GeneratedRoom> GetSecretRooms()
    {
        return new List<GeneratedRoom>(secretRooms);
    }

    #endregion

    #region Debug & Utility

    /// <summary>
    /// ���� ���� ����� ���
    /// </summary>
    [ContextMenu("Print Dungeon Status")]
    public void PrintDungeonStatus()
    {
        JCDebug.Log($"=== Dungeon Layout Status ===\n" +
                 $"Total Rooms: {totalRoomCount}\n" +
                 $"Visited: {visitedRooms.Count}\n" +
                 $"Cleared: {clearedRooms.Count}\n" +
                 $"Completion: {completionPercentage:F1}%\n" +
                 $"Current Room: {currentPlayerRoom?.roomData?.roomName ?? "None"}\n" +
                 $"Is Completed: {isCompleted}");
    }

    /// <summary>
    /// ���� ���� ����� ���
    /// </summary>
    [ContextMenu("Print Connection Info")]
    public void PrintConnectionInfo()
    {
        JCDebug.Log("=== Room Connections ===");
        foreach (var room in allRooms)
        {
            var connections = GetConnectedRooms(room);
            string connectionNames = string.Join(", ", connections.Select(r => r.roomData?.roomName ?? "Unknown"));
            JCDebug.Log($"{room.roomData?.roomName}: [{connectionNames}]");
        }
    }

    private void OnDestroy()
    {
        // �̺�Ʈ ���� ����
        foreach (var room in allRooms)
        {
            if (room.roomComponent != null)
            {
                room.roomComponent.OnRoomEntered -= HandleRoomEntered;
                room.roomComponent.OnRoomCleared -= HandleRoomCleared;
                room.roomComponent.OnRoomExited -= HandleRoomExited;
            }
        }
    }

    #endregion
}