// Assets/Scripts/Level/Room/Components/RoomExit.cs ����
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
    [SerializeField] private RoomExit _connectedExit; // ����� �ⱸ
    [SerializeField] private Transform _teleportDestination; // �ڷ���Ʈ ������
    [SerializeField] private string _targetSceneName = ""; // �� ��ȯ��

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

    // ���� ����
    private bool _isPlayerInside = false;
    private bool _isProcessingExit = false;
    private Collider2D _triggerCollider;
    private Room _parentRoom;
    private Coroutine _exitCoroutine;

    // �̺�Ʈ
    public System.Action OnPlayerExited;
    public System.Action OnExitActivated;
    public System.Action OnExitDeactivated;
    public System.Action OnExitBlocked;
    public System.Action<Transform> OnPlayerTeleported;

    public enum ExitType
    {
        Normal,         // �Ϲ� �ⱸ
        Teleporter,     // �ڷ�����
        SceneTransition, // �� ��ȯ
        OneWay,         // �Ϲ���
        Secret          // ��� �ⱸ
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
            JCDebug.Log("[RoomExit] Collider2D ������Ʈ�� �ʿ��մϴ�!", JCDebug.LogLevel.Error);
        }

        if (_parentRoom == null && _showDebugInfo)
        {
            JCDebug.Log("[RoomExit] �θ� Room�� ã�� �� �����ϴ�.", JCDebug.LogLevel.Warning);
        }
    }

    private void SetupExit()
    {
        // �ⱸ ���⿡ ���� �ڵ� ��ġ ����
        if (_exitDirection != RoomDirection.Any && _parentRoom != null)
        {
            PositionExitByDirection();
        }

        // �ð��� ��� ����
        SetupVisualElements();

        // �� Ŭ���� �̺�Ʈ ����
        if (_parentRoom != null && _requiresRoomClear)
        {
            _parentRoom.OnRoomCleared += HandleRoomCleared;
        }

        JCDebug.Log($"[RoomExit] �ⱸ ���� �Ϸ�: {_exitType} - {_exitDirection}", JCDebug.LogLevel.Info, !_showDebugInfo);
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
        // ��� ���� �ð� ���
        if (_lockedVisual != null)
        {
            _lockedVisual.SetActive(_isLocked);
        }

        // Ȱ�� ���� �ð� ���
        if (_activeVisual != null)
        {
            _activeVisual.SetActive(_isActive);
        }

        // ���� ����
        var spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer != null)
        {
            spriteRenderer.color = _isLocked ? _lockedColor : _exitColor;
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Player") || _isProcessingExit) return;

        JCDebug.Log($"[RoomExit] �÷��̾� ����: {gameObject.name}", JCDebug.LogLevel.Info, !_showDebugInfo);

        _isPlayerInside = true;

        // ���� üũ
        if (_checkPlayerDirection && !IsValidPlayerDirection(other.transform))
        {
            JCDebug.Log("[RoomExit] �߸��� ���� ����", JCDebug.LogLevel.Info, !_showDebugInfo);
            return;
        }

        // �ⱸ ��� ���� ���� üũ
        if (!CanPlayerExit())
        {
            HandleExitBlocked();
            return;
        }

        // ���� ó�� ����
        HandlePlayerExit(other.gameObject);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;

        _isPlayerInside = false;

        // ���� ó�� �ߴ�
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

        // �÷��̾��� �̵� ���� Ȯ��
        var playerMovement = playerTransform.GetComponent<Rigidbody2D>();
        if (playerMovement == null) return true;

        Vector2 playerVelocity = playerMovement.linearVelocity.normalized;
        Vector2 exitDirection = GetDirectionVector(_exitDirection);

        // �ⱸ ����� �÷��̾� �̵� ������ ������ Ȯ��
        float dot = Vector2.Dot(playerVelocity, exitDirection);
        return dot > 0.5f; // �ڻ��� 60�� �̻�
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
        // Ȱ�� ���� üũ
        if (!IsActive)
        {
            JCDebug.Log("[RoomExit] ��Ȱ�� ������ �ⱸ", JCDebug.LogLevel.Info, !_showDebugInfo);
            return false;
        }

        // �� Ŭ���� �䱸���� üũ
        if (_requiresRoomClear && !IsRoomCleared())
        {
            JCDebug.Log("[RoomExit] �� Ŭ��� �ʿ���", JCDebug.LogLevel.Info, !_showDebugInfo);
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

        // ���� ������ ����
        _exitCoroutine = StartCoroutine(ProcessExitCoroutine(player));
    }

    private IEnumerator ProcessExitCoroutine(GameObject player)
    {
        // ���� ������
        if (_exitDelay > 0)
        {
            yield return new WaitForSeconds(_exitDelay);
        }

        // �÷��̾ ���� �ⱸ �ȿ� �ִ��� Ȯ��
        if (!_isPlayerInside)
        {
            _isProcessingExit = false;
            yield break;
        }

        // ���� ȿ�� ���
        PlayExitEffects();

        // ���� Ÿ�Կ� ���� ó��
        yield return StartCoroutine(ProcessExitByType(player));

        // �̺�Ʈ �߻�
        OnPlayerExited?.Invoke();

        _isProcessingExit = false;

        JCDebug.Log($"[RoomExit] �÷��̾� ���� �Ϸ�: {_exitType}", JCDebug.LogLevel.Info, !_showDebugInfo);
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
        // �Ϲ� �ⱸ: �ܼ��� �� ���� ó��
        yield return null;
    }

    private IEnumerator ProcessTeleporterExit(GameObject player)
    {
        // �ڷ�����: ������ ��ġ�� �̵�
        if (_teleportDestination != null)
        {
            if (_fadeOnExit)
            {
                // ���̵� �ƿ�/�� ȿ��
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
        // �� ��ȯ
        if (!string.IsNullOrEmpty(_targetSceneName))
        {
            if (_fadeOnExit)
            {
                yield return StartCoroutine(FadeOut());
            }

            // �� ��ȯ �Ŵ����� ���� �� �ε�
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
        // �Ϲ��� �ⱸ: �ǵ��ƿ� �� ����
        SetLocked(true);
        yield return ProcessNormalExit(player);
    }

    private IEnumerator ProcessSecretExit(GameObject player)
    {
        // ��� �ⱸ: Ư�� ȿ���� �Բ�
        yield return ProcessTeleporterExit(player);

        // ��� �ⱸ �߰� �̺�Ʈ �߻�
        // SecretDiscoveryManager.Instance?.OnSecretExitFound();
    }

    private void HandleExitBlocked()
    {
        // �ⱸ ���� ȿ��
        PlayBlockedEffects();

        // �̺�Ʈ �߻�
        OnExitBlocked?.Invoke();

        JCDebug.Log("[RoomExit] �ⱸ ���ܵ�", JCDebug.LogLevel.Info, !_showDebugInfo);
    }

    private void HandleRoomCleared(Room room)
    {
        // �� Ŭ���� �� �ⱸ ��� ����
        if (_requiresRoomClear)
        {
            SetLocked(false);
            JCDebug.Log("[RoomExit] �� Ŭ����� �ⱸ ��� ����", JCDebug.LogLevel.Info, !_showDebugInfo);
        }
    }

    private void PlayExitEffects()
    {
        // ���� ����Ʈ
        if (_exitEffect != null)
        {
            GameObject effect = Instantiate(_exitEffect, transform.position, transform.rotation);
            Destroy(effect, 3f);
        }

        // ���� ����
        if (_exitSound != null)
        {
            AudioSource.PlayClipAtPoint(_exitSound, transform.position);
        }
    }

    private void PlayBlockedEffects()
    {
        // ���� �ð� ȿ�� (������ ������ ��)
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
        // ���̵� �ƿ� ȿ�� (���� ���������� UI �Ŵ��� ���)
        yield return new WaitForSeconds(0.5f);
    }

    private IEnumerator FadeIn()
    {
        // ���̵� �� ȿ��
        yield return new WaitForSeconds(0.5f);
    }

    private void UpdateVisuals()
    {
        // Ȱ�� ���¿� ���� �ð� ������Ʈ
        if (_activeVisual != null)
        {
            _activeVisual.SetActive(IsActive);
        }

        if (_lockedVisual != null)
        {
            _lockedVisual.SetActive(_isLocked || (_requiresRoomClear && !IsRoomCleared()));
        }

        // ���� ������Ʈ
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
    /// �ⱸ Ȱ��ȭ/��Ȱ��ȭ
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

        JCDebug.Log($"[RoomExit] �ⱸ ���� ����: {(active ? "Ȱ��ȭ" : "��Ȱ��ȭ")}", JCDebug.LogLevel.Info, !_showDebugInfo);
    }

    /// <summary>
    /// �ⱸ ���/����
    /// </summary>
    public void SetLocked(bool locked)
    {
        _isLocked = locked;
        UpdateVisuals();

        JCDebug.Log($"[RoomExit] �ⱸ ��� ���� ����: {(locked ? "���" : "����")}", JCDebug.LogLevel.Info, !_showDebugInfo);
    }

    /// <summary>
    /// ����� �ⱸ ����
    /// </summary>
    public void SetConnectedExit(RoomExit connectedExit)
    {
        _connectedExit = connectedExit;
    }

    /// <summary>
    /// �ڷ���Ʈ ������ ����
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

        // �ⱸ ���� ǥ��
        Gizmos.color = IsActive ? _exitColor : _lockedColor;

        if (_triggerCollider != null)
        {
            Gizmos.DrawWireCube(transform.position, _triggerCollider.bounds.size);
        }
        else
        {
            Gizmos.DrawWireCube(transform.position, Vector3.one);
        }

        // ���� ǥ��
        if (_exitDirection != RoomDirection.Any)
        {
            Gizmos.color = Color.cyan;
            Vector3 directionVector = GetDirectionVector(_exitDirection);
            Gizmos.DrawRay(transform.position, directionVector * 2f);
        }

        // ���� ǥ��
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
        // �̺�Ʈ ���� ����
        if (_parentRoom != null)
        {
            _parentRoom.OnRoomCleared -= HandleRoomCleared;
        }
    }

    #endregion
}