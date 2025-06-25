// Assets/Scripts/Managers/ResourceManager/AddressableManager.cs
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using Cysharp.Threading.Tasks;
using Metamorph.Initialization;
using CustomDebug;
using System;
using System.Collections.Generic;
using System.Threading;

namespace Metamorph.Managers
{
    /// <summary>
    /// Addressable Asset System을 관리하는 매니저
    /// 비동기 로딩, 캐싱, 메모리 관리를 담당
    /// </summary>
    public class AddressableManager : SingletonManager<AddressableManager>, IInitializableAsync
    {
        [Header("Addressable Settings")]
        [SerializeField] private bool _preloadCriticalAssets = true;
        [SerializeField] private bool _enableCaching = true;
        [SerializeField] private int _maxCacheSize = 100;
        [SerializeField] private bool _autoUnloadUnusedAssets = true;

        [Header("Preload Assets")]
        [SerializeField] private List<string> _criticalAssetKeys = new List<string>();
        [SerializeField] private List<AssetReferenceGameObject> _criticalAssetReferences = new List<AssetReferenceGameObject>();

        [Header("Debug")]
        [SerializeField] private bool _logLoadOperations = true;
        [SerializeField] private bool _logCacheOperations = false;

        // 캐시 시스템
        private readonly Dictionary<string, UnityEngine.Object> _assetCache = new();
        private readonly Dictionary<string, AsyncOperationHandle> _activeHandles = new();
        private readonly Dictionary<string, int> _referenceCounters = new();
        private readonly Dictionary<string, DateTime> _lastAccessTimes = new();

        // 초기화 상태
        private bool _isInitialized = false;
        private CancellationTokenSource _initializationCTS;

        // 이벤트
        public event Action<string, UnityEngine.Object> OnAssetLoaded;
        public event Action<string> OnAssetUnloaded;
        public event Action<string, Exception> OnAssetLoadFailed;
        public event Action OnCacheCleared;

        #region IInitializableAsync Implementation

        public string Name => nameof(AddressableManager);
        public InitializationPriority Priority => InitializationPriority.Critical;
        public bool IsInitialized => _isInitialized;

        public async UniTask InitializeAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                JCDebug.Log("[AddressableManager] 초기화 시작");

                _initializationCTS = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

                // Addressable System 초기화
                await InitializeAddressableSystemAsync(_initializationCTS.Token);

                // 중요 에셋 프리로드
                if (_preloadCriticalAssets)
                {
                    await PreloadCriticalAssetsAsync(_initializationCTS.Token);
                }

                // 자동 정리 시스템 시작
                if (_autoUnloadUnusedAssets)
                {
                    StartAutoCleanupSystem();
                }

