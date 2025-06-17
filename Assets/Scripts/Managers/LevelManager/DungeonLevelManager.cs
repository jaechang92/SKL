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

        // ���� ����
        private bool isLevelActive = false;
        private Room currentRoom;
        private List<Room> visitedRooms = new List<Room>();

        // �̺�Ʈ
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

            // ���� ���� �̺�Ʈ ����
            if (dungeonGenerator != null)
            {
                dungeonGenerator.OnDungeonGenerated += HandleDungeonGenerated;
            }
        }

        public void StartLevel(int levelIndex)
        {
            if (levelIndex < 0 || levelIndex >= availableLevels.Count)
            {
                JCDebug.Log($"[DungeonLevelManager] �߸��� ���� �ε���: {levelIndex}", JCDebug.LogLevel.Error);
                return;
            }

            currentLevelIndex = levelIndex;
            currentLevel = availableLevels[levelIndex];

            JCDebug.Log($"[DungeonLevelManager] ���� ����: {currentLevel.levelName}");

            // ���� ����
            if (dungeonGenerator != null)
            {
                currentDungeon = dungeonGenerator.GenerateDungeon(currentLevel);
            }
        }

        private void HandleDungeonGenerated(DungeonLayout dungeon)
        {
            currentDungeon = dungeon;
            isLevelActive = true;

            // �÷��̾ ���� ������ �̵�
            MovePlayerToStartRoom();

            // �� �̺�Ʈ ����
            SubscribeToRoomEvents();

            OnLevelStarted?.Invoke(currentLevel);
            JCDebug.Log($"[DungeonLevelManager] ���� ���� �Ϸ�, ���� Ȱ��ȭ");
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
            JCDebug.Log($"[DungeonLevelManager] �� Ŭ����: {room.name}");

            // ���� ���� Ŭ����Ǹ� ���� �Ϸ�
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

                // ī�޶� ��� ������Ʈ
                UpdateCameraBounds(room);
            }
        }

        private void UpdateCameraBounds(Room room)
        {
            // LevelManager�� �����Ͽ� ī�޶� ��� ������Ʈ
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

            JCDebug.Log($"[DungeonLevelManager] ���� �Ϸ�: {currentLevel.levelName}");

            // ���� ������ �����ϰų� ���� �޴��� ���ư��� ����
            PrepareNextLevel();
        }

        private void PrepareNextLevel()
        {
            // ���� ������ ������ �ڵ� ����, ������ ���� �Ϸ� ó��
            if (currentLevelIndex + 1 < availableLevels.Count)
            {
                // ���� ���� �غ�
                JCDebug.Log("[DungeonLevelManager] ���� ���� �غ� ��...");
            }
            else
            {
                JCDebug.Log("[DungeonLevelManager] ��� ���� �Ϸ�!");
            }
        }
    }
}