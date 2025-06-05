// Assets/Scripts/Player/Components/Animation/PlayerAnimator.cs
using UnityEngine;
using Metamorph.Player.Components.Movement;
using Metamorph.Player.Components.Stats;
using System.Collections;

namespace Metamorph.Player.Components.Animation
{
    /// <summary>
    /// 플레이어의 애니메이션을 관리하는 컴포넌트
    /// PlayerMovement와 연동하여 상태에 따른 애니메이션 전환을 처리
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class PlayerAnimator : MonoBehaviour
    {
        [Header("Animation Parameters")]
        [SerializeField] private string _speedParameter = "Speed";
        [SerializeField] private string _velocityXParameter = "VelocityX";
        [SerializeField] private string _velocityYParameter = "VelocityY";
        [SerializeField] private string _isGroundedParameter = "IsGrounded";
        [SerializeField] private string _isMovingParameter = "IsMoving";
        [SerializeField] private string _isFallingParameter = "IsFalling";
        [SerializeField] private string _isRisingParameter = "IsRising";

        [Header("Trigger Parameters")]
        [SerializeField] private string _jumpTrigger = "Jump";
        [SerializeField] private string _doubleJumpTrigger = "DoubleJump";
        [SerializeField] private string _landedTrigger = "Landed";
        [SerializeField] private string _damageTrigger = "Damage";
        [SerializeField] private string _deathTrigger = "Death";
        [SerializeField] private string _transformTrigger = "Transform";

        [Header("Animation Settings")]
        [SerializeField] private float _movementThreshold = 0.1f;
        [SerializeField] private float _animatorDampTime = 0.1f;
        [SerializeField] private bool _useRootMotion = false;

        [Header("Particle Effects")]
        [SerializeField] private ParticleSystem _jumpParticles;
        [SerializeField] private ParticleSystem _landParticles;
        [SerializeField] private ParticleSystem _doubleJumpParticles;
        [SerializeField] private Transform _particleSpawnPoint;

        [Header("Debug")]
        [SerializeField] private bool _showDebugInfo = false;
        [SerializeField] private bool _logAnimationEvents = false;

        // 컴포넌트 참조
        private Animator _animator;
        private PlayerMovement _playerMovement;
        private PlayerStats _playerStats;
        private SpriteRenderer _spriteRenderer;

        // 애니메이션 상태
        private bool _isAnimatingDeath = false;
        private float _lastHealthPercentage = 1f;

        // 애니메이션 이벤트
        public System.Action OnAttackAnimationHit; // 공격 애니메이션의 타격 시점
        public System.Action OnAnimationComplete; // 특정 애니메이션 완료
        public System.Action OnTransformAnimationComplete; // 변신 애니메이션 완료
        public System.Action OnDeathAnimationComplete; // 사망 애니메이션 완료

        #region Properties

        public bool IsAnimatingDeath => _isAnimatingDeath;
        public bool IsAnimating => _animator != null && _animator.GetCurrentAnimatorStateInfo(0).normalizedTime < 1f;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            InitializeComponents();
            SetupAnimator();
        }

        private void Start()
        {
            SubscribeToEvents();
            SetupParticleSpawnPoint();
        }

        private void Update()
        {
            if (_playerMovement != null && _animator != null)
            {
                UpdateAnimationParameters();
            }
        }

        private void OnDestroy()
        {
            UnsubscribeFromEvents();
        }

        #endregion

        #region Initialization

        private void InitializeComponents()
        {
            _animator = GetComponent<Animator>();
            _playerMovement = GetComponent<PlayerMovement>();
            _playerStats = GetComponent<PlayerStats>();
            _spriteRenderer = GetComponent<SpriteRenderer>();

            if (_animator == null)
            {
                Debug.LogError("[PlayerAnimator] Animator 컴포넌트를 찾을 수 없습니다!");
            }

            if (_playerMovement == null)
            {
                Debug.LogWarning("[PlayerAnimator] PlayerMovement 컴포넌트를 찾을 수 없습니다!");
            }

            if (_playerStats == null)
            {
                Debug.LogWarning("[PlayerAnimator] PlayerStats 컴포넌트를 찾을 수 없습니다!");
            }
        }

        private void SetupAnimator()
        {
            if (_animator != null)
            {
                _animator.applyRootMotion = _useRootMotion;

                // 초기 파라미터 설정
                ResetAnimationParameters();
            }
        }

