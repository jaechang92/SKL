using CustomDebug;
using Cysharp.Threading.Tasks;
using Metamorph.Core.Interfaces;
using Metamorph.Data;
using Metamorph.Initialization;
using Metamorph.Managers;
using System;
using System.IO;
using System.Threading;
using UnityEngine;

// ==================================================
// UniTaskSaveManager - 저장/로드 전용
// ==================================================

public class UniTaskSaveManager : SingletonManager<UniTaskSaveManager>, ISaveManager, IInitializableAsync
{
    public string Name => "Save Manager";

    public InitializationPriority Priority { get; set; } = InitializationPriority.Critical;

    public bool IsInitialized { get; private set; }


    private const string SAVE_FILE_NAME = "PlayerSave.json";
    private const string BACKUP_FILE_NAME = "PlayerSave_Backup.json";

    [Header("Save Configuration")]
    [SerializeField] private bool _autoSave = true;
    [SerializeField] private float _autoSaveInterval = 300f; // 5분
    [SerializeField] private bool _createBackups = true;
    [SerializeField] private int _maxBackups = 3;
    [SerializeField] private bool _encryptSaveData = false;

    private CancellationTokenSource _autoSaveCts;
    private IDataManager _dataManager;

    // 이벤트
    public event Action<PlayerData> OnDataSaved;
    public event Action<PlayerData> OnDataLoaded;
    public event Action<string> OnSaveError;
    public event Action OnAutoSaveTriggered;

    public async UniTask InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            JCDebug.Log("[UniTaskSaveManager] 저장 매니저 초기화 시작");

            // DataManager 참조 획득 (의존성 주입)
            _dataManager = PlayerDataManager.Instance;

            if (_dataManager == null)
            {
                throw new Exception("PlayerDataManager를 찾을 수 없습니다.");
            }

            // 저장된 데이터 로드
            var loadedData = await LoadDataAsync(cancellationToken);
            if (loadedData != null)
            {
                _dataManager.SetPlayerData(loadedData);
            }

