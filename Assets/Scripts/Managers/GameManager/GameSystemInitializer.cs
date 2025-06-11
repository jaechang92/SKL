using System;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
using Metamorph.Initialization;
using Metamorph.Managers;
using CustomDebug;

namespace Metamorph.Initialization
{
    /// <summary>
    /// 기존 GameInitializerManager를 대체하는 통합 초기화 설정 클래스
    /// 이 클래스는 모든 UniTask 기반 매니저들을 등록하고 초기화합니다.
    /// </summary>
    public class GameSystemInitializer : MonoBehaviour
    {
        [Header("Initialization Configuration")]
        [SerializeField] private bool _autoInitializeOnStart = true;
        [SerializeField] private bool _showDebugUI = true;
        [SerializeField] private InitializationSettings _initializationSettings = new InitializationSettings();

        private UniTaskInitializationManager _initializationManager;

        private void Start()
        {
            if (_autoInitializeOnStart)
            {
                InitializeGameSystemAsync().Forget();
            }
        }

        /// <summary>
        /// 게임 시스템 초기화
        /// 기존 GameInitializerManager.InitializeManagers()를 대체
        /// </summary>
        public async UniTaskVoid InitializeGameSystemAsync()
        {
            try
            {
                JCDebug.Log("[GameSystemInitializer] 게임 시스템 초기화 시작");

                // 초기화 매니저 가져오기
                _initializationManager = UniTaskInitializationManager.Instance;

                // 설정 적용
                ApplyInitializationSettings();

                // 디버그 관찰자 등록
                if (_showDebugUI)
                {
                    RegisterDebugObserver();
                }

                // 모든 매니저들을 초기화 시스템에 등록
                RegisterAllManagers();

                // 초기화 실행
                await _initializationManager.InitializeAllAsync(destroyCancellationToken);

                JCDebug.Log("[GameSystemInitializer] 게임 시스템 초기화 완료");
            }
            catch (OperationCanceledException)
            {
                JCDebug.Log("[GameSystemInitializer] 게임 시스템 초기화가 취소됨", JCDebug.LogLevel.Warning);
            }
            catch (Exception ex)
            {
                JCDebug.Log($"[GameSystemInitializer] 게임 시스템 초기화 실패: {ex.Message}", JCDebug.LogLevel.Error);
            }
        }

        private void ApplyInitializationSettings()
        {
            var settings = _initializationManager.Settings;
            settings.timeoutSeconds = _initializationSettings.timeoutSeconds;
            settings.maxRetryAttempts = _initializationSettings.maxRetryAttempts;
            settings.retryDelaySeconds = _initializationSettings.retryDelaySeconds;
            settings.allowConcurrentInitialization = _initializationSettings.allowConcurrentInitialization;
            settings.maxConcurrentSteps = _initializationSettings.maxConcurrentSteps;
            settings.logInitialization = _initializationSettings.logInitialization;
            settings.logPerformanceMetrics = _initializationSettings.logPerformanceMetrics;
        }

        private void RegisterDebugObserver()
        {
            GameObject debugObj = new GameObject("GameSystemDebugObserver");
            debugObj.transform.SetParent(transform);
            var debugObserver = debugObj.AddComponent<UniTaskInitializationDebugObserver>();
            _initializationManager.RegisterObserver(debugObserver);
        }

        /// <summary>
        /// 모든 매니저들을 초기화 시스템에 등록
        /// 우선순위는 각 매니저의 Priority 속성으로 자동 결정됩니다.
        /// </summary>
        private void RegisterAllManagers()
        {
            JCDebug.Log("[GameSystemInitializer] 매니저들을 초기화 시스템에 등록 중");

            // 1. 세이브 데이터 매니저 (Critical 우선순위)
            var saveDataManager = CreateOrGetManager<UniTaskSaveDataManager>("SaveDataManager");
            _initializationManager.RegisterInitializable(saveDataManager);

            // 2. 게임 설정 매니저 (High 우선순위)
            var settingsManager = CreateOrGetManager<UniTaskGameSettingsManager>("GameSettingsManager");
            _initializationManager.RegisterInitializable(settingsManager);

            // 3. 리소스 매니저 (Normal 우선순위)
            var resourceManager = CreateOrGetManager<UniTaskResourceManager>("ResourceManager");
            _initializationManager.RegisterInitializable(resourceManager);

            // 4. 오디오 매니저 (Normal 우선순위)
            var audioManager = CreateOrGetManager<UniTaskAudioManager>("AudioManager");
            _initializationManager.RegisterInitializable(audioManager);

            // 5. 씬 전환 매니저 (Low 우선순위)
            var sceneManager = CreateOrGetManager<UniTaskSceneTransitionManager>("SceneTransitionManager");
            _initializationManager.RegisterInitializable(sceneManager);

            JCDebug.Log("[GameSystemInitializer] 모든 매니저 등록 완료");
        }

