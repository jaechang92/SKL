using CustomDebug;
using Cysharp.Threading.Tasks;
using Metamorph.Initialization;
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using System.Linq;
using UnityEngine.SceneManagement;

namespace Metamorph.Managers
{
    /// <summary>
    /// 게임 리소스를 관리하는 매니저
    /// Resources, Addressables 시스템을 통합 관리하며 캐싱, 프리로딩, 메모리 관리 기능 제공
    /// </summary>
    public class GameResourceManager : SingletonManager<GameResourceManager>, IInitializableAsync
    {
        #region Fields

        [Header("Resource Settings")]
        [SerializeField] private ResourceSettings _resourceSettings;
        [SerializeField] private bool _enableResourceCaching = true;
        [SerializeField] private bool _preloadEssentialResources = true;
        [SerializeField] private bool _logResourceOperations = true;

        [Header("Memory Management")]
        [SerializeField] private float _memoryThresholdMB = 512f;
        [SerializeField] private bool _autoGarbageCollection = true;
        [SerializeField] private float _gcCheckInterval = 30f;

        [Header("Preload Lists")]
        [SerializeField] private List<string> _essentialResourcePaths = new List<string>();
        [SerializeField] private List<string> _addressableKeys = new List<string>();
        
        [Header("Scene-based Loading")]
        [SerializeField] private bool _enableSceneBasedLoading = true;
        [SerializeField] private string _currentSceneName = "";
        
        // 씬별 리소스 추적
        private readonly Dictionary<string, List<string>> _sceneResources = new();
        private readonly Dictionary<string, List<string>> _sceneAddressables = new();

        // 리소스 캐시
        private readonly Dictionary<string, UnityEngine.Object> _resourceCache = new();
        private readonly Dictionary<string, AsyncOperationHandle> _addressableHandles = new();
        private readonly Dictionary<string, ResourceLoadInfo> _loadInfoCache = new();

        // 로딩 상태 추적
        private readonly HashSet<string> _loadingResources = new();
        private readonly Dictionary<string, List<Action<UnityEngine.Object>>> _loadCallbacks = new();

        // 메모리 관리
        private float _currentMemoryUsageMB = 0f;
        private CancellationTokenSource _gcCancellationToken;

        // 초기화 상태
        private bool _isInitialized = false;
        private bool _isPreloading = false;

        #endregion

        #region Scene-based Resource Management

        /// <summary>
        /// 씬 변경 시 호출되는 메서드 (씬별 리소스 관리)
        /// </summary>
        public async UniTask OnSceneChanged(string newSceneName, CancellationToken cancellationToken = default)
        {
            if (!_enableSceneBasedLoading || _resourceSettings == null)
                return;

            try
            {
                JCDebug.Log($"[GameResourceManager] 씬 변경: {_currentSceneName} -> {newSceneName}");

                // 이전 씬의 리소스 언로드
                if (!string.IsNullOrEmpty(_currentSceneName))
                {
                    await UnloadSceneResourcesAsync(_currentSceneName);
                }

                // 새 씬의 리소스 프리로드
                _currentSceneName = newSceneName;
                await LoadSceneResourcesAsync(newSceneName, cancellationToken);

                JCDebug.Log($"[GameResourceManager] 씬 리소스 관리 완료: {newSceneName}");
            }
            catch (Exception ex)
            {
                JCDebug.Log($"[GameResourceManager] 씬 리소스 관리 실패: {ex.Message}", JCDebug.LogLevel.Error);
            }
        }

        /// <summary>
        /// 특정 씬의 리소스를 로드합니다
        /// </summary>
        public async UniTask LoadSceneResourcesAsync(string sceneName, CancellationToken cancellationToken = default)
        {
            var sceneSettings = _resourceSettings.GetScenePreloadSettings(sceneName);
            if (sceneSettings == null || !sceneSettings.preloadOnSceneLoad)
                return;

            JCDebug.Log($"[GameResourceManager] 씬 리소스 로드 시작: {sceneName}");

            var loadTasks = new List<UniTask>();

            // Resources 폴더 리소스 로드
            foreach (string resourcePath in sceneSettings.sceneSpecificResources)
            {
                if (!string.IsNullOrEmpty(resourcePath))
                {
                    loadTasks.Add(LoadAndTrackSceneResourceAsync(sceneName, resourcePath, cancellationToken));
                }
            }

            // Addressables 리소스 로드
            foreach (string addressableKey in sceneSettings.sceneSpecificAddressables)
            {
                if (!string.IsNullOrEmpty(addressableKey))
                {
                    loadTasks.Add(LoadAndTrackSceneAddressableAsync(sceneName, addressableKey, cancellationToken));
                }
            }

            if (loadTasks.Count > 0)
            {
                await ProcessTasksWithLimit(loadTasks, _resourceSettings.maxConcurrentLoads, cancellationToken);
                JCDebug.Log($"[GameResourceManager] 씬 리소스 로드 완료: {sceneName} ({loadTasks.Count}개)");
            }
        }

