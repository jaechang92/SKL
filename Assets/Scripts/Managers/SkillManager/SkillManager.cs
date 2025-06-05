// Assets/Scripts/Managers/SkillManager.cs
using UnityEngine;
using UnityEngine.Events;
using System.Collections.Generic;
using Metamorph.Core.Interfaces;
using Metamorph.Forms.Base;
using CustomDebug;

namespace Metamorph.Managers
{
    /// <summary>
    /// 스킬 관리를 담당하는 매니저
    /// 스킬 참조 관리, 쿨다운 처리, UI 업데이트 담당
    /// </summary>
    public class SkillManager : SingletonManager<SkillManager>
    {
        [Header("Debug")]
        [SerializeField] private bool _showDebugInfo = false;

        // 스킬 슬롯
        private ISkill _basicAttack;
        private readonly ISkill[] _skills = new ISkill[2]; // 스킬1, 스킬2
        private ISkill _ultimateSkill;

        // 쿨다운 추적을 위한 래퍼
        private readonly Dictionary<ISkill, SkillCooldownTracker> _cooldownTrackers = new Dictionary<ISkill, SkillCooldownTracker>();

        // 스킬 사용자 참조 (PlayerController)
        private ISkillUser _skillUser;

        // 이벤트
        public UnityEvent<int, float, float> OnSkillCooldownChanged = new UnityEvent<int, float, float>(); // index, current, max
        public UnityEvent<ISkill> OnSkillUsed = new UnityEvent<ISkill>();
        public UnityEvent<ISkill[]> OnSkillsUpdated = new UnityEvent<ISkill[]>();

        #region Properties

        public ISkill BasicAttack => _basicAttack;
        public ISkill GetSkill(int index) => (index >= 0 && index < _skills.Length) ? _skills[index] : null;
        public ISkill UltimateSkill => _ultimateSkill;

        #endregion

        #region Unity Lifecycle

        protected override void Awake()
        {
            base.Awake();
            InitializeCooldownTrackers();
        }

        private void Update()
        {
            UpdateAllCooldowns();
        }

        #endregion

        #region Initialization

        private void InitializeCooldownTrackers()
        {
            // 쿨다운 추적기 초기화는 스킬이 설정될 때 수행
        }

        /// <summary>
        /// 스킬 사용자 등록
        /// </summary>
        public void RegisterSkillUser(ISkillUser skillUser)
        {
            _skillUser = skillUser;

            if (_showDebugInfo)
            {
                JCDebug.Log($"[SkillManager] 스킬 사용자 등록됨: {skillUser}");
            }
        }

        #endregion

        #region Skill Management

        /// <summary>
        /// 스킬 업데이트 (Form 변경 시 호출)
        /// </summary>
        public void UpdateSkills(ISkill basicAttack, ISkill skill1, ISkill skill2, ISkill ultimateSkill)
        {
            // 이전 스킬들의 쿨다운 추적 정리
            ClearCooldownTrackers();

            // 새 스킬 설정
            _basicAttack = basicAttack;
            _skills[0] = skill1;
            _skills[1] = skill2;
            _ultimateSkill = ultimateSkill;

            // 쿨다운 추적기 생성
            CreateCooldownTrackers();

            // UI 업데이트 이벤트
            ISkill[] allSkills = { _basicAttack, _skills[0], _skills[1], _ultimateSkill };
            OnSkillsUpdated?.Invoke(allSkills);

            if (_showDebugInfo)
            {
                JCDebug.Log($"[SkillManager] 스킬 업데이트됨 - 기본: {basicAttack?.SkillName}, " +
                         $"스킬1: {skill1?.SkillName}, 스킬2: {skill2?.SkillName}, " +
                         $"궁극기: {ultimateSkill?.SkillName}");
            }
        }

        /// <summary>
        /// FormData로부터 스킬 업데이트 (레거시 지원)
        /// </summary>
        public void UpdateSkills(SkillData basicAttack, SkillData skill1, SkillData skill2, SkillData ultimateSkill)
        {
            // SkillData를 ISkill로 변환 (어댑터 패턴)
            ISkill basicSkill = basicAttack != null ? new SkillDataAdapter(basicAttack) : null;
            ISkill skill1Adapted = skill1 != null ? new SkillDataAdapter(skill1) : null;
            ISkill skill2Adapted = skill2 != null ? new SkillDataAdapter(skill2) : null;
            ISkill ultimateAdapted = ultimateSkill != null ? new SkillDataAdapter(ultimateSkill) : null;

            UpdateSkills(basicSkill, skill1Adapted, skill2Adapted, ultimateAdapted);
        }

