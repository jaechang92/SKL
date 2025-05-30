// Assets/Scripts/Core/Interfaces/ManagerInterfaces.cs
using System;

namespace Metamorph.Core.Interfaces
{
    public interface IFormManager
    {
        void RegisterFormChangeListener(Action<IForm> callback);
        void UnregisterFormChangeListener(Action<IForm> callback);
        void SwitchToSecondaryForm();
        IForm GetCurrentForm();
        void EquipForm(IForm form, bool asPrimary);
        void UnlockForm(IForm form);
    }

    public interface ISkillManager
    {
        void UseBasicAttack();
        void UseSkill(int index);
        void UpdateSkills(SkillData basic, SkillData skill1, SkillData skill2, SkillData ultimate);
    }
}