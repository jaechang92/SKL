// Assets/Scripts/Skills/Implementations/BasicMeleeAttack.cs
using UnityEngine;
using Metamorph.Core.Interfaces;

namespace Metamorph.Skills
{
    /// <summary>
    /// 기본 근접 공격 스킬
    /// </summary>
    [CreateAssetMenu(fileName = "BasicMeleeAttack", menuName = "Skills/Basic/Melee Attack")]
    public class BasicMeleeAttack : ScriptableObject, ISkill, ISkillEffect
    {
        [Header("Skill Info")]
        [SerializeField] private string _skillName = "Basic Attack";
        [SerializeField] private float _damage = 10f;
        [SerializeField] private float _range = 2f;
        [SerializeField] private float _cooldown = 0.5f;

        [Header("Effects")]
        [SerializeField] private GameObject _hitEffectPrefab;
        [SerializeField] private AudioClip _hitSound;

        private float _lastUsedTime;

        public string SkillName => _skillName;

        public void Execute(ISkillContext context)
        {
            // 공격 범위 내의 적 탐색
            Collider2D[] enemies = Physics2D.OverlapCircleAll(
                context.Position,
                _range,
                context.EnemyLayer
            );

            foreach (var enemy in enemies)
            {
                // 데미지 계산
                float finalDamage = _damage * context.DamageMultiplier;

                // 데미지 적용
                if (enemy.TryGetComponent<IDamageable>(out var damageable))
                {
                    damageable.TakeDamage(finalDamage);

                    // 히트 이펙트
                    PlayEffect(enemy.transform.position);
                }
            }

            _lastUsedTime = Time.time;
        }

        public bool CanUse()
        {
            return Time.time >= _lastUsedTime + _cooldown;
        }

        public float GetCooldown()
        {
            float remaining = (_lastUsedTime + _cooldown) - Time.time;
            return Mathf.Max(0, remaining);
        }

        public void PlayEffect(Vector3 position)
        {
            if (_hitEffectPrefab != null)
            {
                GameObject effect = Instantiate(_hitEffectPrefab, position, Quaternion.identity);
                Destroy(effect, 1f);
            }

            if (_hitSound != null)
            {
                AudioSource.PlayClipAtPoint(_hitSound, position);
            }
        }
    }
}