// Assets/Scripts/Player/PlayerController.cs
using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;
using System.Collections.Generic;
using Metamorph.Core.Interfaces;
using Metamorph.Player.Components.Movement;
using Metamorph.Player.Components.Stats;
using Metamorph.Player.Components.Animation;
using Metamorph.Forms.Base;
using Metamorph.Player.Skills;
using Metamorph.Managers;

/// <summary>
/// 플레이어의 입력을 처리하고 각 컴포넌트를 조율하는 중앙 컨트롤러
/// </summary>
[RequireComponent(typeof(PlayerMovement))]
[RequireComponent(typeof(PlayerStats))]
[RequireComponent(typeof(PlayerAnimator))]
public class PlayerController : MonoBehaviour, ISkillUser, IFormChangeable
{
    [Header("Input Settings")]
    [SerializeField] private float _inputDeadzone = 0.1f;
    [SerializeField] private bool _enableInputBuffer = true;
    [SerializeField] private float _attackInputBuffer = 0.2f;

    [Header("Combat Settings")]
    [SerializeField] private bool _canAttackInAir = true;
    [SerializeField] private bool _stopMovementDuringAttack = false;

    [Header("Debug")]
    [SerializeField] private bool _showDebugInfo = false;
    [SerializeField] private bool _logInputEvents = false;

    // 컴포넌트 참조
    private PlayerMovement _playerMovement;
    private PlayerStats _playerStats;
    private PlayerAnimator _playerAnimator;
    private PlayerInputActions _inputActions;

    // 입력 액션과 핸들러를 매핑하는 딕셔너리
    private Dictionary<InputAction, System.Action<InputAction.CallbackContext>> _performedBindings;
    private Dictionary<InputAction, System.Action<InputAction.CallbackContext>> _canceledBindings;

    // 입력 상태
    private Vector2 _moveInput;
    private float _attackBufferTimer;
    private bool _isAttacking;

    // 형태 변경 관련
    private IForm _currentForm;

    // 이벤트
    public System.Action<IForm> OnFormChanged;
    public System.Action OnAttackStarted;
    public System.Action OnAttackEnded;

    #region Properties

