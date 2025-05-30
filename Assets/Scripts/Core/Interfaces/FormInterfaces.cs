// Assets/Scripts/Core/Interfaces/FormInterfaces.cs
using UnityEngine;
using System.Collections.Generic;
using Metamorph.Forms.Base;

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
        FormData.FormRarity Rarity { get; }  // FormData.FormRarity로 수정
        FormData.FormType Type { get; }      // FormData.FormType으로 수정
    }
}