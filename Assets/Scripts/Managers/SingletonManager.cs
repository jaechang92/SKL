using CustomDebug;
using Metamorph.Core;
using UnityEngine;

/// <summary>
/// 모든 매니저의 기본 클래스
/// </summary>
public abstract class SingletonManager<T> : MonoBehaviour where T : MonoBehaviour
{
    private static bool _quitting = false;

    private void OnApplicationQuit()
    {
        _quitting = true;
    }

    // 앱 종료 감지
    //[RuntimeInitializeOnLoadMethod]
    //private static void RunOnStart()
    //{
    //    Application.quitting += () => _quitting = true;
    //}

    // 인스턴스 저장 변수
    private static T _instance;

    // 인스턴스 접근 프로퍼티
    public static T Instance
    {
        get
        {        
            // 앱 종료 중이면 null 반환 (중요!)
            if (_quitting)
            {
                return null;
            }

            if (_instance == null)
            {
                // 1. 씬에서 찾기
                _instance = FindAnyObjectByType<T>();

                // 2. 찾지 못했다면 새로 생성
                if (_instance == null)
                {
                    // 매니저 초기화 확인
                    UnifiedGameManager initializer = FindAnyObjectByType<UnifiedGameManager>();

                    // 초기화가 있으면 그것을 통해 생성 요청
                    if (initializer != null && initializer.IsInitialized)
                    {
                        JCDebug.Log($"{typeof(T).Name} 매니저 요청됨 - ManagerInitializer를 통해 생성");
                        
                        _instance = initializer.GetManager<T>();
                        return _instance;
                    }

                    // 초기화가 없거나 초기화되지 않았다면 직접 생성
                    GameObject obj = new GameObject($"__{typeof(T).Name}");
                    _instance = obj.AddComponent<T>();

                    JCDebug.Log($"{typeof(T).Name} 매니저 자동 생성됨");

                    // 초기화 메서드 호출
                    (_instance as SingletonManager<T>)?.OnCreated();

                    // 씬 전환 시에도 유지
                    DontDestroyOnLoad(obj);
                }
            }
            return _instance;
        }
    }

    // 생성 시 초기화 (필요 시 오버라이드)
    protected virtual void OnCreated()
    {
        // 자동 생성 시 필요한 초기화 코드
    }

    // 다중 인스턴스 방지
    protected virtual void Awake()
    {
        if (_instance == null)
        {
            _instance = this as T;
        }
        else if (_instance != this)
        {
            JCDebug.Log($"이미 {typeof(T).Name} 인스턴스가 존재합니다. 중복 오브젝트 제거됨.",JCDebug.LogLevel.Warning);
            Destroy(gameObject);
        }
    }

    // 인스턴스 소멸 시 참조 정리
    protected virtual void OnDestroy()
    {
        // 종료 중이 아닐 때만 인스턴스 참조 제거
        if (!_quitting && _instance == this)
        {
            _instance = null;
        }
    }
}