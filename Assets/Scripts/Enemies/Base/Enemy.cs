using Metamorph.Forms.Base;
using Metamorph.Forms.Data;
using Metamorph.Managers;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 적 기본 클래스 (형태 시스템과 연결을 위한 참조)
public class Enemy : MonoBehaviour
{
    [Header("Enemy Config")]
    public EnemyType enemyType;

    public bool IsAlive { get; private set; } = true;
    private Room currentRoom;

    public void Initialize(EnemyManager manager) { }
    public void Spawn(Vector3 position) { IsAlive = true; }
    public void ResetForPool() { IsAlive = false; }
    public void UpdateAI() { /* AI 로직 */ }
    public void SetLODLevel(EnemyLODLevel level) { /* LOD 처리 */ }
    public void OnPlayerFormChanged(FormData newForm) { /* 형태 반응 */ }
    public void OnPlayerSkillUsed(SkillData skillData) { /* 스킬 반응 */ }
    public void SetCurrentRoom(Room room) { currentRoom = room; }
    public Room GetCurrentRoom() { return currentRoom; }

    // 적 기본 속성
    public float maxHealth;
    public float currentHealth;

    // 피격 시 호출
    public virtual void TakeDamage(float damage)
    {
        currentHealth -= damage;

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    // 추가 효과 적용
    public virtual void ApplyEffect(SkillData.SkillEffectType effectType, float value, float duration)
    {
        switch (effectType)
        {
            case SkillData.SkillEffectType.Stun:
                StartCoroutine(ApplyStun(duration));
                break;
            case SkillData.SkillEffectType.Slow:
                StartCoroutine(ApplySlow(value, duration));
                break;
            case SkillData.SkillEffectType.Bleed:
                StartCoroutine(ApplyDamageOverTime(value, duration, 0.5f));
                break;
            case SkillData.SkillEffectType.Burn:
                StartCoroutine(ApplyDamageOverTime(value, duration, 0.5f));
                break;
            case SkillData.SkillEffectType.Freeze:
                StartCoroutine(ApplyFreeze(duration));
                break;
        }
    }

    // 스턴 효과
    private IEnumerator ApplyStun(float duration)
    {
        // 이동 및 공격 불가 상태로 설정
        SetStunned(true);

        yield return new WaitForSeconds(duration);

        SetStunned(false);
    }

    // 슬로우 효과
    private IEnumerator ApplySlow(float slowPercent, float duration)
    {
        // 이동 속도 감소
        SetMovementSpeedModifier(1 - slowPercent / 100);

        yield return new WaitForSeconds(duration);

        SetMovementSpeedModifier(1.0f);
    }

    // 지속 피해 효과 (출혈, 화상 등)
    private IEnumerator ApplyDamageOverTime(float damagePerTick, float duration, float tickRate)
    {
        float elapsed = 0;

        while (elapsed < duration)
        {
            TakeDamage(damagePerTick);

            yield return new WaitForSeconds(tickRate);
            elapsed += tickRate;
        }
    }

    // 빙결 효과
    private IEnumerator ApplyFreeze(float duration)
    {
        // 완전히 행동 불가 + 시각 효과
        SetFrozen(true);

        yield return new WaitForSeconds(duration);

        SetFrozen(false);
    }

    // 스턴 상태 설정 (실제 구현에서는 적 AI 컴포넌트 참조)
    protected virtual void SetStunned(bool isStunned)
    {
        // 실제 구현: GetComponent<EnemyAI>().SetStunned(isStunned);
    }

    // 이동 속도 수정자 설정
    protected virtual void SetMovementSpeedModifier(float modifier)
    {
        // 실제 구현: GetComponent<EnemyAI>().SetMovementSpeedModifier(modifier);
    }

    // 빙결 상태 설정
    protected virtual void SetFrozen(bool isFrozen)
    {
        // 실제 구현: 
        // GetComponent<EnemyAI>().SetFrozen(isFrozen);
        // GetComponent<SpriteRenderer>().color = isFrozen ? Color.blue : Color.white;
    }

    // 사망 처리
    protected virtual void Die()
    {
        // 보상 드롭, 경험치 지급 등

        // 확률적으로 형태 드롭
        if (Random.value < 0.1f) // 10% 확률
        {
            DropForm();
        }

        IsAlive = false;

        // 오브젝트 제거
        Destroy(gameObject);
    }

    // 형태 드롭 (보스 처치시 100% 드롭으로 오버라이드 가능)
    protected virtual void DropForm()
    {
        // 드롭 가능한 형태 목록에서 랜덤 선택
        FormData[] availableForms = Resources.LoadAll<FormData>("Forms/Drops");
        if (availableForms.Length > 0)
        {
            int randomIndex = UnityEngine.Random.Range(0, availableForms.Length);
            FormData droppedForm = availableForms[randomIndex];

            // 형태 아이템 생성
            GameObject formPickup = Instantiate(
                Resources.Load<GameObject>("Prefabs/FormPickup"),
                transform.position,
                Quaternion.identity
            );

            formPickup.GetComponent<FormPickup>().Initialize(droppedForm);
        }
    }
}