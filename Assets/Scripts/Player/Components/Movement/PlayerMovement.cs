// Assets/Scripts/Player/Components/Movement/PlayerMovement.cs
using UnityEngine;
using Metamorph.Core.Interfaces;
using Metamorph.Player.Components.Stats;

namespace Metamorph.Player.Components.Movement
{
    /// <summary>
    /// 플레이어의 이동을 담당하는 컴포넌트
    /// 수평 이동, 점프, 지면 체크 등을 처리
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class PlayerMovement : MonoBehaviour, IMoveable
    {
        [Header("Ground Detection")]
        [SerializeField] private Transform _groundCheckPoint;
        [SerializeField] private Vector2 _groundCheckSize = new Vector2(0.8f, 0.1f);
        [SerializeField] private LayerMask _groundLayerMask = 1;
        [SerializeField] private float _groundCheckDistance = 0.1f;

        [Header("Movement Settings")]
        [SerializeField] private float _accelerationTime = 0.1f;
        [SerializeField] private float _decelerationTime = 0.1f;
        [SerializeField] private float _airAccelerationMultiplier = 0.6f;
        [SerializeField] private float _maxFallSpeed = -25f;

        [Header("Jump Settings")]
        [SerializeField] private float _coyoteTime = 0.15f;
        [SerializeField] private float _jumpBufferTime = 0.2f;
        [SerializeField] private float _jumpCutMultiplier = 0.5f;
        [SerializeField] private bool _allowDoubleJump = false;

        [Header("Debug")]
        [SerializeField] private bool _showDebugInfo = false;
        [SerializeField] private bool _drawGizmos = true;

        // 컴포넌트 참조
        private Rigidbody2D _rb;
        private PlayerStats _playerStats;
        private SpriteRenderer _spriteRenderer;

        // 이동 상태
        private Vector2 _moveInput;
        private float _currentHorizontalSpeed;
        private bool _facingRight = true;

        // 점프 상태
        private bool _isGrounded;
        private bool _wasGroundedLastFrame;
        private float _lastGroundedTime;
        private float _jumpBufferTimer;
        private bool _jumpRequested;
        private bool _hasDoubleJump;
        private bool _isJumping;

        // 물리 계산용
        private float _horizontalAcceleration;
        private float _horizontalDeceleration;

        // 이벤트
        public System.Action<bool> OnGroundedChanged; // isGrounded
        public System.Action<Vector2> OnMovementChanged; // velocity
        public System.Action OnJumped;
        public System.Action OnLanded;
        public System.Action OnDoubleJumped;

        #region Properties

        public bool IsGrounded => _isGrounded;
        public bool IsMoving => Mathf.Abs(_currentHorizontalSpeed) > 0.1f;
        public bool IsFalling => _rb.velocity.y < -0.1f && !_isGrounded;
        public bool IsRising => _rb.velocity.y > 0.1f;
        public bool FacingRight => _facingRight;
        public Vector2 Velocity => _rb.velocity;
        public float HorizontalSpeed => _currentHorizontalSpeed;

        // 점프 관련 상태
        public bool CanJump => _isGrounded || (_lastGroundedTime + _coyoteTime > Time.time);
        public bool CanDoubleJump => _allowDoubleJump && _hasDoubleJump && !_isGrounded;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            InitializeComponents();
            CalculateMovementParameters();
        }

        private void Start()
        {
            SetupGroundCheckPoint();
        }

        private void Update()
        {
            UpdateGroundCheck();
            UpdateJumpBuffer();
            HandleJumpInput();
            UpdateTimers();
        }

        private void FixedUpdate()
        {
            ApplyHorizontalMovement();
            ApplyVerticalConstraints();

            if (_showDebugInfo)
            {
                LogMovementDebugInfo();
            }
        }

        private void OnDrawGizmos()
        {
            if (_drawGizmos)
            {
                DrawGroundCheckGizmos();
            }
        }

        #endregion

        #region Initialization

