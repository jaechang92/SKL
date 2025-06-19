using CustomDebug;
using Metamorph.Managers;
using UnityEngine;

/// <summary>
/// 적 스폰 포인트 클래스
/// Room 내에서 적이 스폰될 위치와 조건을 정의
/// </summary>
public class EnemySpawnPoint : MonoBehaviour
{
    [Header("Spawn Configuration")]
    [SerializeField] private EnemyType[] _possibleEnemyTypes = { EnemyType.SkeletonWarrior };
    [SerializeField] private float _spawnChance = 1f;
    [SerializeField] private float _spawnCooldown = 5f;
    [SerializeField] private int _maxSpawns = 1;
    [SerializeField] private bool _respawnAfterClear = false;

    [Header("Spawn Conditions")]
    [SerializeField] private bool _requiresPlayerInRoom = true;
    [SerializeField] private float _minDistanceFromPlayer = 2f;
    [SerializeField] private LayerMask _obstacleLayerMask = -1;

    [Header("Visual Debug")]
    [SerializeField] private bool _showGizmos = true;
    [SerializeField] private Color _gizmoColor = Color.red;

    // 상태 관리
    private float _lastSpawnTime = 0f;
    private int _currentSpawnCount = 0;
    private Room _parentRoom;

    public float spawnChance => _spawnChance;

    private void Awake()
    {
        _parentRoom = GetComponentInParent<Room>();

        if (_parentRoom == null)
        {
            JCDebug.Log($"[EnemySpawnPoint] {name}이 Room의 자식이 아닙니다.", JCDebug.LogLevel.Warning);
        }
    }

    /// <summary>
    /// 스폰 가능한지 확인
    /// </summary>
    public bool CanSpawn()
    {
        // 기본 조건 체크
        if (_currentSpawnCount >= _maxSpawns) return false;
        if (Time.time - _lastSpawnTime < _spawnCooldown) return false;

        // 방 상태 체크
        if (_parentRoom != null)
        {
            if (!_parentRoom.IsActive) return false;
            if (_parentRoom.IsCleared && !_respawnAfterClear) return false;
        }

        // 플레이어 거리 체크
        if (_requiresPlayerInRoom && !IsPlayerInValidRange()) return false;

        // 장애물 체크
        if (IsObstructed()) return false;

        return true;
    }

    /// <summary>
    /// 랜덤 적 타입 반환
    /// </summary>
    public EnemyType GetRandomEnemyType()
    {
        if (_possibleEnemyTypes == null || _possibleEnemyTypes.Length == 0)
        {
            return EnemyType.SkeletonWarrior; // 기본값
        }

        return _possibleEnemyTypes[Random.Range(0, _possibleEnemyTypes.Length)];
    }

    /// <summary>
    /// 부모 Room 반환
    /// </summary>
    public Room GetRoom()
    {
        return _parentRoom;
    }

    /// <summary>
    /// 스폰 실행 후 호출 (EnemyManager에서 호출)
    /// </summary>
    public void OnEnemySpawned()
    {
        _lastSpawnTime = Time.time;
        _currentSpawnCount++;
    }

    /// <summary>
    /// 스폰 포인트 리셋 (방 클리어 후 등)
    /// </summary>
    public void ResetSpawnPoint()
    {
        _currentSpawnCount = 0;
        _lastSpawnTime = 0f;
    }

    /// <summary>
    /// 플레이어가 유효한 범위에 있는지 확인
    /// </summary>
    private bool IsPlayerInValidRange()
    {
        var playerController = FindAnyObjectByType<PlayerController>();
        if (playerController == null) return false;

        float distance = Vector3.Distance(transform.position, playerController.transform.position);
        return distance >= _minDistanceFromPlayer;
    }

    /// <summary>
    /// 스폰 위치가 막혀있는지 확인
    /// </summary>
    private bool IsObstructed()
    {
        // 작은 반경으로 겹치는 장애물 체크
        Collider2D obstacleCheck = Physics2D.OverlapCircle(transform.position, 0.5f, _obstacleLayerMask);
        return obstacleCheck != null;
    }

    // ===== 에디터 지원 =====

    private void OnDrawGizmos()
    {
        if (!_showGizmos) return;

        Gizmos.color = CanSpawn() ? _gizmoColor : Color.gray;
        Gizmos.DrawWireSphere(transform.position, 0.5f);

        // 최소 거리 표시
        Gizmos.color = new Color(_gizmoColor.r, _gizmoColor.g, _gizmoColor.b, 0.3f);
        Gizmos.DrawWireSphere(transform.position, _minDistanceFromPlayer);
    }

    private void OnDrawGizmosSelected()
    {
        // 상세 정보 표시
        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(transform.position, 0.3f);

        // 스폰 가능 영역 표시
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, 2f);
    }

    [ContextMenu("Test Spawn Conditions")]
    private void TestSpawnConditions()
    {
        JCDebug.Log($"=== Spawn Point Test: {name} ===");
        JCDebug.Log($"Can Spawn: {CanSpawn()}");
        JCDebug.Log($"Current Spawns: {_currentSpawnCount}/{_maxSpawns}");
        JCDebug.Log($"Cooldown Remaining: {Mathf.Max(0, _spawnCooldown - (Time.time - _lastSpawnTime)):F1}s");
        JCDebug.Log($"Player In Range: {IsPlayerInValidRange()}");
        JCDebug.Log($"Is Obstructed: {IsObstructed()}");
        JCDebug.Log($"Random Enemy Type: {GetRandomEnemyType()}");
    }
}
