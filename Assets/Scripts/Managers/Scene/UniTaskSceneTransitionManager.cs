using System;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;
using Cysharp.Threading.Tasks;
using Metamorph.Initialization;
using CustomDebug;

namespace Metamorph.Managers
{
    /// <summary>
    /// UniTask 기반 씬 전환 매니저 (싱글톤)
    /// </summary>
    public class UniTaskSceneTransitionManager : SingletonManager<UniTaskSceneTransitionManager>, IInitializableAsync
    {
        public string Name => "Scene Transition Manager";
        public InitializationPriority Priority => InitializationPriority.Low;
        public bool IsInitialized { get; private set; }

        [Header("Transition Configuration")]
        [SerializeField] private float _minimumLoadingTime = 0.5f;
        [SerializeField] private bool _allowSceneActivation = true;
        [SerializeField] private bool _unloadUnusedAssets = true;
        [SerializeField] private bool _runGarbageCollection = true;

        private string _currentSceneName;
        private bool _isTransitioning = false;
        private CancellationTokenSource _transitionCts;

        // 이벤트
        public event Action<string> OnSceneLoadStarted;
        public event Action<string, float> OnSceneLoadProgress;
        public event Action<string> OnSceneLoadCompleted;
        public event Action<string> OnSceneUnloadStarted;
        public event Action<string> OnSceneUnloadCompleted;
        public event Action<Exception> OnSceneTransitionFailed;

        public bool IsTransitioning => _isTransitioning;
        public string CurrentSceneName => _currentSceneName;

        public async UniTask InitializeAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                JCDebug.Log("[UniTaskSceneTransitionManager] 씬 전환 매니저 초기화 시작");

                // 현재 씬 정보 가져오기
                _currentSceneName = SceneManager.GetActiveScene().name;

                // 씬 매니저 이벤트 구독
                SceneManager.sceneLoaded += OnSceneLoaded;
                SceneManager.sceneUnloaded += OnSceneUnloaded;

                await UniTask.Delay(TimeSpan.FromSeconds(0.1f).Milliseconds, false, PlayerLoopTiming.Update, cancellationToken);

