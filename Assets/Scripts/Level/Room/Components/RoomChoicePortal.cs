using Cysharp.Threading.Tasks;
using Metamorph.Level.Generation;
using Metamorph.Level.Room;
using UnityEngine;

/// <summary>
/// 방 선택 포털 컴포넌트
/// </summary>
public class RoomChoicePortal : MonoBehaviour
{
    [Header("Portal Settings")]
    [SerializeField] private RoomType _roomType;
    [SerializeField] private int _targetFloor;

    [Header("Visual")]
    [SerializeField] private SpriteRenderer _portalSprite;
    [SerializeField] private SpriteRenderer _roomTypeIcon;
    [SerializeField] private ParticleSystem _portalParticles;

    [Header("Effects")]
    [SerializeField] private GameObject _choiceEffect;
    [SerializeField] private AudioClip _choiceSound;
    [SerializeField] private AudioClip _hoverSound;

    [Header("Room Type Colors")]
    [SerializeField] private Color _normalColor = Color.white;
    [SerializeField] private Color _rewardColor = Color.yellow;
    [SerializeField] private Color _bossColor = Color.red;

    private bool _isInteractable = true;
    private Collider2D _portalCollider;
    private CustomTween _hoverTween;
    private Vector3 _originalScale;

    public RoomType RoomType => _roomType;
    public int TargetFloor => _targetFloor;
    public GameObject ChoiceEffect => _choiceEffect;
    public AudioClip ChoiceSound => _choiceSound;

    private void Awake()
    {
        _portalCollider = GetComponent<Collider2D>();
        if (_portalCollider == null)
        {
            _portalCollider = gameObject.AddComponent<CircleCollider2D>();
            _portalCollider.isTrigger = true;
        }

        // CustomTween 컴포넌트 추가
        _hoverTween = GetComponent<CustomTween>();
        if (_hoverTween == null)
        {
            _hoverTween = gameObject.AddComponent<CustomTween>();
        }

        _originalScale = transform.localScale;
    }

    private void Start()
    {
        UpdateVisuals();
        SetupHoverTween();
    }

    /// <summary>
    /// 호버 트윈 설정
    /// </summary>
    private void SetupHoverTween()
    {
        if (_hoverTween != null)
        {
            // Scale 트윈 설정
            _hoverTween.tweenFlags = TweenFlags.Scale;
            var scaleSettings = _hoverTween.GetTypeSettings(TweenType.Scale);
            scaleSettings.duration = 0.2f;
            scaleSettings.curve = AnimationCurve.EaseInOut(0, 0, 1, 1);
            scaleSettings.pingpong = true;
            scaleSettings.loop = false;

            _hoverTween.SetTypeSettings(TweenType.Scale, scaleSettings);
        }
    }

    /// <summary>
    /// 포털 초기화
    /// </summary>
    public void Initialize(RoomType roomType, int targetFloor)
    {
        _roomType = roomType;
        _targetFloor = targetFloor;
        UpdateVisuals();
    }

    /// <summary>
    /// 시각적 요소 업데이트
    /// </summary>
    private void UpdateVisuals()
    {
        // 방 타입에 따른 색상 변경
        Color roomColor = _roomType switch
        {
            RoomType.Normal => _normalColor,
            RoomType.Reward => _rewardColor,
            RoomType.Boss => _bossColor,
            _ => Color.white
        };

        if (_portalSprite != null)
        {
            _portalSprite.color = roomColor;
        }

        // 방 타입 아이콘 변경
        if (_roomTypeIcon != null)
        {
            var iconSprite = Resources.Load<Sprite>($"Icons/RoomType_{_roomType}");
            if (iconSprite != null)
            {
                _roomTypeIcon.sprite = iconSprite;
            }
        }

        // 파티클 색상
        if (_portalParticles != null)
        {
            var main = _portalParticles.main;
            main.startColor = roomColor;
        }
    }

    /// <summary>
    /// 플레이어 상호작용
    /// </summary>
    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!_isInteractable || !other.CompareTag("Player")) return;

        // 호버 사운드
        if (_hoverSound != null)
        {
            AudioSource.PlayClipAtPoint(_hoverSound, transform.position);
        }

        // 호버 효과
        PlayHoverEffect();
    }

    /// <summary>
    /// 선택 처리
    /// </summary>
    public void OnSelected()
    {
        if (!_isInteractable) return;

        _isInteractable = false;
        RoomChoiceManager.Instance?.ChooseRoom(this).Forget();
    }

    /// <summary>
    /// 페이드 아웃 (CustomTween 사용)
    /// </summary>
    public async UniTask FadeOutAsync()
    {
        var renderers = GetComponentsInChildren<SpriteRenderer>();

        // 파티클 정지
        if (_portalParticles != null)
        {
            _portalParticles.Stop();
        }

        // 간단한 페이드 아웃 (코루틴 사용)
        float fadeTime = 0.5f;
        float elapsed = 0f;
        var originalAlphas = new float[renderers.Length];

        // 원본 알파값 저장
        for (int i = 0; i < renderers.Length; i++)
        {
            originalAlphas[i] = renderers[i].color.a;
        }

        // 페이드 아웃
        while (elapsed < fadeTime)
        {
            elapsed += Time.unscaledDeltaTime;
            float alpha = Mathf.Lerp(1f, 0f, elapsed / fadeTime);

            for (int i = 0; i < renderers.Length; i++)
            {
                Color color = renderers[i].color;
                color.a = originalAlphas[i] * alpha;
                renderers[i].color = color;
            }

            await UniTask.Yield();
        }
    }

    /// <summary>
    /// 호버 효과 (CustomTween 사용)
    /// </summary>
    private void PlayHoverEffect()
    {
        if (_hoverTween != null)
        {
            // 현재 스케일에서 1.1배로 확대 후 원래대로
            Vector3 targetScale = _originalScale * 1.1f;

            // CustomTween을 사용한 스케일 애니메이션
            transform.localScale = _originalScale;
            _hoverTween.PlayTween(TweenType.Scale);
        }
    }

    private void OnMouseDown()
    {
        OnSelected();
    }
}

