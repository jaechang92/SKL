// Assets/Scripts/Level/Room/Components/RoomEntrance.cs 수정
using UnityEngine;
using System.Collections;
using CustomDebug;

[RequireComponent(typeof(BoxCollider2D))]
public class RoomEntrance : MonoBehaviour
{
    [Header("Entrance Settings")]
    [SerializeField] private bool _isActive = true;
    [SerializeField] private bool _requiresKey = false;
    [SerializeField] private string _requiredKeyId = "";
    [SerializeField] private bool _oneTimeUse = false;

    [Header("Entrance Direction")]
    [SerializeField] private RoomDirection _entranceDirection = RoomDirection.Any;
    [SerializeField] private bool _checkPlayerDirection = true;

    [Header("Effects")]
    [SerializeField] private GameObject _activationEffect;
    [SerializeField] private AudioClip _enterSound;
    [SerializeField] private float _activationDelay = 0.1f;

    [Header("Visual Feedback")]
    [SerializeField] private GameObject _lockedVisual;
    [SerializeField] private GameObject _activeVisual;
    [SerializeField] private Color _entranceColor = Color.green;

    [Header("Debug")]
    [SerializeField] private bool _showDebugInfo = false;
    [SerializeField] private bool _drawGizmos = true;

    // 상태 관리
    private bool _hasBeenUsed = false;
    private bool _isPlayerInside = false;
    private Collider2D _triggerCollider;
    private Room _parentRoom;
    private Coroutine _activationCoroutine;

    // 이벤트
    public System.Action OnPlayerEntered;
    public System.Action OnPlayerExited;
    public System.Action OnEntranceActivated;
    public System.Action OnEntranceDeactivated;
    public System.Action OnAccessDenied;

    public enum RoomDirection
    {
        Any,
        North,
        South,
        East,
        West
    }

    // Properties
    public bool IsActive => _isActive && (!_oneTimeUse || !_hasBeenUsed);
    public bool RequiresKey => _requiresKey;
    public string RequiredKeyId => _requiredKeyId;
    public bool IsPlayerInside => _isPlayerInside;
    public RoomDirection EntranceDirection => _entranceDirection;

    private void Awake()
    {
        InitializeComponents();
    }

    private void Start()
    {
        SetupEntrance();
        UpdateVisuals();
    }

    private void InitializeComponents()
    {
        _triggerCollider = GetComponent<Collider2D>();
        _triggerCollider.isTrigger = true;

        _parentRoom = GetComponentInParent<Room>();

        if (_triggerCollider == null)
        {
            JCDebug.Log("[RoomEntrance] Collider2D 컴포넌트가 필요합니다!", JCDebug.LogLevel.Error);
        }

        if (_parentRoom == null && _showDebugInfo)
        {
            JCDebug.Log("[RoomEntrance] 부모 Room을 찾을 수 없습니다.", JCDebug.LogLevel.Warning);
        }
    }

    private void SetupEntrance()
    {
        // 입구 방향에 따른 자동 위치 조정
        if (_entranceDirection != RoomDirection.Any && _parentRoom != null)
        {
            PositionEntranceByDirection();
        }

        // 시각적 요소 설정
        SetupVisualElements();

        JCDebug.Log($"[RoomEntrance] 입구 설정 완료: {_entranceDirection}", JCDebug.LogLevel.Info, !_showDebugInfo);
    }

    private void PositionEntranceByDirection()
    {
        if (_parentRoom == null) return;

        Bounds roomBounds = _parentRoom.GetComponent<Collider2D>()?.bounds ?? new Bounds(transform.position, Vector3.one);
        Vector3 entrancePosition = transform.position;

        switch (_entranceDirection)
        {
            case RoomDirection.North:
                entrancePosition = new Vector3(roomBounds.center.x, roomBounds.max.y, transform.position.z);
                break;
            case RoomDirection.South:
                entrancePosition = new Vector3(roomBounds.center.x, roomBounds.min.y, transform.position.z);
                break;
            case RoomDirection.East:
                entrancePosition = new Vector3(roomBounds.max.x, roomBounds.center.y, transform.position.z);
                break;
            case RoomDirection.West:
                entrancePosition = new Vector3(roomBounds.min.x, roomBounds.center.y, transform.position.z);
                break;
        }

        transform.position = entrancePosition;
    }

