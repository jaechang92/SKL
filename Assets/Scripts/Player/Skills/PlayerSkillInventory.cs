using CustomDebug;
using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

/// <summary>
/// 플레이어 스킬 인벤토리 관리
/// </summary>
public class PlayerSkillInventory : MonoBehaviour
{
    [Header("Player Skills")]
    [SerializeField] private List<SkillData> _availableSkills = new List<SkillData>();
    [SerializeField] private List<SkillData> _equippedSkills = new List<SkillData>();

    // 초기화 완료 플래그
    public bool IsInitialized { get; private set; } = false;

    /// <summary>
    /// 비동기 초기화
    /// </summary>
    public async UniTask InitializeAsync(CancellationToken cancellationToken)
    {
        if (IsInitialized) return;

        try
        {
            // 기본 스킬들 추가
            await LoadDefaultSkillsAsync(cancellationToken);

            // 저장된 스킬 인벤토리 로드
            await LoadSavedInventoryAsync(cancellationToken);

            IsInitialized = true;
            JCDebug.Log("[PlayerSkillInventory] 초기화 완료");
        }
        catch (System.OperationCanceledException)
        {
            JCDebug.Log("[PlayerSkillInventory] 초기화 취소됨");
            throw;
        }
        catch (System.Exception ex)
        {
            JCDebug.Log($"[PlayerSkillInventory] 초기화 실패: {ex.Message}", JCDebug.LogLevel.Error);
            throw;
        }
    }

    private async UniTask LoadDefaultSkillsAsync(CancellationToken cancellationToken)
    {
        // 기본 스킬들 로드 로직
        await UniTask.Delay(50, false, PlayerLoopTiming.Update, cancellationToken);
        JCDebug.Log("[PlayerSkillInventory] 기본 스킬 로드 완료");
    }

    private async UniTask LoadSavedInventoryAsync(CancellationToken cancellationToken)
    {
        // 저장된 인벤토리 로드 로직
        await UniTask.Delay(50, false, PlayerLoopTiming.Update, cancellationToken);
        JCDebug.Log("[PlayerSkillInventory] 저장된 인벤토리 로드 완료");
    }

    /// <summary>
    /// 스킬 추가
    /// </summary>
    public void AddSkill(SkillData skill)
    {
        if (skill != null && !_availableSkills.Contains(skill))
        {
            _availableSkills.Add(skill);
            JCDebug.Log($"[PlayerSkillInventory] 스킬 추가: {skill.skillName}");
        }
    }

    /// <summary>
    /// 스킬 장착
    /// </summary>
    public bool EquipSkill(SkillData skill)
    {
        if (skill != null && _availableSkills.Contains(skill))
        {
            if (!_equippedSkills.Contains(skill))
            {
                _equippedSkills.Add(skill);
                JCDebug.Log($"[PlayerSkillInventory] 스킬 장착: {skill.skillName}");
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 사용 가능한 스킬 목록 반환
    /// </summary>
    public List<SkillData> GetAvailableSkills()
    {
        return new List<SkillData>(_availableSkills);
    }

    /// <summary>
    /// 장착된 스킬 목록 반환
    /// </summary>
    public List<SkillData> GetEquippedSkills()
    {
        return new List<SkillData>(_equippedSkills);
    }
}