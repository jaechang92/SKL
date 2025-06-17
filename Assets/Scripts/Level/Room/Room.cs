// Assets/Scripts/Level/Room/Room.cs ����
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

    // ��Ÿ�� ������
    private GeneratedRoom roomData;
    private List<GameObject> spawnedEnemies = new List<GameObject>();
    private List<GameObject> spawnedItems = new List<GameObject>();

    // �̺�Ʈ
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
        JCDebug.Log($"[Room] �� �ʱ�ȭ: {data.roomData.roomName}");
    }

    private void SetupRoomComponents()
    {
        // �Ա�/�ⱸ �ڵ� Ž��
        if (entrances.Count == 0)
        {
            entrances.AddRange(GetComponentsInChildren<RoomEntrance>());
        }

        if (exits.Count == 0)
        {
            exits.AddRange(GetComponentsInChildren<RoomExit>());
        }

        // ���� ����Ʈ �ڵ� Ž��
        SetupSpawnPoints();

        // �Ա�/�ⱸ �̺�Ʈ ����
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
        // "EnemySpawn" �±׷� �� ���� ����Ʈ ã��
        GameObject[] enemySpawns = GameObject.FindGameObjectsWithTag("EnemySpawn");
        foreach (var spawn in enemySpawns)
        {
            if (IsChildOf(spawn.transform, transform))
            {
                enemySpawnPoints.Add(spawn.transform);
            }
        }

        // "ItemSpawn" �±׷� ������ ���� ����Ʈ ã��
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
        JCDebug.Log($"[Room] �÷��̾ �濡 ����: {roomData?.roomData?.roomName}");
    }

    private void HandlePlayerExited()
    {
        isActive = false;
        OnRoomExited?.Invoke(this);
        JCDebug.Log($"[Room] �÷��̾ �濡�� ����: {roomData?.roomData?.roomName}");
    }

    private void ActivateRoom()
    {
        SpawnEnemies();
        SpawnItems();

        // �� Ȱ��ȭ ȿ��
        PlayActivationEffects();
    }

    private void SpawnEnemies()
    {
        if (roomData?.roomData?.enemySpawnPoints == null) return;

        foreach (var spawnPoint in roomData.roomData.enemySpawnPoints)
        {
            if (Random.value <= spawnPoint.spawnChance)
            {
                // EnemyManager�� ���� �� ����
                // ���� ���������� EnemyManager.Instance.SpawnEnemy() ���
                JCDebug.Log($"[Room] �� ����: {spawnPoint.position}");
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
                // ItemManager�� ���� ������ ����
                JCDebug.Log($"[Room] ������ ����: {spawnPoint.position}");
            }
        }
    }

    private void PlayActivationEffects()
    {
        // �� Ȱ��ȭ �ð�/���� ȿ��
        var particles = GetComponentInChildren<ParticleSystem>();
        if (particles != null)
        {
            particles.Play();
        }
    }

    public void CheckClearConditions()
    {
        // ��� ���� óġ�Ǿ����� Ȯ��
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

        // �� Ŭ���� ����
        SpawnClearRewards();

        JCDebug.Log($"[Room] �� Ŭ����: {roomData?.roomData?.roomName}");
    }

    private void SpawnClearRewards()
    {
        // Ŭ���� ���� ���� (����ġ, ���, ������ ��)
    }
}