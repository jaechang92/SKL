// Assets/Scripts/Player/Components/Stats/PlayerStats.cs
using UnityEngine;
using System.Collections.Generic;
using Metamorph.Core.Interfaces;
using CustomDebug;

namespace Metamorph.Player.Components.Stats
{
    /// <summary>
    /// 플레이어의 모든 스탯을 관리하는 컴포넌트
    /// 기본 스탯, 패시브 효과, 최종 스탯 계산을 담당
    /// </summary>
    public class PlayerStats : MonoBehaviour
    {
        [Header("Base Stats")]
        [SerializeField] private PlayerBaseStats _baseStats;

        [Header("Final Stats (Runtime Only)")]
        [SerializeField] private PlayerFinalStats _finalStats;

        [Header("Health Management")]
        [SerializeField] private float _currentHealth;
        [SerializeField] private bool _initializeHealthOnStart = true;

        [Header("Debug Info")]
        [SerializeField] private bool _showDebugInfo = false;
        [SerializeField] private List<PassiveEffectDebug> _activeEffectsDebug = new List<PassiveEffectDebug>();

        // 패시브 효과 관리
        private Dictionary<PassiveAbility.PassiveAbilityType, float> _passiveEffects =
            new Dictionary<PassiveAbility.PassiveAbilityType, float>();

        // 스탯 변경 이벤트
        public System.Action<float, float> OnHealthChanged; // currentHealth, maxHealth
        public System.Action<PlayerFinalStats> OnStatsChanged; // finalStats

        #region Properties (캡슐화된 접근자)

        // 기본 스탯 접근자 (읽기 전용)
        public float BaseMaxHealth => _baseStats.maxHealth;
        public float BaseMoveSpeed => _baseStats.moveSpeed;
        public float BaseJumpForce => _baseStats.jumpForce;

        // 최종 스탯 접근자 (패시브 효과 적용됨)
        public float MaxHealth => _finalStats.maxHealth;
        public float MoveSpeed => _finalStats.moveSpeed;
        public float JumpForce => _finalStats.jumpForce;
        public float DashForce => _finalStats.moveSpeed * 1.5f; // 예시: 대시 속도는 이동 속도의 1.5배

        // 체력 관련
        public float CurrentHealth => _currentHealth;
        public float HealthPercentage => MaxHealth > 0 ? (_currentHealth / MaxHealth) : 0f;
        public bool IsAlive => _currentHealth > 0;

        // 데미지 관련 (패시브에서 계산됨)
        public float DamageMultiplier { get; private set; } = 1.0f;
        public float DamageReduction { get; private set; } = 0f;

        // 디버그용 접근자
        public PlayerBaseStats BaseStatsDebug => _baseStats;
        public PlayerFinalStats FinalStatsDebug => _finalStats;
        public Dictionary<PassiveAbility.PassiveAbilityType, float> PassiveEffectsDebug =>
            new Dictionary<PassiveAbility.PassiveAbilityType, float>(_passiveEffects);

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            // 최초 스탯 계산
            RecalculateFinalStats();
        }

        private void Start()
        {
            // 체력 초기화
            if (_initializeHealthOnStart && _currentHealth <= 0)
            {
                _currentHealth = MaxHealth;
                OnHealthChanged?.Invoke(_currentHealth, MaxHealth);
            }
        }

