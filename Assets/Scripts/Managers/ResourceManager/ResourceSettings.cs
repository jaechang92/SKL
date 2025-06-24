using System;
using System.Collections.Generic;
using UnityEngine;

namespace Metamorph.Managers
{
    /// <summary>
    /// 리소스 관리 설정을 담는 ScriptableObject
    /// 프리로드 리스트, 캐시 설정, 메모리 관리 옵션 등을 정의
    /// </summary>
    [CreateAssetMenu(fileName = "ResourceSettings", menuName = "Metamorph/Managers/Resource Settings")]
    public class ResourceSettings : ScriptableObject
    {
        [Header("== System Settings ==")]
        [Tooltip("Addressables 시스템 사용 여부")]
        public bool useAddressables = true;

        [Tooltip("리소스 캐싱 활성화")]
        public bool enableCaching = true;

        [Tooltip("게임 시작 시 필수 리소스 프리로드")]
        public bool enablePreloading = true;

        [Header("== Cache Settings ==")]
        [Tooltip("최대 캐시 항목 수")]
        [Range(100, 5000)]
        public int maxCacheSize = 1000;

        [Tooltip("캐시 타임아웃 (초)")]
        [Range(60, 1800)]
        public float cacheTimeoutSeconds = 300f; // 5분

        [Tooltip("캐시 정리 주기 (초)")]
        [Range(10, 300)]
        public float cacheCleanupInterval = 60f;

        [Header("== Loading Settings ==")]
        [Tooltip("비동기 로딩 사용")]
        public bool loadResourcesAsync = true;

        [Tooltip("동시 로딩 최대 개수")]
        [Range(1, 50)]
        public int maxConcurrentLoads = 10;

        [Tooltip("로딩 타임아웃 (초)")]
        [Range(5, 120)]
        public float loadTimeoutSeconds = 30f;

        [Header("== Memory Management ==")]
        [Tooltip("자동 메모리 관리 활성화")]
        public bool enableMemoryManagement = true;

        [Tooltip("메모리 사용량 임계치 (MB)")]
        [Range(128, 2048)]
        public float memoryThresholdMB = 512f;

        [Tooltip("GC 체크 주기 (초)")]
        [Range(10, 300)]
        public float gcCheckIntervalSeconds = 30f;

        [Tooltip("강제 GC 실행 임계치 (MB)")]
        [Range(256, 4096)]
        public float forceGCThresholdMB = 1024f;

        [Header("== Preload Lists ==")]
        [Tooltip("필수 리소스 목록 (Resources 폴더 경로)")]
        public List<PreloadResourceEntry> essentialResources = new List<PreloadResourceEntry>();

        [Tooltip("Addressables 키 목록")]
        public List<PreloadAddressableEntry> addressableResources = new List<PreloadAddressableEntry>();

        [Tooltip("씬별 프리로드 설정")]
        public List<ScenePreloadSettings> scenePreloadSettings = new List<ScenePreloadSettings>();

        [Header("== Advanced Settings ==")]
        [Tooltip("로딩 진행상황 로그 출력")]
        public bool logResourceOperations = true;

        [Tooltip("상세 메모리 정보 로그")]
        public bool logMemoryDetails = false;

        [Tooltip("리소스 로드 실패 시 재시도 횟수")]
        [Range(0, 5)]
        public int loadRetryCount = 2;

        [Tooltip("재시도 간격 (초)")]
        [Range(0.1f, 5f)]
        public float retryDelaySeconds = 1f;

        [Header("== Performance ==")]
        [Tooltip("프레임당 최대 로딩 작업 수")]
        [Range(1, 20)]
        public int maxLoadsPerFrame = 5;

        [Tooltip("프리로드 시 프레임 분산")]
        public bool distributePreloadAcrossFrames = true;

        [Tooltip("로우 우선순위 리소스 지연 시간 (초)")]
        [Range(0f, 2f)]
        public float lowPriorityDelay = 0.1f;

        #region Validation

