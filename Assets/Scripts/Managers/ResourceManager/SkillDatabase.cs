
using UnityEngine;

/// <summary>
/// 스킬 데이터베이스 ScriptableObject
/// </summary>
[CreateAssetMenu(fileName = "New Skill Database", menuName = "Game/Skill Database")]
public class SkillDatabase : ScriptableObject
{
    [Header("Skills")]
    public SkillData[] Skills;

    /// <summary>
    /// ID로 스킬 데이터 가져오기
    /// </summary>
    public SkillData GetSkillById(int skillId)
    {
        foreach (var skill in Skills)
        {
            if (skill.skillID == skillId)
            {
                return skill;
            }
        }
        return null;
    }

    /// <summary>
    /// 이름으로 스킬 데이터 가져오기
    /// </summary>
    public SkillData GetSkillByName(string skillName)
    {
        foreach (var skill in Skills)
        {
            if (skill.skillName == skillName)
            {
                return skill;
            }
        }
        return null;
    }
}