        private void InitializeComponents()
        {
            _rb = GetComponent<Rigidbody2D>();
            _playerStats = GetComponent<PlayerStats>();
            _spriteRenderer = GetComponent<SpriteRenderer>();

            if (_rb == null)
            {
                Debug.LogError("[PlayerMovement] Rigidbody2D 컴포넌트를 찾을 수 없습니다!");
            }

            if (_playerStats == null)
            {
                Debug.LogWarning("[PlayerMovement] PlayerStats 컴포넌트를 찾을 수 없습니다. 기본값을 사용합니다.");
            }
        }

        private void SetupGroundCheckPoint()
        {
            if (_groundCheckPoint == null)
            {
                GameObject groundCheck = new GameObject("GroundCheckPoint");
                groundCheck.transform.SetParent(transform);
                groundCheck.transform.localPosition = new Vector3(0, -0.5f, 0);
                _groundCheckPoint = groundCheck.transform;

                if (_showDebugInfo)
                {
                    Debug.Log("[PlayerMovement] GroundCheckPoint가 자동으로 생성되었습니다.");
                }
            }
        }

        private void CalculateMovementParameters()
        {
            // 가속도 계산 (목표 속도에 도달하는 데 걸리는 시간 기반)
            float targetSpeed = _playerStats?.MoveSpeed ?? 5f;
            _horizontalAcceleration = targetSpeed / _accelerationTime;
            _horizontalDeceleration = targetSpeed / _decelerationTime;
        }

        #endregion

        #region IMoveable Implementation

        public void Move(Vector2 direction)
        {
            _moveInput = direction;

            // 방향 전환 처리
            if (direction.x != 0)
            {
                SetFacingDirection(direction.x > 0);
            }
        }

