// Assets/Scripts/Managers/ResourceManager/UnifiedResourceManager.cs
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using Cysharp.Threading.Tasks;
using Metamorph.Initialization;
using CustomDebug;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Linq;

namespace Metamorph.Managers
{
    /// <summary>
    /// 통합 리소스 매니저
    /// Resources, Addressables 시스템을 통합 관리하며 
    /// 캐싱, 프리로딩, 메모리 관리, 씬 기반 관리, 참조 카운팅 제공
    /// </summary>
    public class GameResourceManager : SingletonManager<GameResourceManager>, IInitializableAsync
    {
        #region Fields

        [Header("Resource Settings")]
        [SerializeField] private ResourceSettings _resourceSettings;
        [SerializeField] private bool _enableResourceCaching = true;
        [SerializeField] private bool _preloadEssentialResources = true;
        [SerializeField] private bool _logResourceOperations = true;

        [Header("Addressables Settings")]
        [SerializeField] private bool _useAddressables = true;
        [SerializeField] private List<string> _criticalAddressableKeys = new List<string>();
        [SerializeField] private List<AssetReferenceGameObject> _criticalAssetReferences = new List<AssetReferenceGameObject>();

        [Header("Memory Management")]
        [SerializeField] private float _memoryThresholdMB = 512f;
        [SerializeField] private bool _autoGarbageCollection = true;
        [SerializeField] private float _gcCheckInterval = 30f;
        [SerializeField] private int _maxCacheSize = 200;

        [Header("Scene Management")]
        [SerializeField] private bool _enableSceneBasedLoading = true;
        [SerializeField] private string _currentSceneName = "";

        [Header("Debug")]
        [SerializeField] private bool _logCacheOperations = false;
        [SerializeField] private bool _logMemoryOperations = false;

        // === 통합된 캐시 시스템 ===
        private readonly Dictionary<string, CachedResourceInfo> _unifiedCache = new();
        private readonly Dictionary<string, AsyncOperationHandle> _addressableHandles = new();
        private readonly Dictionary<string, int> _referenceCounters = new();
        private readonly Dictionary<string, DateTime> _lastAccessTimes = new();

        // === 로딩 상태 관리 ===
        private readonly HashSet<string> _loadingResources = new();
        private readonly Dictionary<string, List<ResourceLoadCallback>> _loadCallbacks = new();

        // === 씬 기반 리소스 추적 ===
        private readonly Dictionary<string, SceneResourceInfo> _sceneResources = new();

        // === 메모리 관리 ===
        private float _currentMemoryUsageMB = 0f;
        private CancellationTokenSource _memoryManagementCTS;

        // === 초기화 상태 ===
        private bool _isInitialized = false;
        private bool _isPreloading = false;
        private CancellationTokenSource _initializationCTS;

        #endregion

        #region Properties

        public bool IsInitialized => _isInitialized;
        public string Name => nameof(GameResourceManager);
        public InitializationPriority Priority => InitializationPriority.Critical;
        public bool IsPreloading => _isPreloading;
        public float CurrentMemoryUsageMB => _currentMemoryUsageMB;
        public int CachedResourceCount => _unifiedCache.Count;
        public int ActiveHandleCount => _addressableHandles.Count;
        public string CurrentSceneName => _currentSceneName;

        #endregion

        #region Events

        public event Action<string, UnityEngine.Object> OnResourceLoaded;
        public event Action<string, string> OnResourceLoadFailed;
        public event Action<string> OnResourceCached;
        public event Action<string> OnResourceEvicted;
        public event Action<float> OnMemoryUsageChanged;
        public event Action OnPreloadCompleted;
        public event Action<string, int> OnReferenceCountChanged;

        #endregion

        #region IInitializableAsync Implementation

        public async UniTask InitializeAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                JCDebug.Log("[UnifiedResourceManager] 초기화 시작");

                _initializationCTS = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

                // 기본 설정 초기화
                InitializeDefaultSettings();

                // Addressables 시스템 초기화
                if (_useAddressables)
                {
                    await InitializeAddressablesAsync(_initializationCTS.Token);
                }

                // 메모리 관리 시스템 시작
                if (_autoGarbageCollection)
                {
                    StartMemoryManagement();
                }

                // 필수 리소스 프리로드
                if (_preloadEssentialResources)
                {
                    await PreloadEssentialResourcesAsync(_initializationCTS.Token);
                }

