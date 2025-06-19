using CustomDebug;
using Cysharp.Threading.Tasks;
using Metamorph.Forms.Base;
using Metamorph.Initialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;

namespace Metamorph.Managers
{
    /// <summary>
    /// 적 관리 시스템
    /// 오브젝트 풀, 스폰 관리, AI 최적화, 방 클리어 체크 등 모든 적 관련 기능 통합
    /// </summary>
    public class EnemyManager : SingletonManager<EnemyManager>, IInitializableAsync
    {
        [Header("Enemy Pool Settings")]
        [SerializeField] private EnemyPoolConfig[] _enemyPoolConfigs;
        [SerializeField] private int _defaultPoolSize = 20;
        [SerializeField] private bool _expandPoolWhenNeeded = true;

        [Header("Spawn Settings")]
        [SerializeField] private float _maxSpawnDistance = 30f;
        [SerializeField] private float _despawnDistance = 40f;
        [SerializeField] private LayerMask _spawnLayerMask = -1;

        [Header("Performance Settings")]
        [SerializeField] private int _maxActiveEnemies = 50;
        [SerializeField] private float _aiUpdateInterval = 0.1f;
        [SerializeField] private float _cullingDistance = 25f;
        [SerializeField] private bool _enableLODSystem = true;

        [Header("Room System Integration")]
        [SerializeField] private bool _autoRegisterWithRooms = true;
        [SerializeField] private float _roomClearCheckInterval = 1f;

        // IManagerInitializable 구현
        public string Name => "EnemyManager";
        public InitializationPriority Priority { get; set; } = InitializationPriority.Normal;
        public bool IsInitialized { get; private set; } = false;

        // 오브젝트 풀 관리
        private Dictionary<EnemyType, Queue<Enemy>> _enemyPools = new Dictionary<EnemyType, Queue<Enemy>>();
        private Dictionary<EnemyType, GameObject> _enemyPrefabs = new Dictionary<EnemyType, GameObject>();
        private List<Enemy> _activeEnemies = new List<Enemy>();
        private Transform _poolParent;

        // 스폰 시스템
        private List<EnemySpawnPoint> _registeredSpawnPoints = new List<EnemySpawnPoint>();
        private Dictionary<Room, List<Enemy>> _roomEnemies = new Dictionary<Room, List<Enemy>>();

        // AI 최적화 시스템
        private List<Enemy> _aiUpdateQueue = new List<Enemy>();
        private int _currentAIUpdateIndex = 0;
        private float _lastAIUpdateTime = 0f;

        // 성능 관리
        private Camera _playerCamera;
        private Transform _playerTransform;
        private float _lastCullingUpdate = 0f;

        // 이벤트 시스템 (옵저버 패턴)
        public event Action<Enemy> OnEnemySpawned;
        public event Action<Enemy> OnEnemyDied;
        public event Action<Enemy> OnEnemyDespawned;
        public event Action<Room> OnRoomCleared;
        public event Action<EnemyType, int> OnEnemyPoolExpanded;

        #region IManagerInitializable 구현

        public async UniTask InitializeAsync(CancellationToken cancellationToken = default)
        {
            if (IsInitialized) return;

            try
            {
                JCDebug.Log("[EnemyManager] 적 시스템 초기화 시작");

                // 1. 기본 설정
                await SetupBasicSystemAsync(cancellationToken);

                // 2. 적 프리팹 로드
                await LoadEnemyPrefabsAsync(cancellationToken);

                // 3. 오브젝트 풀 생성
                await CreateEnemyPoolsAsync(cancellationToken);

                // 4. 시스템 연동 설정
                SetupSystemIntegrations();

                // 5. 성능 최적화 시스템 초기화
                InitializePerformanceSystems();

                IsInitialized = true;
                JCDebug.Log("[EnemyManager] 적 시스템 초기화 완료");
            }
            catch (OperationCanceledException)
            {
                JCDebug.Log("[EnemyManager] 초기화가 취소됨", JCDebug.LogLevel.Warning);
                throw;
            }
            catch (Exception ex)
            {
                JCDebug.Log($"[EnemyManager] 초기화 실패: {ex.Message}", JCDebug.LogLevel.Error);
                throw;
            }
        }

        #endregion

        #region 초기화 메서드들

        private async UniTask SetupBasicSystemAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 풀 부모 오브젝트 생성
            _poolParent = new GameObject("EnemyPools").transform;
            _poolParent.SetParent(transform);

