using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
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
    /// 씬 전환과 관련된 모든 기능을 관리하는 매니저
    /// 페이드 효과, 로딩 화면, 음악 전환 등을 처리
    /// </summary>
    public class SceneTransitionManager : SingletonManager<SceneTransitionManager>, IInitializableAsync
    {
        [Header("Transition Settings")]
        [SerializeField] private float _fadeInDuration = 1f;
        [SerializeField] private float _fadeOutDuration = 1f;
        [SerializeField] private AnimationCurve _fadeCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

        [Header("Loading Screen")]
        [SerializeField] private GameObject _loadingScreenPrefab;
        [SerializeField] private bool _showLoadingScreen = true;
        [SerializeField] private float _minimumLoadingTime = 1f;

        [Header("UI References")]
        [SerializeField] private Canvas _transitionCanvas;
        [SerializeField] private Image _fadeImage;
        [SerializeField] private GameObject _loadingPanel;
        [SerializeField] private Slider _loadingProgressBar;
        [SerializeField] private TextMeshProUGUI _loadingText;
        [SerializeField] private TextMeshProUGUI _sceneNameText;

        [Header("Audio Settings")]
        [SerializeField] private bool _handleMusicTransition = true;
        [SerializeField] private float _musicCrossfadeDuration = 2f;

        // 초기화 관련
        public string Name => "Scene Transition Manager";
        public InitializationPriority Priority => InitializationPriority.Core;
        public bool IsInitialized { get; private set; }

        // 상태 관리
        private bool _isTransitioning = false;
        private string _currentSceneName;
        private string _targetSceneName;
        private CancellationTokenSource _transitionCTS;

        // 매니저 참조
        private MusicManager _musicManager;
        private AudioManager _audioManager;

        // 씬별 음악 설정
        private readonly Dictionary<string, AudioClip> _sceneMusicMap = new Dictionary<string, AudioClip>();

        // 이벤트
        public event Action<string> OnSceneTransitionStarted;
        public event Action<string, float> OnSceneLoadProgress;
        public event Action<string> OnSceneTransitionCompleted;
        public event Action<string, string> OnSceneLoadFailed;

        #region Properties

        public bool IsTransitioning => _isTransitioning;
        public string CurrentSceneName => _currentSceneName;
        public string TargetSceneName => _targetSceneName;

        #endregion

        #region Unity Lifecycle

        protected override void Awake()
        {
            base.Awake();

            // 씬 로드 이벤트 구독
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;

            SetupTransitionUI();
            _currentSceneName = SceneManager.GetActiveScene().name;
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();

            // 이벤트 구독 해제
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;

            _transitionCTS?.Cancel();
            _transitionCTS?.Dispose();
        }

        #endregion

        #region IInitializableAsync Implementation

        public async UniTask InitializeAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                JCDebug.Log("[SceneTransitionManager] 초기화 시작");

                // 기본 설정 초기화
                InitializeDefaultSettings();

                // 매니저 참조 설정
                await SetupManagerReferences(cancellationToken);

                // 씬별 음악 매핑 설정
                SetupSceneMusicMapping();

                // 초기 상태 설정
                SetupInitialState();

                IsInitialized = true;
                JCDebug.Log("[SceneTransitionManager] 초기화 완료", JCDebug.LogLevel.Success);
            }
            catch (Exception ex)
            {
                JCDebug.Log($"[SceneTransitionManager] 초기화 실패: {ex.Message}", JCDebug.LogLevel.Error);
                throw;
            }
        }

        public async UniTask CleanupAsync()
        {
            JCDebug.Log("[SceneTransitionManager] 정리 시작");

            _transitionCTS?.Cancel();

            // 진행 중인 전환 대기
            while (_isTransitioning)
            {
                await UniTask.Delay(100);
            }

            IsInitialized = false;
            JCDebug.Log("[SceneTransitionManager] 정리 완료");
        }

        #endregion

        #region Initialization

        private void InitializeDefaultSettings()
        {
            _transitionCTS = new CancellationTokenSource();
            _currentSceneName = SceneManager.GetActiveScene().name;
        }

        private async UniTask SetupManagerReferences(CancellationToken cancellationToken)
        {
            // MusicManager 참조 대기
            int attempts = 0;
            while (_musicManager == null && attempts < 50)
            {
                _musicManager = MusicManager.Instance;
                if (_musicManager == null)
                {
                    await UniTask.Delay(100, cancellationToken: cancellationToken);
                    attempts++;
                }
            }

            // AudioManager 참조
            _audioManager = AudioManager.Instance;

            if (_musicManager == null)
            {
                JCDebug.Log("[SceneTransitionManager] MusicManager를 찾을 수 없음", JCDebug.LogLevel.Warning);
            }

            if (_audioManager == null)
            {
                JCDebug.Log("[SceneTransitionManager] AudioManager를 찾을 수 없음", JCDebug.LogLevel.Warning);
            }
        }

        private void SetupSceneMusicMapping()
        {
            // 씬별 음악 매핑 (실제 게임에서는 ScriptableObject로 관리)
            // 예시로 리소스에서 로드하는 방식

            // _sceneMusicMap["IntroScene"] = Resources.Load<AudioClip>("Audio/Music/IntroMusic");
            // _sceneMusicMap["GameScene"] = Resources.Load<AudioClip>("Audio/Music/GameMusic");
            // _sceneMusicMap["BossScene"] = Resources.Load<AudioClip>("Audio/Music/BossMusic");

            JCDebug.Log("[SceneTransitionManager] 씬 음악 매핑 설정 완료");
        }

        private void SetupTransitionUI()
        {
            if (_transitionCanvas == null)
            {
                CreateTransitionUI();
            }

            // 초기 상태: 투명
            if (_fadeImage != null)
            {
                _fadeImage.color = new Color(0, 0, 0, 0);
            }

            // 로딩 패널 비활성화
            if (_loadingPanel != null)
            {
                _loadingPanel.SetActive(false);
            }
        }

        private void CreateTransitionUI()
        {
            // Transition Canvas 생성
            GameObject canvasObj = new GameObject("TransitionCanvas");
            DontDestroyOnLoad(canvasObj);

            _transitionCanvas = canvasObj.AddComponent<Canvas>();
            _transitionCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _transitionCanvas.sortingOrder = 9999; // 최상위

            var canvasScaler = canvasObj.AddComponent<CanvasScaler>();
            canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasScaler.referenceResolution = new Vector2(1920, 1080);

            canvasObj.AddComponent<GraphicRaycaster>();

            // Fade Image 생성
            CreateFadeImage();

            // Loading Panel 생성
            CreateLoadingPanel();

            JCDebug.Log("[SceneTransitionManager] 전환 UI 자동 생성 완료");
        }

        private void CreateFadeImage()
        {
            GameObject fadeObj = new GameObject("FadeImage");
            fadeObj.transform.SetParent(_transitionCanvas.transform, false);

            _fadeImage = fadeObj.AddComponent<Image>();
            _fadeImage.color = new Color(0, 0, 0, 0);

            var rectTransform = _fadeImage.rectTransform;
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;
        }

        private void CreateLoadingPanel()
        {
            _loadingPanel = new GameObject("LoadingPanel");
            _loadingPanel.transform.SetParent(_transitionCanvas.transform, false);

            var rectTransform = _loadingPanel.AddComponent<RectTransform>();
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;

            // 씬 이름 텍스트
            CreateText("SceneNameText", "Loading...", new Vector2(0, 100), out _sceneNameText);
            _sceneNameText.fontSize = 48;

            // 로딩 텍스트
            CreateText("LoadingText", "Please Wait...", new Vector2(0, 50), out _loadingText);
            _loadingText.fontSize = 24;

            // 진행률 바
            CreateProgressBar();

            _loadingPanel.SetActive(false);
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
            GameObject sliderObj = new GameObject("LoadingProgressBar");
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

            // Fill
            GameObject fill = new GameObject("Fill");
            fill.transform.SetParent(fillArea.transform, false);
            var fillImage = fill.AddComponent<Image>();
            fillImage.color = new Color(0.3f, 0.6f, 1f, 1f);
            _loadingProgressBar.fillRect = fill.GetComponent<RectTransform>();

            _loadingProgressBar.value = 0f;
        }

        private void SetupInitialState()
        {
            _isTransitioning = false;
        }

        #endregion

        #region Scene Transition

        /// <summary>
        /// 씬 전환 (메인 메서드)
        /// </summary>
        public async UniTask TransitionToSceneAsync(string sceneName, bool showLoading = true)
        {
            if (_isTransitioning)
            {
                JCDebug.Log("[SceneTransitionManager] 이미 전환 중", JCDebug.LogLevel.Warning);
                return;
            }

            if (string.IsNullOrEmpty(sceneName))
            {
                JCDebug.Log("[SceneTransitionManager] 씬 이름이 비어있음", JCDebug.LogLevel.Error);
                return;
            }

            try
            {
                _isTransitioning = true;
                _targetSceneName = sceneName;

                JCDebug.Log($"[SceneTransitionManager] 씬 전환 시작: {_currentSceneName} -> {sceneName}");
                OnSceneTransitionStarted?.Invoke(sceneName);

                // 1. 페이드 아웃
                await FadeOut();

                // 2. 로딩 화면 표시 (옵션)
                if (showLoading && _showLoadingScreen)
                {
                    ShowLoadingScreen(sceneName);
                }

                // 3. 음악 전환 처리
                if (_handleMusicTransition)
                {
                    await HandleMusicTransition(sceneName);
                }

                // 4. 씬 로드
                await LoadSceneAsync(sceneName);

                // 5. 로딩 화면 숨김
                if (showLoading && _showLoadingScreen)
                {
                    HideLoadingScreen();
                }

                // 6. 페이드 인
                await FadeIn();

                _currentSceneName = sceneName;
                _isTransitioning = false;

                OnSceneTransitionCompleted?.Invoke(sceneName);
                JCDebug.Log($"[SceneTransitionManager] 씬 전환 완료: {sceneName}", JCDebug.LogLevel.Success);
            }
            catch (Exception ex)
            {
                _isTransitioning = false;
                var errorMessage = $"씬 전환 실패: {ex.Message}";
                OnSceneLoadFailed?.Invoke(sceneName, errorMessage);
                JCDebug.Log($"[SceneTransitionManager] {errorMessage}", JCDebug.LogLevel.Error);
                throw;
            }
        }

        /// <summary>
        /// 즉시 씬 전환 (페이드 효과 없음)
        /// </summary>
        public async UniTask TransitionToSceneImmediateAsync(string sceneName)
        {
            if (_isTransitioning)
            {
                JCDebug.Log("[SceneTransitionManager] 이미 전환 중", JCDebug.LogLevel.Warning);
                return;
            }

            try
            {
                _isTransitioning = true;
                _targetSceneName = sceneName;

                JCDebug.Log($"[SceneTransitionManager] 즉시 씬 전환: {sceneName}");
                OnSceneTransitionStarted?.Invoke(sceneName);

                await LoadSceneAsync(sceneName);

                _currentSceneName = sceneName;
                _isTransitioning = false;

                OnSceneTransitionCompleted?.Invoke(sceneName);
            }
            catch (Exception ex)
            {
                _isTransitioning = false;
                OnSceneLoadFailed?.Invoke(sceneName, ex.Message);
                throw;
            }
        }

        /// <summary>
        /// 씬을 비동기로 로드
        /// </summary>
        private async UniTask LoadSceneAsync(string sceneName)
        {
            float startTime = Time.time;

            var asyncOperation = SceneManager.LoadSceneAsync(sceneName);
            asyncOperation.allowSceneActivation = false;

            // 90%까지 로딩 진행
            while (asyncOperation.progress < 0.9f)
            {
                float progress = asyncOperation.progress;
                UpdateLoadingProgress(progress);
                OnSceneLoadProgress?.Invoke(sceneName, progress);

                await UniTask.Yield();
            }

            // 최소 로딩 시간 보장
            float elapsedTime = Time.time - startTime;
            if (elapsedTime < _minimumLoadingTime)
            {
                float remainingTime = _minimumLoadingTime - elapsedTime;

                // 남은 시간 동안 90%에서 100%로 점진적 증가
                float startProgress = 0.9f;
                float endProgress = 1.0f;
                float progressRange = endProgress - startProgress;

                float progressStartTime = Time.time;
                while (Time.time - progressStartTime < remainingTime)
                {
                    float progressTime = (Time.time - progressStartTime) / remainingTime;
                    float progress = startProgress + (progressRange * progressTime);

                    UpdateLoadingProgress(progress);
                    OnSceneLoadProgress?.Invoke(sceneName, progress);

                    await UniTask.Yield();
                }
            }

            // 씬 활성화
            UpdateLoadingProgress(1.0f);
            OnSceneLoadProgress?.Invoke(sceneName, 1.0f);

            asyncOperation.allowSceneActivation = true;

            // 씬 로드 완료 대기
            while (!asyncOperation.isDone)
            {
                await UniTask.Yield();
            }

            JCDebug.Log($"[SceneTransitionManager] 씬 로드 완료: {sceneName}");
        }

        #endregion

        #region Fade Effects

        /// <summary>
        /// 페이드 아웃 (화면을 검은색으로)
        /// </summary>
        private async UniTask FadeOut()
        {
            if (_fadeImage == null) return;

            float elapsedTime = 0f;
            Color startColor = _fadeImage.color;
            Color targetColor = new Color(0, 0, 0, 1);

            while (elapsedTime < _fadeOutDuration)
            {
                elapsedTime += Time.deltaTime;
                float progress = elapsedTime / _fadeOutDuration;
                float curveValue = _fadeCurve.Evaluate(progress);

                _fadeImage.color = Color.Lerp(startColor, targetColor, curveValue);
                await UniTask.Yield();
            }

            _fadeImage.color = targetColor;
        }

        /// <summary>
        /// 페이드 인 (화면을 투명하게)
        /// </summary>
        private async UniTask FadeIn()
        {
            if (_fadeImage == null) return;

            float elapsedTime = 0f;
            Color startColor = _fadeImage.color;
            Color targetColor = new Color(0, 0, 0, 0);

            while (elapsedTime < _fadeInDuration)
            {
                elapsedTime += Time.deltaTime;
                float progress = elapsedTime / _fadeInDuration;
                float curveValue = _fadeCurve.Evaluate(progress);

                _fadeImage.color = Color.Lerp(startColor, targetColor, curveValue);
                await UniTask.Yield();
            }

            _fadeImage.color = targetColor;
        }

        #endregion

        #region Loading Screen

        /// <summary>
        /// 로딩 화면 표시
        /// </summary>
        private void ShowLoadingScreen(string sceneName)
        {
            if (_loadingPanel == null) return;

            _loadingPanel.SetActive(true);

            if (_sceneNameText != null)
                _sceneNameText.text = $"Loading {sceneName}...";

            if (_loadingText != null)
                _loadingText.text = "Please wait...";

            if (_loadingProgressBar != null)
                _loadingProgressBar.value = 0f;

            JCDebug.Log($"[SceneTransitionManager] 로딩 화면 표시: {sceneName}");
        }

        /// <summary>
        /// 로딩 화면 숨김
        /// </summary>
        private void HideLoadingScreen()
        {
            if (_loadingPanel != null)
            {
                _loadingPanel.SetActive(false);
            }
        }

        /// <summary>
        /// 로딩 진행률 업데이트
        /// </summary>
        private void UpdateLoadingProgress(float progress)
        {
            if (_loadingProgressBar != null)
            {
                _loadingProgressBar.value = progress;
            }
        }

        #endregion

        #region Music Transition

        /// <summary>
        /// 씬별 음악 전환 처리
        /// </summary>
        private async UniTask HandleMusicTransition(string sceneName)
        {
            if (_musicManager == null || !_musicManager.IsInitialized) return;

            if (_sceneMusicMap.TryGetValue(sceneName, out AudioClip sceneMusic))
            {
                if (sceneMusic != null)
                {
                    await _musicManager.CrossfadeMusicAsync(sceneMusic);
                    JCDebug.Log($"[SceneTransitionManager] 음악 전환: {sceneMusic.name}");
                }
            }
            else
            {
                // 매핑된 음악이 없으면 현재 음악 페이드아웃
                await _musicManager.StopMusicAsync(fadeOut: true);
                JCDebug.Log($"[SceneTransitionManager] 씬 '{sceneName}'에 매핑된 음악 없음 - 음악 정지");
            }
        }

        #endregion

        #region Scene Events

        /// <summary>
        /// 씬 로드 완료 이벤트
        /// </summary>
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            JCDebug.Log($"[SceneTransitionManager] 씬 로드됨: {scene.name}");

            // 필요시 씬별 초기화 로직 추가
            switch (scene.name.ToLower())
            {
                case "gamescene":
                    OnGameSceneLoaded();
                    break;
                case "introscene":
                    OnIntroSceneLoaded();
                    break;
                default:
                    OnGenericSceneLoaded(scene.name);
                    break;
            }
        }

        /// <summary>
        /// 씬 언로드 이벤트
        /// </summary>
        private void OnSceneUnloaded(Scene scene)
        {
            JCDebug.Log($"[SceneTransitionManager] 씬 언로드됨: {scene.name}");
        }

        private void OnGameSceneLoaded()
        {
            // 게임 씬 특화 초기화
            JCDebug.Log("[SceneTransitionManager] 게임 씬 초기화");
        }

        private void OnIntroSceneLoaded()
        {
            // 인트로 씬 특화 초기화
            JCDebug.Log("[SceneTransitionManager] 인트로 씬 초기화");
        }

        private void OnGenericSceneLoaded(string sceneName)
        {
            // 일반 씬 초기화
            JCDebug.Log($"[SceneTransitionManager] 씬 초기화: {sceneName}");
        }

        #endregion

        #region Public Utility Methods

        /// <summary>
        /// 씬별 음악 매핑 추가
        /// </summary>
        public void RegisterSceneMusic(string sceneName, AudioClip musicClip)
        {
            if (string.IsNullOrEmpty(sceneName) || musicClip == null) return;

            _sceneMusicMap[sceneName] = musicClip;
            JCDebug.Log($"[SceneTransitionManager] 씬 음악 등록: {sceneName} -> {musicClip.name}");
        }

        /// <summary>
        /// 현재 씬의 음악 변경
        /// </summary>
        public async UniTask ChangeCurrentSceneMusicAsync(AudioClip newMusic)
        {
            if (_musicManager != null && _musicManager.IsInitialized && newMusic != null)
            {
                await _musicManager.CrossfadeMusicAsync(newMusic);
                JCDebug.Log($"[SceneTransitionManager] 현재 씬 음악 변경: {newMusic.name}");
            }
        }

        /// <summary>
        /// 페이드 설정 변경
        /// </summary>
        public void SetFadeSettings(float fadeInDuration, float fadeOutDuration, AnimationCurve fadeCurve = null)
        {
            _fadeInDuration = Mathf.Max(0.1f, fadeInDuration);
            _fadeOutDuration = Mathf.Max(0.1f, fadeOutDuration);

            if (fadeCurve != null)
                _fadeCurve = fadeCurve;

            JCDebug.Log($"[SceneTransitionManager] 페이드 설정 변경: In={_fadeInDuration}s, Out={_fadeOutDuration}s");
        }

        #endregion

        #region Context Menu (Editor Only)

        [ContextMenu("Test Fade Out")]
        private void ContextMenuTestFadeOut()
        {
            if (Application.isPlaying && !_isTransitioning)
            {
                FadeOut().Forget();
            }
        }

        [ContextMenu("Test Fade In")]
        private void ContextMenuTestFadeIn()
        {
            if (Application.isPlaying && !_isTransitioning)
            {
                FadeIn().Forget();
            }
        }

        [ContextMenu("Print Scene Info")]
        private void ContextMenuPrintSceneInfo()
        {
            JCDebug.Log($"[SceneTransitionManager] 씬 정보:\n" +
                       $"  현재 씬: {_currentSceneName}\n" +
                       $"  타겟 씬: {_targetSceneName}\n" +
                       $"  전환 중: {_isTransitioning}\n" +
                       $"  등록된 음악: {_sceneMusicMap.Count}개");
        }

        #endregion
    }
}