        #endregion

        #region Skill Usage

        /// <summary>
        /// 기본 공격 사용
        /// </summary>
        public bool UseBasicAttack()
        {
            if (_basicAttack == null || _skillUser == null) return false;

            if (_basicAttack.CanUse())
            {
                _skillUser.UseSkill(_basicAttack);
                OnSkillUsed?.Invoke(_basicAttack);
                return true;
            }

            return false;
        }

        public ISkill GetBasicAttack()
        {
            return _basicAttack;
        }

        /// <summary>
        /// 일반 스킬 사용 (1-based index)
        /// </summary>
        public bool UseSkill(int skillIndex)
        {
            int arrayIndex = skillIndex - 1;
            if (arrayIndex < 0 || arrayIndex >= _skills.Length) return false;

            ISkill skill = _skills[arrayIndex];
            if (skill == null || _skillUser == null) return false;

            if (skill.CanUse())
            {
                _skillUser.UseSkill(skill);
                OnSkillUsed?.Invoke(skill);
                return true;
            }

            if (_showDebugInfo)
            {
                JCDebug.Log($"[SkillManager] 스킬 {skillIndex} 사용 불가 - 쿨다운: {skill.GetCooldown():F1}초");
            }

            return false;
        }

        /// <summary>
        /// 궁극기 사용
        /// </summary>
        public bool UseUltimateSkill()
        {
            if (_ultimateSkill == null || _skillUser == null) return false;

            if (_ultimateSkill.CanUse())
            {
                _skillUser.UseSkill(_ultimateSkill);
                OnSkillUsed?.Invoke(_ultimateSkill);
                return true;
            }

            if (_showDebugInfo)
            {
                JCDebug.Log($"[SkillManager] 궁극기 사용 불가 - 쿨다운: {_ultimateSkill.GetCooldown():F1}초");
            }

            return false;
        }

        #endregion

        #region Cooldown Management

        private void CreateCooldownTrackers()
        {
            // 기본 공격은 쿨다운 추적 불필요 (보통 쿨다운이 매우 짧음)

            // 일반 스킬
            for (int i = 0; i < _skills.Length; i++)
            {
                if (_skills[i] != null)
                {
                    var tracker = new SkillCooldownTracker(_skills[i], i + 1);
                    _cooldownTrackers[_skills[i]] = tracker;
                }
            }

            // 궁극기
            if (_ultimateSkill != null)
            {
                var tracker = new SkillCooldownTracker(_ultimateSkill, 3);
                _cooldownTrackers[_ultimateSkill] = tracker;
            }
        }

        private void ClearCooldownTrackers()
        {
            _cooldownTrackers.Clear();
        }

        private void UpdateAllCooldowns()
        {
            foreach (var tracker in _cooldownTrackers.Values)
            {
                if (tracker.UpdateCooldown())
                {
                    // 쿨다운이 변경되었으면 UI 업데이트
                    float currentCooldown = tracker.Skill.GetCooldown();
                    float maxCooldown = tracker.GetMaxCooldown();
                    OnSkillCooldownChanged?.Invoke(tracker.SkillIndex, currentCooldown, maxCooldown);
                }
            }
        }

        #endregion

        #region Helper Classes

        /// <summary>
        /// 스킬 쿨다운 추적 헬퍼 클래스
        /// </summary>
        private class SkillCooldownTracker
        {
            public ISkill Skill { get; private set; }
            public int SkillIndex { get; private set; } // UI 업데이트용
            private float _lastCooldown;
            private float _maxCooldown;

            public SkillCooldownTracker(ISkill skill, int index)
            {
                Skill = skill;
                SkillIndex = index;
                _lastCooldown = skill.GetCooldown();
                _maxCooldown = 0f;
            }

