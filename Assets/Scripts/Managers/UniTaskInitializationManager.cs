using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
using CustomDebug;
using Metamorph.Core.Interfaces;

namespace Metamorph.Initialization
{
    /// <summary>
    /// UniTask 기반 전체 초기화 프로세스를 관리하는 싱글톤 매니저
    /// SOLID 원칙의 단일 책임 원칙을 따라 초기화만 담당
    /// </summary>
    public class UniTaskInitializationManager : SingletonManager<UniTaskInitializationManager>
    {
        [Header("Initialization Settings")]
        [SerializeField] private InitializationSettings _settings = new InitializationSettings();

        // 초기화 대상들과 관찰자들
        private List<IInitializableAsync> _initializables = new List<IInitializableAsync>();
        private List<IInitializationObserver> _observers = new List<IInitializationObserver>();
        private List<InitializationStep> _steps = new List<InitializationStep>();

        // 상태 관리
        private bool _isInitializing = false;
        private bool _isInitialized = false;
        private float _totalProgress = 0f;
        private CancellationTokenSource _cancellationTokenSource;
        private Stopwatch _totalStopwatch = new Stopwatch();

        // 이벤트
        public event Action<InitializationEventArgs> OnProgressUpdated;
        public event Action<TimeSpan> OnInitializationCompleted;
        public event Action<Exception> OnInitializationFailed;
        public event Action OnInitializationCancelled;

        public bool IsInitialized => _isInitialized;
        public bool IsInitializing => _isInitializing;
        public float TotalProgress => _totalProgress;
        public InitializationSettings Settings => _settings;

        /// <summary>
        /// 초기화 대상 등록
        /// </summary>
        public void RegisterInitializable(IInitializableAsync initializable)
        {
            if (initializable == null)
            {
                _logError("초기화 대상이 null입니다.");
                return;
            }

            if (_initializables.Contains(initializable))
            {
                _logWarning($"{initializable.Name}이 이미 등록되어 있습니다.");
                return;
            }

            _initializables.Add(initializable);
            _logMessage($"{initializable.Name} 초기화 대상으로 등록됨 (우선순위: {initializable.Priority})");
        }

        /// <summary>
        /// 관찰자 등록
        /// </summary>
        public void RegisterObserver(IInitializationObserver observer)
        {
            if (observer != null && !_observers.Contains(observer))
            {
                _observers.Add(observer);
            }
        }

        /// <summary>
        /// 관찰자 해제
        /// </summary>
        public void UnregisterObserver(IInitializationObserver observer)
        {
            _observers.Remove(observer);
        }

