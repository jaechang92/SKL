// Assets/Scripts/Player/Components/Movement/PlayerMovement.cs
using UnityEngine;
using Metamorph.Core.Interfaces;
using Metamorph.Player.Components.Stats;
using System.Collections;
using CustomDebug;

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

        [Header("Dash Settings")]
        [SerializeField] private float _dashSpeed = 20f;
        [SerializeField] private float _dashDuration = 0.2f;
        [SerializeField] private float _dashCooldown = 1f;
        [SerializeField] private bool _canDashInAir = true;
        [SerializeField] private int _maxAirDashes = 1;
        [SerializeField] private bool _dashThroughEnemies = true;
        [SerializeField] private AnimationCurve _dashCurve = AnimationCurve.EaseInOut(0, 1, 1, 0);

        [Header("Dash Effects")]
        [SerializeField] private ParticleSystem _dashParticles;
        [SerializeField] private TrailRenderer _dashTrail;
        [SerializeField] private float _dashInvulnerabilityTime = 0.1f;

        [Header("Debug")]
        [SerializeField] private bool _HideDebugInfo = false;
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

        // Dash 상태 변수
        private bool _isDashing;
        private float _dashTimeRemaining;
        private float _dashCooldownTimer;
        private Vector2 _dashDirection;
        private int _airDashesRemaining;
        private float _originalGravityScale;
        private int _originalLayer;


        // 물리 계산용
        private float _horizontalAcceleration;
        private float _horizontalDeceleration;

        // 이벤트
        public System.Action<bool> OnGroundedChanged; // isGrounded
        public System.Action<Vector2> OnMovementChanged; // velocity
        public System.Action OnJumped;
        public System.Action OnLanded;
        public System.Action OnDoubleJumped;
        public System.Action OnDashStarted;
        public System.Action OnDashEnded;
        public System.Action<float> OnDashCooldownChanged; // currentCooldown


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

        // 대쉬 관련 상태
        public bool IsDashing => _isDashing;
        public bool CanDash => !_isDashing && _dashCooldownTimer <= 0 &&
                              (_isGrounded || (_canDashInAir && _airDashesRemaining > 0));
        public float DashCooldownRemaining => _dashCooldownTimer;


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
            UpdateDash();
            UpdateDashCooldown();
        }

        private void FixedUpdate()
        {
            if (!_isDashing) // ← 대시 중이 아닐 때만 일반 이동
            {
                ApplyHorizontalMovement();
            }
            else
            {
                ApplyDashMovement(); // ← 대시 중일 때는 대시 이동
            }

            ApplyVerticalConstraints();

            LogMovementDebugInfo();
            
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
                JCDebug.Log("[PlayerMovement] Rigidbody2D 컴포넌트를 찾을 수 없습니다!",JCDebug.LogLevel.Error);
            }

            if (_playerStats == null)
            {
                JCDebug.Log("[PlayerMovement] PlayerStats 컴포넌트를 찾을 수 없습니다. 기본값을 사용합니다.",JCDebug.LogLevel.Warning);
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

                JCDebug.Log("[PlayerMovement] GroundCheckPoint가 자동으로 생성되었습니다.", JCDebug.LogLevel.Info, _HideDebugInfo);
                
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

        public void Dash(float force)
        {
            RequestDash();
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

        /// <summary>
        /// 대시 요청
        /// </summary>
        public void RequestDash()
        {
            if (!CanDash)
            {

                JCDebug.Log($"[PlayerMovement] 대시 불가 - 쿨다운: {_dashCooldownTimer:F1}s, 공중대시: {_airDashesRemaining}", JCDebug.LogLevel.Info, _HideDebugInfo);
                
                return;
            }

            StartDash();
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
                    ResetAirDashes(); // 공중 대시 횟수 리셋
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


            JCDebug.Log($"[PlayerMovement] 점프 실행! 힘: {jumpForce}", JCDebug.LogLevel.Info, _HideDebugInfo);
            
        }

        private void PerformDoubleJump()
        {
            float jumpForce = (_playerStats?.JumpForce ?? 10f) * 0.8f; // 더블점프는 80% 힘

            _rb.velocity = new Vector2(_rb.velocity.x, jumpForce);
            _hasDoubleJump = false;
            _jumpBufferTimer = 0;

            OnDoubleJumped?.Invoke();

            JCDebug.Log($"[PlayerMovement] 더블점프 실행! 힘: {jumpForce}", JCDebug.LogLevel.Info, _HideDebugInfo);
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

                JCDebug.Log($"[PlayerMovement] 방향 전환: {(_facingRight ? "오른쪽" : "왼쪽")}", JCDebug.LogLevel.Info, _HideDebugInfo);
            }
        }

        #endregion

        #region Private Methods - Dash
        

        /// <summary>
        /// 대시 시작
        /// </summary>
        private void StartDash()
        {
            _isDashing = true;
            _dashTimeRemaining = _dashDuration;
            _dashCooldownTimer = _dashCooldown;

            // 대시 방향 설정 (입력이 없으면 현재 바라보는 방향)
            if (_moveInput.magnitude > 0.1f)
            {
                _dashDirection = _moveInput.normalized;
            }
            else
            {
                _dashDirection = _facingRight ? Vector2.right : Vector2.left;
            }

            // 공중 대시 횟수 감소
            if (!_isGrounded)
            {
                _airDashesRemaining--;
            }

            // 중력 비활성화
            _originalGravityScale = _rb.gravityScale;
            _rb.gravityScale = 0f;

            // 대시 중 무적 (옵션)
            if (_dashInvulnerabilityTime > 0)
            {
                SetDashInvulnerability(true);
            }

            // 이펙트 시작
            StartDashEffects();

            // 이벤트 발생
            OnDashStarted?.Invoke();

            JCDebug.Log($"[PlayerMovement] 대시 시작! 방향: {_dashDirection}", JCDebug.LogLevel.Info, _HideDebugInfo);
        }

        /// <summary>
        /// 대시 업데이트 (Update에서 호출)
        /// </summary>
        private void UpdateDash()
        {
            if (!_isDashing) return;

            _dashTimeRemaining -= Time.deltaTime;

            if (_dashTimeRemaining <= 0)
            {
                EndDash();
            }
        }

        /// <summary>
        /// 대시 쿨다운 업데이트 (Update에서 호출)
        /// </summary>
        private void UpdateDashCooldown()
        {
            if (_dashCooldownTimer > 0)
            {
                _dashCooldownTimer -= Time.deltaTime;
                OnDashCooldownChanged?.Invoke(_dashCooldownTimer);
            }
        }

        /// <summary>
        /// 대시 이동 적용 (FixedUpdate에서 호출)
        /// </summary>
        private void ApplyDashMovement()
        {
            if (!_isDashing) return;

            // 대시 진행도 (0~1)
            float dashProgress = 1f - (_dashTimeRemaining / _dashDuration);

            // 커브를 사용한 속도 조절
            float curveValue = _dashCurve.Evaluate(dashProgress);
            float currentDashSpeed = _dashSpeed * curveValue;

            // 대시 속도 적용
            _rb.velocity = _dashDirection * currentDashSpeed;
        }

        /// <summary>
        /// 대시 종료
        /// </summary>
        private void EndDash()
        {
            _isDashing = false;

            // 중력 복구
            _rb.gravityScale = _originalGravityScale;

            // 대시 후 속도 조절 (급정지 방지)
            float remainingSpeed = _currentHorizontalSpeed * 0.5f; // 50% 속도 유지
            _rb.velocity = new Vector2(remainingSpeed, _rb.velocity.y);

            // 무적 해제
            if (_dashInvulnerabilityTime > 0)
            {
                StartCoroutine(EndDashInvulnerabilityCoroutine());
            }

            // 이펙트 종료
            EndDashEffects();

            // 이벤트 발생
            OnDashEnded?.Invoke();

            JCDebug.Log("[PlayerMovement] 대시 종료", JCDebug.LogLevel.Info, _HideDebugInfo);
        }

        /// <summary>
        /// 대시 무적 설정
        /// </summary>
        private void SetDashInvulnerability(bool invulnerable)
        {
            if (_dashThroughEnemies)
            {
                if (invulnerable)
                {
                    _originalLayer = gameObject.layer;
                    gameObject.layer = LayerMask.NameToLayer("DashInvulnerable"); // 별도 레이어 필요
                }
                else
                {
                    gameObject.layer = _originalLayer;
                }
            }
        }

        /// <summary>
        /// 대시 무적 종료 코루틴
        /// </summary>
        private IEnumerator EndDashInvulnerabilityCoroutine()
        {
            yield return new WaitForSeconds(_dashInvulnerabilityTime);
            SetDashInvulnerability(false);
        }

        /// <summary>
        /// 대시 이펙트 시작
        /// </summary>
        private void StartDashEffects()
        {
            if (_dashParticles != null)
            {
                _dashParticles.Play();
            }

            if (_dashTrail != null)
            {
                _dashTrail.emitting = true;
            }
        }

        /// <summary>
        /// 대시 이펙트 종료
        /// </summary>
        private void EndDashEffects()
        {
            if (_dashParticles != null)
            {
                _dashParticles.Stop();
            }

            if (_dashTrail != null)
            {
                _dashTrail.emitting = false;
            }
        }

        /// <summary>
        /// 공중 대시 횟수 리셋 (착지 시 호출)
        /// </summary>
        private void ResetAirDashes()
        {
            _airDashesRemaining = _maxAirDashes;
        }

        #endregion

        #region Private Methods - Timers & Updates

        private void UpdateTimers()
        {
            // 여기에 필요한 타이머 업데이트 로직 추가
            //if (_showDebugInfo)
            //{
            //    JCDebug.Log($"[PlayerMovement] 타이머 업데이트: " +
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
                JCDebug.Log($"[PlayerMovement] " +
                         $"Grounded: {_isGrounded}, " +
                         $"Speed: {_currentHorizontalSpeed:F2}, " +
                         $"Velocity: {_rb.velocity}, " +
                         $"CanJump: {CanJump}, " +
                         $"CanDoubleJump: {CanDoubleJump}", JCDebug.LogLevel.Info, _HideDebugInfo);
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
            JCDebug.Log($"=== Player Movement Status ===\n" +
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
            JCDebug.Log("[PlayerMovement] 움직임 파라미터가 재계산되었습니다.");
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