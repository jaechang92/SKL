// Assets/Scripts/Level/Room/RoomChoiceManager.cs
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;
using CustomDebug;
using Metamorph.Level.Generation;

namespace Metamorph.Level.Room
{
    /// <summary>
    /// 방 선택 시스템 관리 (CustomTween 사용)
    /// </summary>
    public class RoomChoiceManager : SingletonManager<RoomChoiceManager>
    {
        [Header("UI References")]
        [SerializeField] private RoomChoiceUI _roomChoiceUI;
        [SerializeField] private Canvas _uiCanvas;

        [Header("Choice Settings")]
        [SerializeField] private float _choiceDisplayDelay = 1f;
        [SerializeField] private bool _pauseGameDuringChoice = true;
        [SerializeField] private bool _showChoicePreview = true;

        // 현재 선택 상태
        private List<RoomChoicePortal> _currentChoices = new List<RoomChoicePortal>();
        private bool _isChoosingRoom = false;

        // 이벤트
        public event Action<RoomType, int> OnRoomChosen;
        public event Action OnChoiceStarted;
        public event Action OnChoiceEnded;

        public bool IsChoosingRoom => _isChoosingRoom;

        protected override void OnCreated()
        {
            base.OnCreated();
            InitializeRoomChoiceManager();
        }

        private void InitializeRoomChoiceManager()
        {
            // UI 캔버스 찾기
            if (_uiCanvas == null)
            {
                _uiCanvas = FindAnyObjectByType<Canvas>();
            }

            // UI 생성
            if (_roomChoiceUI == null)
            {
                CreateRoomChoiceUI();
            }

            JCDebug.Log("[RoomChoiceManager] 방 선택 시스템 초기화 완료");
        }

        /// <summary>
        /// 방 선택 UI 생성
        /// </summary>
        private void CreateRoomChoiceUI()
        {
            var uiPrefab = Resources.Load<GameObject>("UI/RoomChoiceUI");
            if (uiPrefab != null && _uiCanvas != null)
            {
                var uiInstance = Instantiate(uiPrefab, _uiCanvas.transform);
                _roomChoiceUI = uiInstance.GetComponent<RoomChoiceUI>();
                _roomChoiceUI.Initialize(this);
            }
        }

        /// <summary>
        /// 방 선택지 표시
        /// </summary>
        public async UniTask ShowRoomChoices(params GameObject[] choicePortals)
        {
            if (_isChoosingRoom) return;

            _isChoosingRoom = true;
            OnChoiceStarted?.Invoke();

            try
            {
                // 포털들을 RoomChoicePortal로 변환
                _currentChoices.Clear();
                foreach (var portal in choicePortals)
                {
                    var choicePortal = portal.GetComponent<RoomChoicePortal>();
                    if (choicePortal != null)
                    {
                        _currentChoices.Add(choicePortal);
                    }
                }

                // 게임 일시정지
                if (_pauseGameDuringChoice)
                {
                    Time.timeScale = 0f;
                }

                // 딜레이 후 UI 표시
                await UniTask.Delay(TimeSpan.FromSeconds(_choiceDisplayDelay).Milliseconds, true);

                // UI 활성화
                if (_roomChoiceUI != null)
                {
                    await _roomChoiceUI.ShowChoices(_currentChoices);
                }

                JCDebug.Log($"[RoomChoiceManager] 방 선택 UI 표시 - {_currentChoices.Count}개 선택지");
            }
            catch (Exception ex)
            {
                JCDebug.Log($"[RoomChoiceManager] 방 선택 표시 실패: {ex.Message}", JCDebug.LogLevel.Error);
                EndRoomChoice();
            }
        }

