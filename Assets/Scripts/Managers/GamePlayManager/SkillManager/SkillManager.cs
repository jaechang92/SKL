// Assets/Scripts/Managers/GamePlayManager/SkillManager/SkillManager.cs
using CustomDebug;
using Cysharp.Threading.Tasks;
using Metamorph.Core.Interfaces;
using Metamorph.Initialization;
using Metamorph.Player.Skills;
using Metamorph.Skills;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;

namespace Metamorph.Managers
{
    /// <summary>
    /// 게임 내 모든 스킬 시스템을 관리하는 매니저
    /// 스킬 등록, 사용, 쿨다운, 업그레이드 등을 담당
    /// </summary>
    public class SkillManager : SingletonManager<SkillManager>, IInitializableAsync
    {
        #region Fields

        [Header("Skill Database")]
        [SerializeField] private SkillDatabase _skillDatabase;
        [SerializeField] private bool _autoLoadSkillDatabase = true;

        [Header("Settings")]
        [SerializeField] private bool _enableGlobalCooldownReduction = false;
        [SerializeField] private float _globalCooldownMultiplier = 1.0f;
        [SerializeField] private bool _logSkillUsage = false;

        [Header("Debug")]
        [SerializeField] private bool _showDebugInfo = false;

        // 등록된 스킬들
        private readonly Dictionary<string, ISkill> _registeredSkills = new();
        private readonly Dictionary<string, SkillData> _skillDataRegistry = new();

        // 스킬 사용자들
        private readonly HashSet<ISkillUser> _skillUsers = new();
        private readonly Dictionary<ISkillUser, SkillUserData> _userSkillData = new();

        // 쿨다운 관리
        private readonly Dictionary<string, float> _skillCooldowns = new();
        private readonly Dictionary<string, float> _skillLastUsedTimes = new();

        // 글로벌 스킬 효과
        private readonly Dictionary<string, List<SkillEffect>> _activeSkillEffects = new();

        // 초기화 상태
        private bool _isInitialized = false;

        #endregion

        #region Properties

        public bool IsInitialized => _isInitialized;
        public string Name => nameof(SkillManager);
        public InitializationPriority Priority => InitializationPriority.Gameplay;
        public SkillDatabase Database => _skillDatabase;
        public int RegisteredSkillCount => _registeredSkills.Count;
        public int ActiveUsersCount => _skillUsers.Count;

        #endregion

        #region Events

        public event Action<ISkillUser, ISkill> OnSkillUsed;
        public event Action<ISkillUser, ISkill> OnSkillCooldownStarted;
        public event Action<ISkillUser, string> OnSkillCooldownEnded;
        public event Action<string, ISkill> OnSkillRegistered;
        public event Action<string> OnSkillUnregistered;
        public event Action<ISkillUser> OnSkillUserRegistered;
        public event Action<ISkillUser> OnSkillUserUnregistered;

        #endregion

        #region IInitializableAsync Implementation

        public async UniTask InitializeAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                JCDebug.Log("[SkillManager] 초기화 시작");

                // 스킬 데이터베이스 로드
                if (_autoLoadSkillDatabase)
                {
                    await LoadSkillDatabaseAsync(cancellationToken);
                }

                // 기본 스킬들 등록
                await RegisterDefaultSkillsAsync(cancellationToken);

                // 글로벌 설정 적용
                ApplyGlobalSettings();

