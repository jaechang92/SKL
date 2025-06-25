// Assets/Scripts/Managers/ResourceManager/ResourceSettings.cs
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Metamorph.Managers
{
    /// <summary>
    /// 리소스 매니저 설정을 관리하는 ScriptableObject
    /// 프리로드할 리소스, 씬별 설정, 최적화 옵션 등을 설정
    /// </summary>
    [CreateAssetMenu(fileName = "ResourceSettings", menuName = "Metamorph/Managers/Resource Settings")]
    public class ResourceSettings : ScriptableObject
    {
        [Header("Essential Resources")]
        [SerializeField] private List<EssentialResourceEntry> _essentialResources = new List<EssentialResourceEntry>();

        [Header("Scene Preload Settings")]
        [SerializeField] private List<ScenePreloadSettings> _sceneSettings = new List<ScenePreloadSettings>();

        [Header("Memory Management")]
        [SerializeField] private MemoryManagementSettings _memorySettings = new MemoryManagementSettings();

        [Header("Addressables Settings")]
        [SerializeField] private AddressablesSettings _addressablesSettings = new AddressablesSettings();

        [Header("Cache Settings")]
        [SerializeField] private CacheSettings _cacheSettings = new CacheSettings();

        [Header("Performance Settings")]
        [SerializeField] private PerformanceSettings _performanceSettings = new PerformanceSettings();

        #region Public Methods

        /// <summary>
        /// 필수 리소스 목록을 우선순위별로 정렬하여 반환
        /// </summary>
        public List<EssentialResourceEntry> GetSortedEssentialResources()
        {
            return _essentialResources
                .Where(entry => !string.IsNullOrEmpty(entry.resourcePath))
                .OrderBy(entry => (int)entry.priority)
                .ThenBy(entry => entry.loadOrder)
                .ToList();
        }

        /// <summary>
        /// 특정 씬의 프리로드 설정 반환
        /// </summary>
        public ScenePreloadSettings GetScenePreloadSettings(string sceneName)
        {
            return _sceneSettings.FirstOrDefault(settings =>
                string.Equals(settings.sceneName, sceneName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 게임 시작 시 로드할 리소스 목록 반환
        /// </summary>
        public List<string> GetGameStartResources()
        {
            return _essentialResources
                .Where(entry => entry.loadOnGameStart)
                .Select(entry => entry.resourcePath)
                .ToList();
        }

        /// <summary>
        /// 특정 우선순위의 리소스 목록 반환
        /// </summary>
        public List<EssentialResourceEntry> GetResourcesByPriority(ResourcePriority priority)
        {
            return _essentialResources
                .Where(entry => entry.priority == priority)
                .OrderBy(entry => entry.loadOrder)
                .ToList();
        }

        /// <summary>
        /// 씬별 설정 추가 또는 업데이트
        /// </summary>
        public void SetScenePreloadSettings(string sceneName, ScenePreloadSettings settings)
        {
            var existingSettings = _sceneSettings.FirstOrDefault(s => s.sceneName == sceneName);
            if (existingSettings != null)
            {
                int index = _sceneSettings.IndexOf(existingSettings);
                _sceneSettings[index] = settings;
            }
            else
            {
                settings.sceneName = sceneName;
                _sceneSettings.Add(settings);
            }
        }

        /// <summary>
        /// 메모리 임계치 확인
        /// </summary>
        public bool ShouldPerformGarbageCollection(float currentMemoryMB)
        {
            return currentMemoryMB > _memorySettings.memoryThresholdMB;
        }

        /// <summary>
        /// 캐시 제거 대상 확인
        /// </summary>
        public bool ShouldEvictCache(int currentCacheSize)
        {
            return currentCacheSize >= _cacheSettings.maxCacheSize;
        }

        #endregion

        #region Properties

        public MemoryManagementSettings MemorySettings => _memorySettings;
        public AddressablesSettings AddressablesSettings => _addressablesSettings;
        public CacheSettings CacheSettings => _cacheSettings;
        public PerformanceSettings PerformanceSettings => _performanceSettings;
        public int TotalEssentialResources => _essentialResources.Count;
        public int TotalSceneSettings => _sceneSettings.Count;

        #endregion

        #region Editor Utilities

#if UNITY_EDITOR
        /// <summary>
        /// 에디터에서 리소스 경로 유효성 검사
        /// </summary>
        [ContextMenu("Validate Resource Paths")]
        public void ValidateResourcePaths()
        {
            var invalidResources = new List<string>();

            foreach (var entry in _essentialResources)
            {
                if (string.IsNullOrEmpty(entry.resourcePath))
                    continue;

                // Resources 폴더에서 확인
                var resource = Resources.Load(entry.resourcePath);
                if (resource == null)
                {
                    invalidResources.Add(entry.resourcePath);
                }
            }

            if (invalidResources.Count > 0)
            {
                Debug.LogWarning($"[ResourceSettings] 유효하지 않은 리소스 경로들:\n{string.Join("\n", invalidResources)}");
            }
            else
            {
                Debug.Log("[ResourceSettings] 모든 리소스 경로가 유효합니다.");
            }
        }

        /// <summary>
        /// 중복 리소스 경로 확인
        /// </summary>
        [ContextMenu("Check Duplicate Resources")]
        public void CheckDuplicateResources()
        {
            var duplicates = _essentialResources
                .GroupBy(entry => entry.resourcePath)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToList();

            if (duplicates.Count > 0)
            {
                Debug.LogWarning($"[ResourceSettings] 중복된 리소스 경로들:\n{string.Join("\n", duplicates)}");
            }
            else
            {
                Debug.Log("[ResourceSettings] 중복된 리소스가 없습니다.");
            }
        }

        /// <summary>
        /// 설정 요약 정보 출력
        /// </summary>
        [ContextMenu("Print Settings Summary")]
        public void PrintSettingsSummary()
        {
            Debug.Log($"[ResourceSettings] 설정 요약:\n" +
                     $"  필수 리소스: {_essentialResources.Count}개\n" +
                     $"  씬 설정: {_sceneSettings.Count}개\n" +
                     $"  메모리 임계치: {_memorySettings.memoryThresholdMB}MB\n" +
                     $"  캐시 최대 크기: {_cacheSettings.maxCacheSize}개\n" +
                     $"  동시 로딩: {_performanceSettings.maxConcurrentLoads}개");
        }
#endif

        #endregion
    }

    #region Data Structures

    /// <summary>
    /// 필수 리소스 항목 정보
    /// </summary>
    [Serializable]
    public class EssentialResourceEntry
    {
        [Header("Resource Info")]
        public string resourcePath;
        public string description;
        public ResourceType resourceType;

        [Header("Loading Settings")]
        public ResourcePriority priority = ResourcePriority.Normal;
        public int loadOrder = 0;
        public bool loadOnGameStart = false;
        public bool keepInMemory = false;

        [Header("Cache Settings")]
        public bool allowCaching = true;
        public float cacheTimeoutSeconds = 300f; // 5분

        [Header("Platform Settings")]
        public List<RuntimePlatform> platformRestrictions = new List<RuntimePlatform>();

        /// <summary>
        /// 현재 플랫폼에서 로드 가능한지 확인
        /// </summary>
        public bool IsValidForCurrentPlatform()
        {
            if (platformRestrictions.Count == 0)
                return true;

            return platformRestrictions.Contains(Application.platform);
        }
    }

    /// <summary>
    /// 씬별 프리로드 설정
    /// </summary>
    [Serializable]
    public class ScenePreloadSettings
    {
        [Header("Scene Info")]
        public string sceneName;
        public bool preloadOnSceneLoad = true;
        public bool unloadOnSceneExit = true;

        [Header("Scene-Specific Resources")]
        public List<string> sceneSpecificResources = new List<string>();
        public List<string> sceneSpecificAddressables = new List<string>();

        [Header("Preload Settings")]
        public PreloadStrategy preloadStrategy = PreloadStrategy.Immediate;
        public float preloadDelay = 0f;
        public int maxConcurrentPreloads = 5;

        [Header("Memory Management")]
        public bool allowSceneResourceCaching = true;
        public float sceneResourceTimeoutSeconds = 600f; // 10분
    }

    /// <summary>
    /// 메모리 관리 설정
    /// </summary>
    [Serializable]
    public class MemoryManagementSettings
    {
        [Header("Memory Thresholds")]
        [Range(64, 2048)]
        public float memoryThresholdMB = 512f;
        [Range(32, 1024)]
        public float criticalMemoryThresholdMB = 768f;

        [Header("Garbage Collection")]
        public bool enableAutoGC = true;
        [Range(10, 300)]
        public float gcCheckInterval = 30f;
        public GCCollectionMode gcMode = GCCollectionMode.Optimized;

        [Header("Memory Monitoring")]
        public bool enableMemoryProfiling = false;
        public float memoryCheckInterval = 5f;
        public bool logMemoryWarnings = true;
    }

    /// <summary>
    /// Addressables 시스템 설정
    /// </summary>
    [Serializable]
    public class AddressablesSettings
    {
        [Header("Initialization")]
        public bool autoInitialize = true;
        public bool initializeOnGameStart = true;

        [Header("Download Settings")]
        public bool enableProgressCallbacks = true;
        public float downloadTimeoutSeconds = 30f;
        public int maxRetryAttempts = 3;

        [Header("Catalog Settings")]
        public bool checkForCatalogUpdates = true;
        public float catalogCheckInterval = 300f; // 5분

        [Header("Bundle Management")]
        public bool enableBundleCaching = true;
        public long maxBundleCacheSize = 1024 * 1024 * 1024; // 1GB
        public bool clearBundleCacheOnVersionChange = true;
    }

    /// <summary>
    /// 캐시 시스템 설정
    /// </summary>
    [Serializable]
    public class CacheSettings
    {
        [Header("Cache Size")]
        [Range(10, 1000)]
        public int maxCacheSize = 200;
        [Range(1, 100)]
        public int cacheEvictionBatchSize = 20;

        [Header("Cache Strategy")]
        public CacheEvictionStrategy evictionStrategy = CacheEvictionStrategy.LeastRecentlyUsed;
        public bool respectReferenceCount = true;

        [Header("Cache Persistence")]
        public bool enablePersistentCache = false;
        public string persistentCachePath = "ResourceCache";
        public float persistentCacheValidityHours = 24f;

        [Header("Cache Monitoring")]
        public bool enableCacheStatistics = true;
        public bool logCacheOperations = false;
    }

    /// <summary>
    /// 성능 관련 설정
    /// </summary>
    [Serializable]
    public class PerformanceSettings
    {
        [Header("Loading Performance")]
        [Range(1, 20)]
        public int maxConcurrentLoads = 5;
        [Range(0.01f, 1f)]
        public float loadingTimeSliceSeconds = 0.1f;

        [Header("Frame Rate Management")]
        public bool enableFrameRateControl = true;
        public int targetFrameRate = 60;
        public bool pauseLoadingOnLowFrameRate = true;
        public int lowFrameRateThreshold = 30;

        [Header("Background Loading")]
        public bool enableBackgroundLoading = true;
        public ThreadPriority backgroundThreadPriority = ThreadPriority.BelowNormal;

        [Header("LOD Management")]
        public bool enableLODOptimization = true;
        public float lodSwitchDistance = 100f;
        public int maxHighDetailObjects = 50;
    }

    #endregion

    #region Enums

    /// <summary>
    /// 리소스 타입 분류
    /// </summary>
    public enum ResourceType
    {
        Texture,
        Audio,
        Prefab,
        Material,
        Animation,
        ScriptableObject,
        Scene,
        Font,
        Shader,
        Other
    }

    /// <summary>
    /// 리소스 로딩 우선순위
    /// </summary>
    public enum ResourcePriority
    {
        Critical = 0,   // 게임 시작에 필수
        High = 1,       // 첫 씬에서 필요
        Normal = 2,     // 일반적인 리소스
        Low = 3,        // 지연 로딩 가능
        Background = 4  // 백그라운드에서 로딩
    }

    /// <summary>
    /// 프리로드 전략
    /// </summary>
    public enum PreloadStrategy
    {
        Immediate,      // 즉시 로딩
        Delayed,        // 지연 로딩
        Progressive,    // 점진적 로딩
        OnDemand        // 요청 시 로딩
    }

    /// <summary>
    /// 캐시 제거 전략
    /// </summary>
    public enum CacheEvictionStrategy
    {
        LeastRecentlyUsed,  // 가장 오래된 것부터
        LeastFrequentlyUsed, // 가장 적게 사용된 것부터
        FirstInFirstOut,    // 먼저 들어온 것부터
        Random,             // 무작위
        BySize              // 크기 기준
    }

    #endregion
}