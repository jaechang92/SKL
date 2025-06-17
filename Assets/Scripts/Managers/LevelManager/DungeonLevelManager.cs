// Assets/Scripts/Managers/LevelManager/DungeonLevelManager.cs
using UnityEngine;
using System.Collections.Generic;
using CustomDebug;

namespace Metamorph.Managers
{
    public class DungeonLevelManager : SingletonManager<DungeonLevelManager>
    {
        [Header("Level Configuration")]
        [SerializeField] private List<LevelData> availableLevels = new List<LevelData>();
        [SerializeField] private DungeonGenerator dungeonGenerator;
        [SerializeField] private Transform playerSpawnPoint;

        [Header("Current Level")]
        [SerializeField] private int currentLevelIndex = 0;
        [SerializeField] private LevelData currentLevel;
        [SerializeField] private DungeonLayout currentDungeon;

        // 레벨 상태
        private bool isLevelActive = false;
        private Room currentRoom;
        private List<Room> visitedRooms = new List<Room>();

        // 이벤트
        public System.Action<LevelData> OnLevelStarted;
        public System.Action<LevelData> OnLevelCompleted;
        public System.Action<Room> OnRoomChanged;

        public LevelData CurrentLevel => currentLevel;
        public Room CurrentRoom => currentRoom;
        public bool IsLevelActive => isLevelActive;

        protected override void OnCreated()
        {
            base.OnCreated();

            if (dungeonGenerator == null)
            {
                dungeonGenerator = FindAnyObjectByType<DungeonGenerator>();
            }

            // 던전 생성 이벤트 구독
            if (dungeonGenerator != null)
            {
                dungeonGenerator.OnDungeonGenerated += HandleDungeonGenerated;
            }
        }

        public void StartLevel(int levelIndex)
        {
            if (levelIndex < 0 || levelIndex >= availableLevels.Count)
            {
                JCDebug.Log($"[DungeonLevelManager] 잘못된 레벨 인덱스: {levelIndex}", JCDebug.LogLevel.Error);
                return;
            }

            currentLevelIndex = levelIndex;
            currentLevel = availableLevels[levelIndex];

            JCDebug.Log($"[DungeonLevelManager] 레벨 시작: {currentLevel.levelName}");

            // 던전 생성
            if (dungeonGenerator != null)
            {
                currentDungeon = dungeonGenerator.GenerateDungeon(currentLevel);
            }
        }

        private void HandleDungeonGenerated(DungeonLayout dungeon)
        {
            currentDungeon = dungeon;
            isLevelActive = true;

            // 플레이어를 시작 방으로 이동
            MovePlayerToStartRoom();

            // 방 이벤트 구독
            SubscribeToRoomEvents();

            OnLevelStarted?.Invoke(currentLevel);
            JCDebug.Log($"[DungeonLevelManager] 던전 생성 완료, 레벨 활성화");
        }

        private void MovePlayerToStartRoom()
        {
            var startRoom = currentDungeon.GetStartRoom();
            if (startRoom != null && playerSpawnPoint != null)
            {
                playerSpawnPoint.position = startRoom.worldPosition;
                SetCurrentRoom(startRoom.roomComponent);
            }
        }

        private void SubscribeToRoomEvents()
        {
            var allRooms = currentDungeon.GetAllRooms();
            foreach (var roomData in allRooms)
            {
                if (roomData.roomComponent != null)
                {
                    roomData.roomComponent.OnRoomEntered += HandleRoomEntered;
                    roomData.roomComponent.OnRoomCleared += HandleRoomCleared;
                }
            }
        }

        private void HandleRoomEntered(Room room)
        {
            SetCurrentRoom(room);

            if (!visitedRooms.Contains(room))
            {
                visitedRooms.Add(room);
            }
        }

        private void HandleRoomCleared(Room room)
        {
            JCDebug.Log($"[DungeonLevelManager] 방 클리어: {room.name}");

            // 보스 방이 클리어되면 레벨 완료
            if (room.RoomType == RoomData.RoomType.Boss)
            {
                CompleteLevel();
            }
        }

        private void SetCurrentRoom(Room room)
        {
            if (currentRoom != room)
            {
                currentRoom = room;
                OnRoomChanged?.Invoke(room);

                // 카메라 경계 업데이트
                UpdateCameraBounds(room);
            }
        }

        private void UpdateCameraBounds(Room room)
        {
            // LevelManager와 연동하여 카메라 경계 업데이트
            var levelManager = LevelManager.Instance;
            if (levelManager != null)
            {
                var roomCollider = room.GetComponent<BoxCollider2D>();
                if (roomCollider != null)
                {
                    levelManager.LoadNewLevel(roomCollider, room.transform.position);
                }
            }
        }

        private void CompleteLevel()
        {
            isLevelActive = false;
            OnLevelCompleted?.Invoke(currentLevel);

            JCDebug.Log($"[DungeonLevelManager] 레벨 완료: {currentLevel.levelName}");

            // 다음 레벨로 진행하거나 메인 메뉴로 돌아가는 로직
            PrepareNextLevel();
        }

        private void PrepareNextLevel()
        {
            // 다음 레벨이 있으면 자동 진행, 없으면 게임 완료 처리
            if (currentLevelIndex + 1 < availableLevels.Count)
            {
                // 다음 레벨 준비
                JCDebug.Log("[DungeonLevelManager] 다음 레벨 준비 중...");
            }
            else
            {
                JCDebug.Log("[DungeonLevelManager] 모든 레벨 완료!");
            }
        }
    }
}