        private void SetupParticleSpawnPoint()
        {
            if (_particleSpawnPoint == null)
            {
                GameObject spawnPoint = new GameObject("ParticleSpawnPoint");
                spawnPoint.transform.SetParent(transform);
                spawnPoint.transform.localPosition = new Vector3(0, -0.5f, 0);
                _particleSpawnPoint = spawnPoint.transform;
            }
        }

        #endregion

        #region Event Subscription

        private void SubscribeToEvents()
        {
            if (_playerMovement != null)
            {
                _playerMovement.OnGroundedChanged += HandleGroundedChanged;
                _playerMovement.OnMovementChanged += HandleMovementChanged;
                _playerMovement.OnJumped += HandleJumped;
                _playerMovement.OnLanded += HandleLanded;
                _playerMovement.OnDoubleJumped += HandleDoubleJumped;
                _playerMovement.OnDashStarted += HandleDashStarted;
                _playerMovement.OnDashEnded += HandleDashEnded;
            }

            if (_playerStats != null)
            {
                _playerStats.OnHealthChanged += HandleHealthChanged;
            }
        }

        private void UnsubscribeFromEvents()
        {
            if (_playerMovement != null)
            {
                _playerMovement.OnGroundedChanged -= HandleGroundedChanged;
                _playerMovement.OnMovementChanged -= HandleMovementChanged;
                _playerMovement.OnJumped -= HandleJumped;
                _playerMovement.OnLanded -= HandleLanded;
                _playerMovement.OnDoubleJumped -= HandleDoubleJumped;
                _playerMovement.OnDashStarted -= HandleDashStarted;
                _playerMovement.OnDashEnded -= HandleDashEnded;
            }

            if (_playerStats != null)
            {
                _playerStats.OnHealthChanged -= HandleHealthChanged;
            }
        }

        #endregion

        #region Animation Parameter Updates

        private void UpdateAnimationParameters()
        {
            if (_isAnimatingDeath) return;

            // 속도 파라미터 업데이트
            float horizontalSpeed = Mathf.Abs(_playerMovement.HorizontalSpeed);
            _animator.SetFloat(_speedParameter, horizontalSpeed, _animatorDampTime, Time.deltaTime);

            // 속도 벡터 파라미터 업데이트
            Vector2 velocity = _playerMovement.Velocity;
            _animator.SetFloat(_velocityXParameter, Mathf.Abs(velocity.x), _animatorDampTime, Time.deltaTime);
            _animator.SetFloat(_velocityYParameter, velocity.y, _animatorDampTime, Time.deltaTime);

            // 상태 파라미터 업데이트
            _animator.SetBool(_isGroundedParameter, _playerMovement.IsGrounded);
            _animator.SetBool(_isMovingParameter, _playerMovement.IsMoving);
            _animator.SetBool(_isFallingParameter, _playerMovement.IsFalling);
            _animator.SetBool(_isRisingParameter, _playerMovement.IsRising);

            if (_showDebugInfo && Time.frameCount % 60 == 0)
            {
                LogAnimationState();
            }
        }

        private void ResetAnimationParameters()
        {
            _animator.SetFloat(_speedParameter, 0f);
            _animator.SetFloat(_velocityXParameter, 0f);
            _animator.SetFloat(_velocityYParameter, 0f);
            _animator.SetBool(_isGroundedParameter, true);
            _animator.SetBool(_isMovingParameter, false);
            _animator.SetBool(_isFallingParameter, false);
            _animator.SetBool(_isRisingParameter, false);
        }

        #endregion

        #region Event Handlers

        private void HandleGroundedChanged(bool isGrounded)
        {
            _animator.SetBool(_isGroundedParameter, isGrounded);

            if (_logAnimationEvents)
            {
                Debug.Log($"[PlayerAnimator] Grounded 상태 변경: {isGrounded}");
            }
        }

        private void HandleMovementChanged(Vector2 velocity)
        {
            // 이미 Update에서 처리하므로 추가 작업 필요시 여기에 구현
        }

        private void HandleJumped()
        {
            _animator.SetTrigger(_jumpTrigger);
            PlayJumpParticles();

            if (_logAnimationEvents)
            {
                Debug.Log("[PlayerAnimator] 점프 애니메이션 트리거");
            }
        }