        /// <summary>
        /// 특정 씬의 리소스를 언로드합니다
        /// </summary>
        public async UniTask UnloadSceneResourcesAsync(string sceneName)
        {
            var sceneSettings = _resourceSettings.GetScenePreloadSettings(sceneName);
            if (sceneSettings == null || !sceneSettings.unloadOnSceneUnload)
                return;

            JCDebug.Log($"[GameResourceManager] 씬 리소스 언로드 시작: {sceneName}");

            int unloadedCount = 0;

            // Resources 폴더 리소스 언로드
            if (_sceneResources.TryGetValue(sceneName, out List<string> resources))
            {
                foreach (string resourcePath in resources)
                {
                    EvictFromCache(resourcePath);
                    unloadedCount++;
                }
                _sceneResources.Remove(sceneName);
            }

            // Addressables 리소스 언로드
            if (_sceneAddressables.TryGetValue(sceneName, out List<string> addressables))
            {
                foreach (string addressableKey in addressables)
                {
                    ReleaseAddressable(addressableKey);
                    unloadedCount++;
                }
                _sceneAddressables.Remove(sceneName);
            }

            if (unloadedCount > 0)
            {
                // 메모리 정리
                await UniTask.Delay(100); // 프레임 대기
                UpdateMemoryUsage();
                
                JCDebug.Log($"[GameResourceManager] 씬 리소스 언로드 완료: {sceneName} ({unloadedCount}개)");
            }
        }

        private async UniTask LoadAndTrackSceneResourceAsync(string sceneName, string resourcePath, CancellationToken cancellationToken)
        {
            var resource = await LoadResourceAsync<UnityEngine.Object>(resourcePath, cancellationToken);
            if (resource != null)
            {
                if (!_sceneResources.ContainsKey(sceneName))
                {
                    _sceneResources[sceneName] = new List<string>();
                }
                _sceneResources[sceneName].Add(resourcePath);
            }
        }

        private async UniTask LoadAndTrackSceneAddressableAsync(string sceneName, string addressableKey, CancellationToken cancellationToken)
        {
            var resource = await LoadAddressableAsync<UnityEngine.Object>(addressableKey, cancellationToken);
            if (resource != null)
            {
                if (!_sceneAddressables.ContainsKey(sceneName))
                {
                    _sceneAddressables[sceneName] = new List<string>();
                }
                _sceneAddressables[sceneName].Add(addressableKey);
            }
        }

        /// <summary>
        /// 현재 씬의 리소스 사용량을 반환합니다
        /// </summary>
        public (int resourceCount, int addressableCount) GetCurrentSceneResourceCount()
        {
            int resourceCount = _sceneResources.TryGetValue(_currentSceneName, out List<string> resources) ? resources.Count : 0;
            int addressableCount = _sceneAddressables.TryGetValue(_currentSceneName, out List<string> addressables) ? addressables.Count : 0;
            
            return (resourceCount, addressableCount);
        }

        #endregion

        #region Public Utility Methods

        /// <summary>
        /// 특정 유형의 리소스들을 일괄 로드합니다
        /// </summary>
        public async UniTask<List<T>> LoadResourcesByTypeAsync<T>(PreloadResourceType resourceType, CancellationToken cancellationToken = default) where T : UnityEngine.Object
        {
            if (_resourceSettings == null)
                return new List<T>();

            var resourceEntries = _resourceSettings.GetResourcesByType(resourceType);
            var results = new List<T>();

            foreach (var entry in resourceEntries)
            {
                var resource = await LoadAsync<T>(entry.resourcePath, cancellationToken);
                if (resource != null)
                {
                    results.Add(resource);
                }
            }

            return results;
        }

        /// <summary>
        /// 특정 우선순위의 리소스들을 일괄 로드합니다
        /// </summary>
        public async UniTask LoadResourcesByPriorityAsync(PreloadPriority priority, CancellationToken cancellationToken = default)
        {
            if (_resourceSettings == null)
                return;

            var resourceEntries = _resourceSettings.GetResourcesByPriority(priority);
            var loadTasks = new List<UniTask>();

            foreach (var entry in resourceEntries)
            {
                loadTasks.Add(LoadResourceWithRetryAsync(entry, cancellationToken));
            }

            if (loadTasks.Count > 0)
            {
                await ProcessTasksWithLimit(loadTasks, _resourceSettings.maxConcurrentLoads, cancellationToken);
                JCDebug.Log($"[GameResourceManager] {priority} 우선순위 리소스 로딩 완료 ({loadTasks.Count}개)");
            }
        }

