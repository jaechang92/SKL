using System;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
using Metamorph.Core.Interfaces;
using System.Linq;
using Metamorph.Initialization;
using CustomDebug;

namespace Metamorph.Initialization
{
    /// <summary>
    /// UniTask 기반 초기화 시스템 부트스트랩 클래스
    /// </summary>
    public class UniTaskInitializationBootstrap : MonoBehaviour
    {
        [Header("Bootstrap Configuration")]
        [SerializeField] private bool _autoStartInitialization = true;
        [SerializeField] private bool _createDebugUI = true;
        [SerializeField] private InitializationSettings _initializationSettings = new InitializationSettings();

        private void Start()
        {
            if (_autoStartInitialization)
            {
                SetupInitializationSystem().Forget();
            }
        }

        /// <summary>
        /// 초기화 시스템 설정 및 시작
        /// </summary>
        public async UniTaskVoid SetupInitializationSystem()
        {
            var initManager = UniTaskInitializationManager.Instance;

            // 설정 적용
            initManager.Settings.timeoutSeconds = _initializationSettings.timeoutSeconds;
            initManager.Settings.maxRetryAttempts = _initializationSettings.maxRetryAttempts;
            initManager.Settings.allowConcurrentInitialization = _initializationSettings.allowConcurrentInitialization;

            // 디버그 UI 생성 (옵션)
            if (_createDebugUI)
            {
                CreateDebugObserver();
            }

            // 수동으로 초기화 시스템 시작하려면:
            try
            {
                await initManager.InitializeAllAsync(destroyCancellationToken);
                JCDebug.Log("[Bootstrap] 초기화 시스템 설정 완료");
            }
            catch (OperationCanceledException)
            {
                JCDebug.Log("[Bootstrap] 초기화 시스템 설정이 취소됨", JCDebug.LogLevel.Warning);
            }
            catch (Exception ex)
            {
                JCDebug.Log($"[Bootstrap] 초기화 시스템 설정 실패: {ex.Message}",JCDebug.LogLevel.Error);
            }
        }

        /// <summary>
        /// 디버그용 관찰자 생성
        /// </summary>
        private void CreateDebugObserver()
        {
            GameObject debugObj = new GameObject("InitializationDebugObserver");
            debugObj.transform.SetParent(transform);
            debugObj.AddComponent<UniTaskInitializationDebugObserver>();
        }
    }

    /// <summary>
    /// UniTask 기반 초기화 진행상황을 디버그로 출력하는 관찰자
    /// </summary>
    public class UniTaskInitializationDebugObserver : MonoBehaviour, IInitializationObserver
    {
        private void Start()
        {
            UniTaskInitializationManager.Instance.RegisterObserver(this);
        }

        public void OnInitializationStepStarted(InitializationStep step)
        {
            JCDebug.Log($"[DEBUG] 초기화 시작: {step.StepName}");
        }

        public void OnInitializationStepCompleted(InitializationStep step)
        {
            JCDebug.Log($"[DEBUG] 초기화 완료: {step.StepName} (소요시간: {step.Duration.TotalSeconds:F2}초)");
        }

        public void OnInitializationStepFailed(InitializationStep step, Exception error)
        {
            JCDebug.Log($"[DEBUG] 초기화 실패: {step.StepName} - {error.Message}",JCDebug.LogLevel.Error);
        }

        public void OnAllInitializationCompleted(TimeSpan totalDuration)
        {
            JCDebug.Log($"[DEBUG] 모든 초기화 완료! 총 소요시간: {totalDuration.TotalSeconds:F2}초");
        }

        public void OnInitializationProgressUpdated(float progress)
        {
            JCDebug.Log($"[DEBUG] 초기화 진행률: {progress * 100:F1}%");
        }

        public void OnInitializationCancelled()
        {
            JCDebug.Log("[DEBUG] 초기화가 취소되었습니다!",JCDebug.LogLevel.Warning);
        }

        private void OnDestroy()
        {
            if (UniTaskInitializationManager.Instance != null)
            {
                UniTaskInitializationManager.Instance.UnregisterObserver(this);
            }
        }
    }
}


namespace Metamorph.UI
{
    /// <summary>
    /// UniTask 기반 사용법 예시 클래스
    /// </summary>
    public class UniTaskInitializationUsageExample : MonoBehaviour
    {
        private void Start()
        {
            // 초기화 시스템 사용 예시
            ExampleUsage().Forget();
        }

