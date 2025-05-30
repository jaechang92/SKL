using Metamorph.Forms.Base;
using UnityEngine;
using UnityEngine.Events;

// 스킬 매니저 클래스 - 스킬 관리 담당
public class SkillManager : SingletonManager<SkillManager>
{
    // 현재 장착된 스킬
    private SkillData _basicAttack;
    private SkillData _skill1;
    private SkillData _skill2;
    private SkillData _ultimateSkill;

    // 쿨다운 관리
    private float _skill1Cooldown;
    private float _skill2Cooldown;
    private float _ultimateSkillCooldown;

    // 데미지 증가 효과 (패시브에서 적용)
    private float _damageMultiplier = 1.0f;

    // 이벤트
    public UnityEvent<float, float> OnSkill1CooldownChanged = new UnityEvent<float, float>();
    public UnityEvent<float, float> OnSkill2CooldownChanged = new UnityEvent<float, float>();
    public UnityEvent<float, float> OnUltimateSkillCooldownChanged = new UnityEvent<float, float>();


    void Start()
    {
    }

    private void OnEnable()
    {
        //FormManager.OnFormChanged += OnFormChanged;
    }

    void Update()
    {
        // 쿨다운 감소
        UpdateCooldowns();
    }

    //private void OnFormChanged(FormData newForm)
    //{
    //    UpdateSkills(newForm.basicAttack, newForm.skillOne, newForm.skillTwo, newForm.ultimateSkill);
    //}

    // 쿨다운 업데이트
    void UpdateCooldowns()
    {
        if (_skill1Cooldown > 0)
        {
            _skill1Cooldown -= Time.deltaTime;
            OnSkill1CooldownChanged.Invoke(_skill1Cooldown, _skill1.cooldown);
        }

        if (_skill2Cooldown > 0)
        {
            _skill2Cooldown -= Time.deltaTime;
            OnSkill2CooldownChanged.Invoke(_skill2Cooldown, _skill2.cooldown);
        }

        if (_ultimateSkillCooldown > 0)
        {
            _ultimateSkillCooldown -= Time.deltaTime;
            OnUltimateSkillCooldownChanged.Invoke(_ultimateSkillCooldown, _ultimateSkill.cooldown);
        }
    }

    // 스킬 업데이트 (형태 변경 시 호출)
    public void UpdateSkills(SkillData basicAttack, SkillData skill1, SkillData skill2, SkillData ultimateSkill)
    {
        _basicAttack = basicAttack;
        _skill1 = skill1;
        _skill2 = skill2;
        _ultimateSkill = ultimateSkill;

        // 쿨다운 초기화 (형태 전환 시 스킬 쿨다운 초기화)
        _skill1Cooldown = 0;
        _skill2Cooldown = 0;
        _ultimateSkillCooldown = 0;

        // UI 업데이트
        UpdateSkillUI();
    }

    // UI 업데이트
    void UpdateSkillUI()
    {
        // 여기서 UI 매니저를 통해 스킬 아이콘 등 업데이트
        // 예시: UIManager.Instance.UpdateSkillIcons(_basicAttack, _skill1, _skill2, _ultimateSkill);
    }

    // 기본 공격 사용
    public void UseBasicAttack()
    {
        if (_basicAttack == null) return;

        // 공격 애니메이션은 PlayerController에서 처리

        // 공격 판정 및 효과 적용
        ApplyDamageInArea(
            _basicAttack.damage * _damageMultiplier,
            _basicAttack.range,
            _basicAttack.effectType,
            _basicAttack.effectValue,
            _basicAttack.effectDuration
        );

        // 이펙트 재생
        PlaySkillEffect(_basicAttack.visualEffectPrefab, _basicAttack.soundEffect);
    }

    // 일반 스킬 사용
    public void UseSkill(int skillIndex)
    {
        SkillData skill = null;
        float cooldown = 0;

        // 스킬 인덱스에 따라 처리
        switch (skillIndex)
        {
            case 1:
                if (_skill1Cooldown > 0) return;
                skill = _skill1;
                _skill1Cooldown = _skill1.cooldown;
                break;
            case 2:
                if (_skill2Cooldown > 0) return;
                skill = _skill2;
                _skill2Cooldown = _skill2.cooldown;
                break;
            default:
                return;
        }

        if (skill == null) return;

        // 애니메이션 트리거
        Animator animator = GetComponentInParent<Animator>();
        animator.SetTrigger("Skill" + skillIndex);

        // 스킬 효과 적용
        ApplyDamageInArea(
            skill.damage * _damageMultiplier,
            skill.range,
            skill.effectType,
            skill.effectValue,
            skill.effectDuration
        );

        // 이펙트 재생
        PlaySkillEffect(skill.visualEffectPrefab, skill.soundEffect);
    }

    // 궁극기 사용
    public void UseUltimateSkill()
    {
        if (_ultimateSkill == null || _ultimateSkillCooldown > 0) return;

        _ultimateSkillCooldown = _ultimateSkill.cooldown;

        // 애니메이션 트리거
        Animator animator = GetComponentInParent<Animator>();
        animator.SetTrigger("Ultimate");

        // 효과 적용
        ApplyDamageInArea(
            _ultimateSkill.damage * _damageMultiplier,
            _ultimateSkill.range,
            _ultimateSkill.effectType,
            _ultimateSkill.effectValue,
            _ultimateSkill.effectDuration
        );

        // 이펙트 재생
        PlaySkillEffect(_ultimateSkill.visualEffectPrefab, _ultimateSkill.soundEffect);
    }

    // 데미지 증가 효과 설정
    public void SetDamageMultiplier(float multiplier)
    {
        _damageMultiplier = multiplier;
    }

    // 범위 내 데미지 적용
    private void ApplyDamageInArea(float damage, float range, SkillData.SkillEffectType effectType,
                                  float effectValue, float effectDuration)
    {
        // 플레이어 위치 기준으로 범위 내 적 검색
        Collider2D[] hits = Physics2D.OverlapCircleAll(
            transform.position,
            range,
            LayerMask.GetMask("Enemy")
        );

        foreach (var hit in hits)
        {
            // 적 컴포넌트 가져오기
            Enemy enemy = hit.GetComponent<Enemy>();
            if (enemy != null)
            {
                // 데미지 적용
                enemy.TakeDamage(damage);

                // 추가 효과 적용
                if (effectType != SkillData.SkillEffectType.None)
                {
                    enemy.ApplyEffect(effectType, effectValue, effectDuration);
                }
            }
        }
    }

    // 스킬 이펙트 재생
    private void PlaySkillEffect(GameObject effectPrefab, AudioClip soundEffect)
    {
        if (effectPrefab != null)
        {
            Instantiate(effectPrefab, transform.position, Quaternion.identity);
        }

        if (soundEffect != null)
        {
            AudioSource audioSource = GetComponent<AudioSource>();
            if (audioSource != null)
            {
                audioSource.PlayOneShot(soundEffect);
            }
        }
    }
}