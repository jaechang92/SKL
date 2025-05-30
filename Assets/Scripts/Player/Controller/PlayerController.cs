using Metamorph.Forms.Base;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.InputSystem;
using UnityEngine;

// PlayerController 클래스 - 플레이어 제어 담당
public class PlayerController : MonoBehaviour
{
    // 기본 컴포넌트 참조
    private Rigidbody2D _rb;
    private PlayerInputActions _inputActions;
    private Animator _animator;
    private SpriteRenderer _spriteRenderer;

    // 직렬화 가능한 내부 구조체 정의
    [System.Serializable]
    private struct PlayerBaseStats
    {
        public float maxHealth;
        public float moveSpeed;
        public float jumpForce;
    }

    [System.Serializable]
    private struct PlayerFinalStats
    {
        public float maxHealth;
        public float moveSpeed;
        public float jumpForce;
    }

    // 구조체를 Inspector에 표시 (private 상태 유지)
    [SerializeField] private PlayerBaseStats _baseStats;
    [SerializeField] private PlayerFinalStats _finalStats;
    [SerializeField] private float _currentHealth;  // 단일 값은 직접 표시

    // 캡슐화된 접근자 (public API)
    public float MaxHealth => _finalStats.maxHealth;
    public float CurrentHealth => _currentHealth;
    public float MoveSpeed => _finalStats.moveSpeed;
    public float JumpForce => _finalStats.jumpForce;

    

    // 패시브 효과 수치
    private Dictionary<PassiveAbility.PassiveAbilityType, float> _passiveEffects =
        new Dictionary<PassiveAbility.PassiveAbilityType, float>();

    // 입력 처리용 변수
    private float _horizontalInput;
    private bool _jumpPressed;
    private bool _isGrounded;

    // 공격/스킬 관련
    private bool _isAttacking;
    private bool _canMove = true;

    void Awake()
    {
        // 필요한 컴포넌트 참조 가져오기
        _rb = GetComponent<Rigidbody2D>();
        _animator = GetComponent<Animator>();
        _spriteRenderer = GetComponent<SpriteRenderer>();
        _inputActions = new PlayerInputActions();

    }

    private void OnEnable()
    {
        // 입력 액션 활성화
        _inputActions.Enable();
        _inputActions.Player.BasicAttack.performed += BasicAttack;
        //_inputActions.Player.Skill1.performed += UseSkill;
    }
    private void OnDisable()
    {
        // 입력 액션 비활성화
        _inputActions.Disable();
        _inputActions.Player.BasicAttack.performed -= BasicAttack;
        //_inputActions.Player.Skill1.performed -= UseSkill;
    }

    void Start()
    {
        // 플레이어 등록 시도
        if (FormManager.Instance != null)
        {
            FormManager.Instance.RegisterPlayer(OnFormChanged);
        }
        else
        {
            // 지연 등록
            StartCoroutine(TryRegisterLater());
        }
    }

    private IEnumerator TryRegisterLater()
    {
        yield return new WaitUntil(() => FormManager.Instance != null);
        FormManager.Instance.RegisterPlayer(OnFormChanged);
    }

    //private void OnDestroy()
    //{
    //    if (FormManager.Instance != null)
    //    {
    //        FormManager.Instance.UnregisterPlayer(OnFormChanged);
    //    }
    //}


    void Update()
    {
        HandleInput();
        UpdateAnimations();
    }

    void FixedUpdate()
    {
        Move();
        CheckGrounded();
    }


    private void OnFormChanged(FormData form)
    {
        if (form == null) return;

        // 애니메이터 업데이트
        _animator.runtimeAnimatorController = form.animatorController;

        // 스탯 업데이트
        UpdateStats(form.maxHealth, form.moveSpeed, form.jumpForce);

        // 패시브 능력 적용
        ResetPassiveEffects();
        foreach (var passive in form.passiveAbilities)
        {
            ApplyPassiveEffect(passive.type, passive.value);
        }
    }

    // 입력 처리
    void HandleInput()
    {
        _horizontalInput = _inputActions.Player.Move.ReadValue<Vector2>().x;

        if (_inputActions.Player.Jump.IsPressed() && _isGrounded)
        {
            _jumpPressed = true;
        }



        //// 스킬 입력
        //if (Input.GetButtonDown("Fire2"))
        //{
        //    UseSkill(1); // 스킬 1
        //}

        //if (Input.GetButtonDown("Fire3"))
        //{
        //    UseSkill(2); // 스킬 2
        //}

        //// 궁극기
        //if (Input.GetKeyDown(KeyCode.R))
        //{
        //    UseUltimateSkill();
        //}

        //// 형태 전환
        //if (Input.GetKeyDown(KeyCode.Q))
        //{
        //    FormManager.Instance.SwitchToSecondaryForm();
        //}
    }

