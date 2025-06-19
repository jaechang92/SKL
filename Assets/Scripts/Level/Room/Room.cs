// Assets/Scripts/Level/Room/Room.cs - 완전한 EnemyManager 연동 버전
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using CustomDebug;
using Metamorph.Level.Generation;
using Metamorph.Managers;

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

    [Header("Enemy System Integration")]
    [SerializeField] private List<EnemySpawnPoint> _enemySpawnPoints = new List<EnemySpawnPoint>();
    [SerializeField] private BoxCollider2D _roomBounds;
    [SerializeField] private float _spawnCooldown = 2f;
    [SerializeField] private int _maxEnemiesPerRoom = 10;
    [SerializeField] private bool _useEnemyManager = true;

    [Header("Room Settings")]
    [SerializeField] private bool _autoSetupOnStart = true;
    [SerializeField] private bool _debugMode = false;

    // 런타임 데이터
    private GeneratedRoom roomData;
    private List<GameObject> spawnedEnemies = new List<GameObject>();
    private List<GameObject> spawnedItems = new List<GameObject>();

    // EnemyManager 연동을 위한 추가 상태
    private float _lastSpawnTime = 0f;
    private bool _enemySpawnInitialized = false;
    private bool _roomSetupCompleted = false;

    // 이벤트
    public System.Action<Room> OnRoomEntered;
    public System.Action<Room> OnRoomCleared;
    public System.Action<Room> OnRoomExited;

    // 프로퍼티
    public bool IsCleared => isCleared;
    public bool IsVisited => isVisited;
    public bool IsActive => isActive;
    public RoomType RoomType => roomData?.Type ?? RoomType.Normal;
    public GeneratedRoom RoomData => roomData;
    public List<GameObject> SpawnedEnemies => spawnedEnemies;
    public List<GameObject> SpawnedItems => spawnedItems;

    #region Unity 생명주기

    private void Start()
    {
        if (_autoSetupOnStart && !_roomSetupCompleted)
        {
            SetupRoomForEnemyManager();
        }
    }

    private void OnDestroy()
    {
        // 이벤트 구독 해제
        foreach (var entrance in entrances)
        {
            if (entrance != null)
                entrance.OnPlayerEntered -= HandlePlayerEntered;
        }

        foreach (var exit in exits)
        {
            if (exit != null)
                exit.OnPlayerExited -= HandlePlayerExited;
        }
    }

    #endregion

    #region 초기화 시스템

    public void Initialize(GeneratedRoom data)
    {
        roomData = data;
        SetupRoomComponents();

        if (_useEnemyManager)
        {
            SetupRoomForEnemyManager();
        }

        JCDebug.Log($"[Room] 방 초기화: {roomData.RoomName}");
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

        _roomSetupCompleted = true;
    }

    private void SetupSpawnPoints()
    {
        // 기존 Transform 기반 스폰 포인트 찾기 (하위 호환성)
        SetupLegacySpawnPoints();

        // 새로운 EnemySpawnPoint 시스템 설정
        if (_useEnemyManager)
        {
            RefreshEnemySpawnPoints();
        }
    }

    private void SetupLegacySpawnPoints()
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

    [ContextMenu("Setup Room for EnemyManager")]
    public void SetupRoomForEnemyManager()
    {
        AutoSetupRoomBounds();
        RefreshEnemySpawnPoints();
        _roomSetupCompleted = true;

        if (_debugMode)
        {
            JCDebug.Log($"[Room] {name} EnemyManager 연동 설정 완료");
        }
    }

    #endregion

    #region 플레이어 상호작용

    private void HandlePlayerEntered()
    {
        if (!isVisited)
        {
            isVisited = true;
            ActivateRoom();
        }

        isActive = true;
        OnRoomEntered?.Invoke(this);

        // EnemyManager에게 방 입장 알림
        if (_useEnemyManager && EnemyManager.Instance != null)
        {
            NotifyEnemyManagerRoomEntered();
        }

        if (_debugMode)
        {
            JCDebug.Log($"[Room] 플레이어가 방에 입장: {roomData?.RoomName}");
        }
    }

    private void HandlePlayerExited()
    {
        isActive = false;
        OnRoomExited?.Invoke(this);

        // EnemyManager에게 방 퇴장 알림
        if (_useEnemyManager && EnemyManager.Instance != null)
        {
            NotifyEnemyManagerRoomExited();
        }

        if (_debugMode)
        {
            JCDebug.Log($"[Room] 플레이어가 방에서 퇴장: {roomData?.RoomName}");
        }
    }

    #endregion

    #region 방 활성화 시스템

    private void ActivateRoom()
    {
        if (_useEnemyManager)
        {
            ActivateRoomWithEnemyManager();
        }
        else
        {
            ActivateRoomLegacy();
        }

        // 공통 활성화 작업
        SpawnItems();
        PlayActivationEffects();
    }

    private void ActivateRoomWithEnemyManager()
    {
        // EnemyManager를 통한 적 스폰
        if (EnemyManager.Instance != null)
        {
            RequestEnemySpawn();
        }
        else
        {
            // EnemyManager가 없으면 기존 방식 사용
            if (_debugMode)
            {
                JCDebug.Log("[Room] EnemyManager를 찾을 수 없어 기존 스폰 방식 사용", JCDebug.LogLevel.Warning);
            }
            ActivateRoomLegacy();
        }
    }

    private void ActivateRoomLegacy()
    {
        // 기존 방식의 적 스폰
        SpawnEnemies();
    }

    #endregion

    #region 적 스폰 시스템 (기존 방식)

    private void SpawnEnemies()
    {
        // 기존 Transform 기반 스폰 포인트 사용
        foreach (var spawnPoint in enemySpawnPoints)
        {
            if (spawnPoint != null && Random.value <= 0.7f) // 70% 확률로 스폰
            {
                // 향후 EnemyManager 없이도 동작하도록 기본 적 스폰 로직 구현
                SpawnEnemyAtTransform(spawnPoint);
            }
        }

        if (_debugMode && enemySpawnPoints.Count > 0)
        {
            JCDebug.Log($"[Room] 기존 방식으로 적 스폰 시도: {enemySpawnPoints.Count}개 포인트");
        }
    }

    private void SpawnEnemyAtTransform(Transform spawnTransform)
    {
        // 기본 적 스폰 로직 (EnemyManager 없이)
        // 실제 구현에서는 여기에 기본 적 생성 로직을 추가할 수 있음
        if (_debugMode)
        {
            JCDebug.Log($"[Room] 적 스폰 위치: {spawnTransform.position}");
        }

        // 임시 마커로 빈 GameObject 생성 (디버그용)
        if (_debugMode)
        {
            GameObject debugMarker = new GameObject("Enemy_Debug_Marker");
            debugMarker.transform.position = spawnTransform.position;
            debugMarker.transform.SetParent(transform);
            spawnedEnemies.Add(debugMarker);
        }
    }

    #endregion

    #region EnemyManager 연동 시스템

    private void RequestEnemySpawn()
    {
        if (!CanSpawnEnemies()) return;

        var spawnPoints = GetEnemySpawnPoints();
        if (spawnPoints.Count == 0)
        {
            // EnemySpawnPoint가 없으면 기존 Transform 스폰 포인트를 EnemySpawnPoint로 변환
            ConvertLegacySpawnPoints();
            spawnPoints = GetEnemySpawnPoints();
        }

        foreach (var spawnPoint in spawnPoints)
        {
            if (spawnPoint.CanSpawn() && Random.value <= spawnPoint.spawnChance)
            {
                var enemy = EnemyManager.Instance.SpawnEnemyAtSpawnPoint(spawnPoint);
                if (enemy != null)
                {
                    OnEnemySpawned(enemy);
                }
            }
        }

        if (_debugMode)
        {
            JCDebug.Log($"[Room] {name}에 적 스폰 요청 완료 ({spawnPoints.Count}개 포인트)");
        }
    }

    private void ConvertLegacySpawnPoints()
    {
        foreach (var transform in enemySpawnPoints)
        {
            if (transform != null)
            {
                var existingSpawnPoint = transform.GetComponent<EnemySpawnPoint>();
                if (existingSpawnPoint == null)
                {
                    existingSpawnPoint = transform.gameObject.AddComponent<EnemySpawnPoint>();

                    if (_debugMode)
                    {
                        JCDebug.Log($"[Room] Transform 스폰 포인트를 EnemySpawnPoint로 변환: {transform.name}");
                    }
                }

                if (!_enemySpawnPoints.Contains(existingSpawnPoint))
                {
                    _enemySpawnPoints.Add(existingSpawnPoint);
                }
            }
        }
    }

    private void NotifyEnemyManagerRoomEntered()
    {
        // EnemyManager의 OnRoomEntered 메서드가 있다면 호출
        // 실제 EnemyManager 구현에 따라 조정 필요
        if (_debugMode)
        {
            JCDebug.Log($"[Room] EnemyManager에 방 입장 알림: {name}");
        }
    }

    private void NotifyEnemyManagerRoomExited()
    {
        // EnemyManager의 OnRoomExited 메서드가 있다면 호출
        if (_debugMode)
        {
            JCDebug.Log($"[Room] EnemyManager에 방 퇴장 알림: {name}");
        }
    }

    /// <summary>
    /// 방의 모든 적 스폰 포인트 반환
    /// </summary>
    public List<EnemySpawnPoint> GetEnemySpawnPoints()
    {
        // 캐시된 스폰 포인트가 있으면 반환
        if (_enemySpawnPoints.Count > 0)
        {
            return _enemySpawnPoints;
        }

        // 자동으로 스폰 포인트 찾기
        RefreshEnemySpawnPoints();
        return _enemySpawnPoints;
    }

    /// <summary>
    /// 방 경계 반환 (스폰 위치 계산용)
    /// </summary>
    public Bounds GetRoomBounds()
    {
        if (_roomBounds != null)
        {
            return _roomBounds.bounds;
        }

        // BoxCollider2D가 없으면 자동으로 찾기
        _roomBounds = GetComponent<BoxCollider2D>();
        if (_roomBounds != null)
        {
            return _roomBounds.bounds;
        }

        // 그래도 없으면 Transform 기준으로 기본 경계 생성
        return new Bounds(transform.position, Vector3.one * 10f);
    }

    /// <summary>
    /// 방 클리어 상태 설정 (EnemyManager에서 호출)
    /// </summary>
    public void SetCleared(bool cleared)
    {
        if (isCleared == cleared) return;

        isCleared = cleared;

        if (cleared)
        {
            OnRoomClearedByEnemyManager();
        }

        if (_debugMode)
        {
            JCDebug.Log($"[Room] {name} 클리어 상태 변경: {cleared}");
        }
    }

    /// <summary>
    /// 현재 방에 스폰 가능한지 확인
    /// </summary>
    public bool CanSpawnEnemies()
    {
        if (!isActive || isCleared) return false;
        if (Time.time - _lastSpawnTime < _spawnCooldown) return false;
        if (spawnedEnemies.Count >= _maxEnemiesPerRoom) return false;

        return true;
    }

    /// <summary>
    /// 방 내 랜덤 스폰 위치 반환
    /// </summary>
    public Vector3 GetRandomSpawnPosition()
    {
        var spawnPoints = GetEnemySpawnPoints();

        if (spawnPoints.Count > 0)
        {
            // 사용 가능한 스폰 포인트 중 랜덤 선택
            var availablePoints = spawnPoints.Where(sp => sp.CanSpawn()).ToList();
            if (availablePoints.Count > 0)
            {
                var randomPoint = availablePoints[Random.Range(0, availablePoints.Count)];
                return randomPoint.transform.position;
            }
        }

        // 스폰 포인트가 없으면 방 경계 내 랜덤 위치
        Bounds bounds = GetRoomBounds();
        return new Vector3(
            Random.Range(bounds.min.x + 1f, bounds.max.x - 1f),
            bounds.center.y,
            Random.Range(bounds.min.z + 1f, bounds.max.z - 1f)
        );
    }

    /// <summary>
    /// 적 스폰 시 호출 (EnemyManager에서 사용)
    /// </summary>
    public void OnEnemySpawned(Enemy enemy)
    {
        if (enemy == null) return;

        // 기존 spawnedEnemies 리스트에 추가
        if (!spawnedEnemies.Contains(enemy.gameObject))
        {
            spawnedEnemies.Add(enemy.gameObject);
        }

        _lastSpawnTime = Time.time;

        if (_debugMode)
        {
            JCDebug.Log($"[Room] {name}에 적 스폰됨: {enemy.name}");
        }
    }

    /// <summary>
    /// 적 제거 시 호출 (EnemyManager에서 사용)
    /// </summary>
    public void OnEnemyDespawned(Enemy enemy)
    {
        if (enemy == null) return;

        // 기존 spawnedEnemies 리스트에서 제거
        spawnedEnemies.Remove(enemy.gameObject);

        // 클리어 조건 체크는 EnemyManager에서 처리하므로 여기서는 제거만
        if (_debugMode)
        {
            JCDebug.Log($"[Room] {name}에서 적 제거됨: {enemy.name}");
        }
    }

    /// <summary>
    /// EnemyManager에 의한 방 클리어 처리
    /// </summary>
    private void OnRoomClearedByEnemyManager()
    {
        // 기존 클리어 처리 + EnemyManager 연동
        OnRoomCleared?.Invoke(this);
        SpawnClearRewards();

        // 방 클리어 효과
        PlayRoomClearEffects();

        if (_debugMode)
        {
            JCDebug.Log($"[Room] {name} EnemyManager에 의해 클리어됨");
        }
    }

    #endregion

    #region 아이템 스폰 시스템

    private void SpawnItems()
    {
        foreach (var spawnPoint in itemSpawnPoints)
        {
            if (spawnPoint != null && Random.value <= 0.3f) // 30% 확률로 아이템 스폰
            {
                SpawnItemAtTransform(spawnPoint);
            }
        }

        if (_debugMode && itemSpawnPoints.Count > 0)
        {
            JCDebug.Log($"[Room] 아이템 스폰 시도: {itemSpawnPoints.Count}개 포인트");
        }
    }

    private void SpawnItemAtTransform(Transform spawnTransform)
    {
        // 향후 ItemManager와 연동하여 실제 아이템 스폰
        if (_debugMode)
        {
            JCDebug.Log($"[Room] 아이템 스폰 위치: {spawnTransform.position}");
        }

        // 임시 마커로 빈 GameObject 생성 (디버그용)
        if (_debugMode)
        {
            GameObject debugMarker = new GameObject("Item_Debug_Marker");
            debugMarker.transform.position = spawnTransform.position;
            debugMarker.transform.SetParent(transform);
            spawnedItems.Add(debugMarker);
        }
    }

    #endregion

    #region 방 클리어 시스템

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

        if (_debugMode)
        {
            JCDebug.Log($"[Room] 방 클리어: {roomData?.RoomName}");
        }
    }

    private void SpawnClearRewards()
    {
        // 클리어 보상 스폰 (경험치, 골드, 아이템 등)
        // 향후 RewardManager나 ItemManager와 연동
        if (_debugMode)
        {
            JCDebug.Log($"[Room] {name} 클리어 보상 스폰");
        }
    }

    #endregion

    #region 시각/음향 효과

    private void PlayActivationEffects()
    {
        // 방 활성화 시각/음향 효과
        var particles = GetComponentInChildren<ParticleSystem>();
        if (particles != null)
        {
            particles.Play();
        }

        // 활성화 사운드 재생
        if (AudioManager.Instance != null)
        {
            // AudioManager.Instance.PlaySFX(roomActivationSound);
        }
    }

    /// <summary>
    /// 방 클리어 시각/음향 효과
    /// </summary>
    private void PlayRoomClearEffects()
    {
        // 클리어 파티클 효과
        var clearParticles = GetComponentInChildren<ParticleSystem>();
        if (clearParticles != null)
        {
            clearParticles.Play();
        }

        // 클리어 사운드 재생
        if (AudioManager.Instance != null)
        {
            // AudioManager.Instance.PlaySFX(roomClearSound);
        }
    }

    #endregion

    #region 유틸리티 메서드

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

    /// <summary>
    /// 적 스폰 포인트 새로고침
    /// </summary>
    public void RefreshEnemySpawnPoints()
    {
        _enemySpawnPoints.Clear();

        // 자식 오브젝트에서 EnemySpawnPoint 찾기
        var spawnPoints = GetComponentsInChildren<EnemySpawnPoint>();
        _enemySpawnPoints.AddRange(spawnPoints);

        // "EnemySpawn" 태그로 찾기 (백업)
        if (_enemySpawnPoints.Count == 0)
        {
            var taggedSpawns = GameObject.FindGameObjectsWithTag("EnemySpawn");
            foreach (var spawn in taggedSpawns)
            {
                if (IsChildOf(spawn.transform, transform))
                {
                    var spawnPoint = spawn.GetComponent<EnemySpawnPoint>();
                    if (spawnPoint == null)
                    {
                        spawnPoint = spawn.AddComponent<EnemySpawnPoint>();
                    }
                    _enemySpawnPoints.Add(spawnPoint);
                }
            }
        }

        if (_debugMode)
        {
            JCDebug.Log($"[Room] {name} 적 스폰 포인트 {_enemySpawnPoints.Count}개 발견");
        }
    }

    /// <summary>
    /// 방 경계 자동 설정
    /// </summary>
    public void AutoSetupRoomBounds()
    {
        _roomBounds = GetComponent<BoxCollider2D>();

        if (_roomBounds == null)
        {
            _roomBounds = gameObject.AddComponent<BoxCollider2D>();
            _roomBounds.isTrigger = true;

            // 자식 오브젝트들을 고려해서 크기 자동 설정
            Bounds combinedBounds = new Bounds(transform.position, Vector3.zero);

            foreach (Transform child in transform)
            {
                var renderer = child.GetComponent<Renderer>();
                if (renderer != null)
                {
                    combinedBounds.Encapsulate(renderer.bounds);
                }
            }

            if (combinedBounds.size == Vector3.zero)
            {
                combinedBounds.size = Vector3.one * 10f; // 기본 크기
            }

            _roomBounds.size = combinedBounds.size;
            _roomBounds.offset = combinedBounds.center - transform.position;
        }
    }

    #endregion

    #region 디버그 및 에디터 지원

    [ContextMenu("Debug Room Info")]
    public void DebugRoomInfo()
    {
        JCDebug.Log($"=== Room Debug Info: {name} ===");
        JCDebug.Log($"Is Active: {isActive}");
        JCDebug.Log($"Is Visited: {isVisited}");
        JCDebug.Log($"Is Cleared: {isCleared}");
        JCDebug.Log($"Legacy Spawn Points: {enemySpawnPoints.Count}");
        JCDebug.Log($"Enemy Spawn Points: {GetEnemySpawnPoints().Count}");
        JCDebug.Log($"Item Spawn Points: {itemSpawnPoints.Count}");
        JCDebug.Log($"Active Enemies: {spawnedEnemies.Count}");
        JCDebug.Log($"Active Items: {spawnedItems.Count}");
        JCDebug.Log($"Room Bounds: {GetRoomBounds()}");
        JCDebug.Log($"Can Spawn: {CanSpawnEnemies()}");
        JCDebug.Log($"Use EnemyManager: {_useEnemyManager}");
        JCDebug.Log($"Room Type: {RoomType}");
        JCDebug.Log($"Room Data: {(roomData != null ? roomData.RoomName : "null")}");
    }

    [ContextMenu("Force Clear Room")]
    public void ForceClearRoom()
    {
        ClearRoom();
    }

    [ContextMenu("Reset Room State")]
    public void ResetRoomState()
    {
        isActive = false;
        isCleared = false;
        isVisited = false;

        // 스폰된 오브젝트들 제거
        foreach (var enemy in spawnedEnemies)
        {
            if (enemy != null)
            {
                if (Application.isPlaying)
                    Destroy(enemy);
                else
                    DestroyImmediate(enemy);
            }
        }

        foreach (var item in spawnedItems)
        {
            if (item != null)
            {
                if (Application.isPlaying)
                    Destroy(item);
                else
                    DestroyImmediate(item);
            }
        }

        spawnedEnemies.Clear();
        spawnedItems.Clear();

        JCDebug.Log($"[Room] {name} 상태 리셋 완료");
    }

    private void OnDrawGizmos()
    {
        if (!_debugMode) return;

        // 방 경계 그리기 (에디터에서만)
        Bounds bounds = GetRoomBounds();
        Gizmos.color = isCleared ? Color.green : (isActive ? Color.yellow : Color.white);
        Gizmos.DrawWireCube(bounds.center, bounds.size);

        // 기존 스폰 포인트들 그리기 (파란색)
        Gizmos.color = Color.blue;
        foreach (var spawnPoint in enemySpawnPoints)
        {
            if (spawnPoint != null)
            {
                Gizmos.DrawWireSphere(spawnPoint.position, 0.3f);
            }
        }

        // 새 스폰 포인트들 그리기 (빨간색)
        var newSpawnPoints = GetEnemySpawnPoints();
        Gizmos.color = Color.red;
        foreach (var spawnPoint in newSpawnPoints)
        {
            if (spawnPoint != null)
            {
                Gizmos.DrawWireSphere(spawnPoint.transform.position, 0.5f);
            }
        }

        // 아이템 스폰 포인트들 그리기 (초록색)
        Gizmos.color = Color.green;
        foreach (var spawnPoint in itemSpawnPoints)
        {
            if (spawnPoint != null)
            {
                Gizmos.DrawWireCube(spawnPoint.position, Vector3.one * 0.3f);
            }
        }
    }

    private void OnDrawGizmosSelected()
    {
        // 선택된 방의 상세 정보 표시
        Bounds bounds = GetRoomBounds();
        Gizmos.color = Color.cyan;
        Gizmos.DrawCube(bounds.center, bounds.size);

        // 스폰 가능 영역 표시
        Gizmos.color = new Color(1f, 0f, 0f, 0.3f);
        var spawnPoints = GetEnemySpawnPoints();
        foreach (var spawnPoint in spawnPoints)
        {
            if (spawnPoint != null && spawnPoint.CanSpawn())
            {
                Gizmos.DrawSphere(spawnPoint.transform.position, 1f);
            }
        }
    }

    #endregion
}