            // 플레이어 및 카메라 참조 설정
            SetupPlayerReferences();

            await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
        }

        private void SetupPlayerReferences()
        {
            // 플레이어 참조 찾기
            var playerController = FindAnyObjectByType<PlayerController>();
            if (playerController != null)
            {
                _playerTransform = playerController.transform;
            }

            // 카메라 참조 찾기
            _playerCamera = Camera.main;
            if (_playerCamera == null)
            {
                _playerCamera = FindAnyObjectByType<Camera>();
            }
        }

        private async UniTask LoadEnemyPrefabsAsync(CancellationToken cancellationToken)
        {
            var enemyPrefabs = Resources.LoadAll<GameObject>("Enemies");
            foreach (var prefab in enemyPrefabs)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var enemy = prefab.GetComponent<Enemy>();
                if (enemy != null && enemy.enemyType != EnemyType.None)
                {
                    _enemyPrefabs[enemy.enemyType] = prefab;
                }
            }
            await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            JCDebug.Log($"[EnemyManager] {_enemyPrefabs.Count}개 적 프리팹 로드 완료");
        }

        private async UniTask CreateEnemyPoolsAsync(CancellationToken cancellationToken)
        {
            foreach (var config in _enemyPoolConfigs)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await CreateEnemyPoolAsync(config.enemyType, config.poolSize, cancellationToken);

                // 3개마다 한 프레임 대기 (부하 분산)
                if (_enemyPools.Count % 3 == 0)
                {
                    await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
                }
            }

            JCDebug.Log($"[EnemyManager] {_enemyPools.Count}개 적 풀 생성 완료");
        }

        private async UniTask CreateEnemyPoolAsync(EnemyType enemyType, int poolSize, CancellationToken cancellationToken)
        {
            if (!_enemyPrefabs.TryGetValue(enemyType, out GameObject prefab))
            {
                JCDebug.Log($"[EnemyManager] {enemyType} 프리팹을 찾을 수 없습니다.", JCDebug.LogLevel.Warning);
                return;
            }

            var pool = new Queue<Enemy>();
            GameObject categoryParent = new GameObject($"{enemyType}_Pool");
            categoryParent.transform.SetParent(_poolParent);

            for (int i = 0; i < poolSize; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                GameObject enemyObj = Instantiate(prefab, categoryParent.transform);
                Enemy enemy = enemyObj.GetComponent<Enemy>();

                // 초기 설정
                enemy.Initialize(this);
                enemy.gameObject.SetActive(false);

                pool.Enqueue(enemy);
            }

            _enemyPools[enemyType] = pool;
            JCDebug.Log($"[EnemyManager] {enemyType} 풀 생성 완료 ({poolSize}개)");
        }

        private void SetupSystemIntegrations()
        {
            // FormManager와 연동
            if (FormManager.Instance != null)
            {
                FormManager.Instance.RegisterPlayer(OnPlayerFormChanged);
            }

            // SkillRemappingSystem과 연동
            SkillRemappingSystem.OnSkillUsed += OnPlayerSkillUsed;

            // Room 시스템과 연동
            if (_autoRegisterWithRooms)
            {
                RegisterWithRoomSystem();
            }
        }

        private void RegisterWithRoomSystem()
        {
            // 씬의 모든 Room에 이벤트 등록
            var rooms = FindObjectsByType<Room>(FindObjectsSortMode.None);
            foreach (var room in rooms)
            {
                room.OnRoomEntered += OnRoomEntered;
                room.OnRoomExited += OnRoomExited;
            }
        }

        private void InitializePerformanceSystems()
        {
            // AI 업데이트 큐 초기화
            _aiUpdateQueue.Clear();

            // 컬링 시스템 초기화
            if (_playerCamera != null)
            {
                JCDebug.Log("[EnemyManager] 성능 최적화 시스템 초기화 완료");
            }
        }

        #endregion

        #region 적 스폰 시스템

        /// <summary>
        /// 특정 위치에 적 스폰
        /// </summary>
        public Enemy SpawnEnemy(EnemyType enemyType, Vector3 position, Room room = null)
        {
            Enemy enemy = GetEnemyFromPool(enemyType);
            if (enemy == null) return null;

            // 적 활성화 및 위치 설정
            enemy.transform.position = position;
            enemy.gameObject.SetActive(true);
            enemy.Spawn(position);

            // 관리 목록에 추가
            _activeEnemies.Add(enemy);
            _aiUpdateQueue.Add(enemy);

            // 방 연결
            if (room != null)
            {
                AssignEnemyToRoom(enemy, room);
            }

            // 이벤트 발생
            OnEnemySpawned?.Invoke(enemy);

            JCDebug.Log($"[EnemyManager] {enemyType} 스폰: {position}");
            return enemy;
        }

        /// <summary>
        /// 스폰 포인트에서 적 스폰
        /// </summary>
        public Enemy SpawnEnemyAtSpawnPoint(EnemySpawnPoint spawnPoint)
        {
            if (spawnPoint == null || !spawnPoint.CanSpawn()) return null;

            // 스폰 확률 체크
            if (UnityEngine.Random.value > spawnPoint.spawnChance) return null;

            // 랜덤 적 타입 선택 (스폰 포인트에 정의된 타입들 중)
            EnemyType enemyType = spawnPoint.GetRandomEnemyType();

            return SpawnEnemy(enemyType, spawnPoint.transform.position, spawnPoint.GetRoom());
        }

        /// <summary>
        /// 웨이브 스폰
        /// </summary>
        public async UniTask SpawnWaveAsync(WaveData waveData, Room room, CancellationToken cancellationToken = default)
        {
            if (waveData == null || room == null) return;

            JCDebug.Log($"[EnemyManager] 웨이브 스폰 시작: {waveData.waveName}");

            for (int i = 0; i < waveData.enemySpawns.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var spawnData = waveData.enemySpawns[i];

                // 스폰 딜레이
                if (spawnData.spawnDelay > 0)
                {
                    await UniTask.Delay(TimeSpan.FromSeconds(spawnData.spawnDelay).Milliseconds, false, PlayerLoopTiming.Update, cancellationToken);
                }

                // 여러 적 동시 스폰
                for (int j = 0; j < spawnData.count; j++)
                {
                    Vector3 spawnPos = GetRandomSpawnPositionInRoom(room);
                    SpawnEnemy(spawnData.enemyType, spawnPos, room);

                    // 동시 스폰 간격
                    if (j < spawnData.count - 1 && spawnData.simultaneousSpawnDelay > 0)
                    {
                        await UniTask.Delay(TimeSpan.FromSeconds(spawnData.simultaneousSpawnDelay).Milliseconds, false, PlayerLoopTiming.Update, cancellationToken);
                    }
                }
            }

            JCDebug.Log($"[EnemyManager] 웨이브 스폰 완료: {waveData.waveName}");
        }

        /// <summary>
        /// 방 내 랜덤 스폰 위치 찾기
        /// </summary>
        private Vector3 GetRandomSpawnPositionInRoom(Room room)
        {
            var spawnPoints = room.GetEnemySpawnPoints();
            if (spawnPoints.Count > 0)
            {
                var randomPoint = spawnPoints[UnityEngine.Random.Range(0, spawnPoints.Count)];
                return randomPoint.transform.position;
            }

            // 스폰 포인트가 없으면 방 중심 근처에서 랜덤 위치
            Bounds roomBounds = room.GetRoomBounds();
            return new Vector3(
                UnityEngine.Random.Range(roomBounds.min.x, roomBounds.max.x),
                roomBounds.center.y,
                UnityEngine.Random.Range(roomBounds.min.z, roomBounds.max.z)
            );
        }

        #endregion

        #region 적 관리 시스템

        /// <summary>
        /// 적 제거 (죽음 또는 디스폰)
        /// </summary>
        public void DespawnEnemy(Enemy enemy, bool isDeath = false)
        {
            if (enemy == null || !_activeEnemies.Contains(enemy)) return;

            // 관리 목록에서 제거
            _activeEnemies.Remove(enemy);
            _aiUpdateQueue.Remove(enemy);

            // 방 연결 해제
            RemoveEnemyFromRoom(enemy);

            // 오브젝트 풀로 반환
            ReturnEnemyToPool(enemy);

            // 이벤트 발생
            if (isDeath)
            {
                OnEnemyDied?.Invoke(enemy);
            }
            else
            {
                OnEnemyDespawned?.Invoke(enemy);
            }
        }

        /// <summary>
        /// 모든 적 제거 (씬 전환 시 등)
        /// </summary>
        public void DespawnAllEnemies()
        {
            var enemiesToRemove = new List<Enemy>(_activeEnemies);
            foreach (var enemy in enemiesToRemove)
            {
                DespawnEnemy(enemy, false);
            }

            JCDebug.Log("[EnemyManager] 모든 적 제거 완료");
        }

        /// <summary>
        /// 특정 방의 모든 적 제거
        /// </summary>
        public void DespawnEnemiesInRoom(Room room)
        {
            if (!_roomEnemies.TryGetValue(room, out List<Enemy> enemies)) return;

            var enemiesToRemove = new List<Enemy>(enemies);
            foreach (var enemy in enemiesToRemove)
            {
                DespawnEnemy(enemy, false);
            }
        }

        #endregion

        #region 오브젝트 풀 시스템

        private Enemy GetEnemyFromPool(EnemyType enemyType)
        {
            if (!_enemyPools.TryGetValue(enemyType, out Queue<Enemy> pool))
            {
                JCDebug.Log($"[EnemyManager] {enemyType} 풀이 존재하지 않습니다.", JCDebug.LogLevel.Warning);
                return null;
            }

            // 풀에서 적 가져오기
            if (pool.Count > 0)
            {
                return pool.Dequeue();
            }

            // 풀이 비어있고 확장 가능한 경우
            if (_expandPoolWhenNeeded)
            {
                ExpandEnemyPool(enemyType, _defaultPoolSize / 2);
                OnEnemyPoolExpanded?.Invoke(enemyType, _defaultPoolSize / 2);

                if (pool.Count > 0)
                {
                    return pool.Dequeue();
                }
            }

            JCDebug.Log($"[EnemyManager] {enemyType} 풀이 비어있습니다.", JCDebug.LogLevel.Warning);
            return null;
        }

        private void ReturnEnemyToPool(Enemy enemy)
        {
            if (enemy == null) return;

            // 적 상태 초기화
            enemy.ResetForPool();
            enemy.gameObject.SetActive(false);

            // 풀로 반환
            if (_enemyPools.TryGetValue(enemy.enemyType, out Queue<Enemy> pool))
            {
                pool.Enqueue(enemy);
            }
        }

        private void ExpandEnemyPool(EnemyType enemyType, int expandCount)
        {
            if (!_enemyPrefabs.TryGetValue(enemyType, out GameObject prefab)) return;

            CreateEnemyPoolAsync(enemyType, expandCount, default).Forget();
            JCDebug.Log($"[EnemyManager] {enemyType} 풀 확장: +{expandCount}개");
        }

        #endregion

        #region 방 시스템 연동

        private void AssignEnemyToRoom(Enemy enemy, Room room)
        {
            if (!_roomEnemies.TryGetValue(room, out List<Enemy> enemies))
            {
                enemies = new List<Enemy>();
                _roomEnemies[room] = enemies;
            }

            if (!enemies.Contains(enemy))
            {
                enemies.Add(enemy);
                enemy.SetCurrentRoom(room);
            }
        }

        private void RemoveEnemyFromRoom(Enemy enemy)
        {
            Room currentRoom = enemy.GetCurrentRoom();
            if (currentRoom != null && _roomEnemies.TryGetValue(currentRoom, out List<Enemy> enemies))
            {
                enemies.Remove(enemy);
                enemy.SetCurrentRoom(null);

                // 방 클리어 체크
                CheckRoomClearCondition(currentRoom);
            }
        }

        private void CheckRoomClearCondition(Room room)
        {
            if (!_roomEnemies.TryGetValue(room, out List<Enemy> enemies)) return;

            // 죽은 적들 제거
            enemies.RemoveAll(e => e == null || !e.IsAlive);

            // 모든 적이 제거되었으면 방 클리어
            if (enemies.Count == 0 && !room.IsCleared)
            {
                room.SetCleared(true);
                OnRoomCleared?.Invoke(room);
                JCDebug.Log($"[EnemyManager] 방 클리어: {room.name}");
            }
        }

        #endregion

        #region 성능 최적화 시스템

        private void Update()
        {
            if (!IsInitialized) return;

            // AI 업데이트 시스템 (부하 분산)
            UpdateEnemyAI();

            // 컬링 시스템
            UpdateEnemyCulling();

            // 거리 기반 디스폰 체크
            CheckDistanceBasedDespawn();

            // 방 클리어 체크 (주기적)
            CheckRoomClearConditions();
        }

        private void UpdateEnemyAI()
        {
            if (Time.time - _lastAIUpdateTime < _aiUpdateInterval) return;

            _lastAIUpdateTime = Time.time;

            if (_aiUpdateQueue.Count == 0) return;

            // 한 프레임에 일부 적만 업데이트 (부하 분산)
            int updatesPerFrame = Mathf.Max(1, _aiUpdateQueue.Count / 10);

            for (int i = 0; i < updatesPerFrame && _currentAIUpdateIndex < _aiUpdateQueue.Count; i++)
            {
                Enemy enemy = _aiUpdateQueue[_currentAIUpdateIndex];
                if (enemy != null && enemy.IsAlive)
                {
                    enemy.UpdateAI();
                }

                _currentAIUpdateIndex++;
            }

            // 인덱스 리셋
            if (_currentAIUpdateIndex >= _aiUpdateQueue.Count)
            {
                _currentAIUpdateIndex = 0;
                _aiUpdateQueue.RemoveAll(e => e == null || !e.IsAlive);
            }
        }

        private void UpdateEnemyCulling()
        {
            if (!_enableLODSystem || _playerCamera == null) return;
            if (Time.time - _lastCullingUpdate < 1f) return; // 1초마다 체크

            _lastCullingUpdate = Time.time;
            Vector3 cameraPos = _playerCamera.transform.position;

            foreach (var enemy in _activeEnemies)
            {
                if (enemy == null) continue;

                float distance = Vector3.Distance(cameraPos, enemy.transform.position);

                // LOD 시스템 적용
                if (distance > _cullingDistance)
                {
                    enemy.SetLODLevel(EnemyLODLevel.Culled);
                }
                else if (distance > _cullingDistance * 0.7f)
                {
                    enemy.SetLODLevel(EnemyLODLevel.Low);
                }
                else if (distance > _cullingDistance * 0.4f)
                {
                    enemy.SetLODLevel(EnemyLODLevel.Medium);
                }
                else
                {
                    enemy.SetLODLevel(EnemyLODLevel.High);
                }
            }
        }

        private void CheckDistanceBasedDespawn()
        {
            if (_playerTransform == null) return;

            Vector3 playerPos = _playerTransform.position;
            var enemiesToDespawn = new List<Enemy>();

            foreach (var enemy in _activeEnemies)
            {
                if (enemy == null) continue;

                float distance = Vector3.Distance(playerPos, enemy.transform.position);
                if (distance > _despawnDistance)
                {
                    enemiesToDespawn.Add(enemy);
                }
            }

            foreach (var enemy in enemiesToDespawn)
            {
                DespawnEnemy(enemy, false);
            }
        }

        private void CheckRoomClearConditions()
        {
            if (Time.time % _roomClearCheckInterval != 0) return;

            foreach (var roomData in _roomEnemies.ToList())
            {
                CheckRoomClearCondition(roomData.Key);
            }
        }

        #endregion

        #region 이벤트 핸들러

        private void OnPlayerFormChanged(FormData newForm)
        {
            // 플레이어 형태 변경 시 적들에게 알림
            foreach (var enemy in _activeEnemies)
            {
                enemy?.OnPlayerFormChanged(newForm);
            }
        }

        private void OnPlayerSkillUsed(int slotIndex, SkillData skillData)
        {
            // 플레이어 스킬 사용 시 적들에게 알림 (AI 반응용)
            foreach (var enemy in _activeEnemies)
            {
                enemy?.OnPlayerSkillUsed(skillData);
            }
        }

        private void OnRoomEntered(Room room)
        {
            // 방 입장 시 적 스폰 (Room에서 호출)
            SpawnEnemiesForRoom(room);
        }

        private void OnRoomExited(Room room)
        {
            // 방 퇴장 시 필요한 처리
        }

        private void SpawnEnemiesForRoom(Room room)
        {
            if (room.IsVisited) return; // 이미 방문한 방은 스폰 안함

            var spawnPoints = room.GetEnemySpawnPoints();
            foreach (var spawnPoint in spawnPoints)
            {
                SpawnEnemyAtSpawnPoint(spawnPoint);
            }
        }

        #endregion

        #region 공개 API

        /// <summary>
        /// 특정 타입의 활성 적 수 반환
        /// </summary>
        public int GetActiveEnemyCount(EnemyType enemyType = EnemyType.All)
        {
            if (enemyType == EnemyType.All)
            {
                return _activeEnemies.Count;
            }

            return _activeEnemies.Count(e => e.enemyType == enemyType);
        }

        /// <summary>
        /// 특정 범위 내 적들 반환
        /// </summary>
        public List<Enemy> GetEnemiesInRange(Vector3 center, float radius)
        {
            return _activeEnemies.Where(e =>
                e != null &&
                Vector3.Distance(e.transform.position, center) <= radius
            ).ToList();
        }

        /// <summary>
        /// 가장 가까운 적 반환
        /// </summary>
        public Enemy GetNearestEnemy(Vector3 position)
        {
            Enemy nearest = null;
            float nearestDistance = float.MaxValue;

            foreach (var enemy in _activeEnemies)
            {
                if (enemy == null) continue;

                float distance = Vector3.Distance(position, enemy.transform.position);
                if (distance < nearestDistance)
                {
                    nearest = enemy;
                    nearestDistance = distance;
                }
            }

            return nearest;
        }

        #endregion

        protected override void OnDestroy()
        {
            base.OnDestroy();

            // 이벤트 구독 해제
            SkillRemappingSystem.OnSkillUsed -= OnPlayerSkillUsed;

            if (FormManager.Instance != null)
            {
                FormManager.Instance.UnregisterPlayer(OnPlayerFormChanged);
            }

            // 모든 적 제거
            DespawnAllEnemies();
        }
    }

    // ===== 보조 클래스 및 열거형 =====

    [System.Serializable]
    public class EnemyPoolConfig
    {
        public EnemyType enemyType;
        public int poolSize = 10;
    }

    [System.Serializable]
    public class WaveData
    {
        public string waveName;
        public EnemySpawnData[] enemySpawns;
    }

    [System.Serializable]
    public class EnemySpawnData
    {
        public EnemyType enemyType;
        public int count = 1;
        public float spawnDelay = 0f;
        public float simultaneousSpawnDelay = 0.1f;
    }

    public enum EnemyType
    {
        None,
        All,
        // 기본 적들
        SkeletonWarrior,
        SkeletonArcher,
        SkeletonMage,
        // 정예 적들
        SkeletonKnight,
        SkeletonBerserker,
        NecromancerApprentice,
        // 보스들
        SkeletonKing,
        LichLord,
        // 특수 적들
        Mimic,
        DeathKnight,
        ShadowAssassin
    }

    public enum EnemyLODLevel
    {
        High,    // 가까운 거리 - 모든 기능 활성화
        Medium,  // 중간 거리 - 일부 기능 비활성화
        Low,     // 먼 거리 - 기본 기능만
        Culled   // 매우 먼 거리 - 거의 모든 기능 비활성화
    }
}

