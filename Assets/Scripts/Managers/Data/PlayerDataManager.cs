using CustomDebug;
using Cysharp.Threading.Tasks;
using Metamorph.Initialization;
using PlayerData;
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

/// <summary>
/// 플레이어의 모든 게임 데이터를 관리하는 매니저
/// 저장/로드, 실시간 업데이트, 데이터 검증을 담당
/// </summary>
public class PlayerDataManager : SingletonManager<PlayerDataManager>, IInitializableAsync
{
    #region Fields

    [Header("Data Settings")]
    [SerializeField] private bool _autoSaveEnabled = true;
    [SerializeField] private float _autoSaveInterval = 30f;
    [SerializeField] private bool _validateDataOnLoad = true;
    [SerializeField] private bool _createBackupOnSave = true;

    [Header("Debug")]
    [SerializeField] private bool _logDataChanges = false;
    [SerializeField] private bool _showDebugInfo = false;

    // 플레이어 데이터
    private PlayerGameData _currentPlayerData;
    private PlayerGameData _backupPlayerData;

    // 초기화 상태
    private bool _isInitialized = false;
    private CancellationTokenSource _autoSaveCTS;

    // 데이터 변경 추적
    private readonly Dictionary<string, object> _changeTracker = new();
    private bool _hasUnsavedChanges = false;

    #endregion

    #region Properties

    public bool IsInitialized => _isInitialized;
    public string Name => nameof(PlayerDataManager);
    public InitializationPriority Priority => InitializationPriority.Core;
    public PlayerGameData CurrentData => _currentPlayerData;
    public bool HasUnsavedChanges => _hasUnsavedChanges;

    // 데이터 접근 프로퍼티들
    public PlayerProfile Profile => _currentPlayerData?.profile;
    public GameProgress Progress => _currentPlayerData?.progress;
    public SkillConfiguration Skills => _currentPlayerData?.skills;
    public InventoryData Inventory => _currentPlayerData?.inventory;
    public GameStatistics Stats => _currentPlayerData?.stats;



    #endregion

    #region Events

    public event Action<PlayerDataChangedEventArgs> OnDataChanged;
    public event Action<PlayerGameData> OnDataLoaded;
    public event Action<PlayerGameData> OnDataSaved;
    public event Action OnDataReset;
    public event Action<string> OnDataValidationFailed;

    #endregion

    #region IInitializableAsync Implementation

    public async UniTask InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            JCDebug.Log("[PlayerDataManager] 초기화 시작");

            // 기본 데이터 초기화
            await InitializeDefaultDataAsync(cancellationToken);

            // 자동 저장 시작
            if (_autoSaveEnabled)
            {
                StartAutoSave();
            }

