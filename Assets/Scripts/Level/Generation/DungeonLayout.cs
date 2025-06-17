// Assets/Scripts/Level/Generation/DungeonLayout.cs 수정
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

    // 이벤트
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
    /// 던전 레이아웃 초기화
    /// </summary>
    public void Initialize(List<GeneratedRoom> rooms, GridSystem gridSystem)
    {
        allRooms = new List<GeneratedRoom>(rooms);
        totalRoomCount = allRooms.Count;
        dungeonSize = new Vector2Int(gridSystem.Width, gridSystem.Height);

        // 특수 방들 분류
        CategorizeRooms();

        // 연결 정보 구축
        BuildConnectionMap();

        // 그리드 맵 구축
        BuildGridMap();

        // 방 이벤트 구독
        SubscribeToRoomEvents();

        // 초기 완료율 계산
        UpdateCompletionPercentage();

        OnLayoutInitialized?.Invoke(this);

        JCDebug.Log($"[DungeonLayout] 던전 레이아웃 초기화 완료: {totalRoomCount}개 방");
    }

    /// <summary>
    /// 방들을 타입별로 분류
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

        JCDebug.Log($"[DungeonLayout] 방 분류 완료 - 보물방:{treasureRooms.Count}, 상점:{shopRooms.Count}, 비밀방:{secretRooms.Count}");
    }

    /// <summary>
    /// 방 간 연결 정보 구축
    /// </summary>
    private void BuildConnectionMap()
    {
        roomConnections.Clear();

        foreach (var room in allRooms)
        {
            roomConnections[room] = new List<GeneratedRoom>(room.connectedRooms);
        }

        // 복도 정보 수집
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

        JCDebug.Log($"[DungeonLayout] 연결 맵 구축 완료: {allCorridors.Count}개 복도");
    }

    /// <summary>
    /// 그리드 위치와 방 매핑 구축
    /// </summary>
    private void BuildGridMap()
    {
        gridToRoomMap.Clear();

        foreach (var room in allRooms)
        {
            // 방이 차지하는 모든 그리드 셀에 대해 매핑
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
    /// 방 이벤트들에 구독
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
    /// 방 입장 처리
    /// </summary>
    private void HandleRoomEntered(Room roomComponent)
    {
        var generatedRoom = allRooms.FirstOrDefault(r => r.roomComponent == roomComponent);
        if (generatedRoom == null) return;

        // 현재 플레이어 방 업데이트
        currentPlayerRoom = generatedRoom;

        // 처음 방문하는 방인지 확인
        if (!generatedRoom.isVisited)
        {
            generatedRoom.isVisited = true;
            visitedRooms.Add(generatedRoom);
            OnRoomDiscovered?.Invoke(generatedRoom);

            JCDebug.Log($"[DungeonLayout] 새로운 방 발견: {generatedRoom.roomData?.roomName}");
        }

        UpdateCompletionPercentage();
    }

    /// <summary>
    /// 방 클리어 처리
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

            JCDebug.Log($"[DungeonLayout] 방 클리어: {generatedRoom.roomData?.roomName}");

            // 보스 방 클리어 시 던전 완료
            if (generatedRoom == bossRoom)
            {
                CompleteDungeon();
            }
        }

        UpdateCompletionPercentage();
    }

    /// <summary>
    /// 방 퇴장 처리
    /// </summary>
    private void HandleRoomExited(Room roomComponent)
    {
        // 현재는 특별한 처리 없음
        // 필요시 추가 로직 구현
    }

    /// <summary>
    /// 완료율 업데이트
    /// </summary>
    private void UpdateCompletionPercentage()
    {
        if (totalRoomCount == 0)
        {
            completionPercentage = 0f;
            return;
        }

        // 방문한 방 50% + 클리어한 방 50%
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
    /// 던전 완료 처리
    /// </summary>
    private void CompleteDungeon()
    {
        isCompleted = true;
        completionPercentage = 100f;
        OnDungeonCompleted?.Invoke(this);

        JCDebug.Log("[DungeonLayout] 던전 완료!");
    }

    #region Public Query Methods

    public GeneratedRoom GetStartRoom()
    {
        return startRoom;
    }

    /// <summary>
    /// 모든 방 목록 반환
    /// </summary>
    public List<GeneratedRoom> GetAllRooms()
    {
        return new List<GeneratedRoom>(allRooms);
    }

    /// <summary>
    /// 특정 타입의 방들 반환
    /// </summary>
    public List<GeneratedRoom> GetRoomsByType(RoomData.RoomType roomType)
    {
        return allRooms.Where(r => r.roomData?.roomType == roomType).ToList();
    }

    /// <summary>
    /// 그리드 위치의 방 반환
    /// </summary>
    public GeneratedRoom GetRoomAtPosition(Vector2Int gridPosition)
    {
        return gridToRoomMap.TryGetValue(gridPosition, out GeneratedRoom room) ? room : null;
    }

    /// <summary>
    /// 특정 방과 연결된 방들 반환
    /// </summary>
    public List<GeneratedRoom> GetConnectedRooms(GeneratedRoom room)
    {
        return roomConnections.TryGetValue(room, out List<GeneratedRoom> connections) ?
               new List<GeneratedRoom>(connections) : new List<GeneratedRoom>();
    }

    /// <summary>
    /// 방 간 경로 찾기
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

        return new List<GeneratedRoom>(); // 경로 없음
    }

    /// <summary>
    /// 시작방에서 특정 방까지의 거리
    /// </summary>
    public int GetDistanceFromStart(GeneratedRoom targetRoom)
    {
        if (startRoom == null || targetRoom == null) return -1;

        var path = FindPath(startRoom, targetRoom);
        return path.Count > 0 ? path.Count - 1 : -1;
    }

    /// <summary>
    /// 아직 방문하지 않은 방들
    /// </summary>
    public List<GeneratedRoom> GetUnvisitedRooms()
    {
        return allRooms.Where(r => !r.isVisited).ToList();
    }

    /// <summary>
    /// 방문했지만 클리어하지 않은 방들
    /// </summary>
    public List<GeneratedRoom> GetUnclearedRooms()
    {
        return allRooms.Where(r => r.isVisited && !r.isCleared).ToList();
    }

    /// <summary>
    /// 보물방들 반환
    /// </summary>
    public List<GeneratedRoom> GetTreasureRooms()
    {
        return new List<GeneratedRoom>(treasureRooms);
    }

    /// <summary>
    /// 상점들 반환
    /// </summary>
    public List<GeneratedRoom> GetShopRooms()
    {
        return new List<GeneratedRoom>(shopRooms);
    }

    /// <summary>
    /// 비밀방들 반환
    /// </summary>
    public List<GeneratedRoom> GetSecretRooms()
    {
        return new List<GeneratedRoom>(secretRooms);
    }

    #endregion

    #region Debug & Utility

    /// <summary>
    /// 던전 상태 디버그 출력
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
    /// 연결 정보 디버그 출력
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
        // 이벤트 구독 해제
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