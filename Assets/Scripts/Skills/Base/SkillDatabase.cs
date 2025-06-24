using System.Collections.Generic;
using UnityEngine;


[CreateAssetMenu(fileName = "SkillDatabase", menuName = "Skills/Database")]
public class SkillDatabase : ScriptableObject
{
    [Header("All Skills")]
    public List<SkillData> allSkills;

    [Header("Skill Categories")]
    public List<SkillData> basicAttacks;
    public List<SkillData> activeSkills;
    public List<SkillData> ultimateSkills;
    public List<SkillData> passiveSkills;
}
