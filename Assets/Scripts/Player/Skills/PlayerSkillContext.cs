// Assets/Scripts/Player/Skills/PlayerSkillContext.cs
using UnityEngine;
using Metamorph.Core.Interfaces;

namespace Metamorph.Player.Skills
{
    /// <summary>
    /// 플레이어의 스킬 실행 컨텍스트
    /// </summary>
    public class PlayerSkillContext : ISkillContext
    {
        public Vector3 Position { get; private set; }
        public float DamageMultiplier { get; private set; }
        public LayerMask EnemyLayer { get; private set; }

        // 추가 정보
        public GameObject Caster { get; private set; }
        public Vector2 FacingDirection { get; private set; }
        public float CriticalChance { get; private set; }
        public bool IsCritical { get; private set; }

        public PlayerSkillContext(
            Vector3 position,
            float damageMultiplier,
            LayerMask enemyLayer,
            GameObject caster = null,
            Vector2? facingDirection = null,
            float criticalChance = 0f)
        {
            Position = position;
            DamageMultiplier = damageMultiplier;
            EnemyLayer = enemyLayer;
            Caster = caster;
            FacingDirection = facingDirection ?? Vector2.right;
            CriticalChance = criticalChance;

            // 크리티컬 판정
            IsCritical = Random.Range(0f, 100f) < CriticalChance;
            if (IsCritical)
            {
                DamageMultiplier *= 2f; // 크리티컬 데미지 2배
            }
        }
    }
}