            IsInitialized = true;
            JCDebug.Log("[UniTaskSaveManager] 저장 매니저 초기화 완료");
        }
        catch (OperationCanceledException)
        {
            JCDebug.Log("[UniTaskSaveManager] 저장 매니저 초기화가 취소됨", JCDebug.LogLevel.Warning);
            throw;
        }
        catch (Exception ex)
        {
            JCDebug.Log($"[UniTaskSaveManager] 저장 매니저 초기화 실패: {ex.Message}", JCDebug.LogLevel.Error);
            throw;
        }
    }

    public async UniTask<PlayerData> LoadDataAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // 로딩 시뮬레이션
            await UniTask.Delay(TimeSpan.FromSeconds(0.5f).Milliseconds, false, PlayerLoopTiming.Update, cancellationToken);

            string savePath = Path.Combine(Application.persistentDataPath, SAVE_FILE_NAME);

            if (File.Exists(savePath))
            {
                string jsonData = await ReadFileAsync(savePath, cancellationToken);

                if (_encryptSaveData)
                {
                    jsonData = DecryptData(jsonData);
                }

                var playerData = JsonUtility.FromJson<PlayerData>(jsonData);

                // 데이터 검증
                if (_dataManager != null && !_dataManager.ValidateData(playerData))
                {
                    throw new Exception("로드된 데이터가 유효하지 않습니다.");
                }

                OnDataLoaded?.Invoke(playerData);
                JCDebug.Log("[UniTaskSaveManager] 플레이어 데이터 로드 완료");
                return playerData;
            }
            else
            {
                // 백업 파일 확인
                string backupPath = Path.Combine(Application.persistentDataPath, BACKUP_FILE_NAME);
                if (File.Exists(backupPath))
                {
                    JCDebug.Log("[UniTaskSaveManager] 메인 세이브 파일이 없어 백업에서 복원", JCDebug.LogLevel.Warning);
                    string backupData = await ReadFileAsync(backupPath, cancellationToken);

                    if (_encryptSaveData)
                    {
                        backupData = DecryptData(backupData);
                    }

                    var playerData = JsonUtility.FromJson<PlayerData>(backupData);
                    OnDataLoaded?.Invoke(playerData);
                    return playerData;
                }
                else
                {
                    JCDebug.Log("[UniTaskSaveManager] 세이브 파일이 없습니다. 기본 데이터를 사용합니다.");
                    return null; // DataManager에서 기본 데이터 생성
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            JCDebug.Log($"[UniTaskSaveManager] 데이터 로드 실패: {ex.Message}", JCDebug.LogLevel.Error);
            OnSaveError?.Invoke($"데이터 로드 실패: {ex.Message}");
            return null;
        }
    }

    public async UniTask SaveDataAsync(PlayerData data, CancellationToken cancellationToken = default)
    {
        try
        {
            if (data == null)
            {
                JCDebug.Log("[UniTaskSaveManager] 저장할 데이터가 없습니다.", JCDebug.LogLevel.Warning);
                return;
            }

            // 현재 플레이 시간 업데이트
            data.lastPlayTime = DateTime.Now.ToBinary();

            // 백업 생성
            if (_createBackups)
            {
                await CreateBackupAsync(cancellationToken);
            }

            string jsonData = JsonUtility.ToJson(data, true);

            if (_encryptSaveData)
            {
                jsonData = EncryptData(jsonData);
            }

            string savePath = Path.Combine(Application.persistentDataPath, SAVE_FILE_NAME);
            await WriteFileAsync(savePath, jsonData, cancellationToken);

            // DataManager에게 저장 완료 알림
            if (_dataManager != null)
            {
                _dataManager.ResetDirtyFlag();
            }

            OnDataSaved?.Invoke(data);
            JCDebug.Log("[UniTaskSaveManager] 플레이어 데이터 저장 완료");
        }
        catch (OperationCanceledException)
        {
            JCDebug.Log("[UniTaskSaveManager] 데이터 저장이 취소됨", JCDebug.LogLevel.Warning);
            throw;
        }
        catch (Exception ex)
        {
            JCDebug.Log($"[UniTaskSaveManager] 데이터 저장 실패: {ex.Message}", JCDebug.LogLevel.Error);
            OnSaveError?.Invoke($"데이터 저장 실패: {ex.Message}");
            throw;
        }
    }

    public void StartAutoSave(IDataManager dataManager)
    {
        if (!_autoSave || dataManager == null) return;

        _dataManager = dataManager;
        _autoSaveCts = new CancellationTokenSource();
        AutoSaveLoop(_autoSaveCts.Token).Forget();
        JCDebug.Log("[UniTaskSaveManager] 자동 저장 시작");
    }

    public void StopAutoSave()
    {
        _autoSaveCts?.Cancel();
        _autoSaveCts?.Dispose();
        _autoSaveCts = null;
        JCDebug.Log("[UniTaskSaveManager] 자동 저장 중지");
    }

    private async UniTaskVoid AutoSaveLoop(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await UniTask.Delay(TimeSpan.FromSeconds(_autoSaveInterval).Milliseconds, false, PlayerLoopTiming.Update, cancellationToken);

                if (_dataManager != null && _dataManager.IsDirty)
                {
                    OnAutoSaveTriggered?.Invoke();
                    await SaveDataAsync(_dataManager.PlayerData, cancellationToken);
                    JCDebug.Log("[UniTaskSaveManager] 자동 저장 완료");
                }
            }
        }
        catch (OperationCanceledException)
        {
            JCDebug.Log("[UniTaskSaveManager] 자동 저장 루프가 취소됨");
        }
        catch (Exception ex)
        {
            JCDebug.Log($"[UniTaskSaveManager] 자동 저장 오류: {ex.Message}", JCDebug.LogLevel.Error);
        }
    }

    private async UniTask CreateBackupAsync(CancellationToken cancellationToken)
    {
        try
        {
            string savePath = Path.Combine(Application.persistentDataPath, SAVE_FILE_NAME);
            string backupPath = Path.Combine(Application.persistentDataPath, BACKUP_FILE_NAME);

            if (File.Exists(savePath))
            {
                await UniTask.SwitchToThreadPool();
                File.Copy(savePath, backupPath, true);
                await UniTask.SwitchToMainThread(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            JCDebug.Log($"[UniTaskSaveManager] 백업 생성 실패: {ex.Message}", JCDebug.LogLevel.Warning);
        }
    }

    private async UniTask<string> ReadFileAsync(string path, CancellationToken cancellationToken)
    {
        await UniTask.SwitchToThreadPool();
        string content = File.ReadAllText(path);
        await UniTask.SwitchToMainThread(cancellationToken);
        return content;
    }

    private async UniTask WriteFileAsync(string path, string content, CancellationToken cancellationToken)
    {
        await UniTask.SwitchToThreadPool();
        File.WriteAllText(path, content);
        await UniTask.SwitchToMainThread(cancellationToken);
    }

    private string EncryptData(string data)
    {
        // 간단한 암호화 (실제 구현에서는 더 강력한 암호화 사용)
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(data);
        return System.Convert.ToBase64String(bytes);
    }

    private string DecryptData(string encryptedData)
    {
        // 간단한 복호화
        byte[] bytes = System.Convert.FromBase64String(encryptedData);
        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    protected override void OnDestroy()
    {
        StopAutoSave();
        base.OnDestroy();
    }
}
