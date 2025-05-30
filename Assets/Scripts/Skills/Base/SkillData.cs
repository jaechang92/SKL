using UnityEngine;

// 스킬 데이터를 저장할 ScriptableObject
[CreateAssetMenu(fileName = "NewSkill", menuName = "Metamorph/Skill")]
public class SkillData : ScriptableObject
{
    public string skillName;
    public Sprite skillIcon;
    public float cooldown;
    public float damage;
    public float range;
    public AnimationClip skillAnimation;
    public GameObject visualEffectPrefab;
    public AudioClip soundEffect;

    // 스킬 효과 정의
    public SkillEffectType effectType;
    public float effectValue;
    public float effectDuration;

    public enum SkillEffectType { None, Stun, Slow, Bleed, Burn, Freeze }
}