        private void OnValidate()
        {
            // 설정값 유효성 검사
            maxCacheSize = Mathf.Max(100, maxCacheSize);
            cacheTimeoutSeconds = Mathf.Max(60, cacheTimeoutSeconds);
            loadTimeoutSeconds = Mathf.Max(5, loadTimeoutSeconds);
            memoryThresholdMB = Mathf.Max(128, memoryThresholdMB);

            // 강제 GC 임계치는 메모리 임계치보다 높아야 함
            forceGCThresholdMB = Mathf.Max(memoryThresholdMB + 128, forceGCThresholdMB);

            ValidatePreloadEntries();
        }

        private void ValidatePreloadEntries()
        {
            // 중복 제거 및 유효성 검사
            for (int i = essentialResources.Count - 1; i >= 0; i--)
            {
                if (string.IsNullOrEmpty(essentialResources[i].resourcePath))
                {
                    essentialResources.RemoveAt(i);
                }
            }

            for (int i = addressableResources.Count - 1; i >= 0; i--)
            {
                if (string.IsNullOrEmpty(addressableResources[i].addressableKey))
                {
                    addressableResources.RemoveAt(i);
                }
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// 특정 씬의 프리로드 설정을 가져옵니다
        /// </summary>
        public ScenePreloadSettings GetScenePreloadSettings(string sceneName)
        {
            return scenePreloadSettings.Find(s => s.sceneName == sceneName);
        }

        /// <summary>
        /// 우선순위별로 정렬된 필수 리소스 목록을 반환합니다
        /// </summary>
        public List<PreloadResourceEntry> GetSortedEssentialResources()
        {
            var sorted = new List<PreloadResourceEntry>(essentialResources);
            sorted.Sort((a, b) => a.priority.CompareTo(b.priority));
            return sorted;
        }

        /// <summary>
        /// 우선순위별로 정렬된 Addressables 리소스 목록을 반환합니다
        /// </summary>
        public List<PreloadAddressableEntry> GetSortedAddressableResources()
        {
            var sorted = new List<PreloadAddressableEntry>(addressableResources);
            sorted.Sort((a, b) => a.priority.CompareTo(b.priority));
            return sorted;
        }

        /// <summary>
        /// 리소스 유형별 필터링
        /// </summary>
        public List<PreloadResourceEntry> GetResourcesByType(PreloadResourceType type)
        {
            return essentialResources.FindAll(r => r.resourceType == type);
        }

        /// <summary>
        /// 우선순위별 필터링
        /// </summary>
        public List<PreloadResourceEntry> GetResourcesByPriority(PreloadPriority priority)
        {
            return essentialResources.FindAll(r => r.priority == priority);
        }

        #endregion
    }

    #region Data Structures

    /// <summary>
    /// 프리로드할 리소스 정보 (Resources 폴더용)
    /// </summary>
    [System.Serializable]
    public class PreloadResourceEntry
    {
        [Tooltip("리소스 경로 (Resources 폴더 기준)")]
        public string resourcePath;

        [Tooltip("리소스 유형")]
        public PreloadResourceType resourceType;

        [Tooltip("로딩 우선순위")]
        public PreloadPriority priority = PreloadPriority.Normal;

        [Tooltip("설명 (에디터용)")]
        public string description;

        [Tooltip("필수 리소스 여부 (로딩 실패 시 게임 진행 불가)")]
        public bool isRequired = false;

        [Tooltip("게임 시작 시 즉시 로드")]
        public bool loadOnGameStart = true;

        public PreloadResourceEntry()
        {
            resourcePath = "";
            resourceType = PreloadResourceType.Other;
            priority = PreloadPriority.Normal;
            description = "";
            isRequired = false;
            loadOnGameStart = true;
        }

        public PreloadResourceEntry(string path, PreloadResourceType type, PreloadPriority prio = PreloadPriority.Normal)
        {
            resourcePath = path;
            resourceType = type;
            priority = prio;
            description = "";
            isRequired = false;
            loadOnGameStart = true;
        }
    }

    /// <summary>
    /// 프리로드할 Addressables 리소스 정보
    /// </summary>
    [System.Serializable]
    public class PreloadAddressableEntry
    {
        [Tooltip("Addressables 키")]
        public string addressableKey;

        [Tooltip("리소스 유형")]
        public PreloadResourceType resourceType;

        [Tooltip("로딩 우선순위")]
        public PreloadPriority priority = PreloadPriority.Normal;

        [Tooltip("설명 (에디터용)")]
        public string description;

        [Tooltip("필수 리소스 여부")]
        public bool isRequired = false;

        [Tooltip("게임 시작 시 즉시 로드")]
        public bool loadOnGameStart = true;

        [Tooltip("라벨 그룹 (Addressables)")]
        public string labelGroup = "";

        public PreloadAddressableEntry()
        {
            addressableKey = "";
            resourceType = PreloadResourceType.Other;
            priority = PreloadPriority.Normal;
            description = "";
            isRequired = false;
            loadOnGameStart = true;
            labelGroup = "";
        }

        public PreloadAddressableEntry(string key, PreloadResourceType type, PreloadPriority prio = PreloadPriority.Normal)
        {
            addressableKey = key;
            resourceType = type;
            priority = prio;
            description = "";
            isRequired = false;
            loadOnGameStart = true;
            labelGroup = "";
        }
    }

    /// <summary>
    /// 씬별 프리로드 설정
    /// </summary>
    [System.Serializable]
    public class ScenePreloadSettings
    {
        [Tooltip("씬 이름")]
        public string sceneName;

        [Tooltip("이 씬에서만 사용할 리소스 목록")]
        public List<string> sceneSpecificResources = new List<string>();

        [Tooltip("이 씬에서만 사용할 Addressables 키")]
        public List<string> sceneSpecificAddressables = new List<string>();

        [Tooltip("씬 로드 시 프리로드 실행")]
        public bool preloadOnSceneLoad = true;

        [Tooltip("씬 언로드 시 리소스 해제")]
        public bool unloadOnSceneUnload = true;

        public ScenePreloadSettings()
        {
            sceneName = "";
            sceneSpecificResources = new List<string>();
            sceneSpecificAddressables = new List<string>();
            preloadOnSceneLoad = true;
            unloadOnSceneUnload = true;
        }

        public ScenePreloadSettings(string scene)
        {
            sceneName = scene;
            sceneSpecificResources = new List<string>();
            sceneSpecificAddressables = new List<string>();
            preloadOnSceneLoad = true;
            unloadOnSceneUnload = true;
        }
    }

    /// <summary>
    /// 리소스 유형 분류
    /// </summary>
    public enum PreloadResourceType
    {
        [Tooltip("텍스처/스프라이트")]
        Texture,
        [Tooltip("오디오 클립")]
        Audio,
        [Tooltip("프리팹")]
        Prefab,
        [Tooltip("애니메이션")]
        Animation,
        [Tooltip("머티리얼")]
        Material,
        [Tooltip("폰트")]
        Font,
        [Tooltip("UI 에셋")]
        UI,
        [Tooltip("스크립터블 오브젝트")]
        ScriptableObject,
        [Tooltip("기타")]
        Other
    }

    /// <summary>
    /// 프리로드 우선순위
    /// </summary>
    public enum PreloadPriority
    {
        [Tooltip("최고 우선순위 (게임 진행 필수)")]
        Critical = 0,
        [Tooltip("높은 우선순위 (주요 시스템)")]
        High = 1,
        [Tooltip("일반 우선순위")]
        Normal = 2,
        [Tooltip("낮은 우선순위 (백그라운드 로딩)")]
        Low = 3
    }

    public enum ResourceSource
    {
        Resources,
        Addressables,
        StreamingAssets,
        Network
    }

    [System.Serializable]
    public class ResourceLoadInfo
    {
        public string path;
        public Type resourceType;
        public ResourceSource source;
        public DateTime loadTime;
        public DateTime lastAccessTime;
        public int accessCount;
        public long fileSizeBytes;
    }

    #endregion
}