                IsInitialized = true;
                JCDebug.Log($"[UniTaskSceneTransitionManager] 씬 전환 매니저 초기화 완료 (현재 씬: {_currentSceneName})");
            }
            catch (OperationCanceledException)
            {
                JCDebug.Log("[UniTaskSceneTransitionManager] 씬 전환 매니저 초기화가 취소됨", JCDebug.LogLevel.Warning);
                throw;
            }
            catch (Exception ex)
            {
                JCDebug.Log($"[UniTaskSceneTransitionManager] 씬 전환 매니저 초기화 실패: {ex.Message}",JCDebug.LogLevel.Error);
                throw;
            }
        }

        public async UniTask LoadSceneAsync(string sceneName, CancellationToken cancellationToken = default)
        {
            await LoadSceneAsync(sceneName, LoadSceneMode.Single, cancellationToken);
        }

        public async UniTask LoadSceneAsync(string sceneName, LoadSceneMode loadMode, CancellationToken cancellationToken = default)
        {
            if (_isTransitioning)
            {
                JCDebug.Log("[UniTaskSceneTransitionManager] 이미 씬 전환이 진행 중입니다.",JCDebug.LogLevel.Warning);
                return;
            }

            if (string.IsNullOrEmpty(sceneName))
            {
                JCDebug.Log("[UniTaskSceneTransitionManager] 씬 이름이 비어있습니다.",JCDebug.LogLevel.Error);
                return;
            }

            _isTransitioning = true;
            _transitionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            try
            {
                OnSceneLoadStarted?.Invoke(sceneName);
                JCDebug.Log($"[UniTaskSceneTransitionManager] 씬 로드 시작: {sceneName}");

                var startTime = Time.time;

                // 씬 로드 실행
                await LoadSceneInternalAsync(sceneName, loadMode, _transitionCts.Token);

                // 최소 로딩 시간 보장
                await EnsureMinimumLoadingTime(startTime, _transitionCts.Token);

                // 후처리 작업
                await PostLoadProcessingAsync(_transitionCts.Token);

                _currentSceneName = sceneName;
                OnSceneLoadCompleted?.Invoke(sceneName);
                JCDebug.Log($"[UniTaskSceneTransitionManager] 씬 로드 완료: {sceneName}");
            }
            catch (OperationCanceledException)
            {
                JCDebug.Log($"[UniTaskSceneTransitionManager] 씬 로드가 취소됨: {sceneName}",JCDebug.LogLevel.Warning);
                throw;
            }
            catch (Exception ex)
            {
                JCDebug.Log($"[UniTaskSceneTransitionManager] 씬 로드 실패: {sceneName} - {ex.Message}", JCDebug.LogLevel.Error);
                OnSceneTransitionFailed?.Invoke(ex);
                throw;
            }
            finally
            {
                _isTransitioning = false;
                _transitionCts?.Dispose();
                _transitionCts = null;
            }
        }

        private async UniTask LoadSceneInternalAsync(string sceneName, LoadSceneMode loadMode, CancellationToken cancellationToken)
        {
            AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneName, loadMode);

            if (!_allowSceneActivation)
            {
                asyncLoad.allowSceneActivation = false;
            }

            // 로딩 진행률 추적
            while (!asyncLoad.isDone)
            {
                cancellationToken.ThrowIfCancellationRequested();

                float progress = _allowSceneActivation ? asyncLoad.progress :
                    Mathf.Clamp01(asyncLoad.progress / 0.9f);

                OnSceneLoadProgress?.Invoke(sceneName, progress);

                // 씬 활성화 허용 (90% 완료 시)
                if (!_allowSceneActivation && asyncLoad.progress >= 0.9f)
                {
                    asyncLoad.allowSceneActivation = true;
                }

                await UniTask.Yield();
            }

            OnSceneLoadProgress?.Invoke(sceneName, 1.0f);
        }

        private async UniTask EnsureMinimumLoadingTime(float startTime, CancellationToken cancellationToken)
        {
            if (_minimumLoadingTime <= 0) return;

            float elapsedTime = Time.time - startTime;
            float remainingTime = _minimumLoadingTime - elapsedTime;

            if (remainingTime > 0)
            {
                await UniTask.Delay(TimeSpan.FromSeconds(remainingTime).Milliseconds, false, PlayerLoopTiming.Update, cancellationToken);
            }
        }

        private async UniTask PostLoadProcessingAsync(CancellationToken cancellationToken)
        {
            // 사용하지 않는 에셋 언로드
            if (_unloadUnusedAssets)
            {
                var unloadOperation = Resources.UnloadUnusedAssets();
                await unloadOperation.ToUniTask(cancellationToken: cancellationToken);
            }

            // 가비지 컬렉션 실행
            if (_runGarbageCollection)
            {
                await UniTask.SwitchToThreadPool();
                System.GC.Collect();
                await UniTask.SwitchToMainThread(cancellationToken);
            }
        }

        public async UniTask UnloadSceneAsync(string sceneName, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(sceneName))
            {
                JCDebug.Log("[UniTaskSceneTransitionManager] 언로드할 씬 이름이 비어있습니다.", JCDebug.LogLevel.Error);
                return;
            }

            try
            {
                OnSceneUnloadStarted?.Invoke(sceneName);
                JCDebug.Log($"[UniTaskSceneTransitionManager] 씬 언로드 시작: {sceneName}");

                AsyncOperation asyncUnload = SceneManager.UnloadSceneAsync(sceneName);
                await asyncUnload.ToUniTask(cancellationToken: cancellationToken);

                OnSceneUnloadCompleted?.Invoke(sceneName);
                JCDebug.Log($"[UniTaskSceneTransitionManager] 씬 언로드 완료: {sceneName}");
            }
            catch (OperationCanceledException)
            {
                JCDebug.Log($"[UniTaskSceneTransitionManager] 씬 언로드가 취소됨: {sceneName}",JCDebug.LogLevel.Warning);
                throw;
            }
            catch (Exception ex)
            {
                JCDebug.Log($"[UniTaskSceneTransitionManager] 씬 언로드 실패: {sceneName} - {ex.Message}",JCDebug.LogLevel.Error);
                OnSceneTransitionFailed?.Invoke(ex);
                throw;
            }
        }

        public async UniTask LoadSceneAdditiveAsync(string sceneName, CancellationToken cancellationToken = default)
        {
            await LoadSceneAsync(sceneName, LoadSceneMode.Additive, cancellationToken);
        }

        public async UniTask ReloadCurrentSceneAsync(CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(_currentSceneName))
            {
                JCDebug.Log("[UniTaskSceneTransitionManager] 현재 씬 정보가 없습니다.", JCDebug.LogLevel.Error);
                return;
            }

            await LoadSceneAsync(_currentSceneName, cancellationToken);
        }

        public async UniTask<bool> IsSceneValidAsync(string sceneName, CancellationToken cancellationToken = default)
        {
            try
            {
                // 간단한 씬 유효성 검사 (실제로는 더 복잡한 검증 로직 필요)
                await UniTask.Yield(cancellationToken);

                // 씬이 빌드 설정에 포함되어 있는지 확인
                for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
                {
                    string scenePath = SceneUtility.GetScenePathByBuildIndex(i);
                    string sceneNameFromPath = System.IO.Path.GetFileNameWithoutExtension(scenePath);

                    if (sceneNameFromPath.Equals(sceneName, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                JCDebug.Log($"[UniTaskSceneTransitionManager] 씬 유효성 검사 실패: {sceneName} - {ex.Message}", JCDebug.LogLevel.Error);
                return false;
            }
        }

        public void CancelTransition()
        {
            if (_isTransitioning && _transitionCts != null)
            {
                JCDebug.Log("[UniTaskSceneTransitionManager] 씬 전환 취소 요청");
                _transitionCts.Cancel();
            }
        }

        // Unity 씬 매니저 이벤트 핸들러
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            JCDebug.Log($"[UniTaskSceneTransitionManager] 씬 로드됨: {scene.name} (모드: {mode})");
        }

        private void OnSceneUnloaded(Scene scene)
        {
            JCDebug.Log($"[UniTaskSceneTransitionManager] 씬 언로드됨: {scene.name}");
        }

        // 편의 메서드들
        public bool IsSceneLoaded(string sceneName)
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                if (SceneManager.GetSceneAt(i).name.Equals(sceneName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        public Scene GetSceneByName(string sceneName)
        {
            return SceneManager.GetSceneByName(sceneName);
        }

        public int GetLoadedSceneCount()
        {
            return SceneManager.sceneCount;
        }

        protected override void OnDestroy()
        {
            // 이벤트 구독 해제
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;

            // 진행 중인 전환 취소
            _transitionCts?.Cancel();
            _transitionCts?.Dispose();

            base.OnDestroy();
        }
    }
}