                _isInitialized = true;
                JCDebug.Log("[AddressableManager] 초기화 완료", JCDebug.LogLevel.Success);
            }
            catch (OperationCanceledException)
            {
                JCDebug.Log("[AddressableManager] 초기화 취소됨", JCDebug.LogLevel.Warning);
                throw;
            }
            catch (Exception ex)
            {
                JCDebug.Log($"[AddressableManager] 초기화 실패: {ex.Message}", JCDebug.LogLevel.Error);
                throw;
            }
        }

        public async UniTask CleanupAsync()
        {
            JCDebug.Log("[AddressableManager] 정리 시작");

            _initializationCTS?.Cancel();
            _initializationCTS?.Dispose();

            // 모든 에셋 언로드
            await UnloadAllAssetsAsync();

            _isInitialized = false;
            JCDebug.Log("[AddressableManager] 정리 완료");
        }

        #endregion

        #region Core Loading Methods

        /// <summary>
        /// 에셋을 비동기로 로드합니다 (제네릭 버전)
        /// </summary>
        public async UniTask<T> LoadAssetAsync<T>(string key, CancellationToken cancellationToken = default) where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(key))
            {
                JCDebug.Log("[AddressableManager] 에셋 키가 비어있습니다", JCDebug.LogLevel.Error);
                return null;
            }

            try
            {
                // 캐시에서 확인
                if (_enableCaching && _assetCache.TryGetValue(key, out UnityEngine.Object cachedAsset))
                {
                    if (cachedAsset is T typedAsset)
                    {
                        UpdateReferenceCount(key, 1);
                        UpdateLastAccessTime(key);

                        if (_logCacheOperations)
                        {
                            JCDebug.Log($"[AddressableManager] 캐시에서 로드: {key}");
                        }

                        return typedAsset;
                    }
                }

                if (_logLoadOperations)
                {
                    JCDebug.Log($"[AddressableManager] 에셋 로드 시작: {key}");
                }

                // Addressable에서 로드
                var handle = Addressables.LoadAssetAsync<T>(key);
                _activeHandles[key] = handle;

                var result = await handle.ToUniTask(cancellationToken: cancellationToken);

                if (result != null)
                {
                    // 캐시에 저장
                    if (_enableCaching)
                    {
                        AddToCache(key, result);
                    }

                    UpdateReferenceCount(key, 1);
                    UpdateLastAccessTime(key);

                    OnAssetLoaded?.Invoke(key, result);

                    if (_logLoadOperations)
                    {
                        JCDebug.Log($"[AddressableManager] 에셋 로드 완료: {key}", JCDebug.LogLevel.Success);
                    }

                    return result;
                }
                else
                {
                    throw new Exception($"에셋 로드 결과가 null입니다: {key}");
                }
            }
            catch (Exception ex)
            {
                var message = $"에셋 로드 실패: {key} - {ex.Message}";
                JCDebug.Log($"[AddressableManager] {message}", JCDebug.LogLevel.Error);
                OnAssetLoadFailed?.Invoke(key, ex);

                // 실패한 핸들 정리
                if (_activeHandles.TryGetValue(key, out AsyncOperationHandle failedHandle))
                {
                    if (failedHandle.IsValid())
                    {
                        Addressables.Release(failedHandle);
                    }
                    _activeHandles.Remove(key);
                }

                return null;
            }
        }

        /// <summary>
        /// AssetReference를 사용한 로드
        /// </summary>
        public async UniTask<T> LoadAssetAsync<T>(AssetReference assetReference, CancellationToken cancellationToken = default) where T : UnityEngine.Object
        {
            if (assetReference == null || !assetReference.RuntimeKeyIsValid())
            {
                JCDebug.Log("[AddressableManager] 유효하지 않은 AssetReference", JCDebug.LogLevel.Error);
                return null;
            }

            string key = assetReference.AssetGUID;
            return await LoadAssetAsync<T>(key, cancellationToken);
        }

        /// <summary>
        /// 게임오브젝트를 인스턴스화하여 로드
        /// </summary>
        public async UniTask<GameObject> InstantiateAsync(string key, Transform parent = null, CancellationToken cancellationToken = default)
        {
            try
            {
                if (_logLoadOperations)
                {
                    JCDebug.Log($"[AddressableManager] 인스턴스화 시작: {key}");
                }

                var handle = Addressables.InstantiateAsync(key, parent);
                _activeHandles[$"{key}_instance_{handle.GetHashCode()}"] = handle;

                var result = await handle.ToUniTask(cancellationToken: cancellationToken);

                if (result != null)
                {
                    UpdateReferenceCount(key, 1);
                    OnAssetLoaded?.Invoke(key, result);

                    if (_logLoadOperations)
                    {
                        JCDebug.Log($"[AddressableManager] 인스턴스화 완료: {key}", JCDebug.LogLevel.Success);
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                var message = $"인스턴스화 실패: {key} - {ex.Message}";
                JCDebug.Log($"[AddressableManager] {message}", JCDebug.LogLevel.Error);
                OnAssetLoadFailed?.Invoke(key, ex);
                return null;
            }
        }

        /// <summary>
        /// AssetReference를 사용한 인스턴스화
        /// </summary>
        public async UniTask<GameObject> InstantiateAsync(AssetReferenceGameObject assetReference, Transform parent = null, CancellationToken cancellationToken = default)
        {
            if (assetReference == null || !assetReference.RuntimeKeyIsValid())
            {
                JCDebug.Log("[AddressableManager] 유효하지 않은 AssetReferenceGameObject", JCDebug.LogLevel.Error);
                return null;
            }

            try
            {
                var handle = assetReference.InstantiateAsync(parent);
                string key = $"{assetReference.AssetGUID}_instance_{handle.GetHashCode()}";
                _activeHandles[key] = handle;

                var result = await handle.ToUniTask(cancellationToken: cancellationToken);
                return result;
            }
            catch (Exception ex)
            {
                JCDebug.Log($"[AddressableManager] AssetReference 인스턴스화 실패: {ex.Message}", JCDebug.LogLevel.Error);
                return null;
            }
        }

        #endregion

        #region Batch Loading

        /// <summary>
        /// 여러 에셋을 배치로 로드합니다
        /// </summary>
        public async UniTask<Dictionary<string, T>> LoadAssetsAsync<T>(List<string> keys, CancellationToken cancellationToken = default) where T : UnityEngine.Object
        {
            var results = new Dictionary<string, T>();
            var tasks = new List<UniTask<(string key, T asset)>>();

            foreach (string key in keys)
            {
                tasks.Add(LoadAssetWithKeyAsync<T>(key, cancellationToken));
            }

            var loadResults = await UniTask.WhenAll(tasks);

            foreach (var (key, asset) in loadResults)
            {
                if (asset != null)
                {
                    results[key] = asset;
                }
            }

            JCDebug.Log($"[AddressableManager] 배치 로드 완료: {results.Count}/{keys.Count}");
            return results;
        }

        private async UniTask<(string key, T asset)> LoadAssetWithKeyAsync<T>(string key, CancellationToken cancellationToken) where T : UnityEngine.Object
        {
            var asset = await LoadAssetAsync<T>(key, cancellationToken);
            return (key, asset);
        }

        /// <summary>
        /// 레이블로 에셋들을 로드합니다
        /// </summary>
        public async UniTask<List<T>> LoadAssetsByLabelAsync<T>(string label, CancellationToken cancellationToken = default) where T : UnityEngine.Object
        {
            try
            {
                if (_logLoadOperations)
                {
                    JCDebug.Log($"[AddressableManager] 레이블 로드 시작: {label}");
                }

                var handle = Addressables.LoadAssetsAsync<T>(label, null);
                var labelKey = $"label_{label}";
                _activeHandles[labelKey] = handle;

                var results = await handle.ToUniTask(cancellationToken: cancellationToken);

                if (_logLoadOperations)
                {
                    JCDebug.Log($"[AddressableManager] 레이블 로드 완료: {label}, 개수: {results.Count}");
                }

                return new List<T>(results);
            }
            catch (Exception ex)
            {
                JCDebug.Log($"[AddressableManager] 레이블 로드 실패: {label} - {ex.Message}", JCDebug.LogLevel.Error);
                return new List<T>();
            }
        }

        #endregion

        #region Memory Management

        /// <summary>
        /// 특정 에셋을 언로드합니다
        /// </summary>
        public void UnloadAsset(string key)
        {
            try
            {
                // 참조 카운트 감소
                if (_referenceCounters.TryGetValue(key, out int currentCount))
                {
                    currentCount--;
                    if (currentCount > 0)
                    {
                        _referenceCounters[key] = currentCount;
                        return; // 아직 참조가 남아있음
                    }
                    else
                    {
                        _referenceCounters.Remove(key);
                    }
                }

                // 캐시에서 제거
                if (_assetCache.Remove(key))
                {
                    if (_logCacheOperations)
                    {
                        JCDebug.Log($"[AddressableManager] 캐시에서 제거: {key}");
                    }
                }

                // Addressable 핸들 해제
                if (_activeHandles.TryGetValue(key, out AsyncOperationHandle handle))
                {
                    if (handle.IsValid())
                    {
                        Addressables.Release(handle);
                    }
                    _activeHandles.Remove(key);
                }

                _lastAccessTimes.Remove(key);
                OnAssetUnloaded?.Invoke(key);

                if (_logLoadOperations)
                {
                    JCDebug.Log($"[AddressableManager] 에셋 언로드: {key}");
                }
            }
            catch (Exception ex)
            {
                JCDebug.Log($"[AddressableManager] 에셋 언로드 실패: {key} - {ex.Message}", JCDebug.LogLevel.Error);
            }
        }

        /// <summary>
        /// 인스턴스화된 게임오브젝트를 해제합니다
        /// </summary>
        public bool ReleaseInstance(GameObject instance)
        {
            try
            {
                return Addressables.ReleaseInstance(instance);
            }
            catch (Exception ex)
            {
                JCDebug.Log($"[AddressableManager] 인스턴스 해제 실패: {ex.Message}", JCDebug.LogLevel.Error);
                return false;
            }
        }

        /// <summary>
        /// 모든 에셋을 언로드합니다
        /// </summary>
        public async UniTask UnloadAllAssetsAsync()
        {
            try
            {
                JCDebug.Log("[AddressableManager] 모든 에셋 언로드 시작");

                // 모든 핸들 해제
                var handleKeys = new List<string>(_activeHandles.Keys);
                foreach (string key in handleKeys)
                {
                    if (_activeHandles.TryGetValue(key, out AsyncOperationHandle handle))
                    {
                        if (handle.IsValid())
                        {
                            Addressables.Release(handle);
                        }
                    }
                }

                _activeHandles.Clear();
                _assetCache.Clear();
                _referenceCounters.Clear();
                _lastAccessTimes.Clear();

                // Addressables 정리
                await Addressables.CleanBundleCache();

                OnCacheCleared?.Invoke();
                JCDebug.Log("[AddressableManager] 모든 에셋 언로드 완료");
            }
            catch (Exception ex)
            {
                JCDebug.Log($"[AddressableManager] 전체 언로드 실패: {ex.Message}", JCDebug.LogLevel.Error);
            }
        }

        #endregion

        #region Private Methods

        private async UniTask InitializeAddressableSystemAsync(CancellationToken cancellationToken)
        {
            try
            {
                // Addressables 초기화 확인
                var initHandle = Addressables.InitializeAsync();
                await initHandle.ToUniTask(cancellationToken: cancellationToken);

                JCDebug.Log("[AddressableManager] Addressable System 초기화 완료");
            }
            catch (Exception ex)
            {
                JCDebug.Log($"[AddressableManager] Addressable System 초기화 실패: {ex.Message}", JCDebug.LogLevel.Error);
                throw;
            }
        }

        private async UniTask PreloadCriticalAssetsAsync(CancellationToken cancellationToken)
        {
            try
            {
                JCDebug.Log("[AddressableManager] 중요 에셋 프리로드 시작");

                // 키 기반 프리로드
                foreach (string key in _criticalAssetKeys)
                {
                    await LoadAssetAsync<UnityEngine.Object>(key, cancellationToken);
                }

                // AssetReference 기반 프리로드
                foreach (var assetRef in _criticalAssetReferences)
                {
                    await LoadAssetAsync<GameObject>(assetRef, cancellationToken);
                }

                JCDebug.Log($"[AddressableManager] 중요 에셋 프리로드 완료: {_criticalAssetKeys.Count + _criticalAssetReferences.Count}개");
            }
            catch (Exception ex)
            {
                JCDebug.Log($"[AddressableManager] 중요 에셋 프리로드 실패: {ex.Message}", JCDebug.LogLevel.Error);
            }
        }

        private void StartAutoCleanupSystem()
        {
            AutoCleanupLoop().Forget();
        }

        private async UniTaskVoid AutoCleanupLoop()
        {
            while (_isInitialized)
            {
                try
                {
                    await UniTask.Delay(TimeSpan.FromMinutes(5)); // 5분마다 정리

                    CleanupUnusedAssets();
                }
                catch (Exception ex)
                {
                    JCDebug.Log($"[AddressableManager] 자동 정리 오류: {ex.Message}", JCDebug.LogLevel.Error);
                }
            }
        }

        private void CleanupUnusedAssets()
        {
            var currentTime = DateTime.Now;
            var keysToRemove = new List<string>();

            foreach (var kvp in _lastAccessTimes)
            {
                // 10분 이상 사용되지 않은 에셋 정리
                if ((currentTime - kvp.Value).TotalMinutes > 10)
                {
                    keysToRemove.Add(kvp.Key);
                }
            }

            foreach (string key in keysToRemove)
            {
                UnloadAsset(key);
            }

            if (keysToRemove.Count > 0)
            {
                JCDebug.Log($"[AddressableManager] 자동 정리 완료: {keysToRemove.Count}개 에셋");
            }
        }

        private void AddToCache(string key, UnityEngine.Object asset)
        {
            if (_assetCache.Count >= _maxCacheSize)
            {
                // 가장 오래된 에셋 제거
                var oldestKey = GetOldestAccessedKey();
                if (!string.IsNullOrEmpty(oldestKey))
                {
                    _assetCache.Remove(oldestKey);
                    _lastAccessTimes.Remove(oldestKey);
                }
            }

            _assetCache[key] = asset;
            UpdateLastAccessTime(key);

            if (_logCacheOperations)
            {
                JCDebug.Log($"[AddressableManager] 캐시에 추가: {key}");
            }
        }

        private string GetOldestAccessedKey()
        {
            string oldestKey = null;
            DateTime oldestTime = DateTime.MaxValue;

            foreach (var kvp in _lastAccessTimes)
            {
                if (kvp.Value < oldestTime)
                {
                    oldestTime = kvp.Value;
                    oldestKey = kvp.Key;
                }
            }

            return oldestKey;
        }

        private void UpdateReferenceCount(string key, int delta)
        {
            if (_referenceCounters.TryGetValue(key, out int currentCount))
            {
                _referenceCounters[key] = currentCount + delta;
            }
            else
            {
                _referenceCounters[key] = delta;
            }
        }

        private void UpdateLastAccessTime(string key)
        {
            _lastAccessTimes[key] = DateTime.Now;
        }

        #endregion

        #region Debug Methods

        /// <summary>
        /// 현재 상태 정보를 출력합니다
        /// </summary>
        [ContextMenu("상태 정보 출력")]
        public void PrintDebugInfo()
        {
            JCDebug.Log($"[AddressableManager] 상태 정보:\n" +
                       $"  초기화 상태: {_isInitialized}\n" +
                       $"  캐시된 에셋: {_assetCache.Count}/{_maxCacheSize}\n" +
                       $"  활성 핸들: {_activeHandles.Count}\n" +
                       $"  참조 카운터: {_referenceCounters.Count}\n" +
                       $"  캐싱 활성화: {_enableCaching}\n" +
                       $"  자동 정리: {_autoUnloadUnusedAssets}");
        }

        /// <summary>
        /// 캐시된 에셋 목록을 출력합니다
        /// </summary>
        [ContextMenu("캐시 목록 출력")]
        public void PrintCacheInfo()
        {
            JCDebug.Log($"[AddressableManager] 캐시된 에셋 ({_assetCache.Count}개):");
            foreach (var kvp in _assetCache)
            {
                var refCount = _referenceCounters.TryGetValue(kvp.Key, out int count) ? count : 0;
                var lastAccess = _lastAccessTimes.TryGetValue(kvp.Key, out DateTime time) ? time.ToString("HH:mm:ss") : "Unknown";

                JCDebug.Log($"  {kvp.Key}: {kvp.Value?.GetType().Name} (참조: {refCount}, 마지막 접근: {lastAccess})");
            }
        }

        #endregion
    }
}