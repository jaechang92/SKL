using CustomDebug;
using Cysharp.Threading.Tasks;
using Metamorph.Initialization;
using Metamorph.Managers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;

/// <summary>
/// 모든 매니저의 등록, 초기화, 관리를 담당하는 통합 매니저 시스템
/// </summary>
public class UnifiedGameManager : SingletonManager<UnifiedGameManager>
{
    #region Fields

    [Header("Initialization Settings")]
    [SerializeField] private bool _autoInitializeOnStart = true;
    [SerializeField] private float _delayBetweenManagers = 0.1f;
    [SerializeField] private bool _createManagerHierarchy = true;
    [SerializeField] private bool _logInitializationProgress = true;

    // 매니저 저장소
    private readonly Dictionary<Type, MonoBehaviour> _registeredManagers = new();
    private readonly Dictionary<Type, InitializationPriority> _managerPriorities = new();
    private readonly List<IInitializableAsync> _initializableManagers = new();

    // 인스펙터용 매니저 목록 (SerializeField로 인스펙터에 표시)
    [Header("Registered Managers List")]
    [SerializeField] private List<ManagerInfo> _managersList = new List<ManagerInfo>();

    // 초기화 상태
    private bool _isInitialized = false;
    private bool _isInitializing = false;
    private CancellationTokenSource _initializationCTS;

    // 매니저 계층 구조
    private Transform _managerParent;
    private readonly Dictionary<InitializationPriority, Transform> _priorityParents = new();

    // 이벤트
    public event Action<InitializationPriority> OnPriorityGroupStarted;
    public event Action<InitializationPriority> OnPriorityGroupCompleted;
    public event Action<Type, bool> OnManagerInitialized; // Type, Success
    public event Action OnAllManagersInitialized;

    #endregion

    #region Properties

    public bool IsInitialized => _isInitialized;
    public bool IsInitializing => _isInitializing;
    public int RegisteredManagerCount => _registeredManagers.Count;

    #endregion

    #region Unity Lifecycle

    protected override void Awake()
    {
        base.Awake();
        InitializeManagerHierarchy();
        RegisterAllManager();
    }

    private async void Start()
    {
        if (_autoInitializeOnStart && !_isInitializing && !_isInitialized)
        {
            await InitializeAllManagersAsync();
        }
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        _initializationCTS?.Cancel();
        _initializationCTS?.Dispose();
        CleanupAllManagersAsync().Forget(); // 비동기 정리
    }
    #endregion

    #region Manager Registration

    public void RegisterAllManager()
    {
        CreateAndRegisterManager<IntroManager>(InitializationPriority.Critical);
        CreateAndRegisterManager<PlayerDataManager>(InitializationPriority.Core);
        CreateAndRegisterManager<SaveManager>(InitializationPriority.Gameplay);
        CreateAndRegisterManager<LoadManager>(InitializationPriority.Gameplay);
        CreateAndRegisterManager<GameResourceManager>(InitializationPriority.Critical);

        //CreateAndRegisterManager<GameSceneTransitionManager>(InitializationPriority.Core);

        //CreateAndRegisterManager<GameSettingsManager>(InitializationPriority.Gameplay);
        CreateAndRegisterManager<SkillManager>(InitializationPriority.Gameplay);
        CreateAndRegisterManager<FormManager>(InitializationPriority.Gameplay);

        CreateAndRegisterManager<AudioManager>(InitializationPriority.Audio);
        CreateAndRegisterManager<MusicManager>(InitializationPriority.Audio);

        //CreateAndRegisterManager<UIManager>(InitializationPriority.UI);
        //CreateAndRegisterManager<PopupManager>(InitializationPriority.UI);

    }

    /// <summary>
    /// 매니저를 등록하고 우선순위를 설정합니다
    /// </summary>
    /// <typeparam name="T">매니저 타입</typeparam>
    /// <param name="manager">매니저 인스턴스</param>
    /// <param name="priority">초기화 우선순위</param>
    public void RegisterManager<T>(T manager, InitializationPriority priority)
        where T : MonoBehaviour, IInitializableAsync
    {
        if (manager == null)
        {
            JCDebug.Log($"[UnifiedGameManager] Null 매니저 등록 시도: {typeof(T).Name}", JCDebug.LogLevel.Error);
            return;
        }

        Type managerType = typeof(T);

        // 이미 등록된 매니저 체크
        if (_registeredManagers.ContainsKey(managerType))
        {
            JCDebug.Log($"[UnifiedGameManager] 이미 등록된 매니저: {managerType.Name}", JCDebug.LogLevel.Warning);
            return;
        }

        // 매니저 등록
        _registeredManagers[managerType] = manager;
        _managerPriorities[managerType] = priority;
        _initializableManagers.Add(manager);

        // 계층 구조에 배치
        if (_createManagerHierarchy)
        {
            OrganizeManagerInHierarchy(manager, priority);
        }

        JCDebug.Log($"[UnifiedGameManager] 매니저 등록 완료: {managerType.Name} (Priority: {priority})");
    }