    // 이동 처리
    void Move()
    {
        if (!_canMove) return;

        _rb.velocity = new Vector2(_horizontalInput * MoveSpeed, _rb.velocity.y);

        // 점프 처리
        if (_jumpPressed)
        {
            _rb.AddForce(Vector2.up * JumpForce, ForceMode2D.Impulse);
            _jumpPressed = false;
            _isGrounded = false;
        }

        // 캐릭터 방향 전환
        if (_horizontalInput != 0)
        {
            _spriteRenderer.flipX = _horizontalInput < 0;
        }
    }

    // 지면 체크
    void CheckGrounded()
    {
        // 레이캐스트로 지면 체크
        RaycastHit2D hit = Physics2D.Raycast(
            transform.position - new Vector3(0, 0.5f, 0),
            Vector2.down,
            0.1f,
            LayerMask.GetMask("Ground")
        );

        _isGrounded = hit.collider != null;
    }

    // 애니메이션 업데이트
    void UpdateAnimations()
    {
        if (_animator == null || _animator.runtimeAnimatorController == null) return;

        _animator.SetFloat("HorizontalSpeed", Mathf.Abs(_horizontalInput));
        _animator.SetBool("IsGrounded", _isGrounded);
        _animator.SetFloat("VerticalVelocity", _rb.velocity.y);
    }
    
    // 기본 공격
    void BasicAttack(InputAction.CallbackContext context)
    {
        _isAttacking = true;
        _animator.SetTrigger("Attack");
        // 공격 로직
        SkillManager.Instance.UseBasicAttack();

        // 공격 종료 시점을 애니메이션 이벤트로 처리하는 게 좋지만
        // 간단한 구현으로 코루틴 사용
        StartCoroutine(ResetAttackState(0.5f));
    }

    void SKill1(InputAction.CallbackContext context)
    {
        //UseSkill();
    }

    // 공격 상태 초기화
    IEnumerator ResetAttackState(float delay)
    {
        yield return new WaitForSeconds(delay);
        _isAttacking = false;
    }

    // 스킬 사용
    void UseSkill(int skillIndex)
    {
        SkillManager.Instance.UseSkill(skillIndex);
    }

    // 궁극기 사용
    void UseUltimateSkill()
    {
        SkillManager.Instance.UseUltimateSkill();
    }

    // 스탯 업데이트 (형태 변경 시 호출됨)
    public void UpdateStats(float maxHealth, float moveSpeed, float jumpForce)
    {
        _baseStats.maxHealth = maxHealth;
        _baseStats.moveSpeed = moveSpeed;
        _baseStats.jumpForce = jumpForce;

        // 첫 설정 시 현재 체력 초기화
        if (_currentHealth <= 0)
        {
            _currentHealth = maxHealth;
        }

        // 최종 스탯 계산
        RecalculateFinalStats();
    }

    // 패시브 효과 초기화
    public void ResetPassiveEffects()
    {
        _passiveEffects.Clear();
    }

    // 패시브 효과 적용
    public void ApplyPassiveEffect(PassiveAbility.PassiveAbilityType type, float value)
    {
        if (_passiveEffects.ContainsKey(type))
        {
            _passiveEffects[type] += value;
        }
        else
        {
            _passiveEffects.Add(type, value);
        }

        // 스탯 재계산
        RecalculateFinalStats();
    }

    // 최종 스탯 계산
    private void RecalculateFinalStats()
    {
        // 기본값으로 초기화
        _finalStats.maxHealth = _baseStats.maxHealth;
        _finalStats.moveSpeed = _baseStats.moveSpeed;
        _finalStats.jumpForce = _baseStats.jumpForce;
        

        // 패시브 효과 적용
        foreach (var effect in _passiveEffects)
        {
            switch (effect.Key)
            {
                case PassiveAbility.PassiveAbilityType.HealthRegen:
                    // 체력 회복은 업데이트 로직에서 처리
                    break;
                case PassiveAbility.PassiveAbilityType.DamageBoost:
                    // 데미지 증가는 스킬 매니저에서 처리
                    break;
                case PassiveAbility.PassiveAbilityType.SpeedBoost:
                    _finalStats.moveSpeed *= (1 + effect.Value / 100);
                    break;
                case PassiveAbility.PassiveAbilityType.JumpBoost:
                    _finalStats.jumpForce *= (1 + effect.Value / 100);
                    break;
                case PassiveAbility.PassiveAbilityType.DamageReduction:
                    // 데미지 감소는 피격 로직에서 처리
                    break;
                case PassiveAbility.PassiveAbilityType.DoubleJump:
                    // 더블 점프는 별도 로직으로 처리
                    break;
            }
        }
    }

    public void OnMove(InputAction.CallbackContext inputValue)
    {
        Vector2 input = inputValue.ReadValue<Vector2>();

        if (input != null)
        {
            _horizontalInput = input.x;
            Debug.Log($"Horizontal Input: {_horizontalInput}");
        }


    }

}