                _isInitialized = true;
                JCDebug.Log("[SkillManager] 초기화 완료", JCDebug.LogLevel.Success);
            }
            catch (Exception ex)
            {
                JCDebug.Log($"[SkillManager] 초기화 실패: {ex.Message}", JCDebug.LogLevel.Error);
                throw;
            }
        }

        public async UniTask CleanupAsync()
        {
            JCDebug.Log("[SkillManager] 정리 시작");

            // 모든 스킬 사용자 해제
            _skillUsers.Clear();
            _userSkillData.Clear();

            // 스킬 등록 해제
            _registeredSkills.Clear();
            _skillDataRegistry.Clear();

            // 쿨다운 정리
            _skillCooldowns.Clear();
            _skillLastUsedTimes.Clear();

            // 활성 효과 정리
            _activeSkillEffects.Clear();

            _isInitialized = false;
            JCDebug.Log("[SkillManager] 정리 완료");

            await UniTask.Yield();
        }

        #endregion

        #region Initialization Methods

        private async UniTask LoadSkillDatabaseAsync(CancellationToken cancellationToken)
        {
            if (_skillDatabase == null)
            {
                //_skillDatabase = Resources.Load<SkillDatabase>("Data/SkillDatabase");
                _skillDatabase = await GameResourceManager.Instance.LoadAsync<SkillDatabase>("Data/SkillDatabase", cancellationToken);

                if (_skillDatabase == null)
                {
                    JCDebug.Log("[SkillManager] SkillDatabase를 찾을 수 없습니다. 기본값으로 생성합니다.", JCDebug.LogLevel.Warning);
                    await CreateDefaultSkillDatabaseAsync(cancellationToken);
                }
            }

            await UniTask.Yield(cancellationToken);
            JCDebug.Log("[SkillManager] 스킬 데이터베이스 로드 완료");
        }

        private async UniTask CreateDefaultSkillDatabaseAsync(CancellationToken cancellationToken)
        {
            // 기본 스킬 데이터베이스 생성 로직
            await UniTask.Yield(cancellationToken);
            JCDebug.Log("[SkillManager] 기본 스킬 데이터베이스 생성 완료");
        }

        private async UniTask RegisterDefaultSkillsAsync(CancellationToken cancellationToken)
        {
            // 기본 근접 공격 스킬 등록
            var basicMeleeAttack = Resources.Load<BasicMeleeAttack>("Skills/BasicMeleeAttack");
            if (basicMeleeAttack != null)
            {
                RegisterSkill("basic_melee_attack", basicMeleeAttack);
            }

            await UniTask.Yield(cancellationToken);
            JCDebug.Log("[SkillManager] 기본 스킬 등록 완료");
        }

        private void ApplyGlobalSettings()
        {
            // 글로벌 설정 적용
            if (_showDebugInfo)
            {
                JCDebug.Log($"[SkillManager] 글로벌 쿨다운 배율: {_globalCooldownMultiplier}");
            }
        }

        #endregion

        #region Skill Registration

        /// <summary>
        /// 스킬을 시스템에 등록합니다
        /// </summary>
        public bool RegisterSkill(string skillId, ISkill skill)
        {
            if (string.IsNullOrEmpty(skillId) || skill == null)
            {
                JCDebug.Log("[SkillManager] 잘못된 스킬 등록 시도", JCDebug.LogLevel.Warning);
                return false;
            }

            if (_registeredSkills.ContainsKey(skillId))
            {
                JCDebug.Log($"[SkillManager] 스킬 ID 중복: {skillId}", JCDebug.LogLevel.Warning);
                return false;
            }

            _registeredSkills[skillId] = skill;

            // SkillData도 함께 등록 (스킬이 SkillData를 가지고 있다면)
            if (skill is MonoBehaviour skillMono && skillMono.TryGetComponent<SkillData>(out var skillData))
            {
                _skillDataRegistry[skillId] = skillData;
            }

            OnSkillRegistered?.Invoke(skillId, skill);

            if (_logSkillUsage)
            {
                JCDebug.Log($"[SkillManager] 스킬 등록: {skillId} ({skill.SkillName})");
            }

            return true;
        }

        /// <summary>
        /// 스킬 등록을 해제합니다
        /// </summary>
        public bool UnregisterSkill(string skillId)
        {
            if (_registeredSkills.Remove(skillId))
            {
                _skillDataRegistry.Remove(skillId);
                _skillCooldowns.Remove(skillId);
                _skillLastUsedTimes.Remove(skillId);

                OnSkillUnregistered?.Invoke(skillId);

                if (_logSkillUsage)
                {
                    JCDebug.Log($"[SkillManager] 스킬 등록 해제: {skillId}");
                }

                return true;
            }

            return false;
        }

        /// <summary>
        /// 등록된 스킬을 가져옵니다
        /// </summary>
        public ISkill GetSkill(string skillId)
        {
            return _registeredSkills.TryGetValue(skillId, out ISkill skill) ? skill : null;
        }

        /// <summary>
        /// 모든 등록된 스킬 ID를 가져옵니다
        /// </summary>
        public IEnumerable<string> GetAllSkillIds()
        {
            return _registeredSkills.Keys;
        }

        #endregion

        #region Skill User Management

        /// <summary>
        /// 스킬 사용자를 등록합니다
        /// </summary>
        public void RegisterSkillUser(ISkillUser skillUser)
        {
            if (skillUser == null) return;

            if (_skillUsers.Add(skillUser))
            {
                _userSkillData[skillUser] = new SkillUserData();
                OnSkillUserRegistered?.Invoke(skillUser);

                if (_logSkillUsage)
                {
                    JCDebug.Log($"[SkillManager] 스킬 사용자 등록: {skillUser.GetType().Name}");
                }
            }
        }

        /// <summary>
        /// 스킬 사용자 등록을 해제합니다
        /// </summary>
        public void UnregisterSkillUser(ISkillUser skillUser)
        {
            if (skillUser == null) return;

            if (_skillUsers.Remove(skillUser))
            {
                _userSkillData.Remove(skillUser);
                OnSkillUserUnregistered?.Invoke(skillUser);

                if (_logSkillUsage)
                {
                    JCDebug.Log($"[SkillManager] 스킬 사용자 등록 해제: {skillUser.GetType().Name}");
                }
            }
        }

        #endregion

        #region Skill Usage

        /// <summary>
        /// 기본 공격을 사용합니다
        /// </summary>
        public bool UseBasicAttack()
        {
            return UseSkillByName("basic_melee_attack");
        }

        /// <summary>
        /// 스킬 이름으로 스킬을 사용합니다
        /// </summary>
        public bool UseSkillByName(string skillId)
        {
            var skill = GetSkill(skillId);
            if (skill == null)
            {
                if (_logSkillUsage)
                {
                    JCDebug.Log($"[SkillManager] 스킬을 찾을 수 없음: {skillId}", JCDebug.LogLevel.Warning);
                }
                return false;
            }

            return ExecuteSkill(skillId, skill);
        }

        /// <summary>
        /// 스킬 인덱스로 스킬을 사용합니다 (플레이어 입력용)
        /// </summary>
        public bool UseSkill(int skillIndex)
        {
            // 현재 활성 플레이어의 장착된 스킬에서 해당 인덱스 스킬 사용
            // PlayerController나 PlayerSkillInventory와 연동 필요
            if (_logSkillUsage)
            {
                JCDebug.Log($"[SkillManager] 스킬 인덱스 사용 요청: {skillIndex}");
            }

            // TODO: 실제 구현에서는 현재 플레이어의 장착된 스킬을 확인
            return false;
        }

        /// <summary>
        /// 궁극기를 사용합니다
        /// </summary>
        public bool UseUltimateSkill()
        {
            // TODO: 현재 플레이어의 궁극기 스킬 실행
            return UseSkillByName("ultimate_skill");
        }

        /// <summary>
        /// 스킬을 실제로 실행합니다
        /// </summary>
        private bool ExecuteSkill(string skillId, ISkill skill)
        {
            // 쿨다운 체크
            if (!CanUseSkill(skillId))
            {
                if (_logSkillUsage)
                {
                    float remainingCooldown = GetSkillCooldown(skillId);
                    JCDebug.Log($"[SkillManager] 스킬 쿨다운 중: {skillId} ({remainingCooldown:F1}초 남음)");
                }
                return false;
            }

            try
            {
                // 스킬 컨텍스트 생성
                var context = CreateSkillContext();

                // 스킬 실행
                skill.Execute(context);

                // 쿨다운 시작
                StartSkillCooldown(skillId, skill);

                // 이벤트 발생
                var primaryUser = _skillUsers.FirstOrDefault(); // 주 사용자 (보통 플레이어)
                if (primaryUser != null)
                {
                    OnSkillUsed?.Invoke(primaryUser, skill);
                }

                if (_logSkillUsage)
                {
                    JCDebug.Log($"[SkillManager] 스킬 사용 완료: {skillId} ({skill.SkillName})");
                }

                return true;
            }
            catch (Exception ex)
            {
                JCDebug.Log($"[SkillManager] 스킬 실행 오류 ({skillId}): {ex.Message}", JCDebug.LogLevel.Error);
                return false;
            }
        }

        /// <summary>
        /// 스킬 컨텍스트를 생성합니다
        /// </summary>
        private ISkillContext CreateSkillContext()
        {
            // 기본 플레이어 위치 및 정보로 컨텍스트 생성
            // 실제 구현에서는 PlayerController나 PlayerStats에서 정보 가져옴
            Vector3 playerPosition = Vector3.zero;
            float damageMultiplier = 1.0f;
            LayerMask enemyLayer = LayerMask.GetMask("Enemy");

            // PlayerController 찾아서 정보 가져오기
            var playerController = FindObjectOfType<MonoBehaviour>(); // PlayerController 타입으로 변경 필요
            if (playerController != null)
            {
                playerPosition = playerController.transform.position;
                // damageMultiplier = playerController.GetDamageMultiplier(); // 실제 메서드로 변경
            }

            return new PlayerSkillContext(
                playerPosition,
                damageMultiplier,
                enemyLayer
            );
        }

        #endregion

        #region Cooldown Management

        /// <summary>
        /// 스킬 사용 가능 여부를 확인합니다
        /// </summary>
        public bool CanUseSkill(string skillId)
        {
            var skill = GetSkill(skillId);
            if (skill == null) return false;

            // 스킬 자체의 사용 가능 여부 체크
            if (!skill.CanUse()) return false;

            // 글로벌 쿨다운 체크
            if (_skillCooldowns.TryGetValue(skillId, out float cooldown))
            {
                return cooldown <= 0f;
            }

            return true;
        }

        /// <summary>
        /// 스킬 쿨다운을 시작합니다
        /// </summary>
        private void StartSkillCooldown(string skillId, ISkill skill)
        {
            float baseCooldown = skill.GetCooldown();
            float adjustedCooldown = baseCooldown * _globalCooldownMultiplier;

            _skillCooldowns[skillId] = adjustedCooldown;
            _skillLastUsedTimes[skillId] = Time.time;

            var primaryUser = _skillUsers.FirstOrDefault();
            if (primaryUser != null)
            {
                OnSkillCooldownStarted?.Invoke(primaryUser, skill);
            }
        }

        /// <summary>
        /// 스킬의 남은 쿨다운 시간을 가져옵니다
        /// </summary>
        public float GetSkillCooldown(string skillId)
        {
            return _skillCooldowns.TryGetValue(skillId, out float cooldown) ? Mathf.Max(0f, cooldown) : 0f;
        }

        /// <summary>
        /// 스킬의 쿨다운 진행률을 가져옵니다 (0-1)
        /// </summary>
        public float GetSkillCooldownProgress(string skillId)
        {
            var skill = GetSkill(skillId);
            if (skill == null) return 1f;

            float totalCooldown = skill.GetCooldown() * _globalCooldownMultiplier;
            float remainingCooldown = GetSkillCooldown(skillId);

            return totalCooldown > 0 ? (totalCooldown - remainingCooldown) / totalCooldown : 1f;
        }

        /// <summary>
        /// 모든 스킬 쿨다운을 업데이트합니다
        /// </summary>
        private void Update()
        {
            if (!_isInitialized) return;

            UpdateCooldowns();
            UpdateSkillEffects();
        }

        private void UpdateCooldowns()
        {
            var keysToRemove = new List<string>();

            foreach (var kvp in _skillCooldowns.ToList())
            {
                string skillId = kvp.Key;
                float currentCooldown = kvp.Value;

                float newCooldown = currentCooldown - Time.deltaTime;

                if (newCooldown <= 0f)
                {
                    keysToRemove.Add(skillId);

                    var primaryUser = _skillUsers.FirstOrDefault();
                    if (primaryUser != null)
                    {
                        OnSkillCooldownEnded?.Invoke(primaryUser, skillId);
                    }
                }
                else
                {
                    _skillCooldowns[skillId] = newCooldown;
                }
            }

            foreach (string key in keysToRemove)
            {
                _skillCooldowns.Remove(key);
            }
        }

        #endregion

        #region Skill Effects Management

        private void UpdateSkillEffects()
        {
            // 활성 스킬 효과들 업데이트
            var effectsToRemove = new List<string>();

            foreach (var kvp in _activeSkillEffects.ToList())
            {
                string effectId = kvp.Key;
                var effects = kvp.Value;

                for (int i = effects.Count - 1; i >= 0; i--)
                {
                    var effect = effects[i];
                    effect.remainingDuration -= Time.deltaTime;

                    if (effect.remainingDuration <= 0f)
                    {
                        effects.RemoveAt(i);
                    }
                }

                if (effects.Count == 0)
                {
                    effectsToRemove.Add(effectId);
                }
            }

            foreach (string effectId in effectsToRemove)
            {
                _activeSkillEffects.Remove(effectId);
            }
        }

        /// <summary>
        /// 스킬 효과를 추가합니다
        /// </summary>
        public void AddSkillEffect(string effectId, SkillEffect effect)
        {
            if (!_activeSkillEffects.ContainsKey(effectId))
            {
                _activeSkillEffects[effectId] = new List<SkillEffect>();
            }

            _activeSkillEffects[effectId].Add(effect);
        }

        #endregion

        #region Skill Database Access

        /// <summary>
        /// 기본 공격 스킬을 가져옵니다
        /// </summary>
        public ISkill GetBasicAttack()
        {
            return GetSkill("basic_melee_attack");
        }

        /// <summary>
        /// 스킬 업데이트 (Form 변경 시 호출)
        /// </summary>
        public void UpdateSkills(SkillData basic, SkillData skill1, SkillData skill2, SkillData ultimate)
        {
            // 기존 스킬들 정리
            ClearUserSkills();

            // 새로운 스킬들 등록
            if (basic != null) RegisterSkillFromData("current_basic", basic);
            if (skill1 != null) RegisterSkillFromData("current_skill1", skill1);
            if (skill2 != null) RegisterSkillFromData("current_skill2", skill2);
            if (ultimate != null) RegisterSkillFromData("current_ultimate", ultimate);

            if (_logSkillUsage)
            {
                JCDebug.Log("[SkillManager] 플레이어 스킬 업데이트 완료");
            }
        }

        private void ClearUserSkills()
        {
            UnregisterSkill("current_basic");
            UnregisterSkill("current_skill1");
            UnregisterSkill("current_skill2");
            UnregisterSkill("current_ultimate");
        }

        private void RegisterSkillFromData(string skillId, SkillData skillData)
        {
            // SkillData를 ISkill로 변환하는 래퍼 생성
            var skillWrapper = new SkillDataWrapper(skillData);
            RegisterSkill(skillId, skillWrapper);
        }

        #endregion

        #region Utility Methods

        /// <summary>
        /// 스킬 매니저의 상태 정보를 출력합니다
        /// </summary>
        public void PrintDebugInfo()
        {
            JCDebug.Log($"[SkillManager] 상태 정보:\n" +
                       $"  초기화 상태: {_isInitialized}\n" +
                       $"  등록된 스킬: {_registeredSkills.Count}\n" +
                       $"  활성 사용자: {_skillUsers.Count}\n" +
                       $"  활성 쿨다운: {_skillCooldowns.Count}\n" +
                       $"  활성 효과: {_activeSkillEffects.Count}\n" +
                       $"  글로벌 쿨다운 배율: {_globalCooldownMultiplier}");
        }

        #endregion

        #region Data Classes

        /// <summary>
        /// 스킬 사용자별 데이터
        /// </summary>
        private class SkillUserData
        {
            public Dictionary<string, float> personalCooldownModifiers = new();
            public Dictionary<string, int> skillLevels = new();
            public List<string> equippedSkills = new();
        }

        /// <summary>
        /// 활성 스킬 효과 데이터
        /// </summary>
        public class SkillEffect
        {
            public string effectId;
            public string effectType;
            public float value;
            public float remainingDuration;
            public GameObject target;

            public SkillEffect(string id, string type, float val, float duration, GameObject tgt = null)
            {
                effectId = id;
                effectType = type;
                value = val;
                remainingDuration = duration;
                target = tgt;
            }
        }

        /// <summary>
        /// SkillData를 ISkill로 래핑하는 클래스
        /// </summary>
        private class SkillDataWrapper : ISkill
        {
            private readonly SkillData _skillData;
            private float _lastUsedTime;

            public string SkillName => _skillData.skillName;

            public SkillDataWrapper(SkillData skillData)
            {
                _skillData = skillData;
                _lastUsedTime = 0f;
            }

            public void Execute(ISkillContext context)
            {
                // 기본적인 데미지 적용 로직
                Collider2D[] targets = Physics2D.OverlapCircleAll(
                    context.Position,
                    _skillData.range,
                    context.EnemyLayer
                );

                foreach (var target in targets)
                {
                    if (target.TryGetComponent<IDamageable>(out var damageable))
                    {
                        float finalDamage = _skillData.damage * context.DamageMultiplier;
                        damageable.TakeDamage(finalDamage);
                    }
                }

                _lastUsedTime = Time.time;

                // 비주얼 이펙트 재생
                if (_skillData.visualEffectPrefab != null)
                {
                    GameObject.Instantiate(_skillData.visualEffectPrefab, context.Position, Quaternion.identity);
                }

                // 사운드 재생
                if (_skillData.soundEffect != null)
                {
                    AudioSource.PlayClipAtPoint(_skillData.soundEffect, context.Position);
                }
            }

            public bool CanUse()
            {
                return Time.time >= _lastUsedTime + _skillData.cooldown;
            }

            public float GetCooldown()
            {
                float remaining = (_lastUsedTime + _skillData.cooldown) - Time.time;
                return Mathf.Max(0f, remaining);
            }
        }

        #endregion
    }
}