        private void HandleDashStarted()
        {
            // 대시 애니메이션은 별도로 구현하지 않음
            // 필요시 대시 애니메이션 트리거 추가 가능
            if (_logAnimationEvents)
            {
                Debug.Log("[PlayerAnimator] 대시 시작 (애니메이션 없음)");
            }
        }

        private void HandleDashEnded()
        {
            // 대시 종료 시 특별한 애니메이션은 없음
            if (_logAnimationEvents)
            {
                Debug.Log("[PlayerAnimator] 대시 종료 (애니메이션 없음)");
            }
        }

        private void HandleLanded()
        {
            _animator.SetTrigger(_landedTrigger);
            PlayLandParticles();

            if (_logAnimationEvents)
            {
                Debug.Log("[PlayerAnimator] 착지 애니메이션 트리거");
            }
        }

        private void HandleDoubleJumped()
        {
            _animator.SetTrigger(_doubleJumpTrigger);
            PlayDoubleJumpParticles();

            if (_logAnimationEvents)
            {
                Debug.Log("[PlayerAnimator] 더블점프 애니메이션 트리거");
            }
        }

        private void HandleHealthChanged(float currentHealth, float maxHealth)
        {
            float healthPercentage = maxHealth > 0 ? currentHealth / maxHealth : 0f;

            // 데미지 받았을 때
            if (healthPercentage < _lastHealthPercentage && currentHealth > 0)
            {
                PlayDamageAnimation();
            }

            // 사망했을 때
            if (currentHealth <= 0 && !_isAnimatingDeath)
            {
                PlayDeathAnimation();
            }

            _lastHealthPercentage = healthPercentage;
        }

        #endregion

        #region Public Animation Methods

        /// <summary>
        /// 공격 애니메이션 재생
        /// </summary>
        public void PlayAttackAnimation(int attackIndex = 0)
        {
            if (_isAnimatingDeath) return;

            string attackTrigger = $"Attack{attackIndex}";
            _animator.SetTrigger(attackTrigger);

            if (_logAnimationEvents)
            {
                Debug.Log($"[PlayerAnimator] 공격 애니메이션 재생: {attackTrigger}");
            }
        }

        /// <summary>
        /// 변신 애니메이션 재생
        /// </summary>
        public void PlayTransformAnimation()
        {
            if (_isAnimatingDeath) return;

            _animator.SetTrigger(_transformTrigger);

            if (_logAnimationEvents)
            {
                Debug.Log("[PlayerAnimator] 변신 애니메이션 재생");
            }
        }

        /// <summary>
        /// 특정 애니메이션 상태로 강제 전환
        /// </summary>
        public void ForcePlayAnimation(string stateName, int layer = 0)
        {
            if (_isAnimatingDeath) return;

            _animator.Play(stateName, layer);

            if (_logAnimationEvents)
            {
                Debug.Log($"[PlayerAnimator] 강제 애니메이션 재생: {stateName}");
            }
        }

        /// <summary>
        /// 애니메이션 속도 조절
        /// </summary>
        public void SetAnimationSpeed(float speed)
        {
            _animator.speed = Mathf.Max(0f, speed);
        }

        /// <summary>
        /// 애니메이션 일시정지/재개
        /// </summary>
        public void PauseAnimation(bool pause)
        {
            _animator.enabled = !pause;
        }

        #endregion

        #region Private Animation Methods

        private void PlayDamageAnimation()
        {
            _animator.SetTrigger(_damageTrigger);

            // 데미지 이펙트 (깜빡임 등)
            StartCoroutine(DamageFlashEffect());

            if (_logAnimationEvents)
            {
                Debug.Log("[PlayerAnimator] 데미지 애니메이션 재생");
            }
        }

        private void PlayDeathAnimation()
        {
            _isAnimatingDeath = true;
            _animator.SetTrigger(_deathTrigger);

            if (_logAnimationEvents)
            {
                Debug.Log("[PlayerAnimator] 사망 애니메이션 재생");
            }
        }

        #endregion

        #region Particle Effects

        private void PlayJumpParticles()
        {
            if (_jumpParticles != null && _particleSpawnPoint != null)
            {
                _jumpParticles.transform.position = _particleSpawnPoint.position;
                _jumpParticles.Play();
            }
        }

        private void PlayLandParticles()
        {
            if (_landParticles != null && _particleSpawnPoint != null)
            {
                _landParticles.transform.position = _particleSpawnPoint.position;
                _landParticles.Play();
            }
        }