        private async UniTaskVoid ExampleUsage()
        {
            var initManager = UniTaskInitializationManager.Instance;

            try
            {
                // 1. 커스텀 관찰자 등록
                var customObserver = gameObject.AddComponent<CustomUniTaskInitializationObserver>();
                initManager.RegisterObserver(customObserver);

                // 2. 추가 초기화 객체 등록
                var customInitializer = gameObject.AddComponent<CustomUniTaskInitializer>();
                initManager.RegisterInitializable(customInitializer);

                // 3. 초기화 실행 (취소 토큰과 함께)
                await initManager.InitializeAllAsync(destroyCancellationToken);

                // 4. 초기화 완료 후 작업
                if (initManager.IsInitialized)
                {
                    JCDebug.Log("초기화 완료! 게임 시작 가능");
                }
            }
            catch (OperationCanceledException)
            {
                JCDebug.Log("초기화가 취소되었습니다.",JCDebug.LogLevel.Warning);
            }
            catch (Exception ex)
            {
                JCDebug.Log($"초기화 중 오류 발생: {ex.Message}", JCDebug.LogLevel.Error);
            }
        }
    }

    /// <summary>
    /// 커스텀 관찰자 예시
    /// </summary>
    public class CustomUniTaskInitializationObserver : MonoBehaviour, IInitializationObserver
    {
        public void OnInitializationStepStarted(InitializationStep step) { }
        public void OnInitializationStepCompleted(InitializationStep step) { }
        public void OnInitializationStepFailed(InitializationStep step, Exception error) { }
        public void OnAllInitializationCompleted(TimeSpan totalDuration)
        {
            JCDebug.Log($"커스텀 관찰자: 모든 초기화 완료! (총 {totalDuration.TotalSeconds:F2}초)");
        }
        public void OnInitializationProgressUpdated(float progress) { }
        public void OnInitializationCancelled()
        {
            JCDebug.Log("커스텀 관찰자: 초기화가 취소됨");
        }
    }

    /// <summary>
    /// 커스텀 초기화 객체 예시
    /// </summary>
    public class CustomUniTaskInitializer : MonoBehaviour, IInitializableAsync
    {
        public string Name => "Custom Initializer";

        public InitializationPriority Priority { get; set; } = InitializationPriority.Low;


        public bool IsInitialized { get; private set; }

        public async UniTask InitializeAsync(CancellationToken cancellationToken = default)
        {
            JCDebug.Log("커스텀 초기화 시작");

            try
            {
                await UniTask.Delay(TimeSpan.FromSeconds(1f).Milliseconds);
                IsInitialized = true;
                JCDebug.Log("커스텀 초기화 완료");
            }
            catch (OperationCanceledException)
            {
                JCDebug.Log("커스텀 초기화가 취소됨", JCDebug.LogLevel.Warning);
                throw;
            }
        }
    }

    /// <summary>
    /// 성능 모니터링을 위한 초기화 관찰자
    /// </summary>
    public class PerformanceMonitoringObserver : MonoBehaviour, IInitializationObserver
    {
        private System.Diagnostics.Stopwatch _totalStopwatch = new System.Diagnostics.Stopwatch();
        private System.Collections.Generic.Dictionary<string, TimeSpan> _stepDurations = new System.Collections.Generic.Dictionary<string, TimeSpan>();

        private void Start()
        {
            UniTaskInitializationManager.Instance.RegisterObserver(this);
            _totalStopwatch.Start();
        }

        public void OnInitializationStepStarted(InitializationStep step)
        {
            JCDebug.Log($"[Performance] {step.StepName} 시작");
        }

        public void OnInitializationStepCompleted(InitializationStep step)
        {
            _stepDurations[step.StepName] = step.Duration;
            JCDebug.Log($"[Performance] {step.StepName} 완료: {step.Duration.TotalMilliseconds:F0}ms");
        }

        public void OnInitializationStepFailed(InitializationStep step, Exception error) { }

        public void OnAllInitializationCompleted(TimeSpan totalDuration)
        {
            _totalStopwatch.Stop();

            JCDebug.Log($"[Performance] 전체 초기화 완료: {totalDuration.TotalSeconds:F2}초");
            JCDebug.Log($"[Performance] 실제 측정 시간: {_totalStopwatch.Elapsed.TotalSeconds:F2}초");

            // 가장 오래 걸린 단계 찾기
            if (_stepDurations.Count > 0)
            {
                var slowestStep = _stepDurations.OrderByDescending(kvp => kvp.Value).First();
                JCDebug.Log($"[Performance] 가장 오래 걸린 단계: {slowestStep.Key} ({slowestStep.Value.TotalSeconds:F2}초)");
            }
        }

        public void OnInitializationProgressUpdated(float progress) { }
        public void OnInitializationCancelled() { }

        private void OnDestroy()
        {
            if (UniTaskInitializationManager.Instance != null)
            {
                UniTaskInitializationManager.Instance.UnregisterObserver(this);
            }
        }
    }
}