        private void OnValidate()
        {
            // 에디터에서 값 변경 시 즉시 계산
            if (Application.isPlaying)
            {
                RecalculateFinalStats();
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Form 데이터로부터 기본 스탯 업데이트
        /// </summary>
        public void UpdateFromFormData(IForm form)
        {
            if (form == null)
            {
                JCDebug.Log("UpdateFromFormData: Form이 null입니다.",JCDebug.LogLevel.Warning);
                return;
            }

            UpdateBaseStats(form.MaxHealth, form.MoveSpeed, form.JumpForce);

            // 패시브 효과 적용
            ResetPassiveEffects();
            foreach (var passive in form.PassiveAbilities)
            {
                ApplyPassiveEffect(passive.type, passive.value);
            }

            JCDebug.Log($"[PlayerStats] Form '{form.FormName}'의 스탯이 적용되었습니다.");
        }

        /// <summary>
        /// 기본 스탯 직접 업데이트
        /// </summary>
        public void UpdateBaseStats(float maxHealth, float moveSpeed, float jumpForce)
        {
            _baseStats.maxHealth = Mathf.Max(0, maxHealth);
            _baseStats.moveSpeed = Mathf.Max(0, moveSpeed);
            _baseStats.jumpForce = Mathf.Max(0, jumpForce);

            // 체력이 처음 설정되는 경우 현재 체력도 업데이트
            if (_currentHealth <= 0)
            {
                _currentHealth = _baseStats.maxHealth;
            }

            RecalculateFinalStats();
        }

        /// <summary>
        /// 패시브 효과 적용
        /// </summary>
        public void ApplyPassiveEffect(PassiveAbility.PassiveAbilityType type, float value)
        {
            if (_passiveEffects.ContainsKey(type))
            {
                _passiveEffects[type] += value;
            }
            else
            {
                _passiveEffects.Add(type, value);
            }

            RecalculateFinalStats();

            if (_showDebugInfo)
            {
                JCDebug.Log($"[PlayerStats] 패시브 효과 적용: {type} +{value}");
            }
        }

        /// <summary>
        /// 특정 패시브 효과 제거
        /// </summary>
        public void RemovePassiveEffect(PassiveAbility.PassiveAbilityType type)
        {
            if (_passiveEffects.Remove(type))
            {
                RecalculateFinalStats();

                if (_showDebugInfo)
                {
                    JCDebug.Log($"[PlayerStats] 패시브 효과 제거: {type}");
                }
            }
        }

        /// <summary>
        /// 모든 패시브 효과 초기화
        /// </summary>
        public void ResetPassiveEffects()
        {
            _passiveEffects.Clear();
            RecalculateFinalStats();

            if (_showDebugInfo)
            {
                JCDebug.Log("[PlayerStats] 모든 패시브 효과가 초기화되었습니다.");
            }
        }

        /// <summary>
        /// 체력 변경 (데미지/힐링)
        /// </summary>
        public void ChangeHealth(float amount, bool canExceedMax = false)
        {
            float previousHealth = _currentHealth;

            if (amount > 0) // 힐링
            {
                _currentHealth = canExceedMax ? _currentHealth + amount : Mathf.Min(_currentHealth + amount, MaxHealth);
            }
            else if (amount < 0) // 데미지
            {
                float actualDamage = Mathf.Abs(amount) * (1f - DamageReduction);
                _currentHealth = Mathf.Max(0, _currentHealth - actualDamage);
            }

            if (!Mathf.Approximately(previousHealth, _currentHealth))
            {
                OnHealthChanged?.Invoke(_currentHealth, MaxHealth);

                if (_showDebugInfo)
                {
                    JCDebug.Log($"[PlayerStats] 체력 변경: {previousHealth:F1} → {_currentHealth:F1}");
                }
            }
        }

        /// <summary>
        /// 체력을 최대치로 회복
        /// </summary>
        public void RestoreToFullHealth()
        {
            ChangeHealth(MaxHealth - _currentHealth);
        }

        /// <summary>
        /// 특정 패시브 효과 값 가져오기
        /// </summary>
        public float GetPassiveEffectValue(PassiveAbility.PassiveAbilityType type)
        {
            return _passiveEffects.TryGetValue(type, out float value) ? value : 0f;
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// 최종 스탯 재계산 (패시브 효과 적용)
        /// </summary>
        private void RecalculateFinalStats()
        {
            // 기본값으로 초기화
            _finalStats.maxHealth = _baseStats.maxHealth;
            _finalStats.moveSpeed = _baseStats.moveSpeed;
            _finalStats.jumpForce = _baseStats.jumpForce;

            // 기본 배율 초기화
            DamageMultiplier = 1.0f;
            DamageReduction = 0f;

            // 패시브 효과 적용
            foreach (var effect in _passiveEffects)
            {
                ApplyPassiveEffectToStats(effect.Key, effect.Value);
            }

            // 최종 스탯이 음수가 되지 않도록 보정
            _finalStats.maxHealth = Mathf.Max(1, _finalStats.maxHealth);
            _finalStats.moveSpeed = Mathf.Max(0.1f, _finalStats.moveSpeed);
            _finalStats.jumpForce = Mathf.Max(0.1f, _finalStats.jumpForce);

            // 데미지 감소는 최대 90%로 제한
            DamageReduction = Mathf.Clamp01(DamageReduction);

            // 체력이 최대치를 초과하지 않도록 조정
            if (_currentHealth > MaxHealth)
            {
                _currentHealth = MaxHealth;
            }

            // 이벤트 발생
            OnStatsChanged?.Invoke(_finalStats);

            // 디버그 정보 업데이트
            UpdateDebugInfo();
        }

        /// <summary>
        /// 개별 패시브 효과를 스탯에 적용
        /// </summary>
        private void ApplyPassiveEffectToStats(PassiveAbility.PassiveAbilityType type, float value)
        {
            switch (type)
            {
                case PassiveAbility.PassiveAbilityType.HealthRegen:
                    // 체력 회복은 Update에서 처리하거나 별도 컴포넌트에서 처리
                    break;

                case PassiveAbility.PassiveAbilityType.DamageBoost:
                    DamageMultiplier *= (1 + value / 100f);
                    break;

                case PassiveAbility.PassiveAbilityType.SpeedBoost:
                    _finalStats.moveSpeed *= (1 + value / 100f);
                    break;

                case PassiveAbility.PassiveAbilityType.JumpBoost:
                    _finalStats.jumpForce *= (1 + value / 100f);
                    break;

                case PassiveAbility.PassiveAbilityType.DamageReduction:
                    DamageReduction += value / 100f;
                    break;

                case PassiveAbility.PassiveAbilityType.DoubleJump:
                    // 더블 점프는 별도 컴포넌트에서 처리
                    break;

                case PassiveAbility.PassiveAbilityType.CriticalChance:
                    // 크리티컬 확률은 별도 변수로 관리
                    break;

                default:
                    if (_showDebugInfo)
                    {
                        JCDebug.Log($"[PlayerStats] 처리되지 않은 패시브 효과: {type}",JCDebug.LogLevel.Warning);
                    }
                    break;
            }
        }

        /// <summary>
        /// 디버그 정보 업데이트
        /// </summary>
        private void UpdateDebugInfo()
        {
            if (!_showDebugInfo) return;

            _activeEffectsDebug.Clear();
            foreach (var effect in _passiveEffects)
            {
                _activeEffectsDebug.Add(new PassiveEffectDebug
                {
                    type = effect.Key,
                    value = effect.Value
                });
            }
        }

        #endregion

        #region Data Structures

        [System.Serializable]
        public struct PlayerBaseStats
        {
            [Range(1, 1000)]
            public float maxHealth;

            [Range(0.1f, 500)]
            public float moveSpeed;

            [Range(0.1f, 1000)]
            public float jumpForce;

            public PlayerBaseStats(float health, float speed, float jump)
            {
                maxHealth = health;
                moveSpeed = speed;
                jumpForce = jump;
            }
        }

        [System.Serializable]
        public struct PlayerFinalStats
        {
            [Header("최종 스탯 (패시브 효과 적용됨)")]
            public float maxHealth;
            public float moveSpeed;
            public float jumpForce;

            public PlayerFinalStats(float health, float speed, float jump)
            {
                maxHealth = health;
                moveSpeed = speed;
                jumpForce = jump;
            }
        }

        [System.Serializable]
        public struct PassiveEffectDebug
        {
            public PassiveAbility.PassiveAbilityType type;
            public float value;
        }

        #endregion

        #region Context Menu (에디터 전용)

        [ContextMenu("체력 완전 회복")]
        private void ContextMenuRestoreHealth()
        {
            RestoreToFullHealth();
        }

        [ContextMenu("현재 스탯 출력")]
        private void ContextMenuPrintStats()
        {
            JCDebug.Log($"=== Player Stats ===\n" +
                     $"Base: HP({_baseStats.maxHealth}) SPD({_baseStats.moveSpeed}) JMP({_baseStats.jumpForce})\n" +
                     $"Final: HP({MaxHealth}) SPD({MoveSpeed}) JMP({JumpForce})\n" +
                     $"Current Health: {_currentHealth}/{MaxHealth} ({HealthPercentage:P1})\n" +
                     $"Damage Multiplier: {DamageMultiplier:F2}\n" +
                     $"Damage Reduction: {DamageReduction:P1}");
        }

        [ContextMenu("모든 패시브 효과 제거")]
        private void ContextMenuClearPassives()
        {
            ResetPassiveEffects();
        }

        #endregion
    }
}