                _isInitialized = true;
                JCDebug.Log("[UnifiedResourceManager] 초기화 완료", JCDebug.LogLevel.Success);
            }
            catch (OperationCanceledException)
            {
                JCDebug.Log("[UnifiedResourceManager] 초기화 취소됨", JCDebug.LogLevel.Warning);
                throw;
            }
            catch (Exception ex)
            {
                JCDebug.Log($"[UnifiedResourceManager] 초기화 실패: {ex.Message}", JCDebug.LogLevel.Error);
                throw;
            }
        }

        public async UniTask CleanupAsync()
        {
            JCDebug.Log("[UnifiedResourceManager] 정리 시작");

            // 취소 토큰 발행
            _initializationCTS?.Cancel();
            _initializationCTS?.Dispose();

            // 메모리 관리 중지
            StopMemoryManagement();

            // 로딩 중인 작업들 대기
            while (_loadingResources.Count > 0)
            {
                await UniTask.Delay(100);
            }

            // 모든 리소스 해제
            await UnloadAllResourcesAsync();

            _isInitialized = false;
            JCDebug.Log("[UnifiedResourceManager] 정리 완료");
        }

        #endregion

        #region Core Loading Methods

        /// <summary>
        /// 통합 리소스 로더 - Resources와 Addressables를 자동 선택
        /// </summary>
        public async UniTask<T> LoadAsync<T>(string path, CancellationToken cancellationToken = default) where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(path))
            {
                JCDebug.Log("[UnifiedResourceManager] 리소스 경로가 비어있습니다", JCDebug.LogLevel.Error);
                return null;
            }

            // 캐시에서 먼저 확인
            if (_enableResourceCaching && TryGetFromCache<T>(path, out T cachedResource))
            {
                UpdateReferenceCount(path, 1);
                UpdateLastAccessTime(path);
                return cachedResource;
            }

            // 이미 로딩 중인지 확인
            if (_loadingResources.Contains(path))
            {
                return await WaitForResourceLoad<T>(path, cancellationToken);
            }

            try
            {
                _loadingResources.Add(path);

                if (_logResourceOperations)
                {
                    JCDebug.Log($"[UnifiedResourceManager] 리소스 로드 시작: {path}");
                }

                T result = null;

                // Addressables 우선 시도
                if (_useAddressables)
                {
                    result = await LoadFromAddressables<T>(path, cancellationToken);
                }

                // Addressables에서 실패했거나 사용하지 않는 경우 Resources 시도
                if (result == null)
                {
                    result = await LoadFromResources<T>(path, cancellationToken);
                }

                if (result != null)
                {
                    // 캐시에 저장
                    CacheResource(path, result, GetResourceSource(path));
                    UpdateReferenceCount(path, 1);
                    OnResourceLoaded?.Invoke(path, result);

                    if (_logResourceOperations)
                    {
                        JCDebug.Log($"[UnifiedResourceManager] 리소스 로드 완료: {path}");
                    }
                }
                else
                {
                    OnResourceLoadFailed?.Invoke(path, "모든 소스에서 리소스를 찾을 수 없음");
                }

                return result;
            }
            catch (Exception ex)
            {
                var message = $"리소스 로드 실패: {path} - {ex.Message}";
                JCDebug.Log($"[UnifiedResourceManager] {message}", JCDebug.LogLevel.Error);
                OnResourceLoadFailed?.Invoke(path, ex.Message);
                return null;
            }
            finally
            {
                _loadingResources.Remove(path);
                TriggerPendingCallbacks(path);
            }
        }

        /// <summary>
        /// 여러 리소스를 배치로 로드
        /// </summary>
        public async UniTask<Dictionary<string, T>> LoadMultipleAsync<T>(List<string> paths, int maxConcurrency = 5, CancellationToken cancellationToken = default) where T : UnityEngine.Object
        {
            var results = new Dictionary<string, T>();

            // 동시 로딩 수 제한
            var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
            var tasks = paths.Select(async path =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    var resource = await LoadAsync<T>(path, cancellationToken);
                    return (path, resource);
                }
                finally
                {
                    semaphore.Release();
                }
            });

            var loadResults = await UniTask.WhenAll(tasks);

            foreach (var (path, resource) in loadResults)
            {
                if (resource != null)
                {
                    results[path] = resource;
                }
            }

            JCDebug.Log($"[UnifiedResourceManager] 배치 로드 완료: {results.Count}/{paths.Count}");
            return results;
        }

        /// <summary>
        /// GameObject 인스턴스화 (Addressables 전용)
        /// </summary>
        public async UniTask<GameObject> InstantiateAsync(string key, Transform parent = null, CancellationToken cancellationToken = default)
        {
            if (!_useAddressables)
            {
                JCDebug.Log("[UnifiedResourceManager] Addressables가 비활성화되어 인스턴스화 불가", JCDebug.LogLevel.Warning);
                return null;
            }

            try
            {
                if (_logResourceOperations)
                {
                    JCDebug.Log($"[UnifiedResourceManager] 인스턴스화 시작: {key}");
                }

                var handle = Addressables.InstantiateAsync(key, parent);
                var instanceKey = $"{key}_instance_{handle.GetHashCode()}";
                _addressableHandles[instanceKey] = handle;

                var result = await handle.ToUniTask(cancellationToken: cancellationToken);

                if (result != null)
                {
                    UpdateReferenceCount(key, 1);
                    OnResourceLoaded?.Invoke(key, result);

                    if (_logResourceOperations)
                    {
                        JCDebug.Log($"[UnifiedResourceManager] 인스턴스화 완료: {key}");
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                JCDebug.Log($"[UnifiedResourceManager] 인스턴스화 실패: {key} - {ex.Message}", JCDebug.LogLevel.Error);
                return null;
            }
        }

        /// <summary>
        /// AssetReference를 사용한 로드
        /// </summary>
        public async UniTask<T> LoadAssetReferenceAsync<T>(AssetReference assetReference, CancellationToken cancellationToken = default) where T : UnityEngine.Object
        {
            if (assetReference == null || !assetReference.RuntimeKeyIsValid())
            {
                JCDebug.Log("[UnifiedResourceManager] 유효하지 않은 AssetReference", JCDebug.LogLevel.Error);
                return null;
            }

            return await LoadAsync<T>(assetReference.AssetGUID, cancellationToken);
        }

        #endregion

        #region Scene-based Resource Management

        /// <summary>
        /// 씬 변경 시 리소스 관리
        /// </summary>
        public async UniTask OnSceneChangedAsync(string newSceneName, CancellationToken cancellationToken = default)
        {
            if (!_enableSceneBasedLoading)
                return;

            try
            {
                JCDebug.Log($"[UnifiedResourceManager] 씬 변경: {_currentSceneName} -> {newSceneName}");

                // 이전 씬 리소스 언로드
                if (!string.IsNullOrEmpty(_currentSceneName))
                {
                    await UnloadSceneResourcesAsync(_currentSceneName);
                }

                // 새 씬 리소스 프리로드
                _currentSceneName = newSceneName;
                await LoadSceneResourcesAsync(newSceneName, cancellationToken);

                JCDebug.Log($"[UnifiedResourceManager] 씬 리소스 관리 완료: {newSceneName}");
            }
            catch (Exception ex)
            {
                JCDebug.Log($"[UnifiedResourceManager] 씬 리소스 관리 실패: {ex.Message}", JCDebug.LogLevel.Error);
            }
        }

        private async UniTask LoadSceneResourcesAsync(string sceneName, CancellationToken cancellationToken)
        {
            if (_resourceSettings == null)
                return;

            var sceneSettings = _resourceSettings.GetScenePreloadSettings(sceneName);
            if (sceneSettings?.preloadOnSceneLoad != true)
                return;

            var sceneInfo = new SceneResourceInfo
            {
                sceneName = sceneName,
                loadedResources = new List<string>(),
                loadedAddressables = new List<string>()
            };

            var loadTasks = new List<UniTask>();

            // Resources 로드
            foreach (var resourcePath in sceneSettings.sceneSpecificResources)
            {
                loadTasks.Add(LoadAndTrackSceneResource(sceneInfo, resourcePath, cancellationToken));
            }

            // Addressables 로드
            foreach (var addressableKey in sceneSettings.sceneSpecificAddressables)
            {
                loadTasks.Add(LoadAndTrackSceneAddressable(sceneInfo, addressableKey, cancellationToken));
            }

            if (loadTasks.Count > 0)
            {
                await UniTask.WhenAll(loadTasks);
                _sceneResources[sceneName] = sceneInfo;

                JCDebug.Log($"[UnifiedResourceManager] 씬 리소스 로드 완료: {sceneName} ({loadTasks.Count}개)");
            }
        }

        private async UniTask UnloadSceneResourcesAsync(string sceneName)
        {
            if (!_sceneResources.TryGetValue(sceneName, out SceneResourceInfo sceneInfo))
                return;

            int unloadedCount = 0;

            // Resources 언로드
            foreach (var resourcePath in sceneInfo.loadedResources)
            {
                UnloadResource(resourcePath);
                unloadedCount++;
            }

            // Addressables 언로드
            foreach (var addressableKey in sceneInfo.loadedAddressables)
            {
                UnloadResource(addressableKey);
                unloadedCount++;
            }

            _sceneResources.Remove(sceneName);

            if (unloadedCount > 0)
            {
                UpdateMemoryUsage();
                JCDebug.Log($"[UnifiedResourceManager] 씬 리소스 언로드 완료: {sceneName} ({unloadedCount}개)");
            }
        }

        private async UniTask LoadAndTrackSceneResource(SceneResourceInfo sceneInfo, string resourcePath, CancellationToken cancellationToken)
        {
            var resource = await LoadAsync<UnityEngine.Object>(resourcePath, cancellationToken);
            if (resource != null)
            {
                sceneInfo.loadedResources.Add(resourcePath);
            }
        }

        private async UniTask LoadAndTrackSceneAddressable(SceneResourceInfo sceneInfo, string addressableKey, CancellationToken cancellationToken)
        {
            var resource = await LoadAsync<UnityEngine.Object>(addressableKey, cancellationToken);
            if (resource != null)
            {
                sceneInfo.loadedAddressables.Add(addressableKey);
            }
        }

        #endregion

        #region Private Loading Methods

        private async UniTask<T> LoadFromAddressables<T>(string key, CancellationToken cancellationToken) where T : UnityEngine.Object
        {
            try
            {
                var handle = Addressables.LoadAssetAsync<T>(key);
                _addressableHandles[key] = handle;

                var result = await handle.ToUniTask(cancellationToken: cancellationToken);

                if (handle.Status == AsyncOperationStatus.Succeeded && result != null)
                {
                    return result;
                }
                else
                {
                    // 실패한 핸들 정리
                    if (handle.IsValid())
                    {
                        Addressables.Release(handle);
                    }
                    _addressableHandles.Remove(key);
                    return null;
                }
            }
            catch (Exception ex)
            {
                if (_logResourceOperations)
                {
                    JCDebug.Log($"[UnifiedResourceManager] Addressables 로드 실패: {key} - {ex.Message}", JCDebug.LogLevel.Warning);
                }
                return null;
            }
        }

        private async UniTask<T> LoadFromResources<T>(string path, CancellationToken cancellationToken) where T : UnityEngine.Object
        {
            try
            {
                var request = Resources.LoadAsync<T>(path);
                await request.ToUniTask(cancellationToken: cancellationToken);

                return request.asset as T;
            }
            catch (Exception ex)
            {
                if (_logResourceOperations)
                {
                    JCDebug.Log($"[UnifiedResourceManager] Resources 로드 실패: {path} - {ex.Message}", JCDebug.LogLevel.Warning);
                }
                return null;
            }
        }

        #endregion

        #region Cache Management

        private bool TryGetFromCache<T>(string path, out T resource) where T : UnityEngine.Object
        {
            resource = null;

            if (_unifiedCache.TryGetValue(path, out CachedResourceInfo cachedInfo))
            {
                if (cachedInfo.resource is T typedResource)
                {
                    resource = typedResource;
                    cachedInfo.lastAccessTime = DateTime.Now;
                    cachedInfo.accessCount++;

                    if (_logCacheOperations)
                    {
                        JCDebug.Log($"[UnifiedResourceManager] 캐시 히트: {path}");
                    }

                    return true;
                }
            }

            return false;
        }

        private void CacheResource(string path, UnityEngine.Object resource, ResourceSource source)
        {
            if (!_enableResourceCaching || resource == null)
                return;

            // 메모리 임계치 체크
            if (_unifiedCache.Count >= _maxCacheSize || ShouldEvictCache())
            {
                EvictOldestCacheEntries();
            }

            var cachedInfo = new CachedResourceInfo
            {
                resource = resource,
                source = source,
                loadTime = DateTime.Now,
                lastAccessTime = DateTime.Now,
                accessCount = 1
            };

            _unifiedCache[path] = cachedInfo;
            UpdateMemoryUsage();
            OnResourceCached?.Invoke(path);

            if (_logCacheOperations)
            {
                JCDebug.Log($"[UnifiedResourceManager] 리소스 캐시: {path} ({source})");
            }
        }

        private bool ShouldEvictCache()
        {
            return _currentMemoryUsageMB > _memoryThresholdMB;
        }

        private void EvictOldestCacheEntries()
        {
            var sortedEntries = _unifiedCache
                .OrderBy(kvp => kvp.Value.lastAccessTime)
                .Take(_unifiedCache.Count / 4) // 25% 제거
                .ToList();

            foreach (var entry in sortedEntries)
            {
                EvictFromCache(entry.Key);
            }

            if (sortedEntries.Count > 0)
            {
                JCDebug.Log($"[UnifiedResourceManager] 캐시 정리: {sortedEntries.Count}개 항목 제거");
            }
        }

        private void EvictFromCache(string path)
        {
            if (_unifiedCache.Remove(path))
            {
                UpdateMemoryUsage();
                OnResourceEvicted?.Invoke(path);

                if (_logCacheOperations)
                {
                    JCDebug.Log($"[UnifiedResourceManager] 캐시 제거: {path}");
                }
            }
        }

        #endregion

        #region Reference Counting

        private void UpdateReferenceCount(string path, int delta)
        {
            if (_referenceCounters.TryGetValue(path, out int currentCount))
            {
                currentCount += delta;
                if (currentCount <= 0)
                {
                    _referenceCounters.Remove(path);
                    OnReferenceCountChanged?.Invoke(path, 0);
                }
                else
                {
                    _referenceCounters[path] = currentCount;
                    OnReferenceCountChanged?.Invoke(path, currentCount);
                }
            }
            else if (delta > 0)
            {
                _referenceCounters[path] = delta;
                OnReferenceCountChanged?.Invoke(path, delta);
            }
        }

        private void UpdateLastAccessTime(string path)
        {
            _lastAccessTimes[path] = DateTime.Now;
        }

        public int GetReferenceCount(string path)
        {
            return _referenceCounters.TryGetValue(path, out int count) ? count : 0;
        }

        #endregion

        #region Resource Unloading

        /// <summary>
        /// 특정 리소스 언로드
        /// </summary>
        public void UnloadResource(string path)
        {
            // 참조 카운트 감소
            UpdateReferenceCount(path, -1);

            // 참조가 남아있으면 언로드하지 않음
            if (GetReferenceCount(path) > 0)
                return;

            try
            {
                // 캐시에서 제거
                EvictFromCache(path);

                // Addressable 핸들 해제
                if (_addressableHandles.TryGetValue(path, out AsyncOperationHandle handle))
                {
                    if (handle.IsValid())
                    {
                        Addressables.Release(handle);
                    }
                    _addressableHandles.Remove(path);
                }

                _lastAccessTimes.Remove(path);

                if (_logResourceOperations)
                {
                    JCDebug.Log($"[UnifiedResourceManager] 리소스 언로드: {path}");
                }
            }
            catch (Exception ex)
            {
                JCDebug.Log($"[UnifiedResourceManager] 리소스 언로드 실패: {path} - {ex.Message}", JCDebug.LogLevel.Error);
            }
        }

        /// <summary>
        /// 인스턴스 해제
        /// </summary>
        public bool ReleaseInstance(GameObject instance)
        {
            try
            {
                return Addressables.ReleaseInstance(instance);
            }
            catch (Exception ex)
            {
                JCDebug.Log($"[UnifiedResourceManager] 인스턴스 해제 실패: {ex.Message}", JCDebug.LogLevel.Error);
                return false;
            }
        }

        /// <summary>
        /// 모든 리소스 언로드
        /// </summary>
        public async UniTask UnloadAllResourcesAsync()
        {
            try
            {
                JCDebug.Log("[UnifiedResourceManager] 모든 리소스 언로드 시작");

                // 모든 Addressable 핸들 해제
                var handleKeys = _addressableHandles.Keys.ToList();
                foreach (string key in handleKeys)
                {
                    if (_addressableHandles.TryGetValue(key, out AsyncOperationHandle handle))
                    {
                        if (handle.IsValid())
                        {
                            Addressables.Release(handle);
                        }
                    }
                }

                _addressableHandles.Clear();
                _unifiedCache.Clear();
                _referenceCounters.Clear();
                _lastAccessTimes.Clear();
                _sceneResources.Clear();

                // Addressables 번들 캐시 정리
                if (_useAddressables)
                {
                    await Addressables.CleanBundleCache();
                }

                UpdateMemoryUsage();
                JCDebug.Log("[UnifiedResourceManager] 모든 리소스 언로드 완료");
            }
            catch (Exception ex)
            {
                JCDebug.Log($"[UnifiedResourceManager] 전체 언로드 실패: {ex.Message}", JCDebug.LogLevel.Error);
            }
        }

        #endregion

        #region Initialization and Memory Management

        private void InitializeDefaultSettings()
        {
            if (_resourceSettings == null)
            {
                _resourceSettings = ScriptableObject.CreateInstance<ResourceSettings>();
            }

            UpdateMemoryUsage();
        }

        private async UniTask InitializeAddressablesAsync(CancellationToken cancellationToken)
        {
            try
            {
                JCDebug.Log("[UnifiedResourceManager] Addressables 초기화");

                var initHandle = Addressables.InitializeAsync();
                await initHandle.ToUniTask(cancellationToken: cancellationToken);

                if (initHandle.Status == AsyncOperationStatus.Succeeded)
                {
                    JCDebug.Log("[UnifiedResourceManager] Addressables 초기화 완료");
                }
                else
                {
                    JCDebug.Log("[UnifiedResourceManager] Addressables 초기화 실패", JCDebug.LogLevel.Warning);
                }
            }
            catch (Exception ex)
            {
                JCDebug.Log($"[UnifiedResourceManager] Addressables 초기화 오류: {ex.Message}", JCDebug.LogLevel.Error);
            }
        }

        private async UniTask PreloadEssentialResourcesAsync(CancellationToken cancellationToken)
        {
            try
            {
                _isPreloading = true;
                JCDebug.Log("[UnifiedResourceManager] 필수 리소스 프리로드 시작");

                var loadTasks = new List<UniTask>();

                // 중요 Addressables 로드
                foreach (string key in _criticalAddressableKeys)
                {
                    loadTasks.Add(LoadAsync<UnityEngine.Object>(key, cancellationToken));
                }

                // AssetReference 로드
                foreach (var assetRef in _criticalAssetReferences)
                {
                    loadTasks.Add(LoadAssetReferenceAsync<GameObject>(assetRef, cancellationToken));
                }

                // ResourceSettings 기반 프리로드
                if (_resourceSettings != null)
                {
                    var essentialResources = _resourceSettings.GetSortedEssentialResources();
                    foreach (var entry in essentialResources.Where(e => e.loadOnGameStart))
                    {
                        loadTasks.Add(LoadAsync<UnityEngine.Object>(entry.resourcePath, cancellationToken));
                    }
                }

                if (loadTasks.Count > 0)
                {
                    await UniTask.WhenAll(loadTasks);
                }

                OnPreloadCompleted?.Invoke();
                JCDebug.Log($"[UnifiedResourceManager] 필수 리소스 프리로드 완료: {loadTasks.Count}개");
            }
            catch (Exception ex)
            {
                JCDebug.Log($"[UnifiedResourceManager] 프리로드 실패: {ex.Message}", JCDebug.LogLevel.Error);
            }
            finally
            {
                _isPreloading = false;
            }
        }

        private void StartMemoryManagement()
        {
            _memoryManagementCTS = new CancellationTokenSource();
            MemoryManagementLoop(_memoryManagementCTS.Token).Forget();
        }

        private void StopMemoryManagement()
        {
            _memoryManagementCTS?.Cancel();
            _memoryManagementCTS?.Dispose();
            _memoryManagementCTS = null;
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
                        if (_logMemoryOperations)
                        {
                            JCDebug.Log($"[UnifiedResourceManager] 메모리 임계치 초과 ({_currentMemoryUsageMB:F1}MB > {_memoryThresholdMB}MB)");
                        }

                        EvictOldestCacheEntries();
                        GC.Collect();

                        await UniTask.Yield();
                        UpdateMemoryUsage();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 정상 취소
            }
            catch (Exception ex)
            {
                JCDebug.Log($"[UnifiedResourceManager] 메모리 관리 루프 오류: {ex.Message}", JCDebug.LogLevel.Error);
            }
        }

        private void UpdateMemoryUsage()
        {
            float previousUsage = _currentMemoryUsageMB;
            _currentMemoryUsageMB = GC.GetTotalMemory(false) / (1024f * 1024f);

            if (Mathf.Abs(_currentMemoryUsageMB - previousUsage) > 10f)
            {
                OnMemoryUsageChanged?.Invoke(_currentMemoryUsageMB);
            }
        }

        #endregion

        #region Utility Methods

        private async UniTask<T> WaitForResourceLoad<T>(string path, CancellationToken cancellationToken) where T : UnityEngine.Object
        {
            T result = null;
            bool completed = false;

            var callback = new ResourceLoadCallback<T>((resource) =>
            {
                result = resource;
                completed = true;
            });

            if (!_loadCallbacks.ContainsKey(path))
            {
                _loadCallbacks[path] = new List<ResourceLoadCallback>();
            }
            _loadCallbacks[path].Add(callback);

            while (!completed && !cancellationToken.IsCancellationRequested)
            {
                await UniTask.Delay(50, cancellationToken: cancellationToken);
            }

            return result;
        }

        private void TriggerPendingCallbacks(string path)
        {
            if (_loadCallbacks.TryGetValue(path, out List<ResourceLoadCallback> callbacks))
            {
                var resource = _unifiedCache.TryGetValue(path, out CachedResourceInfo cachedInfo) ? cachedInfo.resource : null;

                foreach (var callback in callbacks)
                {
                    try
                    {
                        callback.Invoke(resource);
                    }
                    catch (Exception ex)
                    {
                        JCDebug.Log($"[UnifiedResourceManager] 콜백 실행 오류: {ex.Message}", JCDebug.LogLevel.Error);
                    }
                }

                _loadCallbacks.Remove(path);
            }
        }

        private ResourceSource GetResourceSource(string path)
        {
            // Addressables인지 Resources인지 판단
            return _addressableHandles.ContainsKey(path) ? ResourceSource.Addressables : ResourceSource.Resources;
        }

        /// <summary>
        /// 리소스 존재 여부 확인
        /// </summary>
        public bool HasResource(string path)
        {
            return _unifiedCache.ContainsKey(path);
        }

        /// <summary>
        /// 캐시된 리소스 정보 가져오기
        /// </summary>
        public CachedResourceInfo GetCachedResourceInfo(string path)
        {
            return _unifiedCache.TryGetValue(path, out CachedResourceInfo info) ? info : null;
        }

        /// <summary>
        /// 디버그 정보 출력
        /// </summary>
        public void PrintDebugInfo()
        {
            JCDebug.Log($"[UnifiedResourceManager] 상태 정보:\n" +
                       $"  초기화: {_isInitialized}\n" +
                       $"  캐시된 리소스: {_unifiedCache.Count}/{_maxCacheSize}\n" +
                       $"  Addressable 핸들: {_addressableHandles.Count}\n" +
                       $"  참조 카운터: {_referenceCounters.Count}\n" +
                       $"  메모리 사용량: {_currentMemoryUsageMB:F1}MB (임계치: {_memoryThresholdMB}MB)\n" +
                       $"  로딩 중: {_loadingResources.Count}\n" +
                       $"  현재 씬: {_currentSceneName}\n" +
                       $"  관리 중인 씬: {_sceneResources.Count}개\n" +
                       $"  Addressables 사용: {_useAddressables}");
        }

        #endregion

        #region Data Structures

        [System.Serializable]
        public class CachedResourceInfo
        {
            public UnityEngine.Object resource;
            public ResourceSource source;
            public DateTime loadTime;
            public DateTime lastAccessTime;
            public int accessCount;
        }

        [System.Serializable]
        public class SceneResourceInfo
        {
            public string sceneName;
            public List<string> loadedResources;
            public List<string> loadedAddressables;
        }

        public abstract class ResourceLoadCallback
        {
            public abstract void Invoke(UnityEngine.Object resource);
        }

        public class ResourceLoadCallback<T> : ResourceLoadCallback where T : UnityEngine.Object
        {
            private Action<T> _callback;

            public ResourceLoadCallback(Action<T> callback)
            {
                _callback = callback;
            }

            public override void Invoke(UnityEngine.Object resource)
            {
                _callback?.Invoke(resource as T);
            }
        }

        public enum ResourceSource
        {
            Resources,
            Addressables
        }

        #endregion
    }
}