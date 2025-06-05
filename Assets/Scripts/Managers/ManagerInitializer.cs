using Metamorph.Managers;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using CustomDebug;

/// <summary>
/// 게임 전체 매니저 시스템을 초기화하고 관리하는 클래스
/// </summary>
public class ManagerInitializer : SingletonManager<ManagerInitializer>
{
    [Header("Configuration")]
    [SerializeField] private bool _initializeOnAwake = true;
    [SerializeField] private bool _logInitialization = true;

    // 매니저들을 보관하는 사전
    private Dictionary<Type, SingletonManager<MonoBehaviour>> _managers
        = new Dictionary<Type, SingletonManager<MonoBehaviour>>();

    // 매니저 부모 오브젝트
    private GameObject _managerParent;

    // 초기화 완료 플래그
    private bool _isInitialized = false;
    public bool IsInitialized => _isInitialized;

    // 매니저 초기화 완료 이벤트
    public event Action OnManagersInitialized;

    protected override void Awake()
    {
        base.Awake();
        DontDestroyOnLoad(gameObject);

        // Awake에서 자동 초기화 옵션
        if (_initializeOnAwake)
        {
            InitializeManagers();
        }

        // 씬 전환 이벤트 구독
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    /// <summary>
    /// 모든 매니저 초기화
    /// </summary>
    public void InitializeManagers()
    {
        if (_isInitialized) return;

        _logMessage("매니저 시스템 초기화 시작...");

        // 1. 매니저 부모 오브젝트 생성
        _managerParent = new GameObject("-----MANAGERS-----");
        DontDestroyOnLoad(_managerParent);

        // 2. 매니저 생성 및 초기화
        InitializeCoreManagers();
        InitializeGameplayManagers();
        InitializeUIManagers();
        InitializeAudioManagers();

        // 3. 모든 매니저를 적절한 부모 아래에 배치
        OrganizeManagers();

        _isInitialized = true;
        _logMessage("매니저 시스템 초기화 완료!");

        // 이벤트 발생
        OnManagersInitialized?.Invoke();
    }

    /// <summary>
    /// 핵심 시스템 매니저 초기화
    /// </summary>
    private void InitializeCoreManagers()
    {
        // 코어 시스템 매니저들 (데이터, 세이브, 네트워크 등)
        CreateManager<SkillRemappingSystem>("Core");
        //CreateManager<GameManager>("Core");
        //CreateManager<SaveManager>("Core");
        //CreateManager<DataManager>("Core");
        // 필요한 다른 코어 매니저들...
    }

    /// <summary>
    /// 게임플레이 관련 매니저 초기화
    /// </summary>
    private void InitializeGameplayManagers()
    {
        // 게임플레이 관련 매니저들
        CreateManager<FormManager>("Gameplay");
        CreateManager<SkillManager>("Gameplay");
        //CreateManager<EnemyManager>("Gameplay");
        //CreateManager<LevelManager>("Gameplay");
        // 필요한 다른 게임플레이 매니저들...
    }

    /// <summary>
    /// UI 관련 매니저 초기화
    /// </summary>
    private void InitializeUIManagers()
    {
        // UI 관련 매니저들
        //CreateManager<UIManager>("UI");
        //CreateManager<PopupManager>("UI");
        // 필요한 다른 UI 매니저들...
    }

    /// <summary>
    /// 오디오 관련 매니저 초기화
    /// </summary>
    private void InitializeAudioManagers()
    {
        // 오디오 관련 매니저들
        //CreateManager<AudioManager>("Audio");
        //CreateManager<MusicManager>("Audio");
        // 필요한 다른 오디오 매니저들...
    }

    /// <summary>
    /// 매니저 생성 및 등록
    /// </summary>
    private T CreateManager<T>(string category) where T : SingletonManager<T>
    {
        Type managerType = typeof(T);

        // 이미 존재하는 매니저 찾기
        T manager = FindAnyObjectByType<T>();

        // 없으면 새로 생성
        if (manager == null)
        {
            GameObject managerObj = new GameObject($"{managerType.Name}");
            manager = managerObj.AddComponent<T>();
            manager.BroadcastMessage("OnCreated", SendMessageOptions.DontRequireReceiver);
            _logMessage($"{managerType.Name} 생성됨");
        }
        else
        {
            _logMessage($"{managerType.Name} 이미 존재함");
        }

        // 카테고리별 속성 정보 저장 (나중에 구조화하기 위함)
        manager.gameObject.AddComponent<ManagerCategoryTag>().Category = category;


        return manager;
    }

    /// <summary>
    /// 매니저들을 계층적으로 구성
    /// </summary>
    private void OrganizeManagers()
    {
        // 카테고리별 부모 오브젝트 생성
        Dictionary<string, Transform> categoryParents = new Dictionary<string, Transform>();

        // 카테고리별로 매니저 구성
        var allManagers = FindObjectsByType<ManagerCategoryTag>(FindObjectsSortMode.None);

        foreach (var managerTag in allManagers)
        {
            string category = managerTag.Category;

            // 카테고리 부모 없으면 생성
            if (!categoryParents.TryGetValue(category, out Transform categoryParent))
            {
                GameObject categoryObj = new GameObject($"--{category} Managers--");
                categoryObj.transform.SetParent(_managerParent.transform);
                categoryParent = categoryObj.transform;
                categoryParents[category] = categoryParent;
            }

            // 매니저를 해당 카테고리 아래로 이동
            managerTag.transform.SetParent(categoryParent);
        }
    }

    /// <summary>
    /// 씬 전환 시 호출되는 메서드
    /// </summary>
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        _logMessage($"씬 전환 감지: {scene.name}");

        // 씬 전환 후 필요한 작업 수행
        // 예: UI 리셋, 특정 매니저 업데이트 등
    }

    /// <summary>
    /// 특정 매니저 가져오기
    /// </summary>
    public MonoBehaviour GetManager(Type managerType)
    {
        // 타입 기반으로 찾아서 반환
        return FindAnyObjectByType(managerType) as MonoBehaviour;
    }

    /// <summary>
    /// 로그 출력 유틸리티
    /// </summary>
    private void _logMessage(string message)
    {
        if (_logInitialization)
        {
            JCDebug.Log($"[ManagerInitializer] {message}");
        }
    }

    // 앱 종료 시 모든 매니저 정리
    protected void OnApplicationQuit()
    {
        // 모든 매니저 참조 정리
        foreach (var managerObj in _managers.Values)
        {
            if (managerObj != null)
            {
                // 여기서 새 오브젝트 생성하지 않도록 주의
                // 정리 작업만 수행
            }
        }

        // 매니저 딕셔너리 정리
        _managers.Clear();
    }

    protected override void OnDestroy()
    {
        // 이벤트 구독 해제
        SceneManager.sceneLoaded -= OnSceneLoaded;

        // 부모 클래스의 OnDestroy 호출
        base.OnDestroy();
    }
}

/// <summary>
/// 매니저 카테고리 태그 컴포넌트
/// </summary>
public class ManagerCategoryTag : MonoBehaviour
{
    [SerializeField]
    public string Category { get; set; }
}