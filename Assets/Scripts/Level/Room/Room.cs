// Assets/Scripts/Level/Room/Room.cs 수정
using UnityEngine;
using System.Collections.Generic;
using CustomDebug;

public class Room : MonoBehaviour
{
    [Header("Room State")]
    [SerializeField] private bool isActive = false;
    [SerializeField] private bool isCleared = false;
    [SerializeField] private bool isVisited = false;

    [Header("Room Components")]
    [SerializeField] private List<RoomEntrance> entrances = new List<RoomEntrance>();
    [SerializeField] private List<RoomExit> exits = new List<RoomExit>();
    [SerializeField] private List<Transform> enemySpawnPoints = new List<Transform>();
    [SerializeField] private List<Transform> itemSpawnPoints = new List<Transform>();

    // 런타임 데이터
    private GeneratedRoom roomData;
    private List<GameObject> spawnedEnemies = new List<GameObject>();
    private List<GameObject> spawnedItems = new List<GameObject>();

    // 이벤트
    public System.Action<Room> OnRoomEntered;
    public System.Action<Room> OnRoomCleared;
    public System.Action<Room> OnRoomExited;

    public bool IsCleared => isCleared;
    public bool IsVisited => isVisited;
    public bool IsActive => isActive;
    public RoomData.RoomType RoomType => roomData?.roomData?.roomType ?? RoomData.RoomType.Normal;

    public void Initialize(GeneratedRoom data)
    {
        roomData = data;
        SetupRoomComponents();
        JCDebug.Log($"[Room] 방 초기화: {data.roomData.roomName}");
    }

    private void SetupRoomComponents()
    {
        // 입구/출구 자동 탐지
        if (entrances.Count == 0)
        {
            entrances.AddRange(GetComponentsInChildren<RoomEntrance>());
        }

        if (exits.Count == 0)
        {
            exits.AddRange(GetComponentsInChildren<RoomExit>());
        }

        // 스폰 포인트 자동 탐지
        SetupSpawnPoints();

        // 입구/출구 이벤트 연결
        foreach (var entrance in entrances)
        {
            entrance.OnPlayerEntered += HandlePlayerEntered;
        }

        foreach (var exit in exits)
        {
            exit.OnPlayerExited += HandlePlayerExited;
        }
    }

    private void SetupSpawnPoints()
    {
        // "EnemySpawn" 태그로 적 스폰 포인트 찾기
        GameObject[] enemySpawns = GameObject.FindGameObjectsWithTag("EnemySpawn");
        foreach (var spawn in enemySpawns)
        {
            if (IsChildOf(spawn.transform, transform))
            {
                enemySpawnPoints.Add(spawn.transform);
            }
        }

        // "ItemSpawn" 태그로 아이템 스폰 포인트 찾기
        GameObject[] itemSpawns = GameObject.FindGameObjectsWithTag("ItemSpawn");
        foreach (var spawn in itemSpawns)
        {
            if (IsChildOf(spawn.transform, transform))
            {
                itemSpawnPoints.Add(spawn.transform);
            }
        }
    }

    private bool IsChildOf(Transform child, Transform parent)
    {
        while (child.parent != null)
        {
            if (child.parent == parent)
                return true;
            child = child.parent;
        }
        return false;
    }

    private void HandlePlayerEntered()
    {
        if (!isVisited)
        {
            isVisited = true;
            ActivateRoom();
        }

        isActive = true;
        OnRoomEntered?.Invoke(this);
        JCDebug.Log($"[Room] 플레이어가 방에 입장: {roomData?.roomData?.roomName}");
    }

    private void HandlePlayerExited()
    {
        isActive = false;
        OnRoomExited?.Invoke(this);
        JCDebug.Log($"[Room] 플레이어가 방에서 퇴장: {roomData?.roomData?.roomName}");
    }

    private void ActivateRoom()
    {
        SpawnEnemies();
        SpawnItems();

        // 방 활성화 효과
        PlayActivationEffects();
    }

    private void SpawnEnemies()
    {
        if (roomData?.roomData?.enemySpawnPoints == null) return;

        foreach (var spawnPoint in roomData.roomData.enemySpawnPoints)
        {
            if (Random.value <= spawnPoint.spawnChance)
            {
                // EnemyManager를 통해 적 스폰
                // 실제 구현에서는 EnemyManager.Instance.SpawnEnemy() 사용
                JCDebug.Log($"[Room] 적 스폰: {spawnPoint.position}");
            }
        }
    }

    private void SpawnItems()
    {
        if (roomData?.roomData?.itemSpawnPoints == null) return;

        foreach (var spawnPoint in roomData.roomData.itemSpawnPoints)
        {
            if (Random.value <= spawnPoint.spawnChance)
            {
                // ItemManager를 통해 아이템 스폰
                JCDebug.Log($"[Room] 아이템 스폰: {spawnPoint.position}");
            }
        }
    }

    private void PlayActivationEffects()
    {
        // 방 활성화 시각/음향 효과
        var particles = GetComponentInChildren<ParticleSystem>();
        if (particles != null)
        {
            particles.Play();
        }
    }

    public void CheckClearConditions()
    {
        // 모든 적이 처치되었는지 확인
        spawnedEnemies.RemoveAll(enemy => enemy == null);

        if (spawnedEnemies.Count == 0 && !isCleared)
        {
            ClearRoom();
        }
    }

    private void ClearRoom()
    {
        isCleared = true;
        OnRoomCleared?.Invoke(this);

        // 방 클리어 보상
        SpawnClearRewards();

        JCDebug.Log($"[Room] 방 클리어: {roomData?.roomData?.roomName}");
    }

    private void SpawnClearRewards()
    {
        // 클리어 보상 스폰 (경험치, 골드, 아이템 등)
    }
}