    /// <summary>
    /// 매니저를 자동 생성하고 등록합니다
    /// </summary>
    /// <typeparam name="T">매니저 타입</typeparam>
    /// <param name="priority">초기화 우선순위</param>
    /// <returns>생성된 매니저 인스턴스</returns>
    public T CreateAndRegisterManager<T>(InitializationPriority priority)
        where T : MonoBehaviour, IInitializableAsync
    {
        Type managerType = typeof(T);

        // 이미 등록된 매니저가 있는지 확인
        if (_registeredManagers.ContainsKey(managerType))
        {
            JCDebug.Log($"[UnifiedGameManager] 이미 등록된 매니저: {managerType.Name}", JCDebug.LogLevel.Warning);
            return _registeredManagers[managerType] as T;
        }

        // 매니저 오브젝트 생성
        GameObject managerObj = new GameObject($"__{managerType.Name}");
        T manager = managerObj.AddComponent<T>();

        // 등록
        RegisterManager(manager, priority);

        JCDebug.Log($"[UnifiedGameManager] 매니저 생성 및 등록 완료: {managerType.Name}");
        return manager;
    }

    /// <summary>
    /// 등록된 매니저를 가져옵니다
    /// </summary>
    /// <typeparam name="T">매니저 타입</typeparam>
    /// <returns>매니저 인스턴스 또는 null</returns>
    public T GetManager<T>() where T : MonoBehaviour
    {
        Type managerType = typeof(T);

        if (_registeredManagers.TryGetValue(managerType, out MonoBehaviour manager))
        {
            return manager as T;
        }

        // 등록되지 않은 매니저라면 자동 생성 시도 (IInitializableAsync 구현 매니저만)
        if (typeof(IInitializableAsync).IsAssignableFrom(managerType))
        {
            JCDebug.Log($"[UnifiedGameManager] 미등록 매니저 자동 생성 시도: {managerType.Name}");

            // 기본 우선순위로 생성
            var method = typeof(UnifiedGameManager).GetMethod(nameof(CreateAndRegisterManager));
            var genericMethod = method.MakeGenericMethod(managerType);

            try
            {
                return genericMethod.Invoke(this, new object[] { InitializationPriority.Low }) as T;
            }
            catch (Exception ex)
            {
                JCDebug.Log($"[UnifiedGameManager] 매니저 자동 생성 실패: {managerType.Name}, {ex.Message}", JCDebug.LogLevel.Error);
            }
        }

        return null;
    }

    #endregion

    #region Initialization

