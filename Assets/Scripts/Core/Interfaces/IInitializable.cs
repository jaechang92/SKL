using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Metamorph.Initialization
{
    /// <summary>
    /// UniTask 기반 초기화 가능한 객체의 기본 인터페이스
    /// </summary>
    public interface IInitializableAsync
    {
        string Name { get; }
        InitializationPriority Priority { get; }
        UniTask InitializeAsync(CancellationToken cancellationToken = default);
        bool IsInitialized { get; }

        UniTask CleanupAsync();
    }

    /// <summary>
    /// 초기화 우선순위
    /// </summary>
    public enum InitializationPriority
    {
        Critical = 0,    // 핵심 시스템 (Resource 등)
        Core = 100,      // 코어 시스템 (Input, Scene 등)
        Gameplay = 200,  // 게임플레이 (Player, Form, Skill 등)
        Normal = 300, // 일반 시스템 (Enemy, Item 등)
        UI = 400,        // UI 시스템
        Audio = 500,     // 오디오 시스템
        Analytics = 600, // 분석 및 기타
        Low = 900       // 가장 늦게 초기화
    }

    /// <summary>
    /// 초기화 단계 정보
    /// </summary>
    public class InitializationStep
    {
        public string StepName { get; set; }
        public float Progress { get; set; }
        public bool IsCompleted { get; set; }
        public Exception Error { get; set; }
        public IInitializableAsync Target { get; set; }
        public TimeSpan Duration { get; set; }

        public InitializationStep(string stepName, IInitializableAsync target)
        {
            StepName = stepName;
            Target = target;
            Progress = 0f;
            IsCompleted = false;
            Error = null;
            Duration = TimeSpan.Zero;
        }
    }

    /// <summary>
    /// 초기화 이벤트 정보
    /// </summary>
    public class InitializationEventArgs : EventArgs
    {
        public InitializationStep Step { get; }
        public float TotalProgress { get; }
        public bool IsCompleted { get; }
        public TimeSpan TotalDuration { get; }

        public InitializationEventArgs(InitializationStep step, float totalProgress, bool isCompleted, TimeSpan totalDuration)
        {
            Step = step;
            TotalProgress = totalProgress;
            IsCompleted = isCompleted;
            TotalDuration = totalDuration;
        }
    }

    /// <summary>
    /// 초기화 관찰자 인터페이스
    /// </summary>
    public interface IInitializationObserver
    {
        void OnInitializationStepStarted(InitializationStep step);
        void OnInitializationStepCompleted(InitializationStep step);
        void OnInitializationStepFailed(InitializationStep step, Exception error);
        void OnAllInitializationCompleted(TimeSpan totalDuration);
        void OnInitializationProgressUpdated(float progress);
        void OnInitializationCancelled();
    }

    /// <summary>
    /// 초기화 설정 정보
    /// </summary>
    [System.Serializable]
    public class InitializationSettings
    {
        [Header("Timeout Settings")]
        public float timeoutSeconds = 30f;
        public bool enableTimeout = true;

        [Header("Retry Settings")]
        public int maxRetryAttempts = 3;
        public float retryDelaySeconds = 1f;
        public bool enableRetry = true;

        [Header("Performance Settings")]
        public bool allowConcurrentInitialization = false;
        public int maxConcurrentSteps = 3;

        [Header("Debug Settings")]
        public bool logInitialization = true;
        public bool logPerformanceMetrics = true;
    }
}