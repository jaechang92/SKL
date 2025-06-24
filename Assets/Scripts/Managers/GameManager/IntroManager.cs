using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using Cysharp.Threading.Tasks;
using System;
using System.Threading;
using System.Collections.Generic;
using Metamorph.Initialization;
using CustomDebug;
using TMPro;

namespace Metamorph.Managers
{
    /// <summary>
    /// 인트로 씬의 로딩 과정과 UI를 관리하는 매니저
    /// 모든 초기화 완료 후 게임 씬으로 전환
    /// </summary>
    public class IntroManager : SingletonManager<IntroManager>, IInitializableAsync
    {
        [Header("UI References")]
        [SerializeField] private Canvas _introCanvas;
        [SerializeField] private TextMeshProUGUI _loadingText;
        [SerializeField] private TextMeshProUGUI _progressText;
        [SerializeField] private Slider _loadingProgressBar;
        [SerializeField] private TextMeshProUGUI _anyKeyText;
        [SerializeField] private GameObject _loadingPanel;
        [SerializeField] private GameObject _startPanel;

        [Header("Loading Settings")]
        [SerializeField] private float _minimumLoadingTime = 2f;
        [SerializeField] private float _anyKeyBlinkInterval = 1f;
        [SerializeField] private string _targetSceneName = "GameScene";

        [Header("Loading Messages")]
        [SerializeField] private List<LoadingMessage> _loadingMessages = new List<LoadingMessage>();

        [Header("Audio")]
        [SerializeField] private AudioClip _introMusic;
        [SerializeField] private AudioClip _confirmSound;

        // 초기화 관련
        public string Name => "Intro Manager";
        public InitializationPriority Priority => InitializationPriority.Critical;
        public bool IsInitialized { get; private set; }

        // 상태 관리
        private bool _isLoadingComplete = false;
        private bool _canProceed = false;
        private bool _isTransitioning = false;
        private CancellationTokenSource _introCTS;

        // Input System
        private PlayerInputActions _inputActions;

        // 매니저 참조
        private SceneTransitionManager _sceneManager;
        private UnifiedGameManager _gameManager;

        // 이벤트
        public event Action<float> OnLoadingProgress;
        public event Action<string> OnLoadingStatusChanged;
        public event Action OnLoadingComplete;
        public event Action OnSceneTransitionStarted;

        #region Unity Lifecycle

        protected override void Awake()
        {
            base.Awake();
            InitializeInput();
            SetupUI();
        }

        private void Start()
        {
            StartIntroSequence().Forget();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            CleanupInput();
            _introCTS?.Cancel();
            _introCTS?.Dispose();
        }

        #endregion

        #region IInitializableAsync Implementation

        public async UniTask InitializeAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                JCDebug.Log("[IntroManager] 인트로 매니저 초기화 시작");

                // 기본 설정 초기화
                InitializeDefaultSettings();

                // 매니저 참조 설정
                await SetupManagerReferences(cancellationToken);

                // UI 상태 초기화
                SetupInitialUIState();

