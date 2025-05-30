// Assets/Scripts/Forms/Base/FormData.cs
using Metamorph.Core.Interfaces;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Metamorph.Forms.Base
{
    [CreateAssetMenu(fileName = "NewForm", menuName = "Metamorph/Form")]
    public class FormData : ScriptableObject, IForm  // IForm 인터페이스 추가
    {
        [Header("Identification")]
        public string formId;
        public string formName;
        [TextArea(2, 4)]
        public string description;

        [Header("Visual")]
        public Sprite formSprite;
        public Sprite formIcon;
        public RuntimeAnimatorController animatorController;

        [Header("Classification")]
        public FormRarity rarity;
        public FormType type;
        public FormCategory category;

        [Header("Base Stats")]
        public float maxHealth;
        public float moveSpeed;
        public float jumpForce;

        [Header("Skills")]
        public SkillData basicAttack;
        public SkillData skillOne;
        public SkillData skillTwo;
        public SkillData ultimateSkill;

        [Header("Passive Abilities")]
        public List<PassiveAbility> passiveAbilities;

        [Header("Acquisition")]
        public bool unlockedByDefault;
        public string unlockRequirement;

        [NonSerialized]
        public bool isDiscovered;

        #region IForm Implementation

        public string FormId => formId;
        public string FormName => formName;
        public Sprite FormSprite => formSprite;
        public RuntimeAnimatorController AnimatorController => animatorController;
        public float MaxHealth => maxHealth;
        public float MoveSpeed => moveSpeed;
        public float JumpForce => jumpForce;
        public SkillData BasicAttack => basicAttack;
        public SkillData SkillOne => skillOne;
        public SkillData SkillTwo => skillTwo;
        public SkillData UltimateSkill => ultimateSkill;
        public List<PassiveAbility> PassiveAbilities => passiveAbilities;
        public FormData.FormRarity Rarity => rarity;
        public FormData.FormType Type => type;

        #endregion

        private void OnValidate()
        {
            if (string.IsNullOrEmpty(formId))
            {
                formId = Guid.NewGuid().ToString();
            }
        }

        public enum FormRarity { Common, Rare, Epic, Legendary }
        public enum FormType { Warrior, Mage, Assassin, Tank, Support }
        public enum FormCategory { Starter, Enemy, Boss, Special, Hidden }
    }
}