        private void PlayDoubleJumpParticles()
        {
            if (_doubleJumpParticles != null)
            {
                _doubleJumpParticles.transform.position = transform.position;
                _doubleJumpParticles.Play();
            }
        }

        #endregion

        #region Visual Effects

        private IEnumerator DamageFlashEffect()
        {
            if (_spriteRenderer == null) yield break;

            Color originalColor = _spriteRenderer.color;
            float flashDuration = 0.1f;
            int flashCount = 3;

            for (int i = 0; i < flashCount; i++)
            {
                _spriteRenderer.color = Color.red;
                yield return new WaitForSeconds(flashDuration);
                _spriteRenderer.color = originalColor;
                yield return new WaitForSeconds(flashDuration);
            }
        }

        #endregion

        #region Animation Events (애니메이션 클립에서 호출)

        /// <summary>
        /// 공격 애니메이션의 타격 시점에서 호출
        /// </summary>
        public void OnAttackHit()
        {
            OnAttackAnimationHit?.Invoke();

            if (_logAnimationEvents)
            {
                Debug.Log("[PlayerAnimator] 공격 타격 이벤트");
            }
        }

        /// <summary>
        /// 애니메이션 완료 시점에서 호출
        /// </summary>
        public void OnAnimationFinished()
        {
            OnAnimationComplete?.Invoke();

            if (_logAnimationEvents)
            {
                Debug.Log("[PlayerAnimator] 애니메이션 완료 이벤트");
            }
        }

        /// <summary>
        /// 변신 애니메이션 완료 시점에서 호출
        /// </summary>
        public void OnTransformFinished()
        {
            OnTransformAnimationComplete?.Invoke();

            if (_logAnimationEvents)
            {
                Debug.Log("[PlayerAnimator] 변신 애니메이션 완료 이벤트");
            }
        }

        /// <summary>
        /// 사망 애니메이션 완료 시점에서 호출
        /// </summary>
        public void OnDeathFinished()
        {
            OnDeathAnimationComplete?.Invoke();

            if (_logAnimationEvents)
            {
                Debug.Log("[PlayerAnimator] 사망 애니메이션 완료 이벤트");
            }
        }

        /// <summary>
        /// 발소리 이벤트
        /// </summary>
        public void OnFootstep()
        {
            // 발소리 사운드 재생 로직
            if (_logAnimationEvents)
            {
                Debug.Log("[PlayerAnimator] 발소리 이벤트");
            }
        }

        #endregion

        #region Debug

        private void LogAnimationState()
        {
            AnimatorStateInfo stateInfo = _animator.GetCurrentAnimatorStateInfo(0);
            Debug.Log($"[PlayerAnimator] 현재 상태: {stateInfo.fullPathHash}, " +
                     $"정규화 시간: {stateInfo.normalizedTime:F2}, " +
                     $"속도: {_animator.GetFloat(_speedParameter):F2}");
        }

        #endregion

        #region Context Menu (에디터 전용)

        [ContextMenu("데미지 애니메이션 테스트")]
        private void ContextMenuTestDamage()
        {
            if (Application.isPlaying)
            {
                PlayDamageAnimation();
            }
        }

        [ContextMenu("공격 애니메이션 테스트")]
        private void ContextMenuTestAttack()
        {
            if (Application.isPlaying)
            {
                PlayAttackAnimation(0);
            }
        }

        [ContextMenu("변신 애니메이션 테스트")]
        private void ContextMenuTestTransform()
        {
            if (Application.isPlaying)
            {
                PlayTransformAnimation();
            }
        }

        [ContextMenu("현재 애니메이션 상태 출력")]
        private void ContextMenuPrintAnimationState()
        {
            if (_animator == null) return;

            AnimatorStateInfo stateInfo = _animator.GetCurrentAnimatorStateInfo(0);
            AnimatorClipInfo[] clipInfos = _animator.GetCurrentAnimatorClipInfo(0);

            Debug.Log($"=== Animation State ===\n" +
                     $"Current State: {stateInfo.fullPathHash}\n" +
                     $"Normalized Time: {stateInfo.normalizedTime:F2}\n" +
                     $"Speed: {_animator.speed}\n" +
                     $"Current Clips: {clipInfos.Length}");
        }

        #endregion
    }
}