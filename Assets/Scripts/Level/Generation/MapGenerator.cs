// Assets/Scripts/Level/Generation/MapGenerator.cs
using CustomDebug;
using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Metamorph.Level.Generation
{
    /// <summary>
    /// 로그라이크 던전 맵을 생성하는 메인 클래스
    /// 기존 GridSystem과 Room 시스템을 활용
    /// </summary>
    public class MapGenerator : SingletonManager<MapGenerator>
    {
        [Header("Map Generation Settings")]
        [SerializeField] private MapGenerationConfig _config;
        [SerializeField] private GridSystem _gridSystem;
        [SerializeField] private bool _generateOnStart = false;
        [SerializeField] private bool _showDebugVisualization = true;

        [Header("Room Prefabs")]
        [SerializeField] private RoomPrefabDatabase _roomDatabase;
        [SerializeField] private Transform _roomParent;

        [Header("Generation Rules")]
        [SerializeField] private RoomPlacementRules _placementRules;
        [SerializeField] private ConnectionRules _connectionRules;

        // 생성된 맵 데이터
        private GeneratedMapData _currentMapData;
        private Dictionary<Vector2Int, GeneratedRoom> _generatedRooms;
        private List<RoomConnection> _roomConnections;

        // 이벤트
        public System.Action<GeneratedMapData> OnMapGenerated;
        public System.Action<GeneratedRoom> OnRoomCreated;
        public System.Action OnMapCleared;

        #region Unity Lifecycle

        //private void Start()
        //{
        //    if (_generateOnStart)
        //    {
        //        GenerateMap();
        //    }
        //}

        #endregion

        #region Map Generation

        /// <summary>
        /// 메인 맵 생성 메서드
        /// </summary>
        public async UniTask GenerateMap()
        {
            JCDebug.Log("[MapGenerator] 맵 생성 시작");
            
            // 1. 초기화
            InitializeGeneration();

            // 2. 맵 레이아웃 생성
            var layout = GenerateMapLayout();

            // 3. 방 배치
            PlaceRooms(layout);

            // 4. 방 연결
            ConnectRooms();

            // 5. 특수 요소 배치
            PlaceSpecialElements();

            // 6. 최종 검증
            ValidateMap();

            // 7. 맵 데이터 완성
            _currentMapData = CreateMapData();

            OnMapGenerated?.Invoke(_currentMapData);
            JCDebug.Log("[MapGenerator] 맵 생성 완료");

            await UniTask.Yield();
        }

        /// <summary>
        /// 맵 생성 초기화
        /// </summary>
        private void InitializeGeneration()
        {
            _generatedRooms = new Dictionary<Vector2Int, GeneratedRoom>();
            _roomConnections = new List<RoomConnection>();

            // 기존 방들 정리
            ClearExistingRooms();

            // 그리드 시스템 초기화
            if (_gridSystem == null)
            {
                _gridSystem = new GridSystem(_config.mapWidth, _config.mapHeight, _config.cellSize);
            }

            // 부모 오브젝트 설정
            if (_roomParent == null)
            {
                _roomParent = new GameObject("Generated Rooms").transform;
            }
        }

        /// <summary>
        /// 맵 레이아웃 생성 (던전 구조 결정)
        /// </summary>
        private MapLayout GenerateMapLayout()
        {
            var layout = new MapLayout(_config.mapWidth, _config.mapHeight);

            switch (_config.generationType)
            {
                case MapGenerationType.Linear:
                    layout = GenerateLinearLayout();
                    break;
                //case MapGenerationType.Branching:
                //    layout = GenerateBranchingLayout();
                //    break;
                //case MapGenerationType.Grid:
                //    layout = GenerateGridLayout();
                //    break;
                //case MapGenerationType.Organic:
                //    layout = GenerateOrganicLayout();
                //    break;
            }

            return layout;
        }

        /// <summary>
        /// 선형 레이아웃 생성 (스컬 스타일)
        /// </summary>
        private MapLayout GenerateLinearLayout()
        {
            var layout = new MapLayout(_config.mapWidth, _config.mapHeight);

            // 시작점에서 보스방까지 일직선 경로
            Vector2Int currentPos = new Vector2Int(_config.mapWidth / 2, 0);

            for (int floor = 0; floor < _config.floorCount; floor++)
            {
                // 각 층마다 선택지 생성
                if (floor == 0)
                {
                    // 시작 방
                    layout.SetRoomType(currentPos, RoomType.Start);
                }
                else if (floor == _config.floorCount - 1)
                {
                    // 보스 방
                    layout.SetRoomType(currentPos, RoomType.Boss);
                }
                else
                {
                    // 중간 층: 2-3개 선택지
                    int choiceCount = Random.Range(2, 4);
                    for (int i = 0; i < choiceCount; i++)
                    {
                        Vector2Int choicePos = new Vector2Int(currentPos.x + (i - choiceCount / 2), currentPos.y);

                        RoomType roomType = DetermineRoomType(floor, i, choiceCount);
                        layout.SetRoomType(choicePos, roomType);
                    }
                }

                currentPos.y += 1;
            }

            return layout;
        }

        /// <summary>
        /// 방 타입 결정 로직
        /// </summary>
        private RoomType DetermineRoomType(int floor, int choiceIndex, int totalChoices)
        {
            // 방 타입 확률 계산
            float rewardChance = _placementRules.rewardRoomChance;
            float bossChance = _placementRules.eliteRoomChance;

            float roll = Random.Range(0f, 1f);

            if (roll < rewardChance)
                return RoomType.Reward;
            else if (roll < rewardChance + bossChance)
                return RoomType.Elite;
            else
                return RoomType.Normal;
        }

        /// <summary>
        /// 방 배치 실행
        /// </summary>
        private void PlaceRooms(MapLayout layout)
        {
            foreach (var roomPosition in layout.GetAllRoomPositions())
            {
                Vector2Int gridPos = roomPosition.Key;
                RoomType roomType = roomPosition.Value;

                CreateRoom(gridPos, roomType);
            }
        }

        /// <summary>
        /// 개별 방 생성
        /// </summary>
        private void CreateRoom(Vector2Int gridPosition, RoomType roomType)
        {
            // 프리팹 선택
            GameObject roomPrefab = _roomDatabase.GetRoomPrefab(roomType);
            if (roomPrefab == null)
            {
                JCDebug.Log($"[MapGenerator] {roomType} 타입의 방 프리팹을 찾을 수 없습니다.", JCDebug.LogLevel.Error);
                return;
            }

            // 월드 위치 계산
            Vector3 worldPosition = _gridSystem.GridToWorldPosition(gridPosition);

            // 방 생성
            GameObject roomObject = Instantiate(roomPrefab, worldPosition, Quaternion.identity, _roomParent);
            roomObject.name = $"Room_{gridPosition.x}_{gridPosition.y}_{roomType}";

            // GeneratedRoom 컴포넌트 설정
            GeneratedRoom generatedRoom = roomObject.GetComponent<GeneratedRoom>();
            if (generatedRoom == null)
            {
                generatedRoom = roomObject.AddComponent<GeneratedRoom>();
            }

            generatedRoom.Initialize(gridPosition, roomType, worldPosition);

            // 딕셔너리에 추가
            _generatedRooms[gridPosition] = generatedRoom;

            OnRoomCreated?.Invoke(generatedRoom);

            JCDebug.Log($"[MapGenerator] 방 생성 완료: {roomType} at {gridPosition}");
        }

        /// <summary>
        /// 방들 간 연결 처리
        /// </summary>
        private void ConnectRooms()
        {
            foreach (var roomPair in _generatedRooms)
            {
                Vector2Int roomPos = roomPair.Key;
                GeneratedRoom room = roomPair.Value;

                // 인접한 방들과 연결
                ConnectToAdjacentRooms(roomPos, room);
            }
        }

        /// <summary>
        /// 인접 방과의 연결 처리
        /// </summary>
        private void ConnectToAdjacentRooms(Vector2Int roomPosition, GeneratedRoom room)
        {
            // 4방향 체크
            Vector2Int[] directions = {
                Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right
            };

            foreach (Vector2Int direction in directions)
            {
                Vector2Int adjacentPos = roomPosition + direction;

                if (_generatedRooms.ContainsKey(adjacentPos))
                {
                    GeneratedRoom adjacentRoom = _generatedRooms[adjacentPos];
                    CreateConnection(room, adjacentRoom, direction);
                }
            }
        }

        /// <summary>
        /// 방 간 연결 생성
        /// </summary>
        private void CreateConnection(GeneratedRoom roomA, GeneratedRoom roomB, Vector2Int direction)
        {
            // 이미 연결되어 있는지 확인
            if (IsAlreadyConnected(roomA, roomB)) return;

            // Exit/Entrance 생성
            RoomExit exit = CreateRoomExit(roomA, direction);
            RoomEntrance entrance = CreateRoomEntrance(roomB, -direction);

            // 연결 설정
            exit.SetConnectedExit(entrance.GetComponent<RoomExit>());

            // 연결 정보 저장
            var connection = new RoomConnection(roomA, roomB, exit, entrance);
            _roomConnections.Add(connection);

            JCDebug.Log($"[MapGenerator] 방 연결 생성: {roomA.GridPosition} ↔ {roomB.GridPosition}");
        }

        /// <summary>
        /// 방 출구 생성
        /// </summary>
        private RoomExit CreateRoomExit(GeneratedRoom room, Vector2Int direction)
        {
            GameObject exitObject = new GameObject($"Exit_{direction}");
            exitObject.transform.SetParent(room.transform);

            RoomExit roomExit = exitObject.AddComponent<RoomExit>();

            // 위치 설정
            Vector3 localPosition = GetExitPosition(direction);
            exitObject.transform.localPosition = localPosition;

            // Collider 설정
            BoxCollider2D collider = exitObject.AddComponent<BoxCollider2D>();
            collider.isTrigger = true;
            collider.size = Vector2.one;

            return roomExit;
        }

        /// <summary>
        /// 방 입구 생성
        /// </summary>
        private RoomEntrance CreateRoomEntrance(GeneratedRoom room, Vector2Int direction)
        {
            GameObject entranceObject = new GameObject($"Entrance_{direction}");
            entranceObject.transform.SetParent(room.transform);

            RoomEntrance roomEntrance = entranceObject.AddComponent<RoomEntrance>();

            // 위치 설정
            Vector3 localPosition = GetExitPosition(direction);
            entranceObject.transform.localPosition = localPosition;

            // Collider 설정
            BoxCollider2D collider = entranceObject.AddComponent<BoxCollider2D>();
            collider.isTrigger = true;
            collider.size = Vector2.one;

            return roomEntrance;
        }

        /// <summary>
        /// 방향에 따른 출입구 위치 계산
        /// </summary>
        private Vector3 GetExitPosition(Vector2Int direction)
        {
            float roomSize = _config.cellSize * 0.5f;

            if (direction == Vector2Int.up)
                return new Vector3(0, roomSize, 0);
            else if (direction == Vector2Int.down)
                return new Vector3(0, -roomSize, 0);
            else if (direction == Vector2Int.right)
                return new Vector3(roomSize, 0, 0);
            else if (direction == Vector2Int.left)
                return new Vector3(-roomSize, 0, 0);

            return Vector3.zero;
        }

        #endregion

        #region Utility Methods

        private void ClearExistingRooms()
        {
            if (_roomParent != null)
            {
                for (int i = _roomParent.childCount - 1; i >= 0; i--)
                {
                    DestroyImmediate(_roomParent.GetChild(i).gameObject);
                }
            }
        }

        private bool IsAlreadyConnected(GeneratedRoom roomA, GeneratedRoom roomB)
        {
            return _roomConnections.Any(conn =>
                (conn.RoomA == roomA && conn.RoomB == roomB) ||
                (conn.RoomA == roomB && conn.RoomB == roomA));
        }

        private void PlaceSpecialElements()
        {
            // 특수 요소 배치 (아이템, 몬스터, 장애물 등)
            // 추후 구현
        }

        private void ValidateMap()
        {
            // 맵 검증 로직
            // 모든 방이 연결되어 있는지 확인
            // 시작점에서 보스방까지 경로가 있는지 확인
        }

        private GeneratedMapData CreateMapData()
        {
            return new GeneratedMapData
            {
                rooms = _generatedRooms.Values.ToList(),
                connections = _roomConnections,
                gridSize = new Vector2Int(_config.mapWidth, _config.mapHeight),
                cellSize = _config.cellSize
            };
        }

        #endregion

        #region Debug

        private void OnDrawGizmos()
        {
            if (!_showDebugVisualization || _generatedRooms == null) return;

            // 그리드 그리기
            if (_gridSystem != null)
            {
                _gridSystem.DrawGizmos();
            }

            // 방 연결 그리기
            Gizmos.color = Color.yellow;
            foreach (var connection in _roomConnections)
            {
                if (connection.RoomA != null && connection.RoomB != null)
                {
                    Gizmos.DrawLine(
                        connection.RoomA.transform.position,
                        connection.RoomB.transform.position
                    );
                }
            }
        }

        #endregion
    }

    #region Data Classes

    [System.Serializable]
    public class MapGenerationConfig
    {
        public int mapWidth = 10;
        public int mapHeight = 10;
        public float cellSize = 10f;
        public int floorCount = 5;
        public MapGenerationType generationType = MapGenerationType.Linear;
        public int seed = 0; // 0이면 랜덤 시드
    }

    public enum MapGenerationType
    {
        Linear,      // 스컬 스타일 선형
        Branching,   // 분기형
        Grid,        // 격자형
        Organic      // 유기체형
    }

    [System.Serializable]
    public class RoomPlacementRules
    {
        [Range(0f, 1f)] public float rewardRoomChance = 0.3f;
        [Range(0f, 1f)] public float eliteRoomChance = 0.2f;
        public int minRoomsPerFloor = 2;
        public int maxRoomsPerFloor = 4;
    }

    [System.Serializable]
    public class ConnectionRules
    {
        public bool allowBacktracking = false;
        public bool forceLinearProgression = true;
        public float connectionChance = 0.8f;
    }

    public enum RoomType
    {
        Start,      // 시작방
        Normal,     // 일반 전투방
        Elite,      // 엘리트 전투방  
        Reward,     // 보상방
        Boss,       // 보스방
        Shop,       // 상점
        Rest,       // 휴식방
        Event,      // 이벤트방
        Secret      // 비밀방
    }

    public class GeneratedMapData
    {
        public List<GeneratedRoom> rooms;
        public List<RoomConnection> connections;
        public Vector2Int gridSize;
        public float cellSize;
    }

    public class MapLayout
    {
        private Dictionary<Vector2Int, RoomType> _roomLayout;
        private int _width, _height;

        public MapLayout(int width, int height)
        {
            _width = width;
            _height = height;
            _roomLayout = new Dictionary<Vector2Int, RoomType>();
        }

        public void SetRoomType(Vector2Int position, RoomType roomType)
        {
            _roomLayout[position] = roomType;
        }

        public Dictionary<Vector2Int, RoomType> GetAllRoomPositions()
        {
            return _roomLayout;
        }
    }

    public class RoomConnection
    {
        public GeneratedRoom RoomA { get; }
        public GeneratedRoom RoomB { get; }
        public RoomExit Exit { get; }
        public RoomEntrance Entrance { get; }

        public RoomConnection(GeneratedRoom roomA, GeneratedRoom roomB, RoomExit exit, RoomEntrance entrance)
        {
            RoomA = roomA;
            RoomB = roomB;
            Exit = exit;
            Entrance = entrance;
        }
    }

    #endregion
}