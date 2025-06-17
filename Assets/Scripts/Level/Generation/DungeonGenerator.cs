// Assets/Scripts/Level/Generation/DungeonGenerator.cs ����
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

    // ������ ���� ����
    private List<GeneratedRoom> generatedRooms = new List<GeneratedRoom>();
    private DungeonLayout dungeonLayout;
    private GridSystem gridSystem;

    // �̺�Ʈ
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
            JCDebug.Log("[DungeonGenerator] LevelData�� null�Դϴ�!", JCDebug.LogLevel.Error);
            return null;
        }

        _log($"���� ���� ����: {levelData.levelName}");
        OnGenerationStarted?.Invoke();

        // 1. ���� ���� ����
        ClearExistingDungeon();

        // 2. �׸��� �ý��� �ʱ�ȭ
        InitializeGridSystem(levelData);

        // 3. �� ��ġ ��ȹ ����
        var roomPlacements = GenerateRoomLayout(levelData);

        // 4. ����� ������ ����
        GenerateRooms(roomPlacements, levelData);

        // 5. ���� ����
        GenerateCorridors();

        // 6. ���� ���̾ƿ� ����
        dungeonLayout = CreateDungeonLayout();

        _log($"���� ���� �Ϸ�: {generatedRooms.Count}�� �� ������");
        OnGenerationCompleted?.Invoke();
        OnDungeonGenerated?.Invoke(dungeonLayout);

        return dungeonLayout;
    }

    private void InitializeGridSystem(LevelData levelData)
    {
        gridSystem = new GridSystem(levelData.mapSize.x, levelData.mapSize.y);
        _log($"�׸��� �ý��� �ʱ�ȭ: {levelData.mapSize}");
    }

    private List<RoomPlacement> GenerateRoomLayout(LevelData levelData)
    {
        var placements = new List<RoomPlacement>();
        var roomGenerator = new RoomLayoutGenerator(gridSystem, levelData);

        // ���� �� ��ġ
        var startRoom = roomGenerator.PlaceStartRoom();
        placements.Add(startRoom);

        // �Ϲ� ��� ��ġ
        var normalRooms = roomGenerator.PlaceNormalRooms();
        placements.AddRange(normalRooms);

        // ���� �� ��ġ
        var bossRoom = roomGenerator.PlaceBossRoom();
        placements.Add(bossRoom);

        // Ư�� ��� ��ġ (������, ���� ��)
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
            _log($"�� �������� �����ϴ�: {placement.roomData?.roomName}", JCDebug.LogLevel.Warning);
            return null;
        }

        // �� ������Ʈ ����
        Vector3 worldPosition = gridSystem.GridToWorldPosition(placement.gridPosition);
        GameObject roomObject = Instantiate(placement.roomData.roomPrefab, worldPosition, Quaternion.identity, roomParent);
        roomObject.name = $"Room_{placement.roomData.roomName}_{placement.gridPosition.x}_{placement.gridPosition.y}";

        // Room ������Ʈ ����
        Room roomComponent = roomObject.GetComponent<Room>();
        if (roomComponent == null)
        {
            roomComponent = roomObject.AddComponent<Room>();
        }

        // GeneratedRoom ������ ����
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

        // Room ������Ʈ �ʱ�ȭ
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
        _log("���� ���� ���� �Ϸ�");
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

        // �׸��� ǥ��
        gridSystem.DrawGizmos();

        // ������ ��� ǥ��
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