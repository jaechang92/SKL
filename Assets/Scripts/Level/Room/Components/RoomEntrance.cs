// Assets/Scripts/Level/Room/Components/RoomEntrance.cs ����
using UnityEngine;
using System.Collections;
using CustomDebug;

[RequireComponent(typeof(Collider2D))]
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

    // ���� ����
    private bool _hasBeenUsed = false;
    private bool _isPlayerInside = false;
    private Collider2D _triggerCollider;
    private Room _parentRoom;
    private Coroutine _activationCoroutine;

    // �̺�Ʈ
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
            JCDebug.Log("[RoomEntrance] Collider2D ������Ʈ�� �ʿ��մϴ�!", JCDebug.LogLevel.Error);
        }

        if (_parentRoom == null && _showDebugInfo)
        {
            JCDebug.Log("[RoomEntrance] �θ� Room�� ã�� �� �����ϴ�.", JCDebug.LogLevel.Warning);
        }
    }

    private void SetupEntrance()
    {
        // �Ա� ���⿡ ���� �ڵ� ��ġ ����
        if (_entranceDirection != RoomDirection.Any && _parentRoom != null)
        {
            PositionEntranceByDirection();
        }

        // �ð��� ��� ����
        SetupVisualElements();

        JCDebug.Log($"[RoomEntrance] �Ա� ���� �Ϸ�: {_entranceDirection}", JCDebug.LogLevel.Info, !_showDebugInfo);
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
        // ��� ���� �ð� ���
        if (_lockedVisual != null)
        {
            _lockedVisual.SetActive(_requiresKey);
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
            spriteRenderer.color = _entranceColor;
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;

        JCDebug.Log($"[RoomEntrance] �÷��̾� ����: {gameObject.name}", JCDebug.LogLevel.Info, !_showDebugInfo);

        // ���� üũ
        if (_checkPlayerDirection && !IsValidPlayerDirection(other.transform))
        {
            JCDebug.Log("[RoomEntrance] �߸��� ���� ����", JCDebug.LogLevel.Info, !_showDebugInfo);
            return;
        }

        // ���� ���� üũ
        if (!CanPlayerEnter(other.gameObject))
        {
            HandleAccessDenied();
            return;
        }

        // ���� ó��
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

        // �÷��̾��� �̵� ���� Ȯ��
        var playerMovement = playerTransform.GetComponent<Rigidbody2D>();
        if (playerMovement == null) return true;

        Vector2 playerVelocity = playerMovement.linearVelocity.normalized;
        Vector2 entranceDirection = GetDirectionVector(_entranceDirection);

        // �Ա� ����� �÷��̾� �̵� ������ �ݴ����� Ȯ��
        float dot = Vector2.Dot(playerVelocity, -entranceDirection);
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

    private bool CanPlayerEnter(GameObject player)
    {
        // Ȱ�� ���� üũ
        if (!IsActive)
        {
            JCDebug.Log("[RoomEntrance] ��Ȱ�� ������ �Ա�", JCDebug.LogLevel.Info, !_showDebugInfo);
            return false;
        }

        // Ű �䱸���� üũ
        if (_requiresKey)
        {
            if (!PlayerHasRequiredKey(player))
            {
                JCDebug.Log($"[RoomEntrance] �ʿ��� Ű�� ����: {_requiredKeyId}", JCDebug.LogLevel.Info, !_showDebugInfo);
                return false;
            }
        }

        return true;
    }

    private bool PlayerHasRequiredKey(GameObject player)
    {
        // �÷��̾��� �κ��丮���� Ű Ȯ��
        // ���� ���������� InventoryManager�� PlayerInventory ������Ʈ ���

        if (string.IsNullOrEmpty(_requiredKeyId)) return true;

        // �ӽ� ����: PlayerPrefs ���
        return PlayerPrefs.GetInt($"HasKey_{_requiredKeyId}", 0) == 1;
    }

    private void HandlePlayerEntered(GameObject player)
    {
        if (_isPlayerInside) return;

        _isPlayerInside = true;

        // Ȱ��ȭ ������ ����
        if (_activationCoroutine != null)
        {
            StopCoroutine(_activationCoroutine);
        }

        _activationCoroutine = StartCoroutine(ActivateEntranceCoroutine(player));
    }

    private IEnumerator ActivateEntranceCoroutine(GameObject player)
    {
        // Ȱ��ȭ ������
        if (_activationDelay > 0)
        {
            yield return new WaitForSeconds(_activationDelay);
        }

        // �÷��̾ ���� �Ա� �ȿ� �ִ��� Ȯ��
        if (!_isPlayerInside) yield break;

        // ���� ȿ�� ���
        PlayEntranceEffects();

        // Ű �Ҹ� (��ȸ�� Ű�� ���)
        if (_requiresKey && _oneTimeUse)
        {
            ConsumeKey(player);
        }

        // ��� Ƚ�� ������Ʈ
        if (_oneTimeUse)
        {
            _hasBeenUsed = true;
            UpdateVisuals();
        }

        // �̺�Ʈ �߻�
        OnPlayerEntered?.Invoke();
        OnEntranceActivated?.Invoke();

        JCDebug.Log($"[RoomEntrance] �÷��̾� ���� �Ϸ�: {gameObject.name}", JCDebug.LogLevel.Info, !_showDebugInfo);
    }

    private void HandlePlayerExited(GameObject player)
    {
        if (!_isPlayerInside) return;

        _isPlayerInside = false;

        // Ȱ��ȭ �ڷ�ƾ �ߴ�
        if (_activationCoroutine != null)
        {
            StopCoroutine(_activationCoroutine);
            _activationCoroutine = null;
        }

        // �̺�Ʈ �߻�
        OnPlayerExited?.Invoke();

        JCDebug.Log($"[RoomEntrance] �÷��̾� ����: {gameObject.name}", JCDebug.LogLevel.Info, !_showDebugInfo);
    }

    private void HandleAccessDenied()
    {
        // ���� �ź� ȿ��
        PlayAccessDeniedEffects();

        // �̺�Ʈ �߻�
        OnAccessDenied?.Invoke();

        JCDebug.Log("[RoomEntrance] ���� �ź�", JCDebug.LogLevel.Info, !_showDebugInfo);
    }

    private void PlayEntranceEffects()
    {
        // Ȱ��ȭ ����Ʈ
        if (_activationEffect != null)
        {
            GameObject effect = Instantiate(_activationEffect, transform.position, transform.rotation);
            Destroy(effect, 2f);
        }

        // ���� ����
        if (_enterSound != null)
        {
            AudioSource.PlayClipAtPoint(_enterSound, transform.position);
        }
    }

    private void PlayAccessDeniedEffects()
    {
        // ���� �ź� �ð� ȿ�� (������ ������ ��)
        StartCoroutine(FlashEffect(Color.red, 0.5f));

        // ���� �ź� ���� (�ִٸ�)
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
        // Ű �Ҹ� ����
        if (!string.IsNullOrEmpty(_requiredKeyId))
        {
            PlayerPrefs.SetInt($"HasKey_{_requiredKeyId}", 0);
            JCDebug.Log($"[RoomEntrance] Ű �Ҹ�: {_requiredKeyId}", JCDebug.LogLevel.Info, !_showDebugInfo);
        }
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
            _lockedVisual.SetActive(_requiresKey && !_hasBeenUsed);
        }

        // ������ ����
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
    /// �Ա� Ȱ��ȭ/��Ȱ��ȭ
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

        JCDebug.Log($"[RoomEntrance] �Ա� ���� ����: {(active ? "Ȱ��ȭ" : "��Ȱ��ȭ")}", JCDebug.LogLevel.Info, !_showDebugInfo);
    }

    /// <summary>
    /// Ű �䱸���� ����
    /// </summary>
    public void SetKeyRequirement(bool requiresKey, string keyId = "")
    {
        _requiresKey = requiresKey;
        _requiredKeyId = keyId;
        UpdateVisuals();
    }

    /// <summary>
    /// �Ա� ���� (��ȸ�� ��� ���� �ʱ�ȭ)
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

        // �Ա� ���� ǥ��
        Gizmos.color = IsActive ? _entranceColor : Color.gray;

        if (_triggerCollider != null)
        {
            Gizmos.DrawWireCube(transform.position, _triggerCollider.bounds.size);
        }
        else
        {
            Gizmos.DrawWireCube(transform.position, Vector3.one);
        }

        // ���� ǥ��
        if (_entranceDirection != RoomDirection.Any)
        {
            Gizmos.color = Color.yellow;
            Vector3 directionVector = GetDirectionVector(_entranceDirection);
            Gizmos.DrawRay(transform.position, directionVector);
        }
    }

    private void OnDrawGizmosSelected()
    {
        // ���õ� ���¿��� �߰� ���� ǥ��
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