/* 
=== 필요한 Using 구문 ===
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
using Metamorph.Core;
using Metamorph.Forms.Base;
using Metamorph.Forms.Data;
using Metamorph.Managers;
using CustomDebug;

=== Enemy 클래스에 추가해야 할 메서드들 ===
Enemy 클래스에 다음 메서드들을 추가해야 합니다:

public class Enemy : MonoBehaviour
{
    public EnemyType enemyType;
    public bool IsAlive { get; private set; } = true;
    
    public void Initialize(EnemyManager manager) { }
    public void Spawn(Vector3 position) { }
    public void ResetForPool() { }
    public void UpdateAI() { }
    public void SetLODLevel(EnemyLODLevel level) { }
    public void OnPlayerFormChanged(FormData newForm) { }
    public void OnPlayerSkillUsed(SkillData skillData) { }
    public void SetCurrentRoom(Room room) { }
    public Room GetCurrentRoom() { }
}

=== Room 클래스에 추가해야 할 메서드들 ===
Room 클래스에 다음 메서드들을 추가해야 합니다:

public class Room : MonoBehaviour
{
    public List<EnemySpawnPoint> GetEnemySpawnPoints() { }
    public Bounds GetRoomBounds() { }
    public void SetCleared(bool cleared) { }
}

=== UnifiedGameManager 등록 ===
UnifiedGameManager의 RegisterAllManagers에 추가:
RegisterManager<EnemyManager>("Gameplay", InitializationPriority.Normal);

=== 주요 특징 ===
1. 방 기반 적 스폰 시스템
2. 오브젝트 풀을 활용한 메모리 최적화
3. LOD 시스템으로 성능 최적화
4. 웨이브 스폰 시스템 지원
5. FormManager와 연동하여 플레이어 형태에 반응
6. Room 시스템과 완벽 연동
7. AI 부하 분산 시스템
8. 거리 기반 자동 디스폰
9. 방 클리어 조건 자동 체크
10. 다양한 적 타입 지원 (일반/정예/보스/특수)
*/