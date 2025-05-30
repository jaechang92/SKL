using System;
using System.Collections.Generic;
using UnityEngine;

namespace Metamorph.Forms.Base
{
    [CreateAssetMenu(fileName = "NewForm", menuName = "Metamorph/Form")]
    public class FormData : ScriptableObject
    {
        [Header("Identification")]
        public string formId;               // 고유 식별자 (추가됨)
        public string formName;
        [TextArea(2, 4)]
        public string description;          // 설명 추가

        [Header("Visual")]
        public Sprite formSprite;
        public Sprite formIcon;             // 인벤토리/UI용 아이콘 추가
        public RuntimeAnimatorController animatorController;

        [Header("Classification")]
        public FormRarity rarity;
        public FormType type;
        public FormCategory category;       // 카테고리 추가

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
        public bool unlockedByDefault;      // 시작부터 해금되어있는지
        public string unlockRequirement;    // 해금 조건 설명

        // 게임상에서 상태를 저장할 필요가 없는 런타임 데이터
        [NonSerialized]
        public bool isDiscovered;           // 발견 여부

        private void OnValidate()
        {
            // formId가 비어있으면 자동 생성
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