        private T CreateOrGetManager<T>(string managerName) where T : MonoBehaviour, IInitializableAsync
        {
            // 이미 존재하는 매니저 찾기
            T manager = FindAnyObjectByType<T>();

            if (manager == null)
            {
                // 새로 생성
                GameObject managerObj = new GameObject(managerName);
                DontDestroyOnLoad(managerObj);
                manager = managerObj.AddComponent<T>();
                JCDebug.Log($"[GameSystemInitializer] {managerName} 생성됨");
            }
            else
            {
                JCDebug.Log($"[GameSystemInitializer] {managerName} 이미 존재함");
            }

            return manager;
        }

        /// <summary>
        /// 수동으로 특정 매니저만 초기화하고 싶을 때 사용
        /// </summary>
        public async UniTask InitializeSpecificManagerAsync<T>(CancellationToken cancellationToken = default) where T : MonoBehaviour, IInitializableAsync
        {
            var manager = FindAnyObjectByType<T>();
            if (manager != null)
            {
                await manager.InitializeAsync(cancellationToken);
                JCDebug.Log($"[GameSystemInitializer] {typeof(T).Name} 개별 초기화 완료");
            }
            else
            {
                JCDebug.Log($"[GameSystemInitializer] {typeof(T).Name}를 찾을 수 없습니다.",JCDebug.LogLevel.Warning);
            }
        }

        public bool IsAllManagersInitialized()
        {
            return _initializationManager != null && _initializationManager.IsInitialized;
        }

        public float GetInitializationProgress()
        {
            return _initializationManager?.TotalProgress ?? 0f;
        }
    }

}

/*
=== 마이그레이션 가이드 ===

1. 기존 코드 제거:
   - AudioManager.cs (→ UniTaskAudioManager로 교체)
   - GameSettingsManager.cs (→ UniTaskGameSettingsManager로 교체)
   - SaveDataManager.cs (→ UniTaskSaveDataManager로 교체)
   - ResourceManager.cs (→ UniTaskResourceManager로 교체)
   - SceneTransitionManager.cs (→ UniTaskSceneTransitionManager로 교체)
   - GameInitializerManager.cs (→ GameSystemInitializer로 교체)

2. 네임스페이스 변경:
   - 모든 매니저는 이제 Metamorph.Managers 네임스페이스 사용
   - 초기화 관련 클래스는 Metamorph.Initialization 네임스페이스 사용

3. 인터페이스 변경:
   - IInitializable → IInitializableAsync
   - IEnumerator Initialize() → UniTask InitializeAsync(CancellationToken)

4. 사용법 변경:
   기존:
   ```csharp
   yield return StartCoroutine(GameInitializerManager.Instance.InitializeManagers());
   ```
   
   새로운 방식:
   ```csharp
   var initializer = FindAnyObjectByType<GameSystemInitializer>();
   await initializer.InitializeGameSystemAsync();
   ```

5. 설정 방법:
   - GameSystemInitializer 컴포넌트를 씬에 추가
   - _autoInitializeOnStart를 true로 설정하면 자동 초기화
   - _initializationSettings에서 상세 설정 조정

6. 호환성:
   - 급하게 마이그레이션이 어려운 경우 LegacyInitializationAdapter 사용 가능
   - 하지만 가능한 빨리 새로운 시스템으로 이전 권장

=== 주요 개선사항 ===
1. CS1626 오류 완전 해결
2. 더 나은 성능 (UniTask)
3. 취소 토큰 지원
4. 세밀한 진행률 추적
5. 재시도 및 타임아웃 처리
6. 병렬 초기화 옵션
7. 더 나은 오류 처리
8. 성능 메트릭스 지원
*/