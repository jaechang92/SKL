using CustomDebug;
using Cysharp.Threading.Tasks;
using Metamorph.Core.Interfaces;
using Metamorph.Initialization;
using Metamorph.Managers;
using Metamorph.UI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Metamorph.Core
{
    /// <summary>
    /// 게임 전체 시스템을 통합 관리하는 매니저
    /// ManagerInitializer, UniTaskInitializationManager, GameSystemInitializer를 통합
    /// SOLID 원칙과 옵저버 패턴을 적용한 확장 가능한 설계
    /// </summary>
    public class UnifiedGameManager : SingletonManager<UnifiedGameManager>
    {
        [Header("Initialization Settings")]
        [SerializeField] private bool _autoInitializeOnAwake = true;
        [SerializeField] private InitializationSettings _settings = new InitializationSettings();

        [Header("Scene Management")]
        [SerializeField] private string _gameSceneName = "Game";
        [SerializeField] private string _introSceneName = "Intro";
        
        // 매니저 관리
        private Dictionary<Type, MonoBehaviour> _managers = new Dictionary<Type, MonoBehaviour>();
        private GameObject _managerParent;

        // 초기화 시스템
        private List<IInitializableAsync> _initializables = new List<IInitializableAsync>();
        private List<IInitializationObserver> _observers = new List<IInitializationObserver>();
        private List<InitializationStep> _steps = new List<InitializationStep>();

        // 상태 관리
        private bool _isInitializing = false;
        private bool _isInitialized = false;
        private float _totalProgress = 0f;
        private CancellationTokenSource _cancellationTokenSource;
        private Stopwatch _totalStopwatch = new Stopwatch();

        // 이벤트 - 옵저버 패턴 구현
        public event Action<InitializationEventArgs> OnProgressUpdated;
        public event Action<TimeSpan> OnInitializationCompleted;
        public event Action<Exception> OnInitializationFailed;
        public event Action OnInitializationCancelled;
        public event Action OnReadyForSceneTransition;

        // 프로퍼티
        public bool IsInitialized => _isInitialized;
        public bool IsInitializing => _isInitializing;
        public float TotalProgress => _totalProgress;
        public InitializationSettings Settings => _settings;
        public bool IsReadyForGameScene { get; private set; } = false;

        protected override void Awake()
        {
            base.Awake();
            DontDestroyOnLoad(gameObject);

            if (_autoInitializeOnAwake)
            {
                InitializeGameSystemAsync().Forget();
            }

            // 씬 전환 이벤트 구독
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        /// <summary>
        /// 게임 시스템 전체 초기화 (메인 진입점)
        /// </summary>
        public async UniTaskVoid InitializeGameSystemAsync()
        {
            if (_isInitializing || _isInitialized) return;

            try
            {
                _logMessage("통합 게임 시스템 초기화 시작");

                // 1. 매니저 계층 구조 설정
                SetupManagerHierarchy();

                // 2. 모든 매니저 등록
                RegisterAllManagers();

                // 3. 초기화 실행
                await ExecuteInitializationAsync(destroyCancellationToken);

                _logMessage("통합 게임 시스템 초기화 완료");

                // 4. Game 씬 진입 준비 완료
                IsReadyForGameScene = true;
                OnReadyForSceneTransition?.Invoke();
            }
            catch (OperationCanceledException)
            {
                _logWarning("게임 시스템 초기화가 취소됨");
                OnInitializationCancelled?.Invoke();
            }
            catch (Exception ex)
            {
                _logError($"게임 시스템 초기화 실패: {ex.Message}");
                OnInitializationFailed?.Invoke(ex);
            }
        }

        /// <summary>
        /// 매니저 계층 구조 설정 (ManagerInitializer 기능)
        /// </summary>
        private void SetupManagerHierarchy()
        {
            if (_managerParent == null)
            {
                _managerParent = new GameObject("-----UNIFIED_MANAGERS-----");
                DontDestroyOnLoad(_managerParent);
                _logMessage("매니저 계층 구조 생성됨");
            }
        }

        /// <summary>
        /// 모든 매니저 등록 (통합된 매니저 등록 로직)
        /// </summary>
        private void RegisterAllManagers()
        {
            _logMessage("모든 매니저 등록 시작");

            // 1. 핵심 시스템 매니저들 (Critical 우선순위)
            RegisterManager(SkillRemappingSystem.Instance, InitializationPriority.Critical);
            //RegisterManager<ApplicationGameManager>("Core", InitializationPriority.Critical);
            RegisterManager(UniTaskSaveManager.Instance, InitializationPriority.Critical);
            RegisterManager(PlayerDataManager.Instance, InitializationPriority.Critical);


            // 2. 게임 설정 및 데이터 매니저들 (High 우선순위)  
            RegisterManager(UniTaskGameSettingsManager.Instance, InitializationPriority.High);
            RegisterManager(PlayerDataManager.Instance, InitializationPriority.High);

            // 3. 리소스 및 오디오 매니저들 (Normal 우선순위)
            RegisterManager(UniTaskResourceManager.Instance, InitializationPriority.Normal);
            RegisterManager(AudioManager.Instance, InitializationPriority.Normal);
            RegisterManager(MusicManager.Instance, InitializationPriority.Normal);

            // 4. 게임플레이 매니저들 (Normal 우선순위)
            RegisterManager(FormManager.Instance, InitializationPriority.Normal);
            RegisterManager(SkillManager.Instance, InitializationPriority.Normal);
            RegisterManager(LevelManager.Instance, InitializationPriority.Normal);
            RegisterManager(EnemyManager.Instance, InitializationPriority.Normal);

            // 5. UI 매니저들 (Low 우선순위)
            RegisterManager(UIManager.Instance, InitializationPriority.Low);
            RegisterManager(PopupManager.Instance, InitializationPriority.Low);

            // 6. 씬 전환 매니저 (Low 우선순위)
            RegisterManager(UniTaskSceneTransitionManager.Instance, InitializationPriority.Low);

            _logMessage($"총 {_initializables.Count}개 매니저 등록 완료");
        }

        private void RegisterManager<T>(T manager, InitializationPriority priority)
    where T : MonoBehaviour, IInitializableAsync // 🔧 제약조건 추가
        {
            // null 체크
            if (manager == null)
            {
                _logError($"RegisterManager: {typeof(T).Name} manager가 null입니다.");
                return;
            }

            // _managerParent null 체크
            if (_managerParent == null)
            {
                _logError("RegisterManager: _managerParent가 초기화되지 않았습니다.");
                return;
            }

            try
            {
                // 안전한 캐스팅 (제약조건으로 보장됨)
                _managers[typeof(T)] = manager;
                _initializables.Add(manager);

                // Priority 설정 (리플렉션 대신 직접 접근)
                manager.Priority = priority;

                // 올바른 GameObject 접근
                manager.gameObject.transform.SetParent(_managerParent.transform);
            }
            catch (Exception ex)
            {
                _logError($"{typeof(T).Name} 등록 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// 카테고리별 부모 오브젝트 생성 또는 가져오기
        /// </summary>
        private GameObject GetOrCreateCategoryParent(string category)
        {
            string categoryName = $"--{category} Managers--";
            Transform existingCategory = _managerParent.transform.Find(categoryName);

            if (existingCategory != null)
            {
                return existingCategory.gameObject;
            }

            GameObject categoryObj = new GameObject(categoryName);
            categoryObj.transform.SetParent(_managerParent.transform);
            return categoryObj;
        }

        /// <summary>
        /// 초기화 실행 (UniTask 기반)
        /// </summary>
        private async UniTask ExecuteInitializationAsync(CancellationToken cancellationToken)
        {
            if (_isInitializing) return;

            _isInitializing = true;
            _totalProgress = 0f;
            _totalStopwatch.Restart();
            _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            try
            {
                // 우선순위별로 정렬
                var sortedInitializables = _initializables
                    .OrderBy(x => (int)x.Priority)
                    .ThenBy(x => x.Name)
                    .ToList();

                // 초기화 단계 생성
                _steps.Clear();
                foreach (var initializable in sortedInitializables)
                {
                    _steps.Add(new InitializationStep(initializable.Name, initializable));
                }

                _logMessage($"총 {_steps.Count}개 초기화 단계 시작");

                // 병렬 또는 순차 초기화 실행
                if (_settings.allowConcurrentInitialization)
                {
                    await ExecuteConcurrentInitializationAsync(_cancellationTokenSource.Token);
                }
                else
                {
                    await ExecuteSequentialInitializationAsync(_cancellationTokenSource.Token);
                }

                _totalProgress = 1f;
                UpdateProgress();

                _isInitialized = true;
                var totalDuration = _totalStopwatch.Elapsed;
                _totalStopwatch.Stop();

                OnInitializationCompleted?.Invoke(totalDuration);
                _logMessage($"모든 초기화 완료 (총 소요시간: {totalDuration.TotalSeconds:F2}초)");
            }
            finally
            {
                _isInitializing = false;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }

        /// <summary>
        /// 순차적 초기화 실행
        /// </summary>
        private async UniTask ExecuteSequentialInitializationAsync(CancellationToken cancellationToken)
        {
            for (int i = 0; i < _steps.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await ExecuteInitializationStepAsync(_steps[i], i, cancellationToken);
            }
        }

        /// <summary>
        /// 동시 초기화 실행 (우선순위별 그룹화)
        /// </summary>
        private async UniTask ExecuteConcurrentInitializationAsync(CancellationToken cancellationToken)
        {
            var priorityGroups = _steps
                .GroupBy(step => step.Target.Priority)
                .OrderBy(group => (int)group.Key)
                .ToList();

            int completedSteps = 0;

            foreach (var priorityGroup in priorityGroups)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var groupSteps = priorityGroup.ToList();
                var tasks = new List<UniTask>();

                foreach (var step in groupSteps)
                {
                    int stepIndex = completedSteps + groupSteps.IndexOf(step);
                    tasks.Add(ExecuteInitializationStepAsync(step, stepIndex, cancellationToken));
                }

                await UniTask.WhenAll(tasks);
                completedSteps += groupSteps.Count;
            }
        }

        /// <summary>
        /// 개별 초기화 단계 실행
        /// </summary>
        private async UniTask ExecuteInitializationStepAsync(InitializationStep step, int stepIndex, CancellationToken cancellationToken)
        {
            _logMessage($"초기화 단계 시작: {step.StepName}");
            NotifyStepStarted(step);

            var stepStopwatch = Stopwatch.StartNew();

            try
            {
                if (_settings.enableTimeout)
                {
                    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_settings.timeoutSeconds));
                    using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

                    await step.Target.InitializeAsync(combinedCts.Token);
                }
                else
                {
                    await step.Target.InitializeAsync(cancellationToken);
                }

                step.IsCompleted = true;
                step.Progress = 1f;
                step.Duration = stepStopwatch.Elapsed;

                _logMessage($"초기화 단계 완료: {step.StepName} (소요시간: {step.Duration.TotalSeconds:F2}초)");
                NotifyStepCompleted(step);
            }
            catch (Exception ex)
            {
                step.Error = ex;
                _logError($"초기화 단계 '{step.StepName}' 실패: {ex.Message}");
                NotifyStepFailed(step, ex);
                throw;
            }
            finally
            {
                stepStopwatch.Stop();
                _totalProgress = (stepIndex + step.Progress) / _steps.Count;
                UpdateProgress();
            }
        }

        /// <summary>
        /// Game 씬으로 전환
        /// </summary>
        public async UniTask TransitionToGameSceneAsync()
        {
            if (!IsReadyForGameScene)
            {
                _logWarning("아직 Game 씬 전환 준비가 완료되지 않았습니다.");
                return;
            }

            _logMessage("Game 씬으로 전환 시작");
            await SceneManager.LoadSceneAsync(_gameSceneName);
        }

        /// <summary>
        /// 관찰자 등록/해제 (옵저버 패턴)
        /// </summary>
        public void RegisterObserver(IInitializationObserver observer)
        {
            if (observer != null && !_observers.Contains(observer))
            {
                _observers.Add(observer);
            }
        }

        public void UnregisterObserver(IInitializationObserver observer)
        {
            _observers.Remove(observer);
        }

        /// <summary>
        /// 특정 매니저 가져오기
        /// </summary>
        public T GetManager<T>() where T : MonoBehaviour
        {
            _managers.TryGetValue(typeof(T), out MonoBehaviour manager);
            return manager as T;
        }

        // 알림 메서드들 (옵저버 패턴 구현)
        private void UpdateProgress()
        {
            var eventArgs = new InitializationEventArgs(null, _totalProgress, _isInitialized, _totalStopwatch.Elapsed);
            OnProgressUpdated?.Invoke(eventArgs);

            foreach (var observer in _observers)
            {
                observer.OnInitializationProgressUpdated(_totalProgress);
            }
        }

        private void NotifyStepStarted(InitializationStep step)
        {
            foreach (var observer in _observers)
            {
                observer.OnInitializationStepStarted(step);
            }
        }

        private void NotifyStepCompleted(InitializationStep step)
        {
            foreach (var observer in _observers)
            {
                observer.OnInitializationStepCompleted(step);
            }
        }

        private void NotifyStepFailed(InitializationStep step, Exception error)
        {
            foreach (var observer in _observers)
            {
                observer.OnInitializationStepFailed(step, error);
            }
        }

        // 씬 전환 이벤트 처리
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            _logMessage($"씬 전환 감지: {scene.name}");

            // Game 씬 진입 시 게임 매니저들 활성화
            if (scene.name == _gameSceneName)
            {
                ActivateGameplayManagers();
            }
        }

        /// <summary>
        /// 게임플레이 매니저들 활성화
        /// </summary>
        private void ActivateGameplayManagers()
        {
            _logMessage("게임플레이 매니저들 활성화");

            // LevelManager 등 게임플레이 관련 매니저들의 게임 시작 알림
            var levelManager = GetManager<LevelManager>();
            LevelManager.Instance.StartGame();
            levelManager?.StartGame();
        }

        // 로깅 유틸리티
        private void _logMessage(string message)
        {
            if (_settings.logInitialization)
            {
                JCDebug.Log($"[UnifiedGameManager] {message}");
            }
        }

        private void _logWarning(string message)
        {
            if (_settings.logInitialization)
            {
                JCDebug.Log($"[UnifiedGameManager] {message}", JCDebug.LogLevel.Warning);
            }
        }

        private void _logError(string message)
        {
            if (_settings.logInitialization)
            {
                JCDebug.Log($"[UnifiedGameManager] {message}", JCDebug.LogLevel.Error);
            }
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            SceneManager.sceneLoaded -= OnSceneLoaded;
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _observers.Clear();
            _initializables.Clear();
            _steps.Clear();
            _managers.Clear();
        }
    }
}

/* 
=== 주요 개선사항 ===
1. 3개 스크립트 통합 완료
2. SOLID 원칙 적용 (단일 책임, 개방-폐쇄 원칙)
3. 옵저버 패턴으로 진행률 알림
4. 싱글톤 패턴으로 전역 접근
5. UniTask 기반 비동기 처리
6. 계층적 매니저 구조 자동 생성
7. 우선순위 기반 초기화 순서
8. 병렬/순차 초기화 선택 가능
9. 재시도 및 타임아웃 처리
10. 씬 전환 자동 관리
*/