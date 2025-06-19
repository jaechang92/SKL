using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
using Metamorph.Managers;
using CustomDebug;
using Metamorph.Core.Interfaces;

namespace Metamorph.Initialization
{
    /// <summary>
    /// UniTask 기반 게임 매니저들을 초기화하는 클래스
    /// 기존 ManagerInitializer의 책임을 분리
    /// </summary>
    public class UniTaskGameManagerInitializer : MonoBehaviour, IInitializableAsync
    {
        public string Name => "Game Managers";

        public InitializationPriority Priority { get; set; } = InitializationPriority.Critical;


        public bool IsInitialized { get; private set; }

        [Header("Manager Configuration")]
        [SerializeField] private bool _createManagerHierarchy = true;
        [SerializeField] private float _delayBetweenManagers = 0.1f;

        private GameObject _managerParent;
        private Dictionary<string, Transform> _categoryParents = new Dictionary<string, Transform>();

        public async UniTask InitializeAsync(CancellationToken cancellationToken = default)
        {
            JCDebug.Log("[UniTaskGameManagerInitializer] 게임 매니저 초기화 시작");

            try
            {
                // 1. 매니저 부모 오브젝트 생성
                if (_createManagerHierarchy)
                {
                    CreateManagerHierarchy();
                    await UniTask.Yield(cancellationToken); // 프레임 양보
                }

                // 2. 우선순위별 매니저 초기화
                await InitializeCriticalManagersAsync(cancellationToken);
                await InitializeGameplayManagersAsync(cancellationToken);
                await InitializeUIManagersAsync(cancellationToken);
                await InitializeAudioManagersAsync(cancellationToken);

                // 3. 매니저들 계층적으로 정리
                if (_createManagerHierarchy)
                {
                    OrganizeManagers();
                }

                IsInitialized = true;
                JCDebug.Log("[UniTaskGameManagerInitializer] 게임 매니저 초기화 완료");
            }
            catch (OperationCanceledException)
            {
                JCDebug.Log("[UniTaskGameManagerInitializer] 초기화가 취소됨",JCDebug.LogLevel.Warning);
                throw;
            }
            catch (Exception ex)
            {
                JCDebug.Log($"[UniTaskGameManagerInitializer] 초기화 실패: {ex.Message}", JCDebug.LogLevel.Error);
                throw;
            }
        }

        private void CreateManagerHierarchy()
        {
            _managerParent = new GameObject("-----MANAGERS-----");
            DontDestroyOnLoad(_managerParent);

            // 카테고리별 부모 오브젝트 미리 생성
            CreateCategoryParent("Core");
            CreateCategoryParent("Gameplay");
            CreateCategoryParent("UI");
            CreateCategoryParent("Audio");
        }

        private void CreateCategoryParent(string category)
        {
            GameObject categoryObj = new GameObject($"--{category} Managers--");
            categoryObj.transform.SetParent(_managerParent.transform);
            _categoryParents[category] = categoryObj.transform;
        }

        private async UniTask InitializeCriticalManagersAsync(CancellationToken cancellationToken)
        {
            CreateManager<SkillRemappingSystem>("Core");
            await UniTask.Delay(TimeSpan.FromSeconds(_delayBetweenManagers).Milliseconds, false, PlayerLoopTiming.Update, cancellationToken);
            // 다른 핵심 매니저들 추가 가능
        }

        private async UniTask InitializeGameplayManagersAsync(CancellationToken cancellationToken)
        {
            CreateManager<FormManager>("Gameplay");
            await UniTask.Delay(TimeSpan.FromSeconds(_delayBetweenManagers).Milliseconds, false, PlayerLoopTiming.Update, cancellationToken);

            CreateManager<SkillManager>("Gameplay");
            await UniTask.Delay(TimeSpan.FromSeconds(_delayBetweenManagers).Milliseconds, false, PlayerLoopTiming.Update, cancellationToken);

            CreateManager<LevelManager>("Gameplay");
            await UniTask.Delay(TimeSpan.FromSeconds(_delayBetweenManagers).Milliseconds, false, PlayerLoopTiming.Update, cancellationToken);
        }

        private async UniTask InitializeUIManagersAsync(CancellationToken cancellationToken)
        {
            // UI 매니저들 초기화
            await UniTask.Yield(cancellationToken);
        }

        private async UniTask InitializeAudioManagersAsync(CancellationToken cancellationToken)
        {
            // 오디오 매니저들 초기화
            await UniTask.Yield(cancellationToken);
        }

