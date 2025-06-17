// Assets/Scripts/Level/Room/Components/RoomExit.cs 수정
using CustomDebug;
using Cysharp.Threading.Tasks;
using Metamorph.Managers;
using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class RoomExit : MonoBehaviour
{
    [Header("Exit Settings")]
    [SerializeField] private bool _isActive = true;
    [SerializeField] private bool _requiresRoomClear = false;
    [SerializeField] private bool _isLocked = false;
    [SerializeField] private ExitType _exitType = ExitType.Normal;

    [Header("Exit Direction")]
    [SerializeField] private RoomDirection _exitDirection = RoomDirection.Any;
    [SerializeField] private bool _checkPlayerDirection = true;

    [Header("Destination")]
    [SerializeField] private RoomExit _connectedExit; // 연결된 출구
    [SerializeField] private Transform _teleportDestination; // 텔레포트 목적지
    [SerializeField] private string _targetSceneName = ""; // 씬 전환용

    [Header("Effects")]
    [SerializeField] private GameObject _exitEffect;
    [SerializeField] private AudioClip _exitSound;
    [SerializeField] private float _exitDelay = 0.2f;
    [SerializeField] private bool _fadeOnExit = true;

    [Header("Visual Feedback")]
    [SerializeField] private GameObject _lockedVisual;
    [SerializeField] private GameObject _activeVisual;
    [SerializeField] private Color _exitColor = Color.blue;
    [SerializeField] private Color _lockedColor = Color.red;

    [Header("Debug")]
    [SerializeField] private bool _showDebugInfo = false;
    [SerializeField] private bool _drawGizmos = true;

    // 상태 관리
    private bool _isPlayerInside = false;
    private bool _isProcessingExit = false;
    private Collider2D _triggerCollider;
    private Room _parentRoom;
    private Coroutine _exitCoroutine;

    // 이벤트
    public System.Action OnPlayerExited;
    public System.Action OnExitActivated;
    public System.Action OnExitDeactivated;
    public System.Action OnExitBlocked;
    public System.Action<Transform> OnPlayerTeleported;

    public enum ExitType
    {
        Normal,         // 일반 출구
        Teleporter,     // 텔레포터
        SceneTransition, // 씬 전환
        OneWay,         // 일방향
        Secret          // 비밀 출구
    }

    public enum RoomDirection
    {
        Any,
        North,
        South,
        East,
        West
    }

    // Properties
    public bool IsActive => _isActive && !_isLocked && (!_requiresRoomClear || IsRoomCleared());
    public bool IsLocked => _isLocked;
    public ExitType Type => _exitType;
    public RoomDirection ExitDirection => _exitDirection;
    public bool IsPlayerInside => _isPlayerInside;

    private void Awake()
    {
        InitializeComponents();
    }

    private void Start()
    {
        SetupExit();
        UpdateVisuals();
    }

    private void InitializeComponents()
    {
        _triggerCollider = GetComponent<Collider2D>();
        _triggerCollider.isTrigger = true;

        _parentRoom = GetComponentInParent<Room>();

        if (_triggerCollider == null)
        {
            JCDebug.Log("[RoomExit] Collider2D 컴포넌트가 필요합니다!", JCDebug.LogLevel.Error);
        }

        if (_parentRoom == null && _showDebugInfo)
        {
            JCDebug.Log("[RoomExit] 부모 Room을 찾을 수 없습니다.", JCDebug.LogLevel.Warning);
        }
    }

    private void SetupExit()
    {
        // 출구 방향에 따른 자동 위치 조정
        if (_exitDirection != RoomDirection.Any && _parentRoom != null)
        {
            PositionExitByDirection();
        }

        // 시각적 요소 설정
        SetupVisualElements();

        // 방 클리어 이벤트 구독
        if (_parentRoom != null && _requiresRoomClear)
        {
            _parentRoom.OnRoomCleared += HandleRoomCleared;
        }

        JCDebug.Log($"[RoomExit] 출구 설정 완료: {_exitType} - {_exitDirection}", JCDebug.LogLevel.Info, !_showDebugInfo);
    }

    private void PositionExitByDirection()
    {
        if (_parentRoom == null) return;

        Bounds roomBounds = _parentRoom.GetComponent<Collider2D>()?.bounds ?? new Bounds(transform.position, Vector3.one);
        Vector3 exitPosition = transform.position;

        switch (_exitDirection)
        {
            case RoomDirection.North:
                exitPosition = new Vector3(roomBounds.center.x, roomBounds.max.y, transform.position.z);
                break;
            case RoomDirection.South:
                exitPosition = new Vector3(roomBounds.center.x, roomBounds.min.y, transform.position.z);
                break;
            case RoomDirection.East:
                exitPosition = new Vector3(roomBounds.max.x, roomBounds.center.y, transform.position.z);
                break;
            case RoomDirection.West:
                exitPosition = new Vector3(roomBounds.min.x, roomBounds.center.y, transform.position.z);
                break;
        }

        transform.position = exitPosition;
    }

    private void SetupVisualElements()
    {
        // 잠긴 상태 시각 요소
        if (_lockedVisual != null)
        {
            _lockedVisual.SetActive(_isLocked);
        }

        // 활성 상태 시각 요소
        if (_activeVisual != null)
        {
            _activeVisual.SetActive(_isActive);
        }

        // 색상 적용
        var spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer != null)
        {
            spriteRenderer.color = _isLocked ? _lockedColor : _exitColor;
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Player") || _isProcessingExit) return;

        JCDebug.Log($"[RoomExit] 플레이어 감지: {gameObject.name}", JCDebug.LogLevel.Info, !_showDebugInfo);

        _isPlayerInside = true;

        // 방향 체크
        if (_checkPlayerDirection && !IsValidPlayerDirection(other.transform))
        {
            JCDebug.Log("[RoomExit] 잘못된 퇴장 방향", JCDebug.LogLevel.Info, !_showDebugInfo);
            return;
        }

        // 출구 사용 가능 여부 체크
        if (!CanPlayerExit())
        {
            HandleExitBlocked();
            return;
        }

        // 퇴장 처리 시작
        HandlePlayerExit(other.gameObject);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;

        _isPlayerInside = false;

        // 퇴장 처리 중단
        if (_exitCoroutine != null)
        {
            StopCoroutine(_exitCoroutine);
            _exitCoroutine = null;
            _isProcessingExit = false;
        }
    }

    private bool IsValidPlayerDirection(Transform playerTransform)
    {
        if (_exitDirection == RoomDirection.Any) return true;

        // 플레이어의 이동 방향 확인
        var playerMovement = playerTransform.GetComponent<Rigidbody2D>();
        if (playerMovement == null) return true;

        Vector2 playerVelocity = playerMovement.linearVelocity.normalized;
        Vector2 exitDirection = GetDirectionVector(_exitDirection);

        // 출구 방향과 플레이어 이동 방향이 같은지 확인
        float dot = Vector2.Dot(playerVelocity, exitDirection);
        return dot > 0.5f; // 코사인 60도 이상
    }

    private Vector2 GetDirectionVector(RoomDirection direction)
    {
        switch (direction)
        {
            case RoomDirection.North: return Vector2.up;
            case RoomDirection.South: return Vector2.down;
            case RoomDirection.East: return Vector2.right;
            case RoomDirection.West: return Vector2.left;
            default: return Vector2.zero;
        }
    }

    private bool CanPlayerExit()
    {
        // 활성 상태 체크
        if (!IsActive)
        {
            JCDebug.Log("[RoomExit] 비활성 상태의 출구", JCDebug.LogLevel.Info, !_showDebugInfo);
            return false;
        }

        // 방 클리어 요구사항 체크
        if (_requiresRoomClear && !IsRoomCleared())
        {
            JCDebug.Log("[RoomExit] 방 클리어가 필요함", JCDebug.LogLevel.Info, !_showDebugInfo);
            return false;
        }

        return true;
    }

    private bool IsRoomCleared()
    {
        return _parentRoom != null && _parentRoom.IsCleared;
    }

    private void HandlePlayerExit(GameObject player)
    {
        if (_isProcessingExit) return;

        _isProcessingExit = true;

        // 퇴장 딜레이 적용
        _exitCoroutine = StartCoroutine(ProcessExitCoroutine(player));
    }

    private IEnumerator ProcessExitCoroutine(GameObject player)
    {
        // 퇴장 딜레이
        if (_exitDelay > 0)
        {
            yield return new WaitForSeconds(_exitDelay);
        }

        // 플레이어가 아직 출구 안에 있는지 확인
        if (!_isPlayerInside)
        {
            _isProcessingExit = false;
            yield break;
        }

        // 퇴장 효과 재생
        PlayExitEffects();

        // 퇴장 타입에 따른 처리
        yield return StartCoroutine(ProcessExitByType(player));

        // 이벤트 발생
        OnPlayerExited?.Invoke();

        _isProcessingExit = false;

        JCDebug.Log($"[RoomExit] 플레이어 퇴장 완료: {_exitType}", JCDebug.LogLevel.Info, !_showDebugInfo);
    }

    private IEnumerator ProcessExitByType(GameObject player)
    {
        switch (_exitType)
        {
            case ExitType.Normal:
                yield return ProcessNormalExit(player);
                break;
            case ExitType.Teleporter:
                yield return ProcessTeleporterExit(player);
                break;
            case ExitType.SceneTransition:
                yield return ProcessSceneTransitionExit(player);
                break;
            case ExitType.OneWay:
                yield return ProcessOneWayExit(player);
                break;
            case ExitType.Secret:
                yield return ProcessSecretExit(player);
                break;
        }
    }

    private IEnumerator ProcessNormalExit(GameObject player)
    {
        // 일반 출구: 단순히 방 퇴장 처리
        yield return null;
    }

    private IEnumerator ProcessTeleporterExit(GameObject player)
    {
        // 텔레포터: 지정된 위치로 이동
        if (_teleportDestination != null)
        {
            if (_fadeOnExit)
            {
                // 페이드 아웃/인 효과
                yield return StartCoroutine(FadeOut());
            }

            player.transform.position = _teleportDestination.position;
            OnPlayerTeleported?.Invoke(_teleportDestination);

            if (_fadeOnExit)
            {
                yield return StartCoroutine(FadeIn());
            }
        }
        else if (_connectedExit != null)
        {
            if (_fadeOnExit)
            {
                yield return StartCoroutine(FadeOut());
            }

            player.transform.position = _connectedExit.transform.position;
            OnPlayerTeleported?.Invoke(_connectedExit.transform);

            if (_fadeOnExit)
            {
                yield return StartCoroutine(FadeIn());
            }
        }
    }

    private IEnumerator ProcessSceneTransitionExit(GameObject player)
    {
        // 씬 전환
        if (!string.IsNullOrEmpty(_targetSceneName))
        {
            if (_fadeOnExit)
            {
                yield return StartCoroutine(FadeOut());
            }

            // 씬 전환 매니저를 통한 씬 로드
            var sceneManager = FindAnyObjectByType<UniTaskSceneTransitionManager>();
            if (sceneManager != null)
            {
                sceneManager.LoadSceneAsync(_targetSceneName).Forget();
            }
            else
            {
                UnityEngine.SceneManagement.SceneManager.LoadScene(_targetSceneName);
            }
        }
    }

    private IEnumerator ProcessOneWayExit(GameObject player)
    {
        // 일방향 출구: 되돌아올 수 없음
        SetLocked(true);
        yield return ProcessNormalExit(player);
    }

    private IEnumerator ProcessSecretExit(GameObject player)
    {
        // 비밀 출구: 특수 효과와 함께
        yield return ProcessTeleporterExit(player);

        // 비밀 출구 발견 이벤트 발생
        // SecretDiscoveryManager.Instance?.OnSecretExitFound();
    }

    private void HandleExitBlocked()
    {
        // 출구 차단 효과
        PlayBlockedEffects();

        // 이벤트 발생
        OnExitBlocked?.Invoke();

        JCDebug.Log("[RoomExit] 출구 차단됨", JCDebug.LogLevel.Info, !_showDebugInfo);
    }

    private void HandleRoomCleared(Room room)
    {
        // 방 클리어 시 출구 잠금 해제
        if (_requiresRoomClear)
        {
            SetLocked(false);
            JCDebug.Log("[RoomExit] 방 클리어로 출구 잠금 해제", JCDebug.LogLevel.Info, !_showDebugInfo);
        }
    }

    private void PlayExitEffects()
    {
        // 퇴장 이펙트
        if (_exitEffect != null)
        {
            GameObject effect = Instantiate(_exitEffect, transform.position, transform.rotation);
            Destroy(effect, 3f);
        }

        // 퇴장 사운드
        if (_exitSound != null)
        {
            AudioSource.PlayClipAtPoint(_exitSound, transform.position);
        }
    }

    private void PlayBlockedEffects()
    {
        // 차단 시각 효과 (빨간색 깜빡임 등)
        StartCoroutine(FlashEffect(_lockedColor, 0.5f));
    }

    private IEnumerator FlashEffect(Color flashColor, float duration)
    {
        var spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null) yield break;

        Color originalColor = spriteRenderer.color;
        float elapsedTime = 0f;

        while (elapsedTime < duration)
        {
            float t = Mathf.PingPong(elapsedTime * 4f, 1f);
            spriteRenderer.color = Color.Lerp(originalColor, flashColor, t);
            elapsedTime += Time.deltaTime;
            yield return null;
        }

        spriteRenderer.color = originalColor;
    }

    private IEnumerator FadeOut()
    {
        // 페이드 아웃 효과 (실제 구현에서는 UI 매니저 사용)
        yield return new WaitForSeconds(0.5f);
    }

    private IEnumerator FadeIn()
    {
        // 페이드 인 효과
        yield return new WaitForSeconds(0.5f);
    }

    private void UpdateVisuals()
    {
        // 활성 상태에 따른 시각 업데이트
        if (_activeVisual != null)
        {
            _activeVisual.SetActive(IsActive);
        }

        if (_lockedVisual != null)
        {
            _lockedVisual.SetActive(_isLocked || (_requiresRoomClear && !IsRoomCleared()));
        }

        // 색상 업데이트
        var spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer != null)
        {
            Color targetColor = IsActive ? _exitColor : _lockedColor;
            targetColor.a = IsActive ? 1f : 0.5f;
            spriteRenderer.color = targetColor;
        }
    }

    #region Public Methods

    /// <summary>
    /// 출구 활성화/비활성화
    /// </summary>
    public void SetActive(bool active)
    {
        _isActive = active;
        UpdateVisuals();

        if (active)
        {
            OnExitActivated?.Invoke();
        }
        else
        {
            OnExitDeactivated?.Invoke();
        }

        JCDebug.Log($"[RoomExit] 출구 상태 변경: {(active ? "활성화" : "비활성화")}", JCDebug.LogLevel.Info, !_showDebugInfo);
    }

    /// <summary>
    /// 출구 잠금/해제
    /// </summary>
    public void SetLocked(bool locked)
    {
        _isLocked = locked;
        UpdateVisuals();

        JCDebug.Log($"[RoomExit] 출구 잠금 상태 변경: {(locked ? "잠김" : "해제")}", JCDebug.LogLevel.Info, !_showDebugInfo);
    }

    /// <summary>
    /// 연결된 출구 설정
    /// </summary>
    public void SetConnectedExit(RoomExit connectedExit)
    {
        _connectedExit = connectedExit;
    }

    /// <summary>
    /// 텔레포트 목적지 설정
    /// </summary>
    public void SetTeleportDestination(Transform destination)
    {
        _teleportDestination = destination;
    }

    #endregion

    #region Debug & Gizmos

    private void OnDrawGizmos()
    {
        if (!_drawGizmos) return;

        // 출구 범위 표시
        Gizmos.color = IsActive ? _exitColor : _lockedColor;

        if (_triggerCollider != null)
        {
            Gizmos.DrawWireCube(transform.position, _triggerCollider.bounds.size);
        }
        else
        {
            Gizmos.DrawWireCube(transform.position, Vector3.one);
        }

        // 방향 표시
        if (_exitDirection != RoomDirection.Any)
        {
            Gizmos.color = Color.cyan;
            Vector3 directionVector = GetDirectionVector(_exitDirection);
            Gizmos.DrawRay(transform.position, directionVector * 2f);
        }

        // 연결 표시
        if (_connectedExit != null)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawLine(transform.position, _connectedExit.transform.position);
        }

        if (_teleportDestination != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(transform.position, _teleportDestination.position);
        }
    }

    [ContextMenu("Test Exit")]
    private void TestExit()
    {
        if (Application.isPlaying)
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                HandlePlayerExit(player);
            }
        }
    }

    private void OnDestroy()
    {
        // 이벤트 구독 해제
        if (_parentRoom != null)
        {
            _parentRoom.OnRoomCleared -= HandleRoomCleared;
        }
    }

    #endregion
}