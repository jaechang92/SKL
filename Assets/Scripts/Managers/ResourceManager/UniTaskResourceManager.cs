using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
using Metamorph.Initialization;
using CustomDebug;

namespace Metamorph.Managers
{
    /// <summary>
    /// UniTask 기반 리소스 매니저 (싱글톤)
    /// </summary>
    public class UniTaskResourceManager : SingletonManager<UniTaskResourceManager>, IInitializableAsync
    {
        public string Name => "Resource Manager";
        public InitializationPriority Priority => InitializationPriority.Normal;
        public bool IsInitialized { get; private set; }

        [Header("Resource Configuration")]
        [SerializeField]
        private List<string> _essentialResourcePaths = new List<string>
        {
            "UI/GameUI",
            "Players/MainCharacter",
            "Audio/BGM/MainTheme",
            "Effects/CommonEffects",
            "Items/CommonItems"
        };

        [SerializeField] private bool _preloadOnInitialize = true;
        [SerializeField] private bool _allowParallelLoading = true;
        [SerializeField] private int _maxParallelLoads = 5;
        [SerializeField] private bool _cacheLoadedResources = true;

        private Dictionary<string, UnityEngine.Object> _preloadedResources;
        private Dictionary<string, UnityEngine.Object> _cachedResources;
        private SemaphoreSlim _loadingSemaphore;

        // 이벤트
        public event Action<float> OnPreloadProgress;
        public event Action<string, UnityEngine.Object> OnResourceLoaded;
        public event Action<string> OnResourceLoadFailed;
        public event Action OnPreloadCompleted;

        public async UniTask InitializeAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                JCDebug.Log("[UniTaskResourceManager] 리소스 매니저 초기화 시작");

                // 딕셔너리 초기화
                _preloadedResources = new Dictionary<string, UnityEngine.Object>();
                _cachedResources = new Dictionary<string, UnityEngine.Object>();

                // 세마포어 초기화
                _loadingSemaphore = new SemaphoreSlim(_maxParallelLoads, _maxParallelLoads);

                // 필수 리소스 프리로드
                if (_preloadOnInitialize)
                {
                    await PreloadEssentialResourcesAsync(cancellationToken);
                }