        private T CreateManager<T>(string category) where T : SingletonManager<T>
        {
            Type managerType = typeof(T);
            T manager = FindAnyObjectByType<T>();

            if (manager == null)
            {
                GameObject managerObj = new GameObject($"{managerType.Name}");
                manager = managerObj.AddComponent<T>();
                manager.BroadcastMessage("OnCreated", SendMessageOptions.DontRequireReceiver);
                JCDebug.Log($"[UniTaskGameManagerInitializer] {managerType.Name} 생성됨");
            }

            // 카테고리 태그 추가
            var categoryTag = manager.gameObject.GetComponent<ManagerCategoryTag>();
            if (categoryTag == null)
            {
                categoryTag = manager.gameObject.AddComponent<ManagerCategoryTag>();
            }
            categoryTag.Category = category;

            return manager;
        }

        private void OrganizeManagers()
        {
            var allManagers = FindObjectsByType<ManagerCategoryTag>(FindObjectsSortMode.None);

            foreach (var managerTag in allManagers)
            {
                if (_categoryParents.TryGetValue(managerTag.Category, out Transform categoryParent))
                {
                    managerTag.transform.SetParent(categoryParent);
                }
            }
        }
    }

    /// <summary>
    /// UniTask 기반 세이브 데이터를 초기화하는 클래스
    /// </summary>
    public class UniTaskSaveDataInitializer : MonoBehaviour, IInitializableAsync
    {
        public string Name => "Save Data";

        public InitializationPriority Priority { get; set; } = InitializationPriority.High;


        public bool IsInitialized { get; private set; }

        [Header("Save Data Configuration")]
        [SerializeField] private bool _createDefaultDataIfNotExists = true;
        [SerializeField] private float _loadTimeoutSeconds = 10f;

        public async UniTask InitializeAsync(CancellationToken cancellationToken = default)
        {
            JCDebug.Log("[UniTaskSaveDataInitializer] 세이브 데이터 로드 시작");

            try
            {
                // SaveDataManager가 있는지 확인하고 로드
                var saveManager = FindAnyObjectByType<UniTaskSaveDataManager>();
                if (saveManager != null)
                {
                    // 타임아웃과 함께 로드 실행
                    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_loadTimeoutSeconds));
                    using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

                    await saveManager.LoadPlayerDataAsync(combinedCts.Token);
                }
                else if (_createDefaultDataIfNotExists)
                {
                    JCDebug.Log("[UniTaskSaveDataInitializer] SaveDataManager를 찾을 수 없어 기본 데이터 생성",JCDebug.LogLevel.Warning);
                    await CreateDefaultSaveDataAsync(cancellationToken);
                }
                else
                {
                    throw new Exception("SaveDataManager를 찾을 수 없고 기본 데이터 생성도 비활성화됨");
                }

