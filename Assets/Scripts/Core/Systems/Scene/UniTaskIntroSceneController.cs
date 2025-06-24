using CustomDebug;
using Cysharp.Threading.Tasks;
using Metamorph.Core;
using Metamorph.Core.Interfaces;
using Metamorph.Initialization;
using Metamorph.Managers;
using System;
using System.Threading;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Metamorph.UI
{
    /// <summary>
    /// UniTask 기반 리팩토링된 인트로 씬 컨트롤러
    /// InitializationManager와 옵저버 패턴을 활용하여 의존성 분리
    /// </summary>
    public class UniTaskIntroSceneController : MonoBehaviour, IInitializationObserver
    {
        [Header("UI References")]

        [SerializeField] private GameObject _loadingPanel;
        [SerializeField] private Slider _loadingProgressBar;
        [SerializeField] private TMP_Text _loadingStatusText;
        [SerializeField] private TMP_Text _currentStepText;
        [SerializeField] private Button _cancelButton;

        [Header("Scene Configuration")]
        [SerializeField] private string _targetSceneName = "GameScene";
        [SerializeField] private float _minimumLoadingTime = 2f;
        [SerializeField] private bool _skipInputWait = false;
        [SerializeField] private bool _showCancelButton = true;

        [Header("Performance Metrics")]
        [SerializeField] private Text _performanceText;
        [SerializeField] private bool _showPerformanceMetrics = false;

        // 상태 관리
        private bool _isInitializationCompleted = false;
        private bool _canProceedToGame = false;
        private DateTime _initializationStartTime;
        private CancellationTokenSource _initializationCts;

        // 초기화 컴포넌트들
        private UnifiedGameManager _initializationManager;
        //private UniTaskGameManagerInitializer _gameManagerInitializer;
        //private UniTaskSaveDataInitializer _saveDataInitializer;
        //private UniTaskGameSettingsInitializer _gameSettingsInitializer;
        //private UniTaskResourcePreloader _resourcePreloader;
        //private UniTaskNetworkInitializer _networkInitializer;

        private void Start()
        {
            DontDestroyOnLoad(gameObject);
            InitializeComponents();
            StartInitializationProcess().Forget();
        }

        private void Update()
        {
            if (_canProceedToGame && IsAnyInputPressed())
            {
                TransitionToGameScene().Forget();
            }
        }

        private void OnDestroy()
        {
            // 초기화 취소 및 정리
            _initializationCts?.Cancel();
            _initializationCts?.Dispose();

            // 관찰자 해제
            if (_initializationManager != null)
            {
                _initializationManager.UnregisterObserver(this);
            }
        }

        /// <summary>
        /// 컴포넌트들을 초기화하고 설정
        /// </summary>
        private void InitializeComponents()
        {
            // InitializationManager 가져오기
            _initializationManager = UnifiedGameManager.Instance;
            _initializationManager.RegisterObserver(this);

            // 초기화 컴포넌트들 생성 및 등록
            //CreateAndRegisterInitializers();

            // UI 초기 상태 설정
            SetupInitialUI();

            // 취소 버튼 설정
            SetupCancelButton();
        }

        ///// <summary>
        ///// 초기화 컴포넌트들을 생성하고 등록
        ///// </summary>
        //private void CreateAndRegisterInitializers()
        //{
        //    // 게임 매니저 초기화기
        //    _gameManagerInitializer = CreateInitializer<UniTaskGameManagerInitializer>("GameManagerInitializer");

        //    // 세이브 데이터 초기화기
        //    _saveDataInitializer = CreateInitializer<UniTaskSaveDataInitializer>("SaveDataInitializer");

        //    // 게임 설정 초기화기
        //    _gameSettingsInitializer = CreateInitializer<UniTaskGameSettingsInitializer>("GameSettingsInitializer");

        //    // 리소스 프리로더
        //    _resourcePreloader = CreateInitializer<UniTaskResourcePreloader>("ResourcePreloader");

        //    // 네트워크 초기화기
        //    _networkInitializer = CreateInitializer<UniTaskNetworkInitializer>("NetworkInitializer");

        //    JCDebug.Log("[UniTaskIntroController] 모든 초기화 컴포넌트 등록 완료");
        //}

        ///// <summary>
        ///// 초기화 컴포넌트 생성 헬퍼 메서드
        ///// </summary>
        //private T CreateInitializer<T>(string objectName) where T : MonoBehaviour, IInitializableAsync
        //{
        //    GameObject initializerObj = new GameObject(objectName);
        //    initializerObj.transform.SetParent(transform);
        //    T initializer = initializerObj.AddComponent<T>();
        //    _initializationManager.RegisterManager(initializer);
        //    return initializer;
        //}

        /// <summary>
        /// UI 초기 상태 설정
        /// </summary>
        private void SetupInitialUI()
        {
            _loadingPanel?.SetActive(false);
            _currentStepText.gameObject?.SetActive(false);

            if (_loadingProgressBar != null)
            {
                _loadingProgressBar.value = 0f;
            }

            UpdateStatusText("초기화 준비 중...");
            UpdateCurrentStepText("");

            // 성능 메트릭스 UI 설정
            if (_performanceText != null)
            {
                _performanceText.gameObject.SetActive(_showPerformanceMetrics);
            }
        }

        /// <summary>
        /// 취소 버튼 설정
        /// </summary>
        private void SetupCancelButton()
        {
            if (_cancelButton != null)
            {
                _cancelButton.gameObject.SetActive(_showCancelButton);
                _cancelButton.onClick.AddListener(CancelInitialization);
            }
        }

        /// <summary>
        /// 초기화 프로세스 시작
        /// </summary>
        private async UniTaskVoid StartInitializationProcess()
        {
            _initializationStartTime = DateTime.Now;
            _initializationCts = new CancellationTokenSource();

            UpdateStatusText("게임 시스템 초기화 중...");

            try
            {
                JCDebug.Log("[UniTaskIntroController] 초기화 프로세스 시작");

                // InitializationManager를 통한 초기화 실행
                //await _initializationManager.InitializeAllAsync(_initializationCts.Token);

                // 최소 로딩 시간 보장
                await EnsureMinimumLoadingTimeAsync(_initializationCts.Token);

                JCDebug.Log("[UniTaskIntroController] 초기화 프로세스 완료");
            }
            catch (OperationCanceledException)
            {
                JCDebug.Log("[UniTaskIntroController] 초기화 프로세스가 취소되었습니다.", JCDebug.LogLevel.Warning);
                UpdateStatusText("초기화가 취소되었습니다.");
                UpdateCurrentStepText("게임을 다시 시작해주세요.");
            }
            catch (Exception ex)
            {
                JCDebug.Log($"[UniTaskIntroController] 초기화 프로세스 실패: {ex.Message}", JCDebug.LogLevel.Error);
                UpdateStatusText("초기화 중 오류가 발생했습니다.");
                UpdateCurrentStepText($"오류: {ex.Message}");

                // 오류 발생 시 재시도 버튼 표시 등의 처리 가능
                ShowRetryOption();
            }
        }

        /// <summary>
        /// 초기화 취소
        /// </summary>
        public void CancelInitialization()
        {
            if (_initializationCts != null && !_initializationCts.Token.IsCancellationRequested)
            {
                JCDebug.Log("[UniTaskIntroController] 사용자가 초기화 취소 요청");
                _initializationCts.Cancel();

                if (_cancelButton != null)
                {
                    _cancelButton.interactable = false;
                }
            }
        }

        /// <summary>
        /// 최소 로딩 시간 보장
        /// </summary>
        private async UniTask EnsureMinimumLoadingTimeAsync(CancellationToken cancellationToken)
        {
            var elapsedTime = DateTime.Now - _initializationStartTime;
            var remainingTime = TimeSpan.FromSeconds(_minimumLoadingTime) - elapsedTime;

            if (remainingTime.TotalMilliseconds > 0)
            {
                UpdateStatusText("초기화 완료, 대기 중...");
                await UniTask.Delay(remainingTime.Milliseconds, false, PlayerLoopTiming.Update, cancellationToken);
            }
        }

        /// <summary>
        /// 입력 감지
        /// </summary>
        private bool IsAnyInputPressed()
        {
            if (_skipInputWait) return true;

            // 키보드 입력 체크
            if (Keyboard.current?.anyKey.wasPressedThisFrame == true)
                return true;

            // 마우스 입력 체크
            if (Mouse.current != null &&
                (Mouse.current.leftButton.wasPressedThisFrame ||
                 Mouse.current.rightButton.wasPressedThisFrame ||
                 Mouse.current.middleButton.wasPressedThisFrame))
                return true;

            // 게임패드 입력 체크
            if (Gamepad.current != null &&
                (Gamepad.current.buttonSouth.wasPressedThisFrame ||
                 Gamepad.current.buttonEast.wasPressedThisFrame ||
                 Gamepad.current.buttonWest.wasPressedThisFrame ||
                 Gamepad.current.buttonNorth.wasPressedThisFrame))
                return true;

            return false;
        }

        /// <summary>
        /// 게임 씬으로 전환
        /// </summary>
        private async UniTaskVoid TransitionToGameScene()
        {
            _canProceedToGame = false;
            _currentStepText.gameObject?.SetActive(false);
            _loadingPanel?.SetActive(true);

            UpdateStatusText("게임 씬으로 이동 중...");
            UpdateCurrentStepText("씬 전환 준비");

            try
            {
                JCDebug.Log($"[UniTaskIntroController] {_targetSceneName} 씬으로 전환 시작");

                // 씬 전환 매니저가 있다면 사용
                var sceneTransitionManager = FindAnyObjectByType<UniTaskSceneTransitionManager>();
                if (sceneTransitionManager != null)
                {
                    await sceneTransitionManager.LoadSceneAsync(_targetSceneName);
                }
                else
                {
                    // 기본 씬 로딩
                    await LoadSceneWithProgressAsync(_targetSceneName);
                }
            }
            catch (Exception ex)
            {
                JCDebug.Log($"[UniTaskIntroController] 씬 전환 실패: {ex.Message}", JCDebug.LogLevel.Error);
                UpdateStatusText("씬 전환 중 오류가 발생했습니다.");
                UpdateCurrentStepText($"오류: {ex.Message}");
            }
        }

        /// <summary>
        /// 진행률과 함께 씬 로딩
        /// </summary>
        private async UniTask LoadSceneWithProgressAsync(string sceneName)
        {
            AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneName);
            asyncLoad.allowSceneActivation = false;

            while (asyncLoad.progress < 0.9f)
            {
                float progress = Mathf.Clamp01(asyncLoad.progress / 0.9f);
                if (_loadingProgressBar != null)
                {
                    _loadingProgressBar.value = progress;
                }
                UpdateCurrentStepText($"씬 로딩... {progress * 100:F0}%");
                await UniTask.Yield();
            }

            // 씬 활성화 허용
            if (_loadingProgressBar != null)
            {
                _loadingProgressBar.value = 1f;
            }
            UpdateCurrentStepText("씬 로딩 완료");

            asyncLoad.allowSceneActivation = true;
            await asyncLoad.ToUniTask();
        }

        /// <summary>
        /// 재시도 옵션 표시
        /// </summary>
        private void ShowRetryOption()
        {
            // 재시도 버튼이나 UI 표시 로직
            // 실제 구현에서는 별도의 재시도 UI를 만들어 표시
        }

        /// <summary>
        /// 상태 텍스트 업데이트
        /// </summary>
        private void UpdateStatusText(string message, Func<UniTask> onComplete = null)
        {
            if (_loadingStatusText != null)
            {
                _loadingStatusText.text = message;
            }

            if(onComplete != null) onComplete().Forget();
        }

        /// <summary>
        /// 현재 단계 텍스트 업데이트
        /// </summary>
        private void UpdateCurrentStepText(string message)
        {
            if (_currentStepText != null)
            {
                _currentStepText.text = message;
            }
        }

        /// <summary>
        /// 성능 메트릭스 업데이트
        /// </summary>
        private void UpdatePerformanceMetrics(TimeSpan duration, string stepName = null)
        {
            if (_performanceText != null && _showPerformanceMetrics)
            {
                var totalElapsed = DateTime.Now - _initializationStartTime;
                string performanceInfo = $"총 시간: {totalElapsed.TotalSeconds:F2}초";

                if (!string.IsNullOrEmpty(stepName))
                {
                    performanceInfo += $"\n{stepName}: {duration.TotalSeconds:F2}초";
                }

                _performanceText.text = performanceInfo;
            }
        }

        #region IInitializationObserver 구현

        public void OnInitializationStepStarted(InitializationStep step)
        {
            UpdateCurrentStepText($"{step.StepName} 초기화 중...");
            JCDebug.Log($"[UniTaskIntroController] 단계 시작: {step.StepName}");
        }

        public void OnInitializationStepCompleted(InitializationStep step)
        {
            UpdateCurrentStepText($"{step.StepName} 완료");
            UpdatePerformanceMetrics(step.Duration, step.StepName);
            JCDebug.Log($"[UniTaskIntroController] 단계 완료: {step.StepName} (소요시간: {step.Duration.TotalSeconds:F2}초)");
        }

        public void OnInitializationStepFailed(InitializationStep step, Exception error)
        {
            UpdateCurrentStepText($"{step.StepName} 실패: {error.Message}");
            UpdateStatusText("초기화 중 오류가 발생했습니다.");
            JCDebug.Log($"[UniTaskIntroController] 단계 실패: {step.StepName} - {error.Message}", JCDebug.LogLevel.Error);
        }

        public void OnAllInitializationCompleted(TimeSpan totalDuration)
        {
            _isInitializationCompleted = true;

            UpdateStatusText("초기화 완료!", async () =>
            {
                await UniTask.Delay(
                    TimeSpan.FromSeconds(0.5f).Milliseconds,
                    cancellationToken: destroyCancellationToken  // 명시적 토큰 전달
                );

                UpdateStatusText("");
            });
            UpdateCurrentStepText("아무 키나 눌러서 게임 시작");
            UpdatePerformanceMetrics(totalDuration);

            _loadingPanel?.SetActive(false);
            _currentStepText.gameObject?.SetActive(true);

            CustomTween ct = _currentStepText.GetComponent<CustomTween>();
            if (ct != null)
            {
                ct.PlayTween();
            }

            if (_cancelButton != null)
            {
                _cancelButton.gameObject.SetActive(false);
            }

            _canProceedToGame = true;

            JCDebug.Log($"[UniTaskIntroController] 모든 초기화 완료 - 총 소요시간: {totalDuration.TotalSeconds:F2}초");
        }

        public void OnInitializationProgressUpdated(float progress)
        {
            if (_loadingProgressBar != null)
            {
                _loadingProgressBar.value = progress;
            }
        }

        public void OnInitializationCancelled()
        {
            UpdateStatusText("초기화가 취소되었습니다.");
            UpdateCurrentStepText("게임을 다시 시작해주세요.");

            if (_cancelButton != null)
            {
                _cancelButton.interactable = false;
            }

            JCDebug.Log("[UniTaskIntroController] 초기화 취소됨");
        }

        #endregion
    }
}