        public void Jump(float force)
        {
            // force 매개변수는 무시하고 PlayerStats의 값 사용
            RequestJump();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// 점프 요청
        /// </summary>
        public void RequestJump()
        {
            _jumpRequested = true;
            _jumpBufferTimer = _jumpBufferTime;
        }

        /// <summary>
        /// 점프 중단 (키를 뗐을 때)
        /// </summary>
        public void CancelJump()
        {
            if (_isJumping && _rb.velocity.y > 0)
            {
                _rb.velocity = new Vector2(_rb.velocity.x, _rb.velocity.y * _jumpCutMultiplier);
            }
        }

        /// <summary>
        /// 즉시 정지
        /// </summary>
        public void Stop()
        {
            _moveInput = Vector2.zero;
            _rb.velocity = new Vector2(0, _rb.velocity.y);
            _currentHorizontalSpeed = 0;
        }

        /// <summary>
        /// 더블점프 활성화/비활성화
        /// </summary>
        public void SetDoubleJumpEnabled(bool enabled)
        {
            _allowDoubleJump = enabled;
            if (!enabled)
            {
                _hasDoubleJump = false;
            }
        }

        /// <summary>
        /// 강제로 지면 상태 설정 (특수 상황용)
        /// </summary>
        public void ForceGroundedState(bool grounded)
        {
            bool wasGrounded = _isGrounded;
            _isGrounded = grounded;

            if (grounded && !wasGrounded)
            {
                OnLanded?.Invoke();
                _hasDoubleJump = _allowDoubleJump;
            }

            if (wasGrounded != _isGrounded)
            {
                OnGroundedChanged?.Invoke(_isGrounded);
            }
        }

        #endregion

        #region Private Methods - Ground Check

        private void UpdateGroundCheck()
        {
            _wasGroundedLastFrame = _isGrounded;

            // Box cast로 더 정확한 지면 감지
            Vector2 checkPosition = _groundCheckPoint.position;
            _isGrounded = Physics2D.OverlapBox(
                checkPosition,
                _groundCheckSize,
                0f,
                _groundLayerMask
            );

            // 지면 상태 변화 감지
            if (_isGrounded != _wasGroundedLastFrame)
            {
                OnGroundedChanged?.Invoke(_isGrounded);

                if (_isGrounded)
                {
                    OnLanded?.Invoke();
                    _hasDoubleJump = _allowDoubleJump; // 더블점프 리셋
                    _isJumping = false;
                }
            }

            // 마지막 지면 접촉 시간 업데이트
            if (_isGrounded)
            {
                _lastGroundedTime = Time.time;
            }
        }

        #endregion

        #region Private Methods - Jump

        private void UpdateJumpBuffer()
        {
            if (_jumpBufferTimer > 0)
            {
                _jumpBufferTimer -= Time.deltaTime;
            }
        }

        private void HandleJumpInput()
        {
            if (_jumpRequested || _jumpBufferTimer > 0)
            {
                if (CanJump)
                {
                    PerformJump();
                }
                else if (CanDoubleJump)
                {
                    PerformDoubleJump();
                }

                _jumpRequested = false;
            }
        }

        private void PerformJump()
        {
            float jumpForce = _playerStats?.JumpForce ?? 10f;

            _rb.velocity = new Vector2(_rb.velocity.x, jumpForce);
            _isJumping = true;
            _jumpBufferTimer = 0;

            OnJumped?.Invoke();

            if (_showDebugInfo)
            {
                Debug.Log($"[PlayerMovement] 점프 실행! 힘: {jumpForce}");
            }
        }

        private void PerformDoubleJump()
        {
            float jumpForce = (_playerStats?.JumpForce ?? 10f) * 0.8f; // 더블점프는 80% 힘

            _rb.velocity = new Vector2(_rb.velocity.x, jumpForce);
            _hasDoubleJump = false;
            _jumpBufferTimer = 0;

            OnDoubleJumped?.Invoke();

            if (_showDebugInfo)
            {
                Debug.Log($"[PlayerMovement] 더블점프 실행! 힘: {jumpForce}");
            }
        }

        #endregion

        #region Private Methods - Movement

        private void ApplyHorizontalMovement()
        {
            float targetSpeed = _moveInput.x * (_playerStats?.MoveSpeed ?? 5f);
            float acceleration = _isGrounded ? _horizontalAcceleration : _horizontalAcceleration * _airAccelerationMultiplier;

            // 부드러운 가속/감속
            if (Mathf.Abs(targetSpeed) > 0.1f)
            {
                // 가속
                _currentHorizontalSpeed = Mathf.MoveTowards(
                    _currentHorizontalSpeed,
                    targetSpeed,
                    acceleration * Time.fixedDeltaTime
                );
            }
            else
            {
                // 감속
                _currentHorizontalSpeed = Mathf.MoveTowards(
                    _currentHorizontalSpeed,
                    0,
                    _horizontalDeceleration * Time.fixedDeltaTime
                );
            }

            _rb.velocity = new Vector2(_currentHorizontalSpeed, _rb.velocity.y);

            // 이동 이벤트 발생
            OnMovementChanged?.Invoke(_rb.velocity);
        }

        private void ApplyVerticalConstraints()
        {
            // 최대 낙하 속도 제한
            if (_rb.velocity.y < _maxFallSpeed)
            {
                _rb.velocity = new Vector2(_rb.velocity.x, _maxFallSpeed);
            }
        }

        private void SetFacingDirection(bool faceRight)
        {
            if (_facingRight != faceRight)
            {
                _facingRight = faceRight;

                // 스프라이트 뒤집기
                if (_spriteRenderer != null)
                {
                    _spriteRenderer.flipX = !_facingRight;
                }

                if (_showDebugInfo)
                {
                    Debug.Log($"[PlayerMovement] 방향 전환: {(_facingRight ? "오른쪽" : "왼쪽")}");
                }
            }
        }

        #endregion

        #region Private Methods - Timers & Updates

        private void UpdateTimers()
        {
            // 여기에 필요한 타이머 업데이트 로직 추가
            //if (_showDebugInfo)
            //{
            //    Debug.Log($"[PlayerMovement] 타이머 업데이트: " +
            //              $"점프 버퍼: {_jumpBufferTimer:F2}, " +
            //              $"마지막 접지 시간: {_lastGroundedTime:F2}");
            //}
        }

        #endregion

        #region Debug & Gizmos

        private void LogMovementDebugInfo()
        {
            if (Time.fixedTime % 1f < Time.fixedDeltaTime) // 1초마다 출력
            {
                Debug.Log($"[PlayerMovement] " +
                         $"Grounded: {_isGrounded}, " +
                         $"Speed: {_currentHorizontalSpeed:F2}, " +
                         $"Velocity: {_rb.velocity}, " +
                         $"CanJump: {CanJump}, " +
                         $"CanDoubleJump: {CanDoubleJump}");
            }
        }

        private void DrawGroundCheckGizmos()
        {
            if (_groundCheckPoint == null) return;

            Gizmos.color = _isGrounded ? Color.green : Color.red;
            Gizmos.DrawWireCube(_groundCheckPoint.position, _groundCheckSize);

            // 코요테 타임 시각화
            if (!_isGrounded && (_lastGroundedTime + _coyoteTime > Time.time))
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireCube(_groundCheckPoint.position, _groundCheckSize * 1.2f);
            }
        }