                IsInitialized = true;
                JCDebug.Log("[UniTaskSaveDataInitializer] 세이브 데이터 로드 완료");
            }
            catch (OperationCanceledException)
            {
                JCDebug.Log("[UniTaskSaveDataInitializer] 세이브 데이터 로드 취소됨", JCDebug.LogLevel.Warning);
                throw;
            }
            catch (Exception ex)
            {
                JCDebug.Log($"[UniTaskSaveDataInitializer] 세이브 데이터 로드 실패: {ex.Message}", JCDebug.LogLevel.Error);
                throw;
            }
        }

        private async UniTask CreateDefaultSaveDataAsync(CancellationToken cancellationToken)
        {
            // 기본 세이브 데이터 생성 로직
            await UniTask.Delay(TimeSpan.FromSeconds(0.5f).Milliseconds, false, PlayerLoopTiming.Update, cancellationToken);
            JCDebug.Log("[UniTaskSaveDataInitializer] 기본 세이브 데이터 생성 완료");
        }
    }

    /// <summary>
    /// UniTask 기반 게임 설정을 초기화하는 클래스
    /// </summary>
    public class UniTaskGameSettingsInitializer : MonoBehaviour, IInitializableAsync
    {
        public string Name => "Game Settings";
        public InitializationPriority Priority { get; set; } = InitializationPriority.High;

        public bool IsInitialized { get; private set; }

        [Header("Settings Configuration")]
        [SerializeField] private bool _loadFromFile = true;
        [SerializeField] private bool _validateSettings = true;

        public async UniTask InitializeAsync(CancellationToken cancellationToken = default)
        {
            JCDebug.Log("[UniTaskGameSettingsInitializer] 게임 설정 로드 시작");

            try
            {
                var settingsManager = FindAnyObjectByType<UniTaskGameSettingsManager>();
                if (settingsManager != null)
                {
                    if (_loadFromFile)
                    {
                        await settingsManager.LoadSettingsAsync(cancellationToken);
                    }

                    if (_validateSettings)
                    {
                        await settingsManager.ValidateSettingsAsync(cancellationToken);
                    }
                }
                else
                {
                    JCDebug.Log("[UniTaskGameSettingsInitializer] GameSettingsManager를 찾을 수 없습니다.", JCDebug.LogLevel.Warning);
                    await LoadDefaultSettingsAsync(cancellationToken);
                }

                IsInitialized = true;
                JCDebug.Log("[UniTaskGameSettingsInitializer] 게임 설정 로드 완료");
            }
            catch (OperationCanceledException)
            {
                JCDebug.Log("[UniTaskGameSettingsInitializer] 게임 설정 로드 취소됨", JCDebug.LogLevel.Warning);
                throw;
            }
            catch (Exception ex)
            {
                JCDebug.Log($"[UniTaskGameSettingsInitializer] 게임 설정 로드 실패: {ex.Message}", JCDebug.LogLevel.Error);
                throw;
            }
        }

        private async UniTask LoadDefaultSettingsAsync(CancellationToken cancellationToken)
        {
            // 기본 설정 로드 로직
            await UniTask.Delay(TimeSpan.FromSeconds(0.3f).Milliseconds, false, PlayerLoopTiming.Update, cancellationToken);
            JCDebug.Log("[UniTaskGameSettingsInitializer] 기본 설정 로드 완료");
        }
    }

    /// <summary>
    /// UniTask 기반 필수 리소스를 프리로드하는 클래스
    /// </summary>
    public class UniTaskResourcePreloader : MonoBehaviour, IInitializableAsync
    {
        public string Name => "Essential Resources";
        public InitializationPriority Priority { get; set; } = InitializationPriority.Normal;

        public bool IsInitialized { get; private set; }

        [Header("Preload Configuration")]
        [SerializeField] private List<string> _essentialResourcePaths = new List<string>();
        [SerializeField] private bool _preloadFromResourceManager = true;
        [SerializeField] private bool _allowParallelLoading = true;
        [SerializeField] private int _maxParallelLoads = 5;

        public async UniTask InitializeAsync(CancellationToken cancellationToken = default)
        {
            JCDebug.Log("[UniTaskResourcePreloader] 필수 리소스 프리로드 시작");

            try
            {
                if (_preloadFromResourceManager)
                {
                    var resourceManager = FindAnyObjectByType<UniTaskResourceManager>();
                    if (resourceManager != null)
                    {
                        await resourceManager.PreloadEssentialResourcesAsync(cancellationToken);
                    }
                    else
                    {
                        JCDebug.Log("[UniTaskResourcePreloader] ResourceManager를 찾을 수 없습니다.", JCDebug.LogLevel.Warning);
                    }
                }

                // 추가 리소스 프리로드
                await PreloadAdditionalResourcesAsync(cancellationToken);

                IsInitialized = true;
                JCDebug.Log("[UniTaskResourcePreloader] 필수 리소스 프리로드 완료");
            }
            catch (OperationCanceledException)
            {
                JCDebug.Log("[UniTaskResourcePreloader] 리소스 프리로드 취소됨", JCDebug.LogLevel.Warning);
                throw;
            }
            catch (Exception ex)
            {
                JCDebug.Log($"[UniTaskResourcePreloader] 리소스 프리로드 실패: {ex.Message}", JCDebug.LogLevel.Error);
                throw;
            }
        }

        private async UniTask PreloadAdditionalResourcesAsync(CancellationToken cancellationToken)
        {
            if (_essentialResourcePaths.Count == 0) return;

            if (_allowParallelLoading)
            {
                // 병렬 로딩
                var semaphore = new SemaphoreSlim(_maxParallelLoads, _maxParallelLoads);
                var tasks = new List<UniTask>();

                foreach (string resourcePath in _essentialResourcePaths)
                {
                    if (!string.IsNullOrEmpty(resourcePath))
                    {
                        tasks.Add(LoadResourceWithSemaphoreAsync(resourcePath, semaphore, cancellationToken));
                    }
                }

                await UniTask.WhenAll(tasks);
            }
            else
            {
                // 순차 로딩
                foreach (string resourcePath in _essentialResourcePaths)
                {
                    if (!string.IsNullOrEmpty(resourcePath))
                    {
                        await LoadSingleResourceAsync(resourcePath, cancellationToken);
                    }
                }
            }
        }

        private async UniTask LoadResourceWithSemaphoreAsync(string resourcePath, SemaphoreSlim semaphore, CancellationToken cancellationToken)
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                await LoadSingleResourceAsync(resourcePath, cancellationToken);
            }
            finally
            {
                semaphore.Release();
            }
        }

        private async UniTask LoadSingleResourceAsync(string resourcePath, CancellationToken cancellationToken)
        {
            var resource = Resources.LoadAsync(resourcePath);
            await resource.ToUniTask(cancellationToken: cancellationToken);

            if (resource.asset != null)
            {
                JCDebug.Log($"[UniTaskResourcePreloader] 리소스 로드 완료: {resourcePath}");
            }
            else
            {
                JCDebug.Log($"[UniTaskResourcePreloader] 리소스 로드 실패: {resourcePath}", JCDebug.LogLevel.Warning);
            }
        }
    }

    /// <summary>
    /// UniTask 기반 네트워크 연결을 초기화하는 클래스
    /// </summary>
    public class UniTaskNetworkInitializer : MonoBehaviour, IInitializableAsync
    {
        public string Name => "Network Connection";

        public InitializationPriority Priority { get; set; } = InitializationPriority.Low;

        public bool IsInitialized { get; private set; }

        [Header("Network Configuration")]
        [SerializeField] private bool _requireNetworkConnection = false;
        [SerializeField] private float _connectionTimeoutSeconds = 10f;
        [SerializeField] private int _connectionRetryCount = 3;
        [SerializeField] private float _retryDelaySeconds = 2f;

        public async UniTask InitializeAsync(CancellationToken cancellationToken = default)
        {
            JCDebug.Log("[UniTaskNetworkInitializer] 네트워크 초기화 시작");

            try
            {
                if (_requireNetworkConnection)
                {
                    await CheckNetworkConnectionAsync(cancellationToken);
                }

                // 네트워크 매니저 초기화 로직 추가 가능
                await InitializeNetworkManagerAsync(cancellationToken);

                IsInitialized = true;
                JCDebug.Log("[UniTaskNetworkInitializer] 네트워크 초기화 완료");
            }
            catch (OperationCanceledException)
            {
                JCDebug.Log("[UniTaskNetworkInitializer] 네트워크 초기화 취소됨", JCDebug.LogLevel.Warning);
                throw;
            }
            catch (Exception ex)
            {
                JCDebug.Log($"[UniTaskNetworkInitializer] 네트워크 초기화 실패: {ex.Message}", JCDebug.LogLevel.Error);
                throw;
            }
        }

        private async UniTask CheckNetworkConnectionAsync(CancellationToken cancellationToken)
        {
            int attempts = 0;
            while (attempts < _connectionRetryCount)
            {
                try
                {
                    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_connectionTimeoutSeconds));
                    using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

                    await WaitForNetworkConnectionAsync(combinedCts.Token);

                    if (Application.internetReachability != NetworkReachability.NotReachable)
                    {
                        JCDebug.Log("[UniTaskNetworkInitializer] 네트워크 연결 확인됨");
                        return;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw; // 전체 취소는 그대로 전파
                }
                catch (OperationCanceledException)
                {
                    // 타임아웃인 경우 재시도
                }

                attempts++;
                if (attempts < _connectionRetryCount)
                {
                    JCDebug.Log($"[UniTaskNetworkInitializer] 네트워크 연결 재시도 {attempts}/{_connectionRetryCount}", JCDebug.LogLevel.Warning);
                    await UniTask.Delay(TimeSpan.FromSeconds(_retryDelaySeconds).Milliseconds, false, PlayerLoopTiming.Update, cancellationToken);
                }
            }

            if (_requireNetworkConnection)
            {
                throw new Exception("네트워크 연결을 확인할 수 없습니다.");
            }
            else
            {
                JCDebug.Log("[UniTaskNetworkInitializer] 네트워크 연결을 확인할 수 없지만 계속 진행합니다.", JCDebug.LogLevel.Warning);
            }
        }

        private async UniTask WaitForNetworkConnectionAsync(CancellationToken cancellationToken)
        {
            while (Application.internetReachability == NetworkReachability.NotReachable)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await UniTask.Delay(TimeSpan.FromSeconds(0.01f).Milliseconds, false, PlayerLoopTiming.Update, cancellationToken);
            }
        }

        private async UniTask InitializeNetworkManagerAsync(CancellationToken cancellationToken)
        {
            // 네트워크 매니저 초기화 로직
            await UniTask.Delay(TimeSpan.FromSeconds(0.2f).Milliseconds, false, PlayerLoopTiming.Update, cancellationToken);
        }
    }
}