                IsInitialized = true;
                JCDebug.Log("[IntroManager] 인트로 매니저 초기화 완료", JCDebug.LogLevel.Success);
            }
            catch (Exception ex)
            {
                JCDebug.Log($"[IntroManager] 초기화 실패: {ex.Message}", JCDebug.LogLevel.Error);
                throw;
            }
        }

        public async UniTask CleanupAsync()
        {
            JCDebug.Log("[IntroManager] 인트로 매니저 정리 시작");

            _introCTS?.Cancel();

            // 진행 중인 작업 대기
            while (_isTransitioning)
            {
                await UniTask.Delay(100);
            }

            CleanupInput();
            IsInitialized = false;

            JCDebug.Log("[IntroManager] 인트로 매니저 정리 완료");
        }

        #endregion

        #region Initialization

        private void InitializeInput()
        {
            _inputActions = new PlayerInputActions();
            _inputActions.UI.AnyKey.performed += OnAnyKeyPressed;
        }

        private void CleanupInput()
        {
            if (_inputActions != null)
            {
                _inputActions.UI.AnyKey.performed -= OnAnyKeyPressed;
                _inputActions.Dispose();
            }
        }

        private void SetupUI()
        {
            // UI 컴포넌트 자동 생성 (없을 경우)
            if (_introCanvas == null)
            {
                CreateIntroUI();
            }

            // 기본 로딩 메시지 설정
            if (_loadingMessages.Count == 0)
            {
                SetupDefaultLoadingMessages();
            }
        }

        private void CreateIntroUI()
        {
            // Canvas 생성
            GameObject canvasObj = new GameObject("IntroCanvas");
            _introCanvas = canvasObj.AddComponent<Canvas>();
            _introCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _introCanvas.sortingOrder = 1000;

            var canvasScaler = canvasObj.AddComponent<CanvasScaler>();
            canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasScaler.referenceResolution = new Vector2(1920, 1080);

            canvasObj.AddComponent<GraphicRaycaster>();

            // 로딩 패널 생성
            CreateLoadingPanel();

            // 시작 패널 생성
            CreateStartPanel();

            JCDebug.Log("[IntroManager] 인트로 UI 자동 생성 완료");
        }

        private void CreateLoadingPanel()
        {
            _loadingPanel = new GameObject("LoadingPanel");
            _loadingPanel.transform.SetParent(_introCanvas.transform, false);

            var rectTransform = _loadingPanel.AddComponent<RectTransform>();
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;

            // 배경
            var bg = _loadingPanel.AddComponent<Image>();
            bg.color = Color.black;

            // 로딩 텍스트
            CreateText("LoadingText", "Loading...", new Vector2(0, 100), out _loadingText);
            _loadingText.fontSize = 48;

            // 진행률 텍스트
            CreateText("ProgressText", "0%", new Vector2(0, 50), out _progressText);
            _progressText.fontSize = 24;

            // 진행률 바
            CreateProgressBar();
        }

        private void CreateStartPanel()
        {
            _startPanel = new GameObject("StartPanel");
            _startPanel.transform.SetParent(_introCanvas.transform, false);

            var rectTransform = _startPanel.AddComponent<RectTransform>();
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;

            // 배경
            var bg = _startPanel.AddComponent<Image>();
            bg.color = new Color(0, 0, 0, 0.8f);

            // Any Key 텍스트
            CreateText("AnyKeyText", "Press Any Key to Start", new Vector2(0, -100), out _anyKeyText);
            _anyKeyText.fontSize = 36;

            _startPanel.SetActive(false);
        }

        private void CreateText(string name, string text, Vector2 position, out TextMeshProUGUI textComponent)
        {
            GameObject textObj = new GameObject(name);
            textObj.transform.SetParent(_loadingPanel.transform, false);

            textComponent = textObj.AddComponent<TextMeshProUGUI>();
            textComponent.text = text;
            textComponent.fontSize = 24;
            textComponent.color = Color.white;
            textComponent.alignment = TextAlignmentOptions.Center;

            var rectTransform = textComponent.rectTransform;
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = position;
            rectTransform.sizeDelta = new Vector2(800, 60);
        }

        private void CreateProgressBar()
        {
            GameObject sliderObj = new GameObject("ProgressBar");
            sliderObj.transform.SetParent(_loadingPanel.transform, false);

            _loadingProgressBar = sliderObj.AddComponent<Slider>();
            var rectTransform = sliderObj.GetComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = new Vector2(0, -50);
            rectTransform.sizeDelta = new Vector2(600, 30);

            // Background
            GameObject bg = new GameObject("Background");
            bg.transform.SetParent(sliderObj.transform, false);
            var bgImage = bg.AddComponent<Image>();
            bgImage.color = new Color(0.2f, 0.2f, 0.2f, 0.8f);
            _loadingProgressBar.targetGraphic = bgImage;

            // Fill Area
            GameObject fillArea = new GameObject("Fill Area");
            fillArea.transform.SetParent(sliderObj.transform, false);
            var fillRectTransform = fillArea.AddComponent<RectTransform>();
            fillRectTransform.anchorMin = Vector2.zero;
            fillRectTransform.anchorMax = Vector2.one;
            fillRectTransform.offsetMin = Vector2.zero;
            fillRectTransform.offsetMax = Vector2.zero;

            // Fill
            GameObject fill = new GameObject("Fill");
            fill.transform.SetParent(fillArea.transform, false);
            var fillImage = fill.AddComponent<Image>();
            fillImage.color = new Color(0.2f, 0.8f, 0.2f, 1f);
            _loadingProgressBar.fillRect = fill.GetComponent<RectTransform>();

            _loadingProgressBar.value = 0f;
        }

        private void InitializeDefaultSettings()
        {
            _introCTS = new CancellationTokenSource();

            // 기본 타겟 씬 설정
            if (string.IsNullOrEmpty(_targetSceneName))
            {
                _targetSceneName = "GameScene";
            }
        }

        private async UniTask SetupManagerReferences(CancellationToken cancellationToken)
        {
            // UnifiedGameManager 참조
            int attempts = 0;
            while (_gameManager == null && attempts < 50)
            {
                _gameManager = UnifiedGameManager.Instance;
                if (_gameManager == null)
                {
                    await UniTask.Delay(100, cancellationToken: cancellationToken);
                    attempts++;
                }
            }

            // SceneTransitionManager 참조
            _sceneManager = SceneTransitionManager.Instance;
            if (_sceneManager == null)
            {
                JCDebug.Log("[IntroManager] SceneTransitionManager를 찾을 수 없음", JCDebug.LogLevel.Warning);
            }
        }

        private void SetupInitialUIState()
        {
            if (_loadingPanel != null) _loadingPanel.SetActive(true);
            if (_startPanel != null) _startPanel.SetActive(false);

            _isLoadingComplete = false;
            _canProceed = false;
        }

        private void SetupDefaultLoadingMessages()
        {
            _loadingMessages.AddRange(new LoadingMessage[]
            {
                new LoadingMessage(0.0f, "게임 초기화 중..."),
                new LoadingMessage(0.1f, "오디오 시스템 로딩..."),
                new LoadingMessage(0.2f, "플레이어 데이터 로딩..."),
                new LoadingMessage(0.4f, "게임 설정 로딩..."),
                new LoadingMessage(0.6f, "리소스 프리로딩..."),
                new LoadingMessage(0.8f, "매니저 시스템 초기화..."),
                new LoadingMessage(0.95f, "로딩 완료!"),
                new LoadingMessage(1.0f, "게임 준비 완료")
            });
        }

        #endregion

        #region Intro Sequence

        /// <summary>
        /// 인트로 시퀀스 시작
        /// </summary>
        private async UniTaskVoid StartIntroSequence()
        {
            try
            {
                JCDebug.Log("[IntroManager] 인트로 시퀀스 시작");

                // 입력 활성화
                _inputActions.Enable();

                // 인트로 음악 재생
                await PlayIntroMusic();

                // 로딩 프로세스 시작
                await ExecuteLoadingProcess(_introCTS.Token);

                // 로딩 완료 후 처리
                await OnLoadingFinished();

                JCDebug.Log("[IntroManager] 인트로 시퀀스 완료");
            }
            catch (OperationCanceledException)
            {
                JCDebug.Log("[IntroManager] 인트로 시퀀스 취소됨", JCDebug.LogLevel.Warning);
            }
            catch (Exception ex)
            {
                JCDebug.Log($"[IntroManager] 인트로 시퀀스 실패: {ex.Message}", JCDebug.LogLevel.Error);
            }
        }

        /// <summary>
        /// 로딩 프로세스 실행
        /// </summary>
        private async UniTask ExecuteLoadingProcess(CancellationToken cancellationToken)
        {
            float startTime = Time.time;
            float progress = 0f;
            int messageIndex = 0;

            // UnifiedGameManager 초기화 대기 및 진행률 추적
            if (_gameManager != null)
            {
                // 매니저 초기화 시작
                var initTask = _gameManager.InitializeAllManagersAsync(cancellationToken);

                // 초기화 진행률 추적
                while (!initTask.Status.IsCompleted())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // 시간 기반 진행률 계산 (최소 로딩 시간 보장)
                    float timeProgress = (Time.time - startTime) / _minimumLoadingTime;
                    progress = Mathf.Min(timeProgress, 0.9f);

                    // 로딩 메시지 업데이트
                    UpdateLoadingMessage(progress, ref messageIndex);

                    // UI 업데이트
                    UpdateLoadingUI(progress);

                    await UniTask.Delay(50, cancellationToken: cancellationToken);
                }

                // 초기화 완료 대기
                await initTask;
            }
            else
            {
                // 게임 매니저가 없는 경우 시간 기반으로만 진행
                while (progress < 1.0f)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    float timeProgress = (Time.time - startTime) / _minimumLoadingTime;
                    progress = Mathf.Min(timeProgress, 1.0f);

                    UpdateLoadingMessage(progress, ref messageIndex);
                    UpdateLoadingUI(progress);

                    await UniTask.Delay(50, cancellationToken: cancellationToken);
                }
            }

            // 최소 로딩 시간 보장
            float totalTime = Time.time - startTime;
            if (totalTime < _minimumLoadingTime)
            {
                await UniTask.Delay(TimeSpan.FromSeconds(_minimumLoadingTime - totalTime), cancellationToken: cancellationToken);
            }

            // 로딩 완료
            UpdateLoadingUI(1.0f);
            _isLoadingComplete = true;

            JCDebug.Log("[IntroManager] 로딩 프로세스 완료", JCDebug.LogLevel.Success);
        }

        /// <summary>
        /// 로딩 메시지 업데이트
        /// </summary>
        private void UpdateLoadingMessage(float progress, ref int messageIndex)
        {
            if (messageIndex < _loadingMessages.Count &&
                progress >= _loadingMessages[messageIndex].triggerProgress)
            {
                var message = _loadingMessages[messageIndex];

                if (_loadingText != null)
                    _loadingText.text = message.message;

                OnLoadingStatusChanged?.Invoke(message.message);
                messageIndex++;

                JCDebug.Log($"[IntroManager] 로딩 상태: {message.message} ({progress:P1})");
            }
        }

        /// <summary>
        /// 로딩 UI 업데이트
        /// </summary>
        private void UpdateLoadingUI(float progress)
        {
            if (_loadingProgressBar != null)
                _loadingProgressBar.value = progress;

            if (_progressText != null)
                _progressText.text = $"{progress:P0}";

            OnLoadingProgress?.Invoke(progress);
        }

        /// <summary>
        /// 로딩 완료 후 처리
        /// </summary>
        private async UniTask OnLoadingFinished()
        {
            OnLoadingComplete?.Invoke();

            // 로딩 패널 비활성화, 시작 패널 활성화
            if (_loadingPanel != null) _loadingPanel.SetActive(false);
            if (_startPanel != null) _startPanel.SetActive(true);

            // Any Key 텍스트 깜빡임 시작
            StartAnyKeyBlink().Forget();

            _canProceed = true;

            JCDebug.Log("[IntroManager] Any Key 대기 중...");
        }

        /// <summary>
        /// Any Key 텍스트 깜빡임 효과
        /// </summary>
        private async UniTaskVoid StartAnyKeyBlink()
        {
            while (_canProceed && !_isTransitioning)
            {
                if (_anyKeyText != null)
                {
                    _anyKeyText.alpha = _anyKeyText.alpha > 0.5f ? 0.3f : 1.0f;
                }

                await UniTask.Delay(TimeSpan.FromSeconds(_anyKeyBlinkInterval), cancellationToken: _introCTS.Token);
            }
        }

        #endregion

        #region Input Handling

        /// <summary>
        /// Any Key 입력 처리
        /// </summary>
        private void OnAnyKeyPressed(InputAction.CallbackContext context)
        {
            if (!_canProceed || _isTransitioning) return;

            JCDebug.Log("[IntroManager] Any Key 입력 감지 - 게임 씬으로 전환");
            StartGameScene().Forget();
        }

        #endregion

        #region Scene Transition

        /// <summary>
        /// 게임 씬 시작
        /// </summary>
        private async UniTaskVoid StartGameScene()
        {
            if (_isTransitioning) return;

            try
            {
                _isTransitioning = true;
                _inputActions.Disable();

                // 확인 사운드 재생
                await PlayConfirmSound();

                // 씬 전환 이벤트 발생
                OnSceneTransitionStarted?.Invoke();

                // SceneTransitionManager를 통한 씬 전환
                if (_sceneManager != null)
                {
                    await _sceneManager.TransitionToSceneAsync(_targetSceneName);
                }
                else
                {
                    // 직접 씬 전환 (fallback)
                    UnityEngine.SceneManagement.SceneManager.LoadScene(_targetSceneName);
                }

                JCDebug.Log($"[IntroManager] {_targetSceneName} 씬 전환 완료", JCDebug.LogLevel.Success);
            }
            catch (Exception ex)
            {
                JCDebug.Log($"[IntroManager] 씬 전환 실패: {ex.Message}", JCDebug.LogLevel.Error);
                _isTransitioning = false;
                _inputActions.Enable();
            }
        }

        #endregion

        #region Audio

        /// <summary>
        /// 인트로 음악 재생
        /// </summary>
        private async UniTask PlayIntroMusic()
        {
            if (_introMusic == null) return;

            var musicManager = MusicManager.Instance;
            if (musicManager != null && musicManager.IsInitialized)
            {
                await musicManager.PlayMusicAsync(_introMusic, fadeIn: true);
                JCDebug.Log("[IntroManager] 인트로 음악 재생 시작");
            }
        }

        /// <summary>
        /// 확인 사운드 재생
        /// </summary>
        private async UniTask PlayConfirmSound()
        {
            if (_confirmSound == null) return;

            var audioManager = AudioManager.Instance;
            if (audioManager != null && audioManager.IsInitialized)
            {
                audioManager.PlaySFX(_confirmSound);
                await UniTask.Delay(TimeSpan.FromSeconds(_confirmSound.length * 0.5f));
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// 강제로 게임 씬으로 전환 (디버그용)
        /// </summary>
        public void ForceStartGame()
        {
            if (!_isTransitioning)
            {
                _canProceed = true;
                StartGameScene().Forget();
            }
        }

        /// <summary>
        /// 로딩 진행률 강제 설정 (디버그용)
        /// </summary>
        public void SetLoadingProgress(float progress)
        {
            UpdateLoadingUI(Mathf.Clamp01(progress));
        }

        #endregion

        #region Data Classes

        /// <summary>
        /// 로딩 메시지 정보
        /// </summary>
        [System.Serializable]
        public class LoadingMessage
        {
            [Range(0f, 1f)]
            public float triggerProgress;
            public string message;

            public LoadingMessage(float progress, string msg)
            {
                triggerProgress = progress;
                message = msg;
            }
        }

        #endregion

        #region Context Menu (Editor Only)

        [ContextMenu("Force Start Game")]
        private void ContextMenuForceStart()
        {
            if (Application.isPlaying)
            {
                ForceStartGame();
            }
        }

        [ContextMenu("Reset Intro State")]
        private void ContextMenuResetState()
        {
            if (Application.isPlaying)
            {
                _isLoadingComplete = false;
                _canProceed = false;
                _isTransitioning = false;
                SetupInitialUIState();
            }
        }

        #endregion
    }
}

// PlayerInputActions에 UI 액션 맵이 필요합니다
// Input System에서 "UI" 액션 맵에 "AnyKey" 액션을 추가해야 합니다