        /// <summary>
        /// 모든 초기화 실행 (UniTask 기반)
        /// </summary>
        public async UniTask InitializeAllAsync(CancellationToken cancellationToken = default)
        {
            if (_isInitializing || _isInitialized)
            {
                _logWarning("이미 초기화가 진행중이거나 완료되었습니다.");
                return;
            }

            _isInitializing = true;
            _totalProgress = 0f;
            _totalStopwatch.Restart();

            // 취소 토큰 설정
            _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            try
            {
                await ExecuteInitializationProcessAsync(_cancellationTokenSource.Token);

                _isInitialized = true;
                var totalDuration = _totalStopwatch.Elapsed;
                _totalStopwatch.Stop();

                NotifyInitializationCompleted(totalDuration);
                OnInitializationCompleted?.Invoke(totalDuration);

                _logMessage($"모든 초기화 완료 (총 소요시간: {totalDuration.TotalSeconds:F2}초)");
            }
            catch (OperationCanceledException)
            {
                _logWarning("초기화가 취소되었습니다.");
                NotifyInitializationCancelled();
                OnInitializationCancelled?.Invoke();
            }
            catch (Exception ex)
            {
                _logError($"초기화 프로세스 실패: {ex.Message}");
                NotifyInitializationFailed(ex);
                OnInitializationFailed?.Invoke(ex);
                throw; // 예외를 다시 던져서 호출자가 처리할 수 있게 함
            }
            finally
            {
                _isInitializing = false;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }

        /// <summary>
        /// 초기화 중단
        /// </summary>
        public void CancelInitialization()
        {
            if (_isInitializing && _cancellationTokenSource != null)
            {
                _logMessage("초기화 중단 요청됨");
                _cancellationTokenSource.Cancel();
            }
        }

        /// <summary>
        /// 초기화 프로세스 실행
        /// </summary>
        private async UniTask ExecuteInitializationProcessAsync(CancellationToken cancellationToken)
        {
            // 1. 우선순위별로 정렬
            var sortedInitializables = _initializables
                .OrderBy(x => (int)x.Priority)
                .ThenBy(x => x.Name)
                .ToList();

            // 2. 초기화 단계 생성
            _steps.Clear();
            foreach (var initializable in sortedInitializables)
            {
                _steps.Add(new InitializationStep(initializable.Name, initializable));
            }

            _logMessage($"총 {_steps.Count}개의 초기화 단계 시작");

            // 3. 단계별 초기화 실행 (순차 또는 동시)
            if (_settings.allowConcurrentInitialization)
            {
                await ExecuteConcurrentInitializationAsync(cancellationToken);
            }
            else
            {
                await ExecuteSequentialInitializationAsync(cancellationToken);
            }

            _totalProgress = 1f;
            UpdateProgress();
        }

        /// <summary>
        /// 순차적 초기화 실행
        /// </summary>
        private async UniTask ExecuteSequentialInitializationAsync(CancellationToken cancellationToken)
        {
            for (int i = 0; i < _steps.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var step = _steps[i];
                await ExecuteInitializationStepAsync(step, i, cancellationToken);

                if (step.Error != null)
                {
                    throw new Exception($"초기화 단계 '{step.StepName}' 실패", step.Error);
                }
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

                // 동일 우선순위 내에서 동시 실행
                foreach (var step in groupSteps)
                {
                    int stepIndex = completedSteps + groupSteps.IndexOf(step);
                    tasks.Add(ExecuteInitializationStepAsync(step, stepIndex, cancellationToken));
                }

                // 현재 우선순위 그룹의 모든 단계 완료 대기
                await UniTask.WhenAll(tasks);

                // 실패한 단계 확인
                var failedStep = groupSteps.FirstOrDefault(step => step.Error != null);
                if (failedStep != null)
                {
                    throw new Exception($"초기화 단계 '{failedStep.StepName}' 실패", failedStep.Error);
                }

                completedSteps += groupSteps.Count;
            }
        }

        /// <summary>
        /// 개별 초기화 단계 실행 (UniTask 기반)
        /// </summary>
        private async UniTask ExecuteInitializationStepAsync(InitializationStep step, int stepIndex, CancellationToken cancellationToken)
        {
            _logMessage($"초기화 단계 시작: {step.StepName}");
            NotifyStepStarted(step);

            var stepStopwatch = Stopwatch.StartNew();
            int retryCount = 0;
            bool success = false;

            while (!success && retryCount <= _settings.maxRetryAttempts && _settings.enableRetry)
            {
                try
                {
                    // 타임아웃 처리
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

                    success = true;
                    step.IsCompleted = true;
                    step.Progress = 1f;
                    step.Duration = stepStopwatch.Elapsed;

                    _logMessage($"초기화 단계 완료: {step.StepName} (소요시간: {step.Duration.TotalSeconds:F2}초)");
                    NotifyStepCompleted(step);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // 전체 취소 요청 시 재시도하지 않고 즉시 종료
                    throw;
                }
                catch (Exception ex)
                {
                    retryCount++;
                    step.Error = ex;

                    _logError($"초기화 단계 '{step.StepName}' 실패 (시도: {retryCount}/{_settings.maxRetryAttempts + 1}): {ex.Message}");

                    if (retryCount <= _settings.maxRetryAttempts && _settings.enableRetry)
                    {
                        _logMessage($"초기화 단계 '{step.StepName}' 재시도 중...");
                        await UniTask.Delay(TimeSpan.FromSeconds(_settings.retryDelaySeconds).Milliseconds, false, PlayerLoopTiming.Update, cancellationToken);
                        step.Error = null; // 재시도를 위해 오류 초기화
                    }
                    else
                    {
                        NotifyStepFailed(step, ex);
                        throw;
                    }
                }

                // 진행률 업데이트
                _totalProgress = (stepIndex + step.Progress) / _steps.Count;
                UpdateProgress();
            }

            stepStopwatch.Stop();
        }

        /// <summary>
        /// 진행률 업데이트 알림
        /// </summary>
        private void UpdateProgress()
        {
            var eventArgs = new InitializationEventArgs(null, _totalProgress, _isInitialized, _totalStopwatch.Elapsed);
            OnProgressUpdated?.Invoke(eventArgs);

            foreach (var observer in _observers)
            {
                observer.OnInitializationProgressUpdated(_totalProgress);
            }
        }

        /// <summary>
        /// 단계 시작 알림
        /// </summary>
        private void NotifyStepStarted(InitializationStep step)
        {
            foreach (var observer in _observers)
            {
                observer.OnInitializationStepStarted(step);
            }
        }

        /// <summary>
        /// 단계 완료 알림
        /// </summary>
        private void NotifyStepCompleted(InitializationStep step)
        {
            foreach (var observer in _observers)
            {
                observer.OnInitializationStepCompleted(step);
            }
        }

        /// <summary>
        /// 단계 실패 알림
        /// </summary>
        private void NotifyStepFailed(InitializationStep step, Exception error)
        {
            foreach (var observer in _observers)
            {
                observer.OnInitializationStepFailed(step, error);
            }
        }

        /// <summary>
        /// 전체 초기화 완료 알림
        /// </summary>
        private void NotifyInitializationCompleted(TimeSpan totalDuration)
        {
            foreach (var observer in _observers)
            {
                observer.OnAllInitializationCompleted(totalDuration);
            }
        }

        /// <summary>
        /// 전체 초기화 실패 알림
        /// </summary>
        private void NotifyInitializationFailed(Exception error)
        {
            // 개별 단계 실패는 NotifyStepFailed에서 처리
        }

        /// <summary>
        /// 초기화 취소 알림
        /// </summary>
        private void NotifyInitializationCancelled()
        {
            foreach (var observer in _observers)
            {
                observer.OnInitializationCancelled();
            }
        }

        // 로깅 유틸리티 메서드들
        private void _logMessage(string message)
        {
            if (_settings.logInitialization)
            {
                JCDebug.Log($"[UniTaskInitializationManager] {message}");
            }
        }

        private void _logWarning(string message)
        {
            if (_settings.logInitialization)
            {
                JCDebug.Log($"[UniTaskInitializationManager] {message}", JCDebug.LogLevel.Warning);
            }
        }

        private void _logError(string message)
        {
            if (_settings.logInitialization)
            {
                JCDebug.Log($"[UniTaskInitializationManager] {message}", JCDebug.LogLevel.Error);
            }
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _observers.Clear();
            _initializables.Clear();
            _steps.Clear();
        }

    }
}