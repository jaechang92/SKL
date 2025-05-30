// Assets/Scripts/Core/Interfaces/SkillInterfaces.cs
using UnityEngine;

namespace Metamorph.Core.Interfaces
{
    public interface ISkill
    {
        void Execute(ISkillContext context);
        bool CanUse();
        float GetCooldown();
        string SkillName { get; }
    }

    public interface ISkillContext
    {
        Vector3 Position { get; }
        float DamageMultiplier { get; }
        LayerMask EnemyLayer { get; }
    }

    public interface ISkillEffect
    {
        void PlayEffect(Vector3 position);
    }
}