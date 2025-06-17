// Assets/Scripts/Level/Generation/DungeonGenerator.cs 수정
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using CustomDebug;

public class DungeonGenerator : MonoBehaviour
{
    [Header("Generation Settings")]
    [SerializeField] private LevelData currentLevelData;
    [SerializeField] private Transform roomParent;
    [SerializeField] private bool generateOnStart = false;

    [Header("Debug")]
    [SerializeField] private bool showDebugGizmos = true;
    [SerializeField] private bool enableLogging = true;

    // 생성된 던전 정보
    private List<GeneratedRoom> generatedRooms = new List<GeneratedRoom>();
    private DungeonLayout dungeonLayout;
    private GridSystem gridSystem;

    // 이벤트
    public System.Action<DungeonLayout> OnDungeonGenerated;
    public System.Action OnGenerationStarted;
    public System.Action OnGenerationCompleted;

    private void Start()
    {
        if (generateOnStart && currentLevelData != null)
        {
            GenerateDungeon(currentLevelData);
        }
    }

    public DungeonLayout GenerateDungeon(LevelData levelData)
    {
        if (levelData == null)
        {
            JCDebug.Log("[DungeonGenerator] LevelData가 null입니다!", JCDebug.LogLevel.Error);
            return null;
        }

        _log($"던전 생성 시작: {levelData.levelName}");
        OnGenerationStarted?.Invoke();

        // 1. 기존 던전 정리
        ClearExistingDungeon();

        // 2. 그리드 시스템 초기화
        InitializeGridSystem(levelData);

        // 3. 방 배치 계획 생성
        var roomPlacements = GenerateRoomLayout(levelData);

        // 4. 방들을 실제로 생성
        GenerateRooms(roomPlacements, levelData);

        // 5. 복도 연결
        GenerateCorridors();

        // 6. 던전 레이아웃 생성
        dungeonLayout = CreateDungeonLayout();

        _log($"던전 생성 완료: {generatedRooms.Count}개 방 생성됨");
        OnGenerationCompleted?.Invoke();
        OnDungeonGenerated?.Invoke(dungeonLayout);

        return dungeonLayout;
    }

    private void InitializeGridSystem(LevelData levelData)
    {
        gridSystem = new GridSystem(levelData.mapSize.x, levelData.mapSize.y);
        _log($"그리드 시스템 초기화: {levelData.mapSize}");
    }

    private List<RoomPlacement> GenerateRoomLayout(LevelData levelData)
    {
        var placements = new List<RoomPlacement>();
        var roomGenerator = new RoomLayoutGenerator(gridSystem, levelData);

        // 시작 방 배치
        var startRoom = roomGenerator.PlaceStartRoom();
        placements.Add(startRoom);

        // 일반 방들 배치
        var normalRooms = roomGenerator.PlaceNormalRooms();
        placements.AddRange(normalRooms);

        // 보스 방 배치
        var bossRoom = roomGenerator.PlaceBossRoom();
        placements.Add(bossRoom);

        // 특수 방들 배치 (보물방, 상점 등)
        var specialRooms = roomGenerator.PlaceSpecialRooms();
        placements.AddRange(specialRooms);

        return placements;
    }

    private void GenerateRooms(List<RoomPlacement> placements, LevelData levelData)
    {
        generatedRooms.Clear();

        foreach (var placement in placements)
        {
            var generatedRoom = CreateRoom(placement, levelData);
            if (generatedRoom != null)
            {
                generatedRooms.Add(generatedRoom);
            }
        }
    }

    private GeneratedRoom CreateRoom(RoomPlacement placement, LevelData levelData)
    {
        if (placement.roomData?.roomPrefab == null)
        {
            _log($"방 프리팹이 없습니다: {placement.roomData?.roomName}", JCDebug.LogLevel.Warning);
            return null;
        }

        // 방 오브젝트 생성
        Vector3 worldPosition = gridSystem.GridToWorldPosition(placement.gridPosition);
        GameObject roomObject = Instantiate(placement.roomData.roomPrefab, worldPosition, Quaternion.identity, roomParent);
        roomObject.name = $"Room_{placement.roomData.roomName}_{placement.gridPosition.x}_{placement.gridPosition.y}";

        // Room 컴포넌트 설정
        Room roomComponent = roomObject.GetComponent<Room>();
        if (roomComponent == null)
        {
            roomComponent = roomObject.AddComponent<Room>();
        }

        // GeneratedRoom 데이터 생성
        var generatedRoom = new GeneratedRoom
        {
            roomObject = roomObject,
            roomComponent = roomComponent,
            roomData = placement.roomData,
            gridPosition = placement.gridPosition,
            worldPosition = worldPosition,
            isVisited = false,
            isCleared = false
        };

        // Room 컴포넌트 초기화
        roomComponent.Initialize(generatedRoom);

        return generatedRoom;
    }

    private void GenerateCorridors()
    {
        var corridorGenerator = new CorridorGenerator(gridSystem, generatedRooms);
        corridorGenerator.GenerateAllCorridors();
    }

    private DungeonLayout CreateDungeonLayout()
    {
        var layout = new DungeonLayout();
        layout.Initialize(generatedRooms, gridSystem);
        return layout;
    }

    private void ClearExistingDungeon()
    {
        if (roomParent != null)
        {
            for (int i = roomParent.childCount - 1; i >= 0; i--)
            {
                DestroyImmediate(roomParent.GetChild(i).gameObject);
            }
        }

        generatedRooms.Clear();
        _log("기존 던전 정리 완료");
    }

    private void _log(string message, JCDebug.LogLevel level = JCDebug.LogLevel.Info)
    {
        if (enableLogging)
        {
            JCDebug.Log($"[DungeonGenerator] {message}", level);
        }
    }

    private void OnDrawGizmos()
    {
        if (!showDebugGizmos || gridSystem == null) return;

        // 그리드 표시
        gridSystem.DrawGizmos();

        // 생성된 방들 표시
        foreach (var room in generatedRooms)
        {
            if (room.roomObject != null)
            {
                Gizmos.color = room.isCleared ? Color.green : Color.red;
                Gizmos.DrawWireCube(room.worldPosition, Vector3.one);
            }
        }
    }
}