        /// <summary>
        /// 메모리 사용량 강제 정리
        /// </summary>
        public async UniTask ForceCleanupMemoryAsync()
        {
            JCDebug.Log("[GameResourceManager] 강제 메모리 정리 시작");

            // 캐시 정리
            EvictOldestCacheEntries();
            
            // GC 강제 실행
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            
            await UniTask.Delay(100);
            UpdateMemoryUsage();
            
            JCDebug.Log($"[GameResourceManager] 강제 메모리 정리 완료 - 현재 사용량: {_currentMemoryUsageMB:F1}MB");
        }

        #region Properties

        public bool IsInitialized => _isInitialized;
        public string Name => nameof(GameResourceManager);
        public InitializationPriority Priority => InitializationPriority.Critical;
        public bool IsPreloading => _isPreloading;
        public float CurrentMemoryUsageMB => _currentMemoryUsageMB;
        public int CachedResourceCount => _resourceCache.Count;
        public ResourceSettings Settings => _resourceSettings;

        #endregion

        #region Events

        public event Action<string, UnityEngine.Object> OnResourceLoaded;
        public event Action<string, string> OnResourceLoadFailed;
        public event Action<float> OnMemoryUsageChanged;
        public event Action<string> OnResourceCached;
        public event Action<string> OnResourceEvicted;
        public event Action OnPreloadCompleted;

        #endregion

        #region IInitializableAsync Implementation

        public async UniTask InitializeAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                JCDebug.Log("[GameResourceManager] 초기화 시작");

                // 기본 설정 초기화
                InitializeDefaultSettings();

                // Addressables 시스템 초기화
                await InitializeAddressablesAsync(cancellationToken);

                // 메모리 관리 시스템 시작
                if (_autoGarbageCollection)
                {
                    StartMemoryManagement();
                }

                // 필수 리소스 프리로드
                if (_preloadEssentialResources)
                {
                    await PreloadEssentialResourcesAsync(cancellationToken);
                }

