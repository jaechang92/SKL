// Assets/Scripts/Core/Interfaces/ManagerInterfaces.cs
using Cysharp.Threading.Tasks;
using PlayerData;
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
        PlayerGameData PlayerData { get; }
        bool IsDirty { get; }

        event Action<PlayerGameData> OnDataChanged;
        event Action<string> OnDataError;

        void SetPlayerData(PlayerGameData data);
        void MarkDirty();
        void ResetDirtyFlag();
        PlayerGameData CreateDefaultData();
        bool ValidateData(PlayerGameData data);
    }

    public interface ISaveManager
    {
        event Action<PlayerGameData> OnDataSaved;
        event Action<PlayerGameData> OnDataLoaded;
        event Action<string> OnSaveError;
        event Action OnAutoSaveTriggered;

        UniTask<PlayerGameData> LoadDataAsync(CancellationToken cancellationToken = default);
        UniTask SaveDataAsync(PlayerGameData data, CancellationToken cancellationToken = default);
        void StartAutoSave(IDataManager dataManager);
        void StopAutoSave();
    }

}