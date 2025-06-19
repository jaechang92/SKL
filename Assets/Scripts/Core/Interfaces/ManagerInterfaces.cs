// Assets/Scripts/Core/Interfaces/ManagerInterfaces.cs
using Cysharp.Threading.Tasks;
using Metamorph.Data;
using Metamorph.Initialization;
using System;
using System.Threading;

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

    // ==================================================
    // 인터페이스 정의 (확장성을 위한)
    // ==================================================
    public interface IDataManager
    {
        PlayerData PlayerData { get; }
        bool IsDirty { get; }

        event Action<PlayerData> OnDataChanged;
        event Action<string> OnDataError;

        void SetPlayerData(PlayerData data);
        void MarkDirty();
        void ResetDirtyFlag();
        PlayerData CreateDefaultData();
        bool ValidateData(PlayerData data);
    }

    public interface ISaveManager
    {
        event Action<PlayerData> OnDataSaved;
        event Action<PlayerData> OnDataLoaded;
        event Action<string> OnSaveError;
        event Action OnAutoSaveTriggered;

        UniTask<PlayerData> LoadDataAsync(CancellationToken cancellationToken = default);
        UniTask SaveDataAsync(PlayerData data, CancellationToken cancellationToken = default);
        void StartAutoSave(IDataManager dataManager);
        void StopAutoSave();
    }

}