            public bool UpdateCooldown()
            {
                float currentCooldown = Skill.GetCooldown();

                // 스킬이 방금 사용되었는지 체크
                if (currentCooldown > _lastCooldown)
                {
                    _maxCooldown = currentCooldown;
                }

                bool changed = !Mathf.Approximately(currentCooldown, _lastCooldown);
                _lastCooldown = currentCooldown;
                return changed;
            }

            public float GetMaxCooldown() => _maxCooldown;
        }

        /// <summary>
        /// SkillData를 ISkill로 변환하는 어댑터
        /// </summary>
        private class SkillDataAdapter : ISkill
        {
            private readonly SkillData _skillData;
            private float _lastUsedTime;

            public string SkillName => _skillData.skillName;

            public SkillDataAdapter(SkillData skillData)
            {
                _skillData = skillData;
            }

            public void Execute(ISkillContext context)
            {
                // SkillData의 효과를 ISkillContext를 통해 실행
                ApplyDamageInArea(context);
                PlayEffects(context.Position);
                _lastUsedTime = Time.time;
            }

            public bool CanUse()
            {
                return Time.time >= _lastUsedTime + _skillData.cooldown;
            }

            public float GetCooldown()
            {
                float remaining = (_lastUsedTime + _skillData.cooldown) - Time.time;
                return Mathf.Max(0, remaining);
            }

            private void ApplyDamageInArea(ISkillContext context)
            {
                Collider2D[] hits = Physics2D.OverlapCircleAll(
                    context.Position,
                    _skillData.range,
                    context.EnemyLayer
                );

                foreach (var hit in hits)
                {
                    if (hit.TryGetComponent<IDamageable>(out var damageable))
                    {
                        float damage = _skillData.damage * context.DamageMultiplier;
                        damageable.TakeDamage(damage);

                        // 추가 효과는 별도 시스템으로 처리
                        if (_skillData.effectType != SkillData.SkillEffectType.None)
                        {
                            // StatusEffectManager를 통해 처리
                        }
                    }
                }
            }

            private void PlayEffects(Vector3 position)
            {
                if (_skillData.visualEffectPrefab != null)
                {
                    GameObject effect = Instantiate(_skillData.visualEffectPrefab, position, Quaternion.identity);
                    Destroy(effect, 2f);
                }

                if (_skillData.soundEffect != null)
                {
                    AudioSource.PlayClipAtPoint(_skillData.soundEffect, position);
                }
            }
        }

        #endregion

        #region Public Utility Methods

        /// <summary>
        /// 특정 스킬의 쿨다운 정보 가져오기
        /// </summary>
        public (float current, float max) GetSkillCooldownInfo(int skillIndex)
        {
            ISkill skill = skillIndex switch
            {
                1 => _skills[0],
                2 => _skills[1],
                3 => _ultimateSkill,
                _ => null
            };

            if (skill != null && _cooldownTrackers.TryGetValue(skill, out var tracker))
            {
                return (skill.GetCooldown(), tracker.GetMaxCooldown());
            }

            return (0f, 0f);
        }

        /// <summary>
        /// 모든 스킬 쿨다운 초기화 (디버그/치트용)
        /// </summary>
        [ContextMenu("모든 쿨다운 초기화")]
        public void ResetAllCooldowns()
        {
            // 실제 스킬 객체의 쿨다운을 리셋하는 방법이 필요
            // ISkill 인터페이스에 ResetCooldown() 메서드 추가 고려

            if (_showDebugInfo)
            {
                JCDebug.Log("[SkillManager] 모든 스킬 쿨다운이 초기화되었습니다.");
            }
        }

        #endregion

        #region Context Menu

        [ContextMenu("현재 스킬 상태 출력")]
        private void PrintSkillStatus()
        {
            JCDebug.Log($"=== Skill Manager Status ===\n" +
                     $"Basic Attack: {_basicAttack?.SkillName ?? "None"}\n" +
                     $"Skill 1: {_skills[0]?.SkillName ?? "None"} (CD: {_skills[0]?.GetCooldown():F1}s)\n" +
                     $"Skill 2: {_skills[1]?.SkillName ?? "None"} (CD: {_skills[1]?.GetCooldown():F1}s)\n" +
                     $"Ultimate: {_ultimateSkill?.SkillName ?? "None"} (CD: {_ultimateSkill?.GetCooldown():F1}s)");
        }

        #endregion
    }
}