    private void SetupVisualElements()
    {
        // 잠긴 상태 시각 요소
        if (_lockedVisual != null)
        {
            _lockedVisual.SetActive(_requiresKey);
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
            spriteRenderer.color = _entranceColor;
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;

        JCDebug.Log($"[RoomEntrance] 플레이어 감지: {gameObject.name}", JCDebug.LogLevel.Info, !_showDebugInfo);

        // 방향 체크
        if (_checkPlayerDirection && !IsValidPlayerDirection(other.transform))
        {
            JCDebug.Log("[RoomEntrance] 잘못된 입장 방향", JCDebug.LogLevel.Info, !_showDebugInfo);
            return;
        }

        // 접근 권한 체크
        if (!CanPlayerEnter(other.gameObject))
        {
            HandleAccessDenied();
            return;
        }

        // 입장 처리
        HandlePlayerEntered(other.gameObject);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;

        HandlePlayerExited(other.gameObject);
    }

    private bool IsValidPlayerDirection(Transform playerTransform)
    {
        if (_entranceDirection == RoomDirection.Any) return true;

        // 플레이어의 이동 방향 확인
        var playerMovement = playerTransform.GetComponent<Rigidbody2D>();
        if (playerMovement == null) return true;

        Vector2 playerVelocity = playerMovement.linearVelocity.normalized;
        Vector2 entranceDirection = GetDirectionVector(_entranceDirection);

        // 입구 방향과 플레이어 이동 방향이 반대인지 확인
        float dot = Vector2.Dot(playerVelocity, -entranceDirection);
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

    private bool CanPlayerEnter(GameObject player)
    {
        // 활성 상태 체크
        if (!IsActive)
        {
            JCDebug.Log("[RoomEntrance] 비활성 상태의 입구", JCDebug.LogLevel.Info, !_showDebugInfo);
            return false;
        }

        // 키 요구사항 체크
        if (_requiresKey)
        {
            if (!PlayerHasRequiredKey(player))
            {
                JCDebug.Log($"[RoomEntrance] 필요한 키가 없음: {_requiredKeyId}", JCDebug.LogLevel.Info, !_showDebugInfo);
                return false;
            }
        }

        return true;
    }

    private bool PlayerHasRequiredKey(GameObject player)
    {
        // 플레이어의 인벤토리에서 키 확인
        // 실제 구현에서는 InventoryManager나 PlayerInventory 컴포넌트 사용

        if (string.IsNullOrEmpty(_requiredKeyId)) return true;

        // 임시 구현: PlayerPrefs 사용
        return PlayerPrefs.GetInt($"HasKey_{_requiredKeyId}", 0) == 1;
    }

    private void HandlePlayerEntered(GameObject player)
    {
        if (_isPlayerInside) return;

        _isPlayerInside = true;

        // 활성화 딜레이 적용
        if (_activationCoroutine != null)
        {
            StopCoroutine(_activationCoroutine);
        }

        _activationCoroutine = StartCoroutine(ActivateEntranceCoroutine(player));
    }

    private IEnumerator ActivateEntranceCoroutine(GameObject player)
    {
        // 활성화 딜레이
        if (_activationDelay > 0)
        {
            yield return new WaitForSeconds(_activationDelay);
        }

        // 플레이어가 아직 입구 안에 있는지 확인
        if (!_isPlayerInside) yield break;

        // 입장 효과 재생
        PlayEntranceEffects();

        // 키 소모 (일회용 키인 경우)
        if (_requiresKey && _oneTimeUse)
        {
            ConsumeKey(player);
        }

        // 사용 횟수 업데이트
        if (_oneTimeUse)
        {
            _hasBeenUsed = true;
            UpdateVisuals();
        }

        // 이벤트 발생
        OnPlayerEntered?.Invoke();
        OnEntranceActivated?.Invoke();

        JCDebug.Log($"[RoomEntrance] 플레이어 입장 완료: {gameObject.name}", JCDebug.LogLevel.Info, !_showDebugInfo);
    }

    private void HandlePlayerExited(GameObject player)
    {
        if (!_isPlayerInside) return;

        _isPlayerInside = false;

        // 활성화 코루틴 중단
        if (_activationCoroutine != null)
        {
            StopCoroutine(_activationCoroutine);
            _activationCoroutine = null;
        }

        // 이벤트 발생
        OnPlayerExited?.Invoke();

        JCDebug.Log($"[RoomEntrance] 플레이어 퇴장: {gameObject.name}", JCDebug.LogLevel.Info, !_showDebugInfo);
    }

    private void HandleAccessDenied()
    {
        // 접근 거부 효과
        PlayAccessDeniedEffects();

        // 이벤트 발생
        OnAccessDenied?.Invoke();

        JCDebug.Log("[RoomEntrance] 접근 거부", JCDebug.LogLevel.Info, !_showDebugInfo);
    }

    private void PlayEntranceEffects()
    {
        // 활성화 이펙트
        if (_activationEffect != null)
        {
            GameObject effect = Instantiate(_activationEffect, transform.position, transform.rotation);
            Destroy(effect, 2f);
        }

        // 입장 사운드
        if (_enterSound != null)
        {
            AudioSource.PlayClipAtPoint(_enterSound, transform.position);
        }
    }

    private void PlayAccessDeniedEffects()
    {
        // 접근 거부 시각 효과 (빨간색 깜빡임 등)
        StartCoroutine(FlashEffect(Color.red, 0.5f));

        // 접근 거부 사운드 (있다면)
        // AudioSource.PlayClipAtPoint(deniedSound, transform.position);
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

    private void ConsumeKey(GameObject player)
    {
        // 키 소모 로직
        if (!string.IsNullOrEmpty(_requiredKeyId))
        {
            PlayerPrefs.SetInt($"HasKey_{_requiredKeyId}", 0);
            JCDebug.Log($"[RoomEntrance] 키 소모: {_requiredKeyId}", JCDebug.LogLevel.Info, !_showDebugInfo);
        }
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
            _lockedVisual.SetActive(_requiresKey && !_hasBeenUsed);
        }

        // 투명도 조절
        var spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer != null)
        {
            Color color = spriteRenderer.color;
            color.a = IsActive ? 1f : 0.5f;
            spriteRenderer.color = color;
        }
    }

    #region Public Methods

    /// <summary>
    /// 입구 활성화/비활성화
    /// </summary>
    public void SetActive(bool active)
    {
        _isActive = active;
        UpdateVisuals();

        if (active)
        {
            OnEntranceActivated?.Invoke();
        }
        else
        {
            OnEntranceDeactivated?.Invoke();
        }

        JCDebug.Log($"[RoomEntrance] 입구 상태 변경: {(active ? "활성화" : "비활성화")}", JCDebug.LogLevel.Info, !_showDebugInfo);
    }

    /// <summary>
    /// 키 요구사항 설정
    /// </summary>
    public void SetKeyRequirement(bool requiresKey, string keyId = "")
    {
        _requiresKey = requiresKey;
        _requiredKeyId = keyId;
        UpdateVisuals();
    }

    /// <summary>
    /// 입구 리셋 (일회용 사용 상태 초기화)
    /// </summary>
    public void ResetEntrance()
    {
        _hasBeenUsed = false;
        _isPlayerInside = false;
        UpdateVisuals();
    }

    #endregion

    #region Debug & Gizmos

    private void OnDrawGizmos()
    {
        if (!_drawGizmos) return;

        // 입구 범위 표시
        Gizmos.color = IsActive ? _entranceColor : Color.gray;

        if (_triggerCollider != null)
        {
            Gizmos.DrawWireCube(transform.position, _triggerCollider.bounds.size);
        }
        else
        {
            Gizmos.DrawWireCube(transform.position, Vector3.one);
        }

        // 방향 표시
        if (_entranceDirection != RoomDirection.Any)
        {
            Gizmos.color = Color.yellow;
            Vector3 directionVector = GetDirectionVector(_entranceDirection);
            Gizmos.DrawRay(transform.position, directionVector);
        }
    }

    private void OnDrawGizmosSelected()
    {
        // 선택된 상태에서 추가 정보 표시
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, 0.5f);
    }

    [ContextMenu("Test Entrance")]
    private void TestEntrance()
    {
        if (Application.isPlaying)
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                HandlePlayerEntered(player);
            }
        }
    }

    #endregion
}