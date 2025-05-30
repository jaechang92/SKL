using System;
using UnityEngine;

// 패시브 능력 클래스
[Serializable]
public class PassiveAbility
{
    public string abilityName;
    public string description;
    public PassiveAbilityType type;
    public float value;

    public enum PassiveAbilityType
    {
        HealthRegen, DamageBoost, SpeedBoost, JumpBoost,
        CriticalChance, DamageReduction, DoubleJump
    }
}