                _isInitialized = true;
                JCDebug.Log("[GameResourceManager] 초기화 완료", JCDebug.LogLevel.Success);
            }
            catch (Exception ex)
            {
                JCDebug.Log($"[GameResourceManager] 초기화 실패: {ex.Message}", JCDebug.LogLevel.Error);
                throw;
            }
        }

        public async UniTask CleanupAsync()
        {
            JCDebug.Log("[GameResourceManager] 정리 시작");

            // 메모리 관리 중지
            StopMemoryManagement();

            // 로딩 중인 작업들 대기
            while (_loadingResources.Count > 0)
            {
                await UniTask.Delay(100);
            }

            // 모든 Addressable 핸들 해제
            await ReleaseAllAddressableHandlesAsync();

            // 캐시 정리
            ClearAllCache();

            _isInitialized = false;
            JCDebug.Log("[GameResourceManager] 정리 완료");
        }

        #endregion

        #region Initialization Methods

        private void InitializeDefaultSettings()
        {
            if (_resourceSettings == null)
            {
                _resourceSettings = ScriptableObject.CreateInstance<ResourceSettings>();
            }

            // 메모리 사용량 초기 계산
            UpdateMemoryUsage();
        }

        private async UniTask InitializeAddressablesAsync(CancellationToken cancellationToken)
        {
            try
            {
                if (_resourceSettings.useAddressables)
                {
                    JCDebug.Log("[GameResourceManager] Addressables 시스템 초기화");
                    
                    // Addressables 초기화 대기
                    var initHandle = Addressables.InitializeAsync();
                    await initHandle.ToUniTask(cancellationToken: cancellationToken);

                    if (initHandle.Status == AsyncOperationStatus.Succeeded)
                    {
                        JCDebug.Log("[GameResourceManager] Addressables 초기화 완료");
                    }
                    else
                    {
                        JCDebug.Log("[GameResourceManager] Addressables 초기화 실패", JCDebug.LogLevel.Warning);
                    }
                }
            }
            catch (Exception ex)
            {
                JCDebug.Log($"[GameResourceManager] Addressables 초기화 오류: {ex.Message}", JCDebug.LogLevel.Error);
            }
        }

        private async UniTask PreloadEssentialResourcesAsync(CancellationToken cancellationToken)
        {
            if (_resourceSettings == null) 
            {
                JCDebug.Log("[GameResourceManager] ResourceSettings가 없어 프리로드를 건너뜁니다.", JCDebug.LogLevel.Warning);
                return;
            }

            var essentialResources = _resourceSettings.GetSortedEssentialResources();
            var addressableResources = _resourceSettings.GetSortedAddressableResources();

            if (essentialResources.Count == 0 && addressableResources.Count == 0)
            {
                JCDebug.Log("[GameResourceManager] 프리로드할 리소스가 없습니다.");
                return;
            }

            try
            {
                _isPreloading = true;
                JCDebug.Log($"[GameResourceManager] 필수 리소스 프리로드 시작 (Resources: {essentialResources.Count}, Addressables: {addressableResources.Count})");

                // 우선순위별로 로딩
                await PreloadByPriority(essentialResources, addressableResources, cancellationToken);

                OnPreloadCompleted?.Invoke();
                JCDebug.Log("[GameResourceManager] 필수 리소스 프리로드 완료", JCDebug.LogLevel.Success);
            }
            catch (Exception ex)
            {
                JCDebug.Log($"[GameResourceManager] 프리로드 실패: {ex.Message}", JCDebug.LogLevel.Error);
            }
            finally
            {
                _isPreloading = false;
            }
        }

        /// <summary>
        /// 우선순위별로 리소스를 순차 로딩합니다
        /// </summary>
        private async UniTask PreloadByPriority(List<PreloadResourceEntry> resourceEntries, List<PreloadAddressableEntry> addressableEntries, CancellationToken cancellationToken)
        {
            // Critical 우선순위부터 순차 로딩
            for (PreloadPriority priority = PreloadPriority.Critical; priority <= PreloadPriority.Low; priority++)
            {
                var priorityResources = resourceEntries.Where(r => r.priority == priority && r.loadOnGameStart).ToList();
                var priorityAddressables = addressableEntries.Where(a => a.priority == priority && a.loadOnGameStart).ToList();

                if (priorityResources.Count > 0 || priorityAddressables.Count > 0)
                {
                    JCDebug.Log($"[GameResourceManager] {priority} 우선순위 리소스 로딩 시작 (R:{priorityResources.Count}, A:{priorityAddressables.Count})");

                    var loadTasks = new List<UniTask>();

                    // Resources 로딩
                    foreach (var entry in priorityResources)
                    {
                        loadTasks.Add(LoadResourceWithRetryAsync(entry, cancellationToken));
                    }

                    // Addressables 로딩
                    foreach (var entry in priorityAddressables)
                    {
                        loadTasks.Add(LoadAddressableWithRetryAsync(entry, cancellationToken));
                    }

                    // 동시 로딩 개수 제한
                    await ProcessTasksWithLimit(loadTasks, _resourceSettings.maxConcurrentLoads, cancellationToken);

                    // 우선순위간 딜레이 (Low 우선순위만)
                    if (priority == PreloadPriority.Low && _resourceSettings.lowPriorityDelay > 0)
                    {
                        await UniTask.Delay(TimeSpan.FromSeconds(_resourceSettings.lowPriorityDelay), cancellationToken: cancellationToken);
                    }
                }
            }
        }

        /// <summary>
        /// 제한된 동시 실행으로 작업들을 처리합니다
        /// </summary>
        private async UniTask ProcessTasksWithLimit(List<UniTask> tasks, int maxConcurrent, CancellationToken cancellationToken)
        {
            if (_resourceSettings.distributePreloadAcrossFrames)
            {
                // 프레임 분산 처리
                int processed = 0;
                while (processed < tasks.Count)
                {
                    int batchSize = Mathf.Min(_resourceSettings.maxLoadsPerFrame, tasks.Count - processed);
                    var batch = tasks.Skip(processed).Take(batchSize);
                    
                    await UniTask.WhenAll(batch);
                    processed += batchSize;
                    
                    if (processed < tasks.Count)
                    {
                        await UniTask.Yield(cancellationToken);
                    }
                }
            }
            else
            {
                // 제한된 동시 실행
                var semaphore = new SemaphoreSlim(maxConcurrent, maxConcurrent);
                var limitedTasks = tasks.Select(async task =>
                {
                    await semaphore.WaitAsync(cancellationToken);
                    try
                    {
                        await task;
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                await UniTask.WhenAll(limitedTasks);
            }
        }

        /// <summary>
        /// 재시도가 포함된 Resources 로딩
        /// </summary>
        private async UniTask LoadResourceWithRetryAsync(PreloadResourceEntry entry, CancellationToken cancellationToken)
        {
            for (int attempt = 0; attempt <= _resourceSettings.loadRetryCount; attempt++)
            {
                try
                {
                    var resource = await LoadResourceAsync<UnityEngine.Object>(entry.resourcePath, cancellationToken);
                    
                    if (resource != null)
                    {
                        return;
                    }
                    else if (entry.isRequired)
                    {
                        throw new Exception($"필수 리소스 로딩 실패: {entry.resourcePath}");
                    }
                }
                catch (Exception ex)
                {
                    if (attempt == _resourceSettings.loadRetryCount)
                    {
                        if (entry.isRequired)
                        {
                            JCDebug.Log($"[GameResourceManager] 필수 리소스 로딩 최종 실패: {entry.resourcePath} - {ex.Message}", JCDebug.LogLevel.Error);
                            throw;
                        }
                        else
                        {
                            JCDebug.Log($"[GameResourceManager] 선택적 리소스 로딩 실패: {entry.resourcePath} - {ex.Message}", JCDebug.LogLevel.Warning);
                        }
                    }
                    else
                    {
                        JCDebug.Log($"[GameResourceManager] 리소스 로딩 재시도 {attempt + 1}/{_resourceSettings.loadRetryCount}: {entry.resourcePath}");
                        await UniTask.Delay(TimeSpan.FromSeconds(_resourceSettings.retryDelaySeconds), cancellationToken: cancellationToken);
                    }
                }
            }
        }

        /// <summary>
        /// 재시도가 포함된 Addressables 로딩
        /// </summary>
        private async UniTask LoadAddressableWithRetryAsync(PreloadAddressableEntry entry, CancellationToken cancellationToken)
        {
            for (int attempt = 0; attempt <= _resourceSettings.loadRetryCount; attempt++)
            {
                try
                {
                    var resource = await LoadAddressableAsync<UnityEngine.Object>(entry.addressableKey, cancellationToken);
                    
                    if (resource != null)
                    {
                        return;
                    }
                    else if (entry.isRequired)
                    {
                        throw new Exception($"필수 Addressables 리소스 로딩 실패: {entry.addressableKey}");
                    }
                }
                catch (Exception ex)
                {
                    if (attempt == _resourceSettings.loadRetryCount)
                    {
                        if (entry.isRequired)
                        {
                            JCDebug.Log($"[GameResourceManager] 필수 Addressables 리소스 로딩 최종 실패: {entry.addressableKey} - {ex.Message}", JCDebug.LogLevel.Error);
                            throw;
                        }
                        else
                        {
                            JCDebug.Log($"[GameResourceManager] 선택적 Addressables 리소스 로딩 실패: {entry.addressableKey} - {ex.Message}", JCDebug.LogLevel.Warning);
                        }
                    }
                    else
                    {
                        JCDebug.Log($"[GameResourceManager] Addressables 리소스 로딩 재시도 {attempt + 1}/{_resourceSettings.loadRetryCount}: {entry.addressableKey}");
                        await UniTask.Delay(TimeSpan.FromSeconds(_resourceSettings.retryDelaySeconds), cancellationToken: cancellationToken);
                    }
                }
            }
        }

        #endregion

        #region Resource Loading - Generic Methods

        /// <summary>
        /// 리소스를 비동기로 로드합니다 (Resources + Addressables 통합)
        /// </summary>
        public async UniTask<T> LoadAsync<T>(string path, CancellationToken cancellationToken = default) where T : UnityEngine.Object
        {
            // 캐시에서 먼저 확인
            if (_enableResourceCaching && _resourceCache.TryGetValue(path, out UnityEngine.Object cachedResource))
            {
                if (cachedResource is T typedResource)
                {
                    if (_logResourceOperations)
                    {
                        JCDebug.Log($"[GameResourceManager] 캐시에서 로드: {path}");
                    }
                    return typedResource;
                }
            }

            // Addressables 우선 시도
            if (_resourceSettings.useAddressables)
            {
                var addressableResult = await LoadAddressableAsync<T>(path, cancellationToken);
                if (addressableResult != null)
                {
                    return addressableResult;
                }
            }

            // Resources 폴더에서 로드
            return await LoadResourceAsync<T>(path, cancellationToken);
        }

        /// <summary>
        /// 여러 리소스를 병렬로 로드합니다
        /// </summary>
        public async UniTask<Dictionary<string, T>> LoadMultipleAsync<T>(List<string> paths, CancellationToken cancellationToken = default) where T : UnityEngine.Object
        {
            var result = new Dictionary<string, T>();
            var loadTasks = new List<UniTask<(string path, T resource)>>();

            foreach (string path in paths)
            {
                loadTasks.Add(LoadWithPathAsync<T>(path, cancellationToken));
            }

            var results = await UniTask.WhenAll(loadTasks);

            foreach (var (path, resource) in results)
            {
                if (resource != null)
                {
                    result[path] = resource;
                }
            }

            return result;
        }

        private async UniTask<(string, T)> LoadWithPathAsync<T>(string path, CancellationToken cancellationToken) where T : UnityEngine.Object
        {
            var resource = await LoadAsync<T>(path, cancellationToken);
            return (path, resource);
        }

        #endregion

        #region Resources Folder Loading

        /// <summary>
        /// Resources 폴더에서 리소스를 로드합니다
        /// </summary>
        public async UniTask<T> LoadResourceAsync<T>(string path, CancellationToken cancellationToken = default) where T : UnityEngine.Object
        {
            try
            {
                // 이미 로딩 중인지 확인
                if (_loadingResources.Contains(path))
                {
                    return await WaitForResourceLoad<T>(path, cancellationToken);
                }

                _loadingResources.Add(path);

                if (_logResourceOperations)
                {
                    JCDebug.Log($"[GameResourceManager] Resources 로드 시작: {path}");
                }

                // Resources.LoadAsync 사용
                var request = Resources.LoadAsync<T>(path);
                await request.ToUniTask(cancellationToken: cancellationToken);

                T resource = request.asset as T;

                if (resource != null)
                {
                    // 캐시에 저장
                    if (_enableResourceCaching)
                    {
                        CacheResource(path, resource);
                    }

                    // 로드 정보 업데이트
                    UpdateLoadInfo(path, resource, ResourceSource.Resources);

                    OnResourceLoaded?.Invoke(path, resource);

                    if (_logResourceOperations)
                    {
                        JCDebug.Log($"[GameResourceManager] Resources 로드 완료: {path}");
                    }
                }
                else
                {
                    OnResourceLoadFailed?.Invoke(path, "Resources에서 리소스를 찾을 수 없음");
                    JCDebug.Log($"[GameResourceManager] Resources 로드 실패: {path}", JCDebug.LogLevel.Warning);
                }

                return resource;
            }
            catch (Exception ex)
            {
                OnResourceLoadFailed?.Invoke(path, ex.Message);
                JCDebug.Log($"[GameResourceManager] Resources 로드 오류 ({path}): {ex.Message}", JCDebug.LogLevel.Error);
                return null;
            }
            finally
            {
                _loadingResources.Remove(path);
                TriggerPendingCallbacks(path, null);
            }
        }

        #endregion

        #region Addressables Loading

        /// <summary>
        /// Addressables에서 리소스를 로드합니다
        /// </summary>
        public async UniTask<T> LoadAddressableAsync<T>(string key, CancellationToken cancellationToken = default) where T : UnityEngine.Object
        {
            if (!_resourceSettings.useAddressables)
            {
                return null;
            }

            try
            {
                // 이미 로딩 중인지 확인
                if (_loadingResources.Contains(key))
                {
                    return await WaitForResourceLoad<T>(key, cancellationToken);
                }

                _loadingResources.Add(key);

                if (_logResourceOperations)
                {
                    JCDebug.Log($"[GameResourceManager] Addressables 로드 시작: {key}");
                }

                // Addressables.LoadAssetAsync 사용
                var handle = Addressables.LoadAssetAsync<T>(key);
                var result = await handle.ToUniTask(cancellationToken: cancellationToken);

                if (handle.Status == AsyncOperationStatus.Succeeded && result != null)
                {
                    // 핸들 저장 (해제를 위해)
                    _addressableHandles[key] = handle;

                    // 캐시에 저장
                    if (_enableResourceCaching)
                    {
                        CacheResource(key, result);
                    }

                    // 로드 정보 업데이트
                    UpdateLoadInfo(key, result, ResourceSource.Addressables);

                    OnResourceLoaded?.Invoke(key, result);

                    if (_logResourceOperations)
                    {
                        JCDebug.Log($"[GameResourceManager] Addressables 로드 완료: {key}");
                    }

                    return result;
                }
                else
                {
                    // 실패한 핸들 해제
                    if (handle.IsValid())
                    {
                        Addressables.Release(handle);
                    }

                    OnResourceLoadFailed?.Invoke(key, "Addressables에서 리소스를 찾을 수 없음");
                    JCDebug.Log($"[GameResourceManager] Addressables 로드 실패: {key}", JCDebug.LogLevel.Warning);
                    return null;
                }
            }
            catch (Exception ex)
            {
                OnResourceLoadFailed?.Invoke(key, ex.Message);
                JCDebug.Log($"[GameResourceManager] Addressables 로드 오류 ({key}): {ex.Message}", JCDebug.LogLevel.Error);
                return null;
            }
            finally
            {
                _loadingResources.Remove(key);
                TriggerPendingCallbacks(key, null);
            }
        }

        /// <summary>
        /// Addressables 핸들을 해제합니다
        /// </summary>
        public void ReleaseAddressable(string key)
        {
            if (_addressableHandles.TryGetValue(key, out AsyncOperationHandle handle))
            {
                if (handle.IsValid())
                {
                    Addressables.Release(handle);
                }
                
                _addressableHandles.Remove(key);
                _resourceCache.Remove(key);
                _loadInfoCache.Remove(key);

                if (_logResourceOperations)
                {
                    JCDebug.Log($"[GameResourceManager] Addressables 해제: {key}");
                }
            }
        }

        private async UniTask ReleaseAllAddressableHandlesAsync()
        {
            var tasks = new List<UniTask>();

            foreach (var kvp in _addressableHandles)
            {
                if (kvp.Value.IsValid())
                {
                    tasks.Add(UniTask.Create(async () =>
                    {
                        Addressables.Release(kvp.Value);
                        await UniTask.Yield();
                    }));
                }
            }

            await UniTask.WhenAll(tasks);
            _addressableHandles.Clear();

            JCDebug.Log("[GameResourceManager] 모든 Addressables 핸들 해제 완료");
        }

        #endregion

        #region Cache Management

        private void CacheResource(string path, UnityEngine.Object resource)
        {
            if (!_enableResourceCaching || resource == null)
                return;

            // 메모리 사용량 체크
            if (ShouldEvictCache())
            {
                EvictOldestCacheEntries();
            }

            _resourceCache[path] = resource;
            UpdateMemoryUsage();
            OnResourceCached?.Invoke(path);

            if (_logResourceOperations)
            {
                JCDebug.Log($"[GameResourceManager] 리소스 캐시됨: {path}");
            }
        }

        private bool ShouldEvictCache()
        {
            return _currentMemoryUsageMB > _memoryThresholdMB;
        }

        private void EvictOldestCacheEntries()
        {
            var sortedEntries = _loadInfoCache
                .Where(kvp => _resourceCache.ContainsKey(kvp.Key))
                .OrderBy(kvp => kvp.Value.lastAccessTime)
                .Take(_resourceCache.Count / 4) // 25% 제거
                .ToList();

            foreach (var entry in sortedEntries)
            {
                EvictFromCache(entry.Key);
            }

            JCDebug.Log($"[GameResourceManager] 캐시 정리 완료: {sortedEntries.Count}개 항목 제거");
        }

        private void EvictFromCache(string path)
        {
            if (_resourceCache.Remove(path))
            {
                _loadInfoCache.Remove(path);
                UpdateMemoryUsage();
                OnResourceEvicted?.Invoke(path);

                if (_logResourceOperations)
                {
                    JCDebug.Log($"[GameResourceManager] 캐시에서 제거: {path}");
                }
            }
        }

        public void ClearCache(string path)
        {
            EvictFromCache(path);
            ReleaseAddressable(path);
        }

        public void ClearAllCache()
        {
            var keys = _resourceCache.Keys.ToList();
            foreach (string key in keys)
            {
                EvictFromCache(key);
            }

            JCDebug.Log("[GameResourceManager] 모든 캐시 정리 완료");
        }

        #endregion

        #region Memory Management

        private void StartMemoryManagement()
        {
            _gcCancellationToken = new CancellationTokenSource();
            MemoryManagementLoop(_gcCancellationToken.Token).Forget();
        }

        private void StopMemoryManagement()
        {
            _gcCancellationToken?.Cancel();
            _gcCancellationToken?.Dispose();
            _gcCancellationToken = null;
        }

        private async UniTaskVoid MemoryManagementLoop(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    await UniTask.Delay(TimeSpan.FromSeconds(_gcCheckInterval), cancellationToken: cancellationToken);

                    UpdateMemoryUsage();

                    if (_currentMemoryUsageMB > _memoryThresholdMB)
                    {
                        JCDebug.Log($"[GameResourceManager] 메모리 임계치 초과 ({_currentMemoryUsageMB:F1}MB > {_memoryThresholdMB}MB), 정리 실행");
                        
                        EvictOldestCacheEntries();
                        
                        // GC 실행
                        GC.Collect();
                        await UniTask.Yield();
                        
                        UpdateMemoryUsage();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 정상적인 취소
            }
            catch (Exception ex)
            {
                JCDebug.Log($"[GameResourceManager] 메모리 관리 루프 오류: {ex.Message}", JCDebug.LogLevel.Error);
            }
        }

        private void UpdateMemoryUsage()
        {
            float previousUsage = _currentMemoryUsageMB;
            
            // 대략적인 메모리 사용량 계산 (정확하지 않지만 참고용)
            _currentMemoryUsageMB = GC.GetTotalMemory(false) / (1024f * 1024f);

            if (Mathf.Abs(_currentMemoryUsageMB - previousUsage) > 10f) // 10MB 이상 변화시에만 이벤트 발생
            {
                OnMemoryUsageChanged?.Invoke(_currentMemoryUsageMB);
            }
        }

        #endregion

        #region Utility Methods

        private async UniTask<T> WaitForResourceLoad<T>(string path, CancellationToken cancellationToken) where T : UnityEngine.Object
        {
            // 콜백 등록
            if (!_loadCallbacks.ContainsKey(path))
            {
                _loadCallbacks[path] = new List<Action<UnityEngine.Object>>();
            }

            T result = null;
            bool callbackTriggered = false;

            _loadCallbacks[path].Add((resource) =>
            {
                result = resource as T;
                callbackTriggered = true;
            });

            // 로딩 완료까지 대기
            while (!callbackTriggered && !cancellationToken.IsCancellationRequested)
            {
                await UniTask.Delay(50, cancellationToken: cancellationToken);
            }

            return result;
        }

        private void TriggerPendingCallbacks(string path, UnityEngine.Object resource)
        {
            if (_loadCallbacks.TryGetValue(path, out List<Action<UnityEngine.Object>> callbacks))
            {
                foreach (var callback in callbacks)
                {
                    try
                    {
                        callback?.Invoke(resource);
                    }
                    catch (Exception ex)
                    {
                        JCDebug.Log($"[GameResourceManager] 콜백 실행 오류: {ex.Message}", JCDebug.LogLevel.Error);
                    }
                }

                _loadCallbacks.Remove(path);
            }
        }

        private void UpdateLoadInfo(string path, UnityEngine.Object resource, ResourceSource source)
        {
            _loadInfoCache[path] = new ResourceLoadInfo
            {
                path = path,
                resourceType = resource.GetType(),
                source = source,
                loadTime = DateTime.Now,
                lastAccessTime = DateTime.Now,
                accessCount = 1
            };
        }

        /// <summary>
        /// 리소스 존재 여부를 확인합니다
        /// </summary>
        public bool HasResource(string path)
        {
            return _resourceCache.ContainsKey(path) || _addressableHandles.ContainsKey(path);
        }

        /// <summary>
        /// 로드된 리소스 정보를 가져옵니다
        /// </summary>
        public ResourceLoadInfo GetLoadInfo(string path)
        {
            return _loadInfoCache.TryGetValue(path, out ResourceLoadInfo info) ? info : null;
        }

        /// <summary>
        /// 디버그 정보를 출력합니다
        /// </summary>
        public void PrintDebugInfo()
        {
            var (sceneResourceCount, sceneAddressableCount) = GetCurrentSceneResourceCount();
            
            JCDebug.Log($"[GameResourceManager] 상태 정보:\n" +
                       $"  초기화 상태: {_isInitialized}\n" +
                       $"  캐시된 리소스: {_resourceCache.Count}\n" +
                       $"  Addressables 핸들: {_addressableHandles.Count}\n" +
                       $"  메모리 사용량: {_currentMemoryUsageMB:F1}MB (임계치: {_memoryThresholdMB}MB)\n" +
                       $"  로딩 중: {_loadingResources.Count}\n" +
                       $"  프리로드 중: {_isPreloading}\n" +
                       $"  현재 씬: {_currentSceneName}\n" +
                       $"  씬 리소스: R{sceneResourceCount}, A{sceneAddressableCount}\n" +
                       $"  씬별 추적: {_sceneResources.Count}개 씬\n" +
                       $"  설정: {(_resourceSettings != null ? "로드됨" : "없음")}");
        }

        /// <summary>
        /// 상세 메모리 정보를 출력합니다
        /// </summary>
        public void PrintDetailedMemoryInfo()
        {
            if (!_resourceSettings?.logMemoryDetails == true)
                return;

            var resourcesByType = _loadInfoCache.Values
                .GroupBy(info => info.resourceType?.Name ?? "Unknown")
                .ToDictionary(g => g.Key, g => g.Count());

            JCDebug.Log($"[GameResourceManager] 상세 메모리 정보:\n" +
                       $"  전체 메모리: {GC.GetTotalMemory(false) / (1024 * 1024):F1}MB\n" +
                       $"  캐시된 리소스 유형별 개수:\n" +
                       string.Join("\n", resourcesByType.Select(kvp => $"    {kvp.Key}: {kvp.Value}개")) +
                       $"\n  씬별 리소스 분포:\n" +
                       string.Join("\n", _sceneResources.Select(kvp => $"    {kvp.Key}: {kvp.Value.Count}개")));
        }

        #endregion

        #endregion

    }
}

