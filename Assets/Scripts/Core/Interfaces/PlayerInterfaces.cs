// Assets/Scripts/Core/Interfaces/PlayerInterfaces.cs
using UnityEngine;

namespace Metamorph.Core.Interfaces
{
    public interface IMoveable
    {
        void Move(Vector2 direction);
        void Jump(float force);

        void Dash(float force);
    }

    public interface ISkillUser
    {
        void UseSkill(ISkill skill);
        void UseBasicAttack();
    }

    public interface IFormChangeable
    {
        void ChangeForm(IForm form);
    }

    public interface IDamageable
    {
        void TakeDamage(float damage);
        float CurrentHealth { get; }
        float MaxHealth { get; }
    }
}