        #endregion

        #region Context Menu (에디터 전용)

        [ContextMenu("점프 테스트")]
        private void ContextMenuTestJump()
        {
            if (Application.isPlaying)
            {
                RequestJump();
            }
        }

        [ContextMenu("이동 정지")]
        private void ContextMenuStop()
        {
            if (Application.isPlaying)
            {
                Stop();
            }
        }

        [ContextMenu("현재 상태 출력")]
        private void ContextMenuPrintStatus()
        {
            Debug.Log($"=== Player Movement Status ===\n" +
                     $"Grounded: {_isGrounded}\n" +
                     $"Velocity: {_rb?.velocity}\n" +
                     $"Horizontal Speed: {_currentHorizontalSpeed:F2}\n" +
                     $"Facing Right: {_facingRight}\n" +
                     $"Can Jump: {CanJump}\n" +
                     $"Can Double Jump: {CanDoubleJump}\n" +
                     $"Is Moving: {IsMoving}");
        }

        [ContextMenu("움직임 파라미터 재계산")]
        private void ContextMenuRecalculateParameters()
        {
            CalculateMovementParameters();
            Debug.Log("[PlayerMovement] 움직임 파라미터가 재계산되었습니다.");
        }

        #endregion

        #region Public Events Setup

        /// <summary>
        /// 외부에서 이벤트 등록을 위한 메서드
        /// </summary>
        public void RegisterMovementEvents(
            System.Action<bool> onGroundedChanged = null,
            System.Action<Vector2> onMovementChanged = null,
            System.Action onJumped = null,
            System.Action onLanded = null,
            System.Action onDoubleJumped = null)
        {
            if (onGroundedChanged != null) OnGroundedChanged += onGroundedChanged;
            if (onMovementChanged != null) OnMovementChanged += onMovementChanged;
            if (onJumped != null) OnJumped += onJumped;
            if (onLanded != null) OnLanded += onLanded;
            if (onDoubleJumped != null) OnDoubleJumped += onDoubleJumped;
        }

        /// <summary>
        /// 이벤트 등록 해제
        /// </summary>
        public void UnregisterMovementEvents(
            System.Action<bool> onGroundedChanged = null,
            System.Action<Vector2> onMovementChanged = null,
            System.Action onJumped = null,
            System.Action onLanded = null,
            System.Action onDoubleJumped = null)
        {
            if (onGroundedChanged != null) OnGroundedChanged -= onGroundedChanged;
            if (onMovementChanged != null) OnMovementChanged -= onMovementChanged;
            if (onJumped != null) OnJumped -= onJumped;
            if (onLanded != null) OnLanded -= onLanded;
            if (onDoubleJumped != null) OnDoubleJumped -= onDoubleJumped;
        }

        #endregion
    }
}