    /// <summary>
    /// 모든 등록된 매니저를 우선순위에 따라 초기화합니다
    /// </summary>
    public async UniTask InitializeAllManagersAsync(CancellationToken cancellationToken = default)
    {
        if (_isInitializing || _isInitialized)
        {
            JCDebug.Log("[UnifiedGameManager] 이미 초기화 중이거나 완료됨", JCDebug.LogLevel.Warning);
            return;
        }

        _isInitializing = true;
        _initializationCTS = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            JCDebug.Log($"[UnifiedGameManager] 매니저 초기화 시작 - 총 {_initializableManagers.Count}개");

            // 우선순위별로 그룹화
            var priorityGroups = _initializableManagers
                .GroupBy(m => m.Priority)
                .OrderBy(g => (int)g.Key)
                .ToList();

            foreach (var group in priorityGroups)
            {
                var priority = group.Key;
                var managers = group.ToList();

                OnPriorityGroupStarted?.Invoke(priority);

                if (_logInitializationProgress)
                {
                    JCDebug.Log($"[UnifiedGameManager] {priority} 우선순위 매니저 초기화 시작 ({managers.Count}개)");
                }

                // 동일 우선순위 매니저들을 병렬로 초기화
                var initTasks = managers.Select(async manager =>
                {
                    try
                    {
                        await manager.InitializeAsync(_initializationCTS.Token);
                        OnManagerInitialized?.Invoke(manager.GetType(), true);

                        if (_logInitializationProgress)
                        {
                            JCDebug.Log($"[UnifiedGameManager] {manager.GetType().Name} 초기화 완료");
                        }
                    }
                    catch (Exception ex)
                    {
                        OnManagerInitialized?.Invoke(manager.GetType(), false);
                        JCDebug.Log($"[UnifiedGameManager] {manager.GetType().Name} 초기화 실패: {ex.Message}", JCDebug.LogLevel.Error);
                    }
                }).ToArray();

                await UniTask.WhenAll(initTasks);

                OnPriorityGroupCompleted?.Invoke(priority);

                // 우선순위 그룹 간 딜레이
                if (_delayBetweenManagers > 0)
                {
                    await UniTask.Delay(TimeSpan.FromSeconds(_delayBetweenManagers), cancellationToken: _initializationCTS.Token);
                }
            }

            _isInitialized = true;
            OnAllManagersInitialized?.Invoke();

            JCDebug.Log("[UnifiedGameManager] 모든 매니저 초기화 완료", JCDebug.LogLevel.Success);
        }
        catch (OperationCanceledException)
        {
            JCDebug.Log("[UnifiedGameManager] 매니저 초기화 취소됨", JCDebug.LogLevel.Warning);
            throw;
        }
        catch (Exception ex)
        {
            JCDebug.Log($"[UnifiedGameManager] 매니저 초기화 실패: {ex.Message}", JCDebug.LogLevel.Error);
            throw;
        }
        finally
        {
            _isInitializing = false;
        }
    }

    /// <summary>
    /// 모든 매니저를 정리합니다
    /// </summary>
    public async UniTask CleanupAllManagersAsync()
    {
        JCDebug.Log("[UnifiedGameManager] 매니저 정리 시작");

        var cleanupTasks = _initializableManagers.Select(async manager =>
        {
            try
            {
                await manager.CleanupAsync();
                JCDebug.Log($"[UnifiedGameManager] {manager.GetType().Name} 정리 완료");
            }
            catch (Exception ex)
            {
                JCDebug.Log($"[UnifiedGameManager] {manager.GetType().Name} 정리 실패: {ex.Message}", JCDebug.LogLevel.Error);
            }
        }).ToArray();

        await UniTask.WhenAll(cleanupTasks);

        JCDebug.Log("[UnifiedGameManager] 모든 매니저 정리 완료");
    }

    #endregion

    #region Manager Hierarchy

    private void InitializeManagerHierarchy()
    {
        if (!_createManagerHierarchy) return;

        _managerParent = new GameObject("-----MANAGERS-----").transform;
        DontDestroyOnLoad(_managerParent.gameObject);
        gameObject.transform.SetParent(_managerParent);

        // 우선순위별 부모 오브젝트 생성
        foreach (InitializationPriority priority in Enum.GetValues(typeof(InitializationPriority)))
        {
            GameObject priorityObj = new GameObject($"--{priority} Managers--");
            priorityObj.transform.SetParent(_managerParent);
            _priorityParents[priority] = priorityObj.transform;
        }
    }

    private void OrganizeManagerInHierarchy(MonoBehaviour manager, InitializationPriority priority)
    {
        if (!_createManagerHierarchy || manager == null) return;

        if (_priorityParents.TryGetValue(priority, out Transform parent))
        {
            manager.transform.SetParent(parent);
        }
    }

    #endregion

    #region Utility Methods

    /// <summary>
    /// 등록된 모든 매니저의 정보를 출력합니다
    /// </summary>
    public void PrintManagerInfo()
    {
        JCDebug.Log($"[UnifiedGameManager] 등록된 매니저 정보 ({_registeredManagers.Count}개):");

        foreach (var kvp in _registeredManagers)
        {
            var priority = _managerPriorities[kvp.Key];
            var isInitialized = kvp.Value is IInitializableAsync initializable ? initializable.IsInitialized : false;

            JCDebug.Log($"  {kvp.Key.Name} - Priority: {priority}, Initialized: {isInitialized}");
        }
    }

    /// <summary>
    /// 특정 우선순위의 매니저들이 모두 초기화되었는지 확인합니다
    /// </summary>
    public bool IsePriorityGroupInitialized(InitializationPriority priority)
    {
        return _initializableManagers
            .Where(m => m.Priority == priority)
            .All(m => m.IsInitialized);
    }

    #endregion

    /// <summary>
    /// 인스펙터에서 매니저 정보를 표시하기 위한 클래스
    /// </summary>
    [System.Serializable]
    public class ManagerInfo
    {
        public string managerName;
        public MonoBehaviour managerReference;
        public bool isInitialized;

        public ManagerInfo(string name, MonoBehaviour reference, bool initialized)
        {
            managerName = name;
            managerReference = reference;
            isInitialized = initialized;
        }
    }

    /// <summary>
    /// Dictionary의 매니저들을 List로 변환하여 인스펙터에 표시
    /// </summary>
    [ContextMenu("Update Managers List")]
    private void UpdateManagersList()
    {
        _managersList.Clear();

        foreach (var kvp in _registeredManagers)
        {
            Type managerType = kvp.Key;
            MonoBehaviour managerInstance = kvp.Value;

            if (managerInstance != null)
            {
                // 초기화 상태 확인 (IInitializableAsync 인터페이스가 있다면)
                bool isInitialized = false;
                if (managerInstance is IInitializableAsync initializableManager)
                {
                    isInitialized = initializableManager.IsInitialized;
                }

                ManagerInfo info = new ManagerInfo(
                    managerType.Name,
                    managerInstance,
                    isInitialized
                );

                _managersList.Add(info);
            }
        }

        // Priority별로 정렬후 이름순으로 정렬
        _managersList.Sort((a, b) => 
        {
            InitializationPriority aPriority = _managerPriorities[a.managerReference.GetType()];
            InitializationPriority bPriority = _managerPriorities[b.managerReference.GetType()];
            
            // Priority 비교
            int priorityComparison = aPriority.CompareTo(bPriority);
            if (priorityComparison != 0)
                return priorityComparison;
            // 이름 비교
            return string.Compare(a.managerName, b.managerName);
        });
    }
}