    public bool IsAttacking => _isAttacking;
    public bool CanMove => !_isAttacking || !_stopMovementDuringAttack;
    public bool CanAttack => (!_isAttacking) && (_canAttackInAir || _playerMovement.IsGrounded);
    public IForm CurrentForm => _currentForm;

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        InitializeComponents();
        InitializeInput();
    }

    private void Start()
    {
        RegisterToFormManager();
        SetupAnimatorEvents();
    }

    private void Update()
    {
        UpdateInputBuffers();
        ProcessBufferedInputs();
    }

    private void FixedUpdate()
    {
        ProcessMovementInput();
    }

    private void OnEnable()
    {
        EnableInput();
    }

    private void OnDisable()
    {
        DisableInput();
    }

    private void OnDestroy()
    {
        UnregisterFromFormManager();
    }

    #endregion

    #region Initialization

    private void InitializeComponents()
    {
        _playerMovement = GetComponent<PlayerMovement>();
        _playerStats = GetComponent<PlayerStats>();
        _playerAnimator = GetComponent<PlayerAnimator>();

        if (_playerMovement == null || _playerStats == null || _playerAnimator == null)
        {
            Debug.LogError("[PlayerController] 필수 컴포넌트가 누락되었습니다!");
        }
    }

    private void InitializeInput()
    {
        _inputActions = new PlayerInputActions();

        // Performed 바인딩
        _performedBindings = new Dictionary<InputAction, System.Action<InputAction.CallbackContext>>
            {
                { _inputActions.Player.Move, OnMovePerformed },
                { _inputActions.Player.Jump, OnJumpPerformed },
                { _inputActions.Player.BasicAttack, OnBasicAttackPerformed },
                { _inputActions.Player.Skill1, OnSkill1Performed },
                { _inputActions.Player.Skill2, OnSkill2Performed },
                { _inputActions.Player.Skill3, OnSkill3Performed },
                { _inputActions.Player.FormSwitch, OnFormSwitchPerformed }
            };

        // Canceled 바인딩
        _canceledBindings = new Dictionary<InputAction, System.Action<InputAction.CallbackContext>>
            {
                { _inputActions.Player.Move, OnMoveCanceled },
                { _inputActions.Player.Jump, OnJumpCanceled }
            };
    }

    private void SetupAnimatorEvents()
    {
        if (_playerAnimator != null)
        {
            _playerAnimator.OnAttackAnimationHit += HandleAttackHit;
            _playerAnimator.OnAnimationComplete += HandleAnimationComplete;
            _playerAnimator.OnTransformAnimationComplete += HandleTransformComplete;
        }
    }

    #endregion

    #region Input Management

    private void EnableInput()
    {
        _inputActions.Enable();

        // Performed 이벤트 구독
        foreach (var binding in _performedBindings)
        {
            binding.Key.performed += binding.Value;
        }

        // Canceled 이벤트 구독
        foreach (var binding in _canceledBindings)
        {
            binding.Key.canceled += binding.Value;
        }
    }

    private void DisableInput()
    {
        // Performed 이벤트 구독 해제
        foreach (var binding in _performedBindings)
        {
            binding.Key.performed -= binding.Value;
        }

        // Canceled 이벤트 구독 해제
        foreach (var binding in _canceledBindings)
        {
            binding.Key.canceled -= binding.Value;
        }

        _inputActions.Disable();
    }

    #endregion

    #region Form Management

    private void RegisterToFormManager()
    {
        if (FormManager.Instance != null)
        {
            FormManager.Instance.RegisterPlayer(HandleFormChanged);
        }
        else
        {
            StartCoroutine(TryRegisterLater());
        }
    }

    private void UnregisterFromFormManager()
    {
        if (FormManager.Instance != null)
        {
            FormManager.Instance.UnregisterPlayer(HandleFormChanged);
        }
    }

    private IEnumerator TryRegisterLater()
    {
        yield return new WaitUntil(() => FormManager.Instance != null);
        FormManager.Instance.RegisterPlayer(HandleFormChanged);
    }

    private void HandleFormChanged(FormData formData)
    {
        if (formData == null) return;

        _currentForm = formData;

        // 스탯 업데이트는 PlayerStats가 처리
        _playerStats.UpdateFromFormData(formData);

        // 애니메이터 업데이트
        if (_playerAnimator != null && formData.animatorController != null)
        {
            GetComponent<Animator>().runtimeAnimatorController = formData.animatorController;
        }

        // 스킬 업데이트
        if (SkillManager.Instance != null)
        {
            SkillManager.Instance.UpdateSkills(
                formData.basicAttack,
                formData.skillOne,
                formData.skillTwo,
                formData.ultimateSkill
            );
        }

        // 변신 애니메이션 재생
        _playerAnimator.PlayTransformAnimation();

        OnFormChanged?.Invoke(formData);

        if (_showDebugInfo)
        {
            Debug.Log($"[PlayerController] 형태 변경: {formData.formName}");
        }
    }

    #endregion

    #region Input Handlers - Movement

    private void OnMovePerformed(InputAction.CallbackContext context)
    {
        _moveInput = context.ReadValue<Vector2>();

        if (_logInputEvents)
        {
            Debug.Log($"[PlayerController] 이동 입력: {_moveInput}");
        }
    }

    private void OnMoveCanceled(InputAction.CallbackContext context)
    {
        _moveInput = Vector2.zero;
    }

    private void ProcessMovementInput()
    {
        if (CanMove && _playerMovement != null)
        {
            // 데드존 적용
            float horizontalInput = Mathf.Abs(_moveInput.x) > _inputDeadzone ? _moveInput.x : 0f;
            _playerMovement.Move(new Vector2(horizontalInput, 0));
        }
    }

    #endregion

    #region Input Handlers - Jump

    private void OnJumpPerformed(InputAction.CallbackContext context)
    {
        if (_playerMovement != null)
        {
            _playerMovement.RequestJump();
        }

        if (_logInputEvents)
        {
            Debug.Log("[PlayerController] 점프 입력");
        }
    }

    private void OnJumpCanceled(InputAction.CallbackContext context)
    {
        if (_playerMovement != null)
        {
            _playerMovement.CancelJump();
        }
    }

    #endregion

    #region Input Handlers - Combat

    private void OnBasicAttackPerformed(InputAction.CallbackContext context)
    {
        if (_enableInputBuffer || CanAttack)
        {
            _attackBufferTimer = _attackInputBuffer;
        }

        if (_logInputEvents)
        {
            Debug.Log("[PlayerController] 기본 공격 입력");
        }
    }

    private void OnSkill1Performed(InputAction.CallbackContext context)
    {
        UseSkill(1);
    }

    private void OnSkill2Performed(InputAction.CallbackContext context)
    {
        UseSkill(2);
    }

    private void OnSkill3Performed(InputAction.CallbackContext context)
    {
        UseSkill(3);
    }

    private void UseSkill(int skillIndex)
    {
        if (!CanAttack) return;

        if (SkillManager.Instance != null && SkillManager.Instance.UseSkill(skillIndex))
        {
            StartAttack();

            if (_logInputEvents)
            {
                Debug.Log($"[PlayerController] 스킬 {skillIndex} 사용");
            }
        }
    }

    #endregion

    #region Input Handlers - Form

    private void OnFormSwitchPerformed(InputAction.CallbackContext context)
    {
        if (FormManager.Instance != null)
        {
            FormManager.Instance.SwitchToSecondaryForm();

            if (_logInputEvents)
            {
                Debug.Log("[PlayerController] 형태 전환 입력");
            }
        }
    }

    #endregion

    #region Attack Management

    private void UpdateInputBuffers()
    {
        if (_attackBufferTimer > 0)
        {
            _attackBufferTimer -= Time.deltaTime;
        }
    }

    private void ProcessBufferedInputs()
    {
        // 버퍼된 공격 입력 처리
        if (_attackBufferTimer > 0 && CanAttack)
        {
            PerformBasicAttack();
            _attackBufferTimer = 0;
        }
    }

    private void PerformBasicAttack()
    {
        if (SkillManager.Instance != null && SkillManager.Instance.UseBasicAttack())
        {
            StartAttack();
            _playerAnimator.PlayAttackAnimation(0);
        }
    }

    private void StartAttack()
    {
        _isAttacking = true;
        OnAttackStarted?.Invoke();

        if (_stopMovementDuringAttack)
        {
            _playerMovement.Stop();
        }
    }

    private void EndAttack()
    {
        _isAttacking = false;
        OnAttackEnded?.Invoke();
    }

    #endregion

    #region Animation Event Handlers

    private void HandleAttackHit()
    {
        // 실제 데미지 처리 로직
        if (_showDebugInfo)
        {
            Debug.Log("[PlayerController] 공격 타격!");
        }
    }

    private void HandleAnimationComplete()
    {
        if (_isAttacking)
        {
            EndAttack();
        }
    }

    private void HandleTransformComplete()
    {
        if (_showDebugInfo)
        {
            Debug.Log("[PlayerController] 변신 완료!");
        }
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// 이동 가능 여부 설정
    /// </summary>
    public void SetMovementEnabled(bool enabled)
    {
        if (!enabled)
        {
            _playerMovement.Stop();
        }
    }

    /// <summary>
    /// 공격 가능 여부 설정
    /// </summary>
    public void SetCombatEnabled(bool enabled)
    {
        if (!enabled && _isAttacking)
        {
            EndAttack();
        }
    }

    /// <summary>
    /// 입력 완전 차단/해제
    /// </summary>
    public void SetInputEnabled(bool enabled)
    {
        if (enabled)
        {
            EnableInput();
        }
        else
        {
            DisableInput();
            _moveInput = Vector2.zero;
            _playerMovement.Stop();
        }
    }

    #endregion

    #region ISkillUser Implementation

    public void UseSkill(ISkill skill)
    {
        if (!CanAttack || skill == null) return;

        // 스킬 사용 가능 여부 체크
        if (!skill.CanUse())
        {
            if (_showDebugInfo)
            {
                Debug.Log($"[PlayerController] '{skill.SkillName}' 사용 불가 (쿨다운: {skill.GetCooldown():F1}초)");
            }
            return;
        }

        // 스킬 컨텍스트 생성
        ISkillContext context = CreateSkillContext();

        // 공격 시작
        StartAttack();

        // 애니메이션 재생
        PlaySkillAnimation(skill);

        // 스킬 실행
        skill.Execute(context);

        // 스킬 이펙트 재생 (스킬이 ISkillEffect도 구현하는 경우)
        if (skill is ISkillEffect skillEffect)
        {
            skillEffect.PlayEffect(context.Position);
        }

        if (_showDebugInfo)
        {
            Debug.Log($"[PlayerController] '{skill.SkillName}' 사용됨");
        }
    }

    public void UseBasicAttack()
    {
        if (!CanAttack) return;

        // 기본 공격도 ISkill 인터페이스를 구현한 객체로 처리
        ISkill basicAttack = SkillManager.Instance?.GetBasicAttack();
        if (basicAttack != null)
        {
            UseSkill(basicAttack);
        }
    }
    public void UseUltimateSkill()
    {
        if (CanAttack && SkillManager.Instance != null)
        {
            if (SkillManager.Instance.UseUltimateSkill())
            {
                StartAttack();
                _playerAnimator.PlayAttackAnimation(3); // 궁극기 애니메이션
            }
        }
    }

    #endregion

    #region IFormChangeable Implementation

    public void ChangeForm(IForm newForm)
    {
        if (newForm != null && newForm is FormData formData)
        {
            HandleFormChanged(formData);
        }
    }

    #endregion

    #region Skill Context Creation

    /// <summary>
    /// 현재 플레이어 상태로부터 스킬 컨텍스트 생성
    /// </summary>
    private ISkillContext CreateSkillContext()
    {
        return new PlayerSkillContext(
            position: transform.position,
            damageMultiplier: _playerStats.DamageMultiplier,
            enemyLayer: LayerMask.GetMask("Enemy"),
            caster: this.gameObject,
            facingDirection: _playerMovement.FacingRight ? Vector2.right : Vector2.left
        );
    }

    /// <summary>
    /// 스킬에 맞는 애니메이션 재생
    /// </summary>
    private void PlaySkillAnimation(ISkill skill)
    {
        // 스킬 이름이나 태그로 애니메이션 매핑
        Dictionary<string, int> animationMap = new Dictionary<string, int>
    {
        { "BasicAttack", 0 },
        { "Skill1", 1 },
        { "Skill2", 2 },
        { "Ultimate", 3 }
    };

        // 스킬 이름에서 애니메이션 인덱스 찾기
        foreach (var kvp in animationMap)
        {
            if (skill.SkillName.Contains(kvp.Key))
            {
                _playerAnimator.PlayAttackAnimation(kvp.Value);
                return;
            }
        }

        // 기본 공격 애니메이션
        _playerAnimator.PlayAttackAnimation(0);
    }

    #endregion

    #region Context Menu (에디터 전용)

    [ContextMenu("현재 상태 출력")]
    private void ContextMenuPrintStatus()
    {
        Debug.Log($"=== Player Controller Status ===\n" +
                 $"Can Move: {CanMove}\n" +
                 $"Can Attack: {CanAttack}\n" +
                 $"Is Attacking: {_isAttacking}\n" +
                 $"Current Form: {_currentForm?.FormName ?? "None"}\n" +
                 $"Move Input: {_moveInput}");
    }

    [ContextMenu("형태 전환 테스트")]
    private void ContextMenuTestFormSwitch()
    {
        if (Application.isPlaying && FormManager.Instance != null)
        {
            FormManager.Instance.SwitchToSecondaryForm();
        }
    }

    #endregion


}