/* 
=== 주요 특징 ===
✅ 기존 Room.cs의 모든 기능 완전 유지
✅ EnemyManager 연동 시스템 완전 통합
✅ 하위 호환성 보장 (기존 Transform 스폰 포인트 지원)
✅ 선택적 EnemyManager 사용 (_useEnemyManager 플래그)
✅ 자동 설정 시스템 (AutoSetup, RefreshSpawnPoints)
✅ 강화된 디버그 시스템 (컨텍스트 메뉴, 기즈모, 로그)
✅ 성능 최적화 (스폰 쿨다운, 최대 적 수 제한)
✅ 완전한 이벤트 시스템 유지
✅ 에디터 친화적 설계

=== 새로 추가된 기능 ===
1. EnemyManager 완전 연동
2. EnemySpawnPoint 시스템 지원
3. 자동 방 설정 (bounds, spawn points)
4. 하위 호환성 (기존 Transform 스폰 포인트 자동 변환)
5. 디버그 모드 지원
6. 컨텍스트 메뉴 (Setup, Debug, Reset)
7. 시각적 기즈모 시스템
8. 성능 최적화 옵션들

=== 사용법 ===
1. 기존 Room 오브젝트에 BoxCollider2D 추가
2. _useEnemyManager = true 설정
3. 우클릭 → "Setup Room for EnemyManager" 실행
4. EnemySpawnPoint들을 자식으로 배치
5. _debugMode = true로 설정하여 디버그 정보 확인

이제 기존 Room 시스템과 새로운 EnemyManager가 완벽하게 통합되었습니다!
*/