            _isInitialized = true;
            JCDebug.Log("[PlayerDataManager] 초기화 완료", JCDebug.LogLevel.Success);
        }
        catch (Exception ex)
        {
            JCDebug.Log($"[PlayerDataManager] 초기화 실패: {ex.Message}", JCDebug.LogLevel.Error);
            throw;
        }
    }

    public async UniTask CleanupAsync()
    {
        JCDebug.Log("[PlayerDataManager] 정리 시작");

        // 자동 저장 중지
        StopAutoSave();

        // 미저장 데이터가 있으면 저장
        if (_hasUnsavedChanges)
        {
            await SavePlayerDataAsync();
        }

        _isInitialized = false;
        JCDebug.Log("[PlayerDataManager] 정리 완료");
    }

    #endregion

    #region Data Initialization

    private async UniTask InitializeDefaultDataAsync(CancellationToken cancellationToken)
    {
        // 새로운 데이터 생성
        _currentPlayerData = new PlayerGameData();

        // 백업 데이터 생성
        if (_createBackupOnSave)
        {
            _backupPlayerData = new PlayerGameData();
        }

        await UniTask.Yield(cancellationToken);

        JCDebug.Log("[PlayerDataManager] 기본 데이터 초기화 완료");
    }

    #endregion

    #region Data Management

    /// <summary>
    /// 플레이어 데이터를 비동기로 로드합니다
    /// </summary>
    public async UniTask<bool> LoadPlayerDataAsync()
    {
        try
        {
            JCDebug.Log("[PlayerDataManager] 데이터 로드 시작");

            // LoadManager를 통해 데이터 로드
            var loadManager = LoadManager.Instance;
            if (loadManager == null)
            {
                JCDebug.Log("[PlayerDataManager] LoadManager를 찾을 수 없음", JCDebug.LogLevel.Error);
                return false;
            }

            // 실제 로드 작업은 LoadManager에서 수행
            var loadedData = await loadManager.LoadPlayerDataAsync();

            if (loadedData != null)
            {
                _currentPlayerData = loadedData;

                // 데이터 유효성 검증
                if (_validateDataOnLoad)
                {
                    ValidatePlayerData();
                }

                // 백업 생성
                if (_createBackupOnSave)
                {
                    CreateBackup();
                }

                _hasUnsavedChanges = false;
                OnDataLoaded?.Invoke(_currentPlayerData);

                JCDebug.Log("[PlayerDataManager] 데이터 로드 완료", JCDebug.LogLevel.Success);
                return true;
            }
            else
            {
                JCDebug.Log("[PlayerDataManager] 로드할 데이터가 없음 - 기본 데이터 사용", JCDebug.LogLevel.Warning);
                return false;
            }
        }
        catch (Exception ex)
        {
            JCDebug.Log($"[PlayerDataManager] 데이터 로드 실패: {ex.Message}", JCDebug.LogLevel.Error);
            return false;
        }
    }

    /// <summary>
    /// 플레이어 데이터를 비동기로 저장합니다
    /// </summary>
    public async UniTask<bool> SavePlayerDataAsync()
    {
        try
        {
            if (_currentPlayerData == null)
            {
                JCDebug.Log("[PlayerDataManager] 저장할 데이터가 없음", JCDebug.LogLevel.Warning);
                return false;
            }

            JCDebug.Log("[PlayerDataManager] 데이터 저장 시작");

            // 백업 생성
            if (_createBackupOnSave)
            {
                CreateBackup();
            }

            // 저장 시간 업데이트
            _currentPlayerData.saveTimestamp = DateTime.Now.ToBinary();

            // SaveManager를 통해 데이터 저장
            var saveManager = SaveManager.Instance;
            if (saveManager == null)
            {
                JCDebug.Log("[PlayerDataManager] SaveManager를 찾을 수 없음", JCDebug.LogLevel.Error);
                return false;
            }

            bool saveResult = await saveManager.SavePlayerDataAsync(_currentPlayerData);

            if (saveResult)
            {
                _hasUnsavedChanges = false;
                OnDataSaved?.Invoke(_currentPlayerData);

                JCDebug.Log("[PlayerDataManager] 데이터 저장 완료", JCDebug.LogLevel.Success);
                return true;
            }
            else
            {
                JCDebug.Log("[PlayerDataManager] 데이터 저장 실패", JCDebug.LogLevel.Error);
                return false;
            }
        }
        catch (Exception ex)
        {
            JCDebug.Log($"[PlayerDataManager] 데이터 저장 실패: {ex.Message}", JCDebug.LogLevel.Error);
            return false;
        }
    }

    /// <summary>
    /// 플레이어 데이터를 초기 상태로 리셋합니다
    /// </summary>
    public void ResetPlayerData()
    {
        JCDebug.Log("[PlayerDataManager] 데이터 리셋");

        _currentPlayerData = new PlayerGameData();
        _hasUnsavedChanges = true;

        OnDataReset?.Invoke();
        NotifyDataChanged("AllData", null, _currentPlayerData);
    }

    #endregion

    #region Player Profile Methods

    /// <summary>
    /// 플레이어 레벨을 업데이트합니다
    /// </summary>
    public void UpdatePlayerLevel(int newLevel)
    {
        if (_currentPlayerData?.profile == null) return;

        int oldLevel = _currentPlayerData.profile.level;
        _currentPlayerData.profile.level = newLevel;

        MarkAsChanged();
        NotifyDataChanged("PlayerLevel", oldLevel, newLevel);
    }

    /// <summary>
    /// 플레이어 경험치를 추가합니다
    /// </summary>
    public void AddExperience(long exp)
    {
        if (_currentPlayerData?.profile == null || exp <= 0) return;

        long oldExp = _currentPlayerData.profile.experience;
        _currentPlayerData.profile.experience += exp;

        // 레벨업 체크
        CheckLevelUp();

        MarkAsChanged();
        NotifyDataChanged("PlayerExperience", oldExp, _currentPlayerData.profile.experience);
    }

    /// <summary>
    /// 플레이어 체력을 업데이트합니다
    /// </summary>
    public void UpdatePlayerHealth(int currentHealth, int maxHealth = -1)
    {
        if (_currentPlayerData?.profile == null) return;

        int oldCurrentHealth = _currentPlayerData.profile.currentHealth;
        int oldMaxHealth = _currentPlayerData.profile.maxHealth;

        _currentPlayerData.profile.currentHealth = Mathf.Max(0, currentHealth);

        if (maxHealth > 0)
        {
            _currentPlayerData.profile.maxHealth = maxHealth;
        }

        // 현재 체력이 최대 체력을 초과하지 않도록 제한
        _currentPlayerData.profile.currentHealth = Mathf.Min(
            _currentPlayerData.profile.currentHealth,
            _currentPlayerData.profile.maxHealth
        );

        MarkAsChanged();
        NotifyDataChanged("PlayerHealth",
            new { current = oldCurrentHealth, max = oldMaxHealth },
            new { current = _currentPlayerData.profile.currentHealth, max = _currentPlayerData.profile.maxHealth });
    }

    /// <summary>
    /// 플레이어 마나를 업데이트합니다
    /// </summary>
    public void UpdatePlayerMana(int currentMana, int maxMana = -1)
    {
        if (_currentPlayerData?.profile == null) return;

        int oldCurrentMana = _currentPlayerData.profile.currentMana;
        int oldMaxMana = _currentPlayerData.profile.maxMana;

        _currentPlayerData.profile.currentMana = Mathf.Max(0, currentMana);

        if (maxMana > 0)
        {
            _currentPlayerData.profile.maxMana = maxMana;
        }

        _currentPlayerData.profile.currentMana = Mathf.Min(
            _currentPlayerData.profile.currentMana,
            _currentPlayerData.profile.maxMana
        );

        MarkAsChanged();
        NotifyDataChanged("PlayerMana",
            new { current = oldCurrentMana, max = oldMaxMana },
            new { current = _currentPlayerData.profile.currentMana, max = _currentPlayerData.profile.maxMana });
    }

    private void CheckLevelUp()
    {
        if (_currentPlayerData?.profile == null) return;

        while (_currentPlayerData.profile.experience >= _currentPlayerData.profile.experienceToNext)
        {
            _currentPlayerData.profile.experience -= _currentPlayerData.profile.experienceToNext;
            _currentPlayerData.profile.level++;

            // 다음 레벨 경험치 계산 (레벨 * 100 + 기본 100)
            _currentPlayerData.profile.experienceToNext = _currentPlayerData.profile.level * 100 + 100;

            // 레벨업 보상 (스킬 포인트, 능력치 포인트 등)
            _currentPlayerData.profile.availableAttributePoints += 2;
            _currentPlayerData.skills.availableSkillPoints += 1;

            JCDebug.Log($"[PlayerDataManager] 레벨업! 새 레벨: {_currentPlayerData.profile.level}");
        }
    }

    #endregion

    #region Game Progress Methods

    /// <summary>
    /// 스테이지 완료를 기록합니다
    /// </summary>
    public void CompleteStage(string stageId)
    {
        if (_currentPlayerData?.progress == null || string.IsNullOrEmpty(stageId)) return;

        if (!_currentPlayerData.progress.completedStages.Contains(stageId))
        {
            _currentPlayerData.progress.completedStages.Add(stageId);
            MarkAsChanged();
            NotifyDataChanged("StageCompleted", null, stageId);

            JCDebug.Log($"[PlayerDataManager] 스테이지 완료: {stageId}");
        }
    }

    /// <summary>
    /// 새로운 지역을 잠금 해제합니다
    /// </summary>
    public void UnlockArea(string areaId)
    {
        if (_currentPlayerData?.progress == null || string.IsNullOrEmpty(areaId)) return;

        if (!_currentPlayerData.progress.unlockedAreas.Contains(areaId))
        {
            _currentPlayerData.progress.unlockedAreas.Add(areaId);
            MarkAsChanged();
            NotifyDataChanged("AreaUnlocked", null, areaId);

            JCDebug.Log($"[PlayerDataManager] 지역 잠금해제: {areaId}");
        }
    }

    /// <summary>
    /// 세이브 포인트를 발견했을 때 기록합니다
    /// </summary>
    public void DiscoverSavePoint(string savePointId, Vector3 position, string sceneName)
    {
        if (_currentPlayerData?.progress == null || string.IsNullOrEmpty(savePointId)) return;

        if (!_currentPlayerData.progress.discoveredSavePoints.Contains(savePointId))
        {
            _currentPlayerData.progress.discoveredSavePoints.Add(savePointId);
        }

        // 마지막 활성 세이브 포인트 업데이트
        _currentPlayerData.progress.lastActiveSavePoint = savePointId;
        _currentPlayerData.lastSavePosition = position;
        _currentPlayerData.lastSaveSceneName = sceneName;

        MarkAsChanged();
        NotifyDataChanged("SavePointDiscovered", null, savePointId);

        JCDebug.Log($"[PlayerDataManager] 세이브 포인트 발견: {savePointId}");
    }

    /// <summary>
    /// 보스 처치를 기록합니다
    /// </summary>
    public void DefeatBoss(string bossId)
    {
        if (_currentPlayerData?.progress == null || string.IsNullOrEmpty(bossId)) return;

        if (!_currentPlayerData.progress.defeatedBosses.Contains(bossId))
        {
            _currentPlayerData.progress.defeatedBosses.Add(bossId);
            MarkAsChanged();
            NotifyDataChanged("BossDefeated", null, bossId);

            JCDebug.Log($"[PlayerDataManager] 보스 처치: {bossId}");
        }
    }

    #endregion

    #region Auto Save System

    private void StartAutoSave()
    {
        if (_autoSaveCTS != null)
        {
            _autoSaveCTS.Cancel();
            _autoSaveCTS.Dispose();
        }

        _autoSaveCTS = new CancellationTokenSource();
        AutoSaveLoop(_autoSaveCTS.Token).Forget();

        JCDebug.Log($"[PlayerDataManager] 자동 저장 시작 (간격: {_autoSaveInterval}초)");
    }

    private void StopAutoSave()
    {
        if (_autoSaveCTS != null)
        {
            _autoSaveCTS.Cancel();
            _autoSaveCTS.Dispose();
            _autoSaveCTS = null;
        }

        JCDebug.Log("[PlayerDataManager] 자동 저장 중지");
    }

    private async UniTaskVoid AutoSaveLoop(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await UniTask.Delay(TimeSpan.FromSeconds(_autoSaveInterval), cancellationToken: cancellationToken);

                if (_hasUnsavedChanges)
                {
                    await SavePlayerDataAsync();
                    JCDebug.Log("[PlayerDataManager] 자동 저장 실행");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 정상적인 취소
        }
        catch (Exception ex)
        {
            JCDebug.Log($"[PlayerDataManager] 자동 저장 루프 오류: {ex.Message}", JCDebug.LogLevel.Error);
        }
    }

    #endregion

    #region Data Validation

    private void ValidatePlayerData()
    {
        if (_currentPlayerData == null)
        {
            OnDataValidationFailed?.Invoke("PlayerData is null");
            return;
        }

        // 기본 값 검증
        if (_currentPlayerData.profile.level < 1)
        {
            _currentPlayerData.profile.level = 1;
            OnDataValidationFailed?.Invoke("Player level was below 1, corrected to 1");
        }

        if (_currentPlayerData.profile.currentHealth < 0)
        {
            _currentPlayerData.profile.currentHealth = 0;
            OnDataValidationFailed?.Invoke("Player health was below 0, corrected to 0");
        }

        if (_currentPlayerData.profile.maxHealth < 1)
        {
            _currentPlayerData.profile.maxHealth = 100;
            OnDataValidationFailed?.Invoke("Max health was below 1, corrected to 100");
        }

        // 더 많은 검증 로직...
        JCDebug.Log("[PlayerDataManager] 데이터 유효성 검증 완료");
    }

    #endregion

    #region Backup System

    private void CreateBackup()
    {
        if (_currentPlayerData == null) return;

        try
        {
            // 딥 카피를 위해 JSON 직렬화/역직렬화 사용
            string json = JsonUtility.ToJson(_currentPlayerData);
            _backupPlayerData = JsonUtility.FromJson<PlayerGameData>(json);

            JCDebug.Log("[PlayerDataManager] 백업 생성 완료");
        }
        catch (Exception ex)
        {
            JCDebug.Log($"[PlayerDataManager] 백업 생성 실패: {ex.Message}", JCDebug.LogLevel.Error);
        }
    }

    /// <summary>
    /// 백업 데이터로 복원합니다
    /// </summary>
    public bool RestoreFromBackup()
    {
        if (_backupPlayerData == null)
        {
            JCDebug.Log("[PlayerDataManager] 백업 데이터가 없음", JCDebug.LogLevel.Warning);
            return false;
        }

        try
        {
            string json = JsonUtility.ToJson(_backupPlayerData);
            _currentPlayerData = JsonUtility.FromJson<PlayerGameData>(json);

            _hasUnsavedChanges = true;
            OnDataLoaded?.Invoke(_currentPlayerData);

            JCDebug.Log("[PlayerDataManager] 백업에서 복원 완료", JCDebug.LogLevel.Success);
            return true;
        }
        catch (Exception ex)
        {
            JCDebug.Log($"[PlayerDataManager] 백업 복원 실패: {ex.Message}", JCDebug.LogLevel.Error);
            return false;
        }
    }

    #endregion

    #region Utility Methods

    private void MarkAsChanged()
    {
        _hasUnsavedChanges = true;
    }

    private void NotifyDataChanged(string propertyName, object oldValue, object newValue)
    {
        if (_logDataChanges)
        {
            JCDebug.Log($"[PlayerDataManager] 데이터 변경: {propertyName} = {oldValue} -> {newValue}");
        }

        var args = new PlayerDataChangedEventArgs(propertyName, oldValue, newValue);
        OnDataChanged?.Invoke(args);
    }

    /// <summary>
    /// 디버그 정보를 출력합니다
    /// </summary>
    public void PrintDebugInfo()
    {
        if (_currentPlayerData == null)
        {
            JCDebug.Log("[PlayerDataManager] 플레이어 데이터가 없음");
            return;
        }

        JCDebug.Log($"[PlayerDataManager] 플레이어 데이터 정보:\n" +
                   $"  플레이어: {_currentPlayerData.profile.playerName} (Lv.{_currentPlayerData.profile.level})\n" +
                   $"  체력: {_currentPlayerData.profile.currentHealth}/{_currentPlayerData.profile.maxHealth}\n" +
                   $"  마나: {_currentPlayerData.profile.currentMana}/{_currentPlayerData.profile.maxMana}\n" +
                   $"  진행: 챕터 {_currentPlayerData.progress.currentChapter}, 스테이지 {_currentPlayerData.progress.currentStage}\n" +
                   $"  소울: {_currentPlayerData.inventory.souls}\n" +
                   $"  미저장 변경사항: {_hasUnsavedChanges}");
    }

    #endregion
}