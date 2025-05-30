// Assets/Scripts/Core/Interfaces/FormInterfaces.cs
using UnityEngine;
using System;
using System.Collections.Generic;

namespace Metamorph.Core.Interfaces
{
    public interface IForm
    {
        string FormId { get; }
        string FormName { get; }
        Sprite FormSprite { get; }
        RuntimeAnimatorController AnimatorController { get; }

        float MaxHealth { get; }
        float MoveSpeed { get; }
        float JumpForce { get; }

        SkillData BasicAttack { get; }
        SkillData SkillOne { get; }
        SkillData SkillTwo { get; }
        SkillData UltimateSkill { get; }

        List<PassiveAbility> PassiveAbilities { get; }
        FormRarity Rarity { get; }
        FormType Type { get; }
    }

    public enum FormRarity { Common, Rare, Epic, Legendary }
    public enum FormType { Warrior, Mage, Assassin, Tank, Support }
}