                IsInitialized = true;
                JCDebug.Log("[UniTaskResourceManager] 리소스 매니저 초기화 완료");
            }
            catch (OperationCanceledException)
            {
                JCDebug.Log("[UniTaskResourceManager] 리소스 매니저 초기화가 취소됨",JCDebug.LogLevel.Warning);
                throw;
            }
            catch (Exception ex)
            {
                JCDebug.Log($"[UniTaskResourceManager] 리소스 매니저 초기화 실패: {ex.Message}",JCDebug.LogLevel.Error);
                throw;
            }
        }

        public async UniTask PreloadEssentialResourcesAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                if (_essentialResourcePaths == null || _essentialResourcePaths.Count == 0)
                {
                    JCDebug.Log("[UniTaskResourceManager] 프리로드할 필수 리소스가 없습니다.",JCDebug.LogLevel.Warning);
                    OnPreloadProgress?.Invoke(1.0f);
                    OnPreloadCompleted?.Invoke();
                    return;
                }

                JCDebug.Log($"[UniTaskResourceManager] {_essentialResourcePaths.Count}개의 필수 리소스 프리로드 시작");

                if (_allowParallelLoading)
                {
                    await LoadResourcesParallelAsync(_essentialResourcePaths, cancellationToken);
                }
                else
                {
                    await LoadResourcesSequentialAsync(_essentialResourcePaths, cancellationToken);
                }

                OnPreloadProgress?.Invoke(1.0f);
                OnPreloadCompleted?.Invoke();
                JCDebug.Log("[UniTaskResourceManager] 필수 리소스 프리로드 완료");
            }
            catch (OperationCanceledException)
            {
                JCDebug.Log("[UniTaskResourceManager] 리소스 프리로드가 취소됨", JCDebug.LogLevel.Warning);
                throw;
            }
            catch (Exception ex)
            {
                JCDebug.Log($"[UniTaskResourceManager] 리소스 프리로드 실패: {ex.Message}",JCDebug.LogLevel.Error);
                throw;
            }
        }

        private async UniTask LoadResourcesParallelAsync(List<string> resourcePaths, CancellationToken cancellationToken)
        {
            var tasks = new List<UniTask>();
            var completedCount = 0;

            foreach (string resourcePath in resourcePaths)
            {
                if (!string.IsNullOrEmpty(resourcePath))
                {
                    tasks.Add(LoadSingleResourceWithSemaphoreAsync(
                        resourcePath,
                        () =>
                        {
                            completedCount++;
                            float progress = (float)completedCount / resourcePaths.Count;
                            OnPreloadProgress?.Invoke(progress);
                        },
                        cancellationToken
                    ));
                }
            }

            await UniTask.WhenAll(tasks);
        }

        private async UniTask LoadResourcesSequentialAsync(List<string> resourcePaths, CancellationToken cancellationToken)
        {
            for (int i = 0; i < resourcePaths.Count; i++)
            {
                if (!string.IsNullOrEmpty(resourcePaths[i]))
                {
                    await LoadSingleResourceAsync(resourcePaths[i], cancellationToken);

                    float progress = (float)(i + 1) / resourcePaths.Count;
                    OnPreloadProgress?.Invoke(progress);
                }
            }
        }

        private async UniTask LoadSingleResourceWithSemaphoreAsync(string resourcePath, Action onComplete, CancellationToken cancellationToken)
        {
            await _loadingSemaphore.WaitAsync(cancellationToken);
            try
            {
                await LoadSingleResourceAsync(resourcePath, cancellationToken);
                onComplete?.Invoke();
            }
            finally
            {
                _loadingSemaphore.Release();
            }
        }

        private async UniTask LoadSingleResourceAsync(string resourcePath, CancellationToken cancellationToken)
        {
            try
            {
                ResourceRequest request = Resources.LoadAsync(resourcePath);
                await request.ToUniTask(cancellationToken: cancellationToken);

                if (request.asset != null)
                {
                    _preloadedResources[resourcePath] = request.asset;
                    OnResourceLoaded?.Invoke(resourcePath, request.asset);
                    JCDebug.Log($"[UniTaskResourceManager] 리소스 로드 완료: {resourcePath}");
                }
                else
                {
                    OnResourceLoadFailed?.Invoke(resourcePath);
                    JCDebug.Log($"[UniTaskResourceManager] 리소스 로드 실패: {resourcePath}",JCDebug.LogLevel.Warning);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                OnResourceLoadFailed?.Invoke(resourcePath);
                JCDebug.Log($"[UniTaskResourceManager] 리소스 로드 중 오류 발생: {resourcePath} - {ex.Message}",JCDebug.LogLevel.Error);
            }
        }

        public T GetPreloadedResource<T>(string resourcePath) where T : UnityEngine.Object
        {
            if (_preloadedResources != null && _preloadedResources.TryGetValue(resourcePath, out UnityEngine.Object resource))
            {
                return resource as T;
            }
            return null;
        }

        public async UniTask<T> LoadResourceAsync<T>(string resourcePath, CancellationToken cancellationToken = default) where T : UnityEngine.Object
        {
            try
            {
                // 캐시에서 먼저 확인
                if (_cacheLoadedResources && _cachedResources.TryGetValue(resourcePath, out UnityEngine.Object cachedResource))
                {
                    return cachedResource as T;
                }

                // 프리로드된 리소스에서 확인
                if (_preloadedResources.TryGetValue(resourcePath, out UnityEngine.Object preloadedResource))
                {
                    return preloadedResource as T;
                }

                // 새로 로드
                ResourceRequest request = Resources.LoadAsync<T>(resourcePath);
                await request.ToUniTask(cancellationToken: cancellationToken);

                if (request.asset != null)
                {
                    T loadedResource = request.asset as T;

                    // 캐시에 저장
                    if (_cacheLoadedResources)
                    {
                        _cachedResources[resourcePath] = loadedResource;
                    }

                    OnResourceLoaded?.Invoke(resourcePath, loadedResource);
                    return loadedResource;
                }
                else
                {
                    OnResourceLoadFailed?.Invoke(resourcePath);
                    JCDebug.Log($"[UniTaskResourceManager] 리소스 로드 실패: {resourcePath}", JCDebug.LogLevel.Warning);
                    return null;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                OnResourceLoadFailed?.Invoke(resourcePath);
                JCDebug.Log($"[UniTaskResourceManager] 리소스 로드 오류: {resourcePath} - {ex.Message}", JCDebug.LogLevel.Error);
                return null;
            }
        }

        public async UniTask<List<T>> LoadMultipleResourcesAsync<T>(List<string> resourcePaths, CancellationToken cancellationToken = default) where T : UnityEngine.Object
        {
            var results = new List<T>();

            if (_allowParallelLoading)
            {
                var tasks = new List<UniTask<T>>();
                foreach (string path in resourcePaths)
                {
                    if (!string.IsNullOrEmpty(path))
                    {
                        tasks.Add(LoadResourceAsync<T>(path, cancellationToken));
                    }
                }

                var loadedResources = await UniTask.WhenAll(tasks);
                results.AddRange(loadedResources);
            }
            else
            {
                foreach (string path in resourcePaths)
                {
                    if (!string.IsNullOrEmpty(path))
                    {
                        var resource = await LoadResourceAsync<T>(path, cancellationToken);
                        results.Add(resource);
                    }
                }
            }

            return results;
        }

        public void UnloadResource(string resourcePath)
        {
            if (_preloadedResources.ContainsKey(resourcePath))
            {
                _preloadedResources.Remove(resourcePath);
            }

            if (_cachedResources.ContainsKey(resourcePath))
            {
                _cachedResources.Remove(resourcePath);
            }

            // Resources.UnloadUnusedAssets는 비동기이지만 여기서는 동기적으로 처리
            Resources.UnloadUnusedAssets();
        }

        public async UniTask UnloadUnusedResourcesAsync()
        {
            var operation = Resources.UnloadUnusedAssets();
            await operation.ToUniTask();
            JCDebug.Log("[UniTaskResourceManager] 사용하지 않는 리소스 언로드 완료");
        }

        public void ClearCache()
        {
            _cachedResources?.Clear();
            JCDebug.Log("[UniTaskResourceManager] 리소스 캐시 정리 완료");
        }

        public void AddEssentialResource(string resourcePath)
        {
            if (!string.IsNullOrEmpty(resourcePath) && !_essentialResourcePaths.Contains(resourcePath))
            {
                _essentialResourcePaths.Add(resourcePath);
            }
        }

        public void RemoveEssentialResource(string resourcePath)
        {
            _essentialResourcePaths.Remove(resourcePath);
        }

        // 편의 메서드들
        public bool IsResourcePreloaded(string resourcePath)
        {
            return _preloadedResources.ContainsKey(resourcePath);
        }

        public bool IsResourceCached(string resourcePath)
        {
            return _cachedResources.ContainsKey(resourcePath);
        }

        public int GetPreloadedResourceCount()
        {
            return _preloadedResources.Count;
        }

        public int GetCachedResourceCount()
        {
            return _cachedResources.Count;
        }

        public List<string> GetPreloadedResourcePaths()
        {
            return new List<string>(_preloadedResources.Keys);
        }

        protected override void OnDestroy()
        {
            _loadingSemaphore?.Dispose();
            _preloadedResources?.Clear();
            _cachedResources?.Clear();
            base.OnDestroy();
        }
    }
}