        /// <summary>
        /// 방 선택 처리
        /// </summary>
        public async UniTask ChooseRoom(RoomChoicePortal chosenPortal)
        {
            if (!_isChoosingRoom || chosenPortal == null) return;

            try
            {
                JCDebug.Log($"[RoomChoiceManager] 방 선택됨: {chosenPortal.RoomType}, 층: {chosenPortal.TargetFloor}");

                // 선택 효과 재생
                await PlayChoiceEffectAsync(chosenPortal);

                // UI 숨기기
                if (_roomChoiceUI != null)
                {
                    await _roomChoiceUI.HideChoices();
                }

                // 선택되지 않은 포털들 제거
                await RemoveUnselectedPortalsAsync(chosenPortal);

                // 새 맵 생성
                await MapGenerator.Instance.GenerateMap();

                // 이벤트 발생
                OnRoomChosen?.Invoke(chosenPortal.RoomType, chosenPortal.TargetFloor);
            }
            catch (Exception ex)
            {
                JCDebug.Log($"[RoomChoiceManager] 방 선택 처리 실패: {ex.Message}", JCDebug.LogLevel.Error);
            }
            finally
            {
                EndRoomChoice();
            }
        }

        /// <summary>
        /// 방 선택 종료
        /// </summary>
        private void EndRoomChoice()
        {
            _isChoosingRoom = false;

            // 게임 재개
            if (_pauseGameDuringChoice)
            {
                Time.timeScale = 1f;
            }

            // 정리
            _currentChoices.Clear();

            OnChoiceEnded?.Invoke();
        }

        /// <summary>
        /// 선택 효과 재생
        /// </summary>
        private async UniTask PlayChoiceEffectAsync(RoomChoicePortal chosenPortal)
        {
            if (chosenPortal.ChoiceEffect != null)
            {
                var effect = Instantiate(chosenPortal.ChoiceEffect, chosenPortal.transform.position, Quaternion.identity);
                Destroy(effect, 2f);
            }

            if (chosenPortal.ChoiceSound != null)
            {
                AudioSource.PlayClipAtPoint(chosenPortal.ChoiceSound, chosenPortal.transform.position);
            }

            await UniTask.Delay(500, true); // 0.5초 대기
        }

        /// <summary>
        /// 선택되지 않은 포털들 제거
        /// </summary>
        private async UniTask RemoveUnselectedPortalsAsync(RoomChoicePortal chosenPortal)
        {
            foreach (var portal in _currentChoices)
            {
                if (portal != chosenPortal)
                {
                    // 페이드 아웃 효과
                    await portal.FadeOutAsync();
                    Destroy(portal.gameObject);
                }
            }
        }
    }

    // ===========================================
    // 방 선택 포털 (CustomTween 사용)
    // ===========================================

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

    // ===========================================
    // 방 선택 UI (CustomTween 사용)
    // ===========================================

    /// <summary>
    /// 방 선택 UI 컴포넌트
    /// </summary>
    public class RoomChoiceUI : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private GameObject _choicePanel;
        [SerializeField] private Transform _choiceContainer;
        [SerializeField] private GameObject _roomChoiceButtonPrefab;

        [Header("Animation Settings")]
        [SerializeField] private float _showDuration = 0.5f;
        [SerializeField] private float _hideDuration = 0.3f;

        private RoomChoiceManager _roomChoiceManager;
        private List<GameObject> _choiceButtons = new List<GameObject>();
        private CustomTween _panelTween;

        public void Initialize(RoomChoiceManager manager)
        {
            _roomChoiceManager = manager;

            if (_choicePanel != null)
            {
                _choicePanel.SetActive(false);
                
                // CustomTween 컴포넌트 추가
                _panelTween = _choicePanel.GetComponent<CustomTween>();
                if (_panelTween == null)
                {
                    _panelTween = _choicePanel.AddComponent<CustomTween>();
                }
                
                SetupPanelTween();
            }
        }

        /// <summary>
        /// 패널 트윈 설정
        /// </summary>
        private void SetupPanelTween()
        {
            if (_panelTween != null)
            {
                // Scale 트윈 설정
                _panelTween.tweenFlags = TweenFlags.Scale;
                var scaleSettings = _panelTween.GetTypeSettings(TweenType.Scale);
                scaleSettings.duration = _showDuration;
                scaleSettings.curve = AnimationCurve.EaseInOut(0, 0, 1, 1);
                scaleSettings.loop = false;
                scaleSettings.pingpong = false;
                
                _panelTween.SetTypeSettings(TweenType.Scale, scaleSettings);
            }
        }

        /// <summary>
        /// 선택지 표시
        /// </summary>
        public async UniTask ShowChoices(List<RoomChoicePortal> choices)
        {
            if (_choicePanel == null) return;

            // 기존 버튼들 정리
            ClearChoiceButtons();

            // 새 버튼들 생성
            CreateChoiceButtons(choices);

            // 패널 표시
            _choicePanel.SetActive(true);

            // CustomTween을 사용한 스케일 애니메이션
            _choicePanel.transform.localScale = Vector3.zero;
            if (_panelTween != null)
            {
                _panelTween.PlayTween(TweenType.Scale);
            }

            await UniTask.Delay(TimeSpan.FromSeconds(_showDuration).Milliseconds, true);
        }

        /// <summary>
        /// 선택지 숨기기
        /// </summary>
        public async UniTask HideChoices()
        {
            if (_choicePanel == null) return;

            // 간단한 스케일 다운 애니메이션
            float elapsed = 0f;
            Vector3 startScale = _choicePanel.transform.localScale;
            
            while (elapsed < _hideDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = elapsed / _hideDuration;
                _choicePanel.transform.localScale = Vector3.Lerp(startScale, Vector3.zero, t);
                await UniTask.Yield();
            }

            _choicePanel.SetActive(false);
            ClearChoiceButtons();
        }

        /// <summary>
        /// 선택 버튼들 생성
        /// </summary>
        private void CreateChoiceButtons(List<RoomChoicePortal> choices)
        {
            if (_roomChoiceButtonPrefab == null || _choiceContainer == null) return;

            foreach (var choice in choices)
            {
                var buttonObj = Instantiate(_roomChoiceButtonPrefab, _choiceContainer);
                var button = buttonObj.GetComponent<RoomChoiceButton>();

                if (button != null)
                {
                    button.Initialize(choice, _roomChoiceManager);
                    _choiceButtons.Add(buttonObj);
                }
            }
        }

        /// <summary>
        /// 선택 버튼들 정리
        /// </summary>
        private void ClearChoiceButtons()
        {
            foreach (var button in _choiceButtons)
            {
                if (button != null)
                {
                    Destroy(button);
                }
            }
            _choiceButtons.Clear();
        }
    }

    /// <summary>
    /// 방 선택 버튼
    /// </summary>
    public class RoomChoiceButton : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private Button _button;
        [SerializeField] private Image _roomIcon;
        [SerializeField] private Text _roomName;
        [SerializeField] private Text _roomDescription;

        private RoomChoicePortal _associatedPortal;
        private RoomChoiceManager _roomChoiceManager;

        public void Initialize(RoomChoicePortal portal, RoomChoiceManager manager)
        {
            _associatedPortal = portal;
            _roomChoiceManager = manager;

            SetupButton();
            UpdateUI();
        }

        private void SetupButton()
        {
            if (_button != null)
            {
                _button.onClick.AddListener(() => {
                    _associatedPortal?.OnSelected();
                });
            }
        }

        private void UpdateUI()
        {
            if (_associatedPortal == null) return;

            // 방 이름
            if (_roomName != null)
            {
                _roomName.text = GetRoomDisplayName(_associatedPortal.RoomType);
            }

            // 방 설명
            if (_roomDescription != null)
            {
                _roomDescription.text = GetRoomDescription(_associatedPortal.RoomType);
            }

            // 방 아이콘
            if (_roomIcon != null)
            {
                var iconSprite = Resources.Load<Sprite>($"Icons/RoomType_{_associatedPortal.RoomType}");
                if (iconSprite != null)
                {
                    _roomIcon.sprite = iconSprite;
                }
            }
        }

        private string GetRoomDisplayName(RoomType roomType)
        {
            return roomType switch
            {
                RoomType.Normal => "일반 방",
                RoomType.Reward => "보상 방",
                RoomType.Boss => "보스 방",
                _ => "알 수 없는 방"
            };
        }

        private string GetRoomDescription(RoomType roomType)
        {
            return roomType switch
            {
                RoomType.Normal => "일반적인 적들과 전투",
                RoomType.Reward => "보상을 얻을 수 있는 방",
                RoomType.Boss => "강력한 보스와의 전투",
                _ => ""
            };
        }
    }
}