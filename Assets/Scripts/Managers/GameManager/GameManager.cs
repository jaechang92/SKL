// ============= GameManager.cs =============
using UnityEngine;
using System.Collections;
using CustomDebug;

/// <summary>
/// 게임 전체를 관리하는 매니저 클래스
/// 의존성: CameraController, Player, PortalManager
/// </summary>
public class GameManager : SingletonManager<GameManager>
{
    [Header("Game References")]
    [SerializeField] private Transform player;
    [SerializeField] private CameraController cameraController;
    [SerializeField] private BoxCollider2D mapBoundary;

    [Header("Game Settings")]
    [SerializeField] private bool autoFindPlayer = true;
    [SerializeField] private bool autoFindCamera = true;
    [SerializeField] private bool debugMode = true;

    // 게임 상태
    public bool IsGameInitialized { get; private set; } = false;
    public Transform Player => player;


    void Start()
    {
        StartCoroutine(InitializeGame());
    }

    /// <summary>
    /// 게임 초기화 코루틴
    /// </summary>
    private IEnumerator InitializeGame()
    {
        JCDebug.Log("GameManager: Starting game initialization...");

        // 1단계: 필수 컴포넌트 찾기
        yield return StartCoroutine(FindRequiredComponents());

        // 2단계: 카메라 시스템 초기화
        yield return StartCoroutine(InitializeCameraSystem());

        // 3단계: 기타 시스템 초기화
        InitializeOtherSystems();

        // 초기화 완료
        IsGameInitialized = true;
        JCDebug.Log("GameManager: Game initialization completed!");
    }

    /// <summary>
    /// 필수 컴포넌트들을 찾는 코루틴
    /// </summary>
    private IEnumerator FindRequiredComponents()
    {
        // 플레이어 자동 탐색
        if (autoFindPlayer && player == null)
        {
            GameObject playerObj = FindFirstObjectByType<PlayerController>().gameObject;
            if (playerObj != null)
            {
                player = playerObj.transform;
                JCDebug.Log($"GameManager: Player found automatically - {player.name}");
            }
            else
            {
                JCDebug.Log("GameManager: Player not found! Please assign player or add 'Player' tag.",JCDebug.LogLevel.Error);
            }
        }

        // 카메라 컨트롤러 자동 탐색
        if (autoFindCamera && cameraController == null)
        {
            cameraController = FindAnyObjectByType<CameraController>();
            if (cameraController != null)
            {
                JCDebug.Log($"GameManager: CameraController found automatically - {cameraController.name}");
            }
            else
            {
                JCDebug.Log("GameManager: CameraController not found!",JCDebug.LogLevel.Error);
            }
        }

        // 맵 경계 자동 탐색
        if (mapBoundary == null)
        {
            GameObject boundaryObj = GameObject.FindGameObjectWithTag("MapBoundary");
            if (boundaryObj != null)
            {
                mapBoundary = boundaryObj.GetComponent<BoxCollider2D>();
                JCDebug.Log($"GameManager: Map boundary found automatically - {boundaryObj.name}");
            }
        }

        yield return null;
    }

    /// <summary>
    /// 카메라 시스템 초기화
    /// </summary>
    private IEnumerator InitializeCameraSystem()
    {
        if (cameraController != null && player != null)
        {
            // 카메라 타겟 설정
            cameraController.SetTarget(player);
            JCDebug.Log("GameManager: Camera target set to player");

            // 맵 경계 설정
            if (mapBoundary != null)
            {
                cameraController.UpdateBoundaries(mapBoundary);
                JCDebug.Log("GameManager: Camera boundaries updated");
            }

            yield return new WaitForSeconds(0.1f);
        }
        else
        {
            JCDebug.Log("GameManager: Cannot initialize camera - missing CameraController or Player!", JCDebug.LogLevel.Error);
        }
    }

    /// <summary>
    /// 기타 시스템들 초기화
    /// </summary>
    private void InitializeOtherSystems()
    {
        // 포탈 시스템은 자동 초기화됨 (PortalManager Awake에서)

        // 추가 시스템들 초기화
        // InitializeUISystem();
        // InitializeAudioSystem();
        // InitializeSaveSystem();
    }

    /// <summary>
    /// 새로운 레벨 로드 시 카메라 재설정
    /// </summary>
    /// <param name="newMapBoundary">새 맵의 경계</param>
    /// <param name="playerStartPosition">플레이어 시작 위치</param>
    public void LoadNewLevel(BoxCollider2D newMapBoundary, Vector3 playerStartPosition)
    {
        if (cameraController != null && player != null)
        {
            // 플레이어 위치 설정
            player.position = playerStartPosition;

            // 카메라 경계 업데이트
            mapBoundary = newMapBoundary;
            cameraController.UpdateBoundaries(newMapBoundary);

            // 카메라 즉시 이동
            cameraController.SetTargetImmediate(player, playerStartPosition);

            JCDebug.Log($"GameManager: New level loaded - Player at {playerStartPosition}");
        }
    }

    /// <summary>
    /// 게임 일시정지/재개
    /// </summary>
    /// <param name="isPaused">일시정지 상태</param>
    public void SetGamePaused(bool isPaused)
    {
        Time.timeScale = isPaused ? 0f : 1f;
        JCDebug.Log($"GameManager: Game {(isPaused ? "paused" : "resumed")}");
    }

    /// <summary>
    /// 디버그 정보 출력
    /// </summary>
    void Update()
    {
        if (debugMode && Input.GetKeyDown(KeyCode.G))
        {
            JCDebug.Log($"GameManager Status - Initialized: {IsGameInitialized}, " +
                     $"Player: {(player != null ? player.name : "None")}, " +
                     $"Camera: {(cameraController != null ? "OK" : "Missing")}");
        }
    }
}