using CustomDebug;
using UnityEngine;

public class CameraController : SingletonManager<CameraController>
{
    [Header("Camera Settings")]
    [SerializeField] private float followSpeed = 5f;
    [SerializeField] private Vector3 offset = new Vector3(0, 0, -10f);

    [Header("Map Boundaries")]
    [SerializeField] private Transform mapBoundary;
    [SerializeField] private BoxCollider2D boundaryCollider;

    private Transform target;
    private Camera cam;
    private CameraBoundaryChecker boundaryChecker;
    private CameraSmoothFollow smoothFollow;
    private CameraEventHandler eventHandler;

    // SmoothDamp용 속도 변수 추가
    private Vector3 velocity = Vector3.zero;

    protected override void Awake()
    {
        base.Awake();
        InitializeComponents();
    }

    void Start()
    {
        SetupBoundaries();
        RegisterEvents();
    }

    private void FixedUpdate()
    {
        if (target != null)
        {
            UpdateCameraPosition();
        }
    }


    protected override void OnDestroy()
    {
        base.OnDestroy();
        UnregisterEvents();
    }

    // 컴포넌트 초기화 (Dependency Injection 개념 적용)
    private void InitializeComponents()
    {
        cam = GetComponent<Camera>();
        if (cam == null)
            cam = Camera.main;

        boundaryChecker = new CameraBoundaryChecker();
        smoothFollow = new CameraSmoothFollow();
        eventHandler = new CameraEventHandler(this);
    }

    // 경계 설정
    private void SetupBoundaries()
    {
        if (boundaryCollider != null)
        {
            boundaryChecker.SetBoundaries(boundaryCollider.bounds, cam);
        }
    }

    // 이벤트 등록 (Observer Pattern)
    private void RegisterEvents()
    {
        eventHandler.RegisterPortalEvents();
    }

    private void UnregisterEvents()
    {
        eventHandler.UnregisterPortalEvents();
    }


    // 메인 카메라 위치 업데이트 로직 SmoothDamp 사용으로 더 부드러운 움직임
    private void UpdateCameraPosition()
    {
        Vector3 desiredPosition = smoothFollow.CalculateDesiredPosition(target.position, offset);
        Vector3 clampedPosition = boundaryChecker.ClampToBoundaries(desiredPosition);

        // SmoothDamp 사용 (Lerp 대신)
        float smoothTime = 1f / followSpeed; // speed에서 smoothTime 계산
        Vector3 newPosition = Vector3.SmoothDamp(transform.position, clampedPosition, ref velocity, smoothTime);
        transform.position = newPosition;
    }

    // 외부 인터페이스
    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
    }

    public void SetTargetImmediate(Transform newTarget, Vector3 position)
    {
        target = newTarget;
        transform.position = boundaryChecker.ClampToBoundaries(position + offset);
    }

    public void UpdateBoundaries(BoxCollider2D newBoundary)
    {
        boundaryCollider = newBoundary;
        SetupBoundaries();
    }
}