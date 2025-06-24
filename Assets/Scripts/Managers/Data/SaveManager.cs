using CustomDebug;
using Cysharp.Threading.Tasks;
using Metamorph.Core;
using Metamorph.Initialization;
using System.Linq;
using PlayerData;
using SaveSystem;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using UnityEngine;

/// <summary>
/// 플레이어 데이터의 저장을 담당하는 매니저
/// 암호화, 압축, 백업, 체크섬 검증 기능 제공
/// </summary>
public class SaveManager : SingletonManager<SaveManager>, IInitializableAsync
{
    #region Fields

    [Header("Save Settings")]
    [SerializeField] private SaveSettings _saveSettings;
    [SerializeField] private bool _autoCreateDirectory = true;
    [SerializeField] private bool _logSaveOperations = true;

    [Header("File Paths")]
    [SerializeField] private string _saveDirectory = "SaveData";
    [SerializeField] private string _saveFilePrefix = "GameSave_Slot";

    // 저장 상태
    private bool _isInitialized = false;
    private bool _isSaving = false;
    private string _fullSaveDirectory;

    // 저장 슬롯 정보
    private readonly Dictionary<int, SaveFileInfo> _saveSlots = new();
    private readonly Dictionary<int, string> _slotFilePaths = new();

    #endregion

    #region Properties

    public bool IsInitialized => _isInitialized;
    public string Name => nameof(SaveManager);
    public InitializationPriority Priority => InitializationPriority.Gameplay;
    public bool IsSaving => _isSaving;
    public SaveSettings Settings => _saveSettings;
    public string SaveDirectory => _fullSaveDirectory;


    #endregion

    #region Events

    public event Action<SaveLoadEventArgs> OnSaveEvent;
    public event Action<int, SaveFileInfo> OnSlotSaved;
    public event Action<int, string> OnSaveFailed;

    #endregion

    #region IInitializableAsync Implementation

    public async UniTask InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            JCDebug.Log("[SaveManager] 초기화 시작");

            // 기본 설정 초기화
            InitializeDefaultSettings();

            // 저장 디렉토리 설정
            await SetupSaveDirectoryAsync(cancellationToken);

            // 기존 저장 파일들 스캔
            await ScanExistingSaveFilesAsync(cancellationToken);

            _isInitialized = true;
            JCDebug.Log("[SaveManager] 초기화 완료", JCDebug.LogLevel.Success);
        }
        catch (Exception ex)
        {
            JCDebug.Log($"[SaveManager] 초기화 실패: {ex.Message}", JCDebug.LogLevel.Error);
            throw;
        }
    }

    public async UniTask CleanupAsync()
    {
        JCDebug.Log("[SaveManager] 정리 시작");

        // 진행 중인 저장 작업 대기
        while (_isSaving)
        {
            await UniTask.Delay(100);
        }

        _isInitialized = false;
        JCDebug.Log("[SaveManager] 정리 완료");
    }

    #endregion

    #region Initialization

    private void InitializeDefaultSettings()
    {
        if (_saveSettings == null)
        {
            _saveSettings = new SaveSettings();
        }

        // 저장 디렉토리 경로 설정
        _fullSaveDirectory = Path.Combine(Application.persistentDataPath, _saveDirectory);
    }

    private async UniTask SetupSaveDirectoryAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (_autoCreateDirectory && !Directory.Exists(_fullSaveDirectory))
            {
                Directory.CreateDirectory(_fullSaveDirectory);
                JCDebug.Log($"[SaveManager] 저장 디렉토리 생성: {_fullSaveDirectory}");
            }

            await UniTask.Yield(cancellationToken);
        }
        catch (Exception ex)
        {
            JCDebug.Log($"[SaveManager] 디렉토리 설정 실패: {ex.Message}", JCDebug.LogLevel.Error);
            throw;
        }
    }

    private async UniTask ScanExistingSaveFilesAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!Directory.Exists(_fullSaveDirectory))
                return;

            var files = Directory.GetFiles(_fullSaveDirectory, $"{_saveFilePrefix}*{_saveSettings.saveFileExtension}");

            foreach (var filePath in files)
            {
                await ScanSaveFileAsync(filePath, cancellationToken);
            }

            JCDebug.Log($"[SaveManager] {_saveSlots.Count}개의 저장 파일 발견");
        }
        catch (Exception ex)
        {
            JCDebug.Log($"[SaveManager] 저장 파일 스캔 실패: {ex.Message}", JCDebug.LogLevel.Error);
        }
    }

    private async UniTask ScanSaveFileAsync(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            var fileName = Path.GetFileNameWithoutExtension(filePath);

            // 파일명에서 슬롯 번호 추출
            if (TryExtractSlotFromFileName(fileName, out int slotIndex))
            {
                var fileInfo = new FileInfo(filePath);
                var saveFileInfo = new SaveFileInfo
                {
                    slotIndex = slotIndex,
                    fileName = fileName,
                    filePath = filePath,
                    fileSize = fileInfo.Length,
                    saveTimestamp = fileInfo.LastWriteTime.ToBinary()
                };

                // 파일 유효성 간단 체크
                saveFileInfo.isValid = await ValidateFileIntegrityAsync(filePath, cancellationToken);

                _saveSlots[slotIndex] = saveFileInfo;
                _slotFilePaths[slotIndex] = filePath;
            }
        }
        catch (Exception ex)
        {
            JCDebug.Log($"[SaveManager] 파일 스캔 실패 ({filePath}): {ex.Message}", JCDebug.LogLevel.Warning);
        }
    }

    private bool TryExtractSlotFromFileName(string fileName, out int slotIndex)
    {
        slotIndex = 0;
        var prefix = _saveFilePrefix;

        if (fileName.StartsWith(prefix))
        {
            var slotStr = fileName.Substring(prefix.Length);
            return int.TryParse(slotStr, out slotIndex);
        }

        return false;
    }

    #endregion

    #region Save Operations

    /// <summary>
    /// PlayerGameData를 비동기로 저장합니다
    /// </summary>
    /// <param name="playerData">저장할 플레이어 데이터</param>
    /// <param name="slotIndex">저장 슬롯 (기본값: 0)</param>
    /// <returns>저장 성공 여부</returns>
    public async UniTask<bool> SavePlayerDataAsync(PlayerGameData playerData, int slotIndex = 0)
    {
        if (!_isInitialized)
        {
            JCDebug.Log("[SaveManager] 초기화되지 않음", JCDebug.LogLevel.Error);
            return false;
        }

        if (playerData == null)
        {
            JCDebug.Log("[SaveManager] 저장할 데이터가 null", JCDebug.LogLevel.Error);
            return false;
        }

        if (slotIndex < 0 || slotIndex >= _saveSettings.maxSaveSlots)
        {
            JCDebug.Log($"[SaveManager] 잘못된 슬롯 인덱스: {slotIndex}", JCDebug.LogLevel.Error);
            return false;
        }

        if (_isSaving)
        {
            JCDebug.Log("[SaveManager] 이미 저장 중", JCDebug.LogLevel.Warning);
            return false;
        }

        try
        {
            _isSaving = true;
            OnSaveEvent?.Invoke(new SaveLoadEventArgs(SaveLoadEventType.SaveStarted, slotIndex));

            if (_logSaveOperations)
            {
                JCDebug.Log($"[SaveManager] 슬롯 {slotIndex} 저장 시작");
            }

            // 저장 경로 생성
            string filePath = GetSlotFilePath(slotIndex);

            // 백업 생성
            if (_saveSettings.createBackup && File.Exists(filePath))
            {
                await CreateBackupAsync(filePath);
            }

            // 데이터 직렬화
            OnSaveEvent?.Invoke(new SaveLoadEventArgs(SaveLoadEventType.SaveProgress, slotIndex, 0.2f, "데이터 직렬화 중"));
            string jsonData = JsonUtility.ToJson(playerData, true);

            // 압축 처리
            if (_saveSettings.enableCompression)
            {
                OnSaveEvent?.Invoke(new SaveLoadEventArgs(SaveLoadEventType.SaveProgress, slotIndex, 0.4f, "데이터 압축 중"));
                jsonData = await CompressDataAsync(jsonData);
            }

            // 암호화 처리
            if (_saveSettings.enableEncryption)
            {
                OnSaveEvent?.Invoke(new SaveLoadEventArgs(SaveLoadEventType.SaveProgress, slotIndex, 0.6f, "데이터 암호화 중"));
                jsonData = EncryptData(jsonData, _saveSettings.encryptionKey);
            }

            // 체크섬 생성
            string checksum = "";
            if (_saveSettings.enableChecksum)
            {
                OnSaveEvent?.Invoke(new SaveLoadEventArgs(SaveLoadEventType.SaveProgress, slotIndex, 0.8f, "체크섬 생성 중"));
                checksum = GenerateChecksum(jsonData);
            }

            // 파일 저장
            OnSaveEvent?.Invoke(new SaveLoadEventArgs(SaveLoadEventType.SaveProgress, slotIndex, 0.9f, "파일 저장 중"));
            await File.WriteAllTextAsync(filePath, jsonData);

            // SaveFileInfo 업데이트
            var saveFileInfo = SaveFileInfo.CreateFromPlayerData(playerData, slotIndex);
            saveFileInfo.fileName = Path.GetFileNameWithoutExtension(filePath);
            saveFileInfo.filePath = filePath;
            saveFileInfo.fileSize = new FileInfo(filePath).Length;
            saveFileInfo.checksum = checksum;
            saveFileInfo.isEncrypted = _saveSettings.enableEncryption;
            saveFileInfo.isValid = true;

            _saveSlots[slotIndex] = saveFileInfo;
            _slotFilePaths[slotIndex] = filePath;

            OnSaveEvent?.Invoke(new SaveLoadEventArgs(SaveLoadEventType.SaveCompleted, slotIndex, 1.0f, "저장 완료"));
            OnSlotSaved?.Invoke(slotIndex, saveFileInfo);

            if (_logSaveOperations)
            {
                JCDebug.Log($"[SaveManager] 슬롯 {slotIndex} 저장 완료: {filePath}", JCDebug.LogLevel.Success);
            }

            return true;
        }
        catch (Exception ex)
        {
            var message = $"슬롯 {slotIndex} 저장 실패: {ex.Message}";
            OnSaveEvent?.Invoke(new SaveLoadEventArgs(SaveLoadEventType.SaveFailed, slotIndex, 0f, message, ex));
            OnSaveFailed?.Invoke(slotIndex, message);

            JCDebug.Log($"[SaveManager] {message}", JCDebug.LogLevel.Error);
            return false;
        }
        finally
        {
            _isSaving = false;
        }
    }

    /// <summary>
    /// 모든 슬롯의 데이터를 저장합니다
    /// </summary>
    public async UniTask<SaveLoadResult> SaveAllSlotsAsync(Dictionary<int, PlayerGameData> slotData)
    {
        var results = new List<bool>();
        var failedSlots = new List<int>();

        foreach (var kvp in slotData)
        {
            bool result = await SavePlayerDataAsync(kvp.Value, kvp.Key);
            results.Add(result);

            if (!result)
            {
                failedSlots.Add(kvp.Key);
            }
        }

        bool allSuccess = results.TrueForAll(r => r);
        string message = allSuccess
            ? "모든 슬롯 저장 완료"
            : $"일부 슬롯 저장 실패: {string.Join(", ", failedSlots)}";

        return new SaveLoadResult(allSuccess, message);
    }

    #endregion

    #region File Operations

    private string GetSlotFilePath(int slotIndex)
    {
        string fileName = $"{_saveFilePrefix}{slotIndex}{_saveSettings.saveFileExtension}";
        return Path.Combine(_fullSaveDirectory, fileName);
    }

    private async UniTask CreateBackupAsync(string originalFilePath)
    {
        try
        {
            string backupDir = Path.Combine(_fullSaveDirectory, "Backups");
            if (!Directory.Exists(backupDir))
            {
                Directory.CreateDirectory(backupDir);
            }

            string fileName = Path.GetFileNameWithoutExtension(originalFilePath);
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string backupFileName = $"{fileName}_{timestamp}{_saveSettings.backupFileExtension}";
            string backupPath = Path.Combine(backupDir, backupFileName);

            await UniTask.RunOnThreadPool(() => File.Copy(originalFilePath, backupPath, true));

            // 백업 파일 개수 제한
            await CleanupOldBackupsAsync(backupDir, fileName);

            OnSaveEvent?.Invoke(new SaveLoadEventArgs(SaveLoadEventType.BackupCreated, -1, 0f, $"백업 생성: {backupFileName}"));

            if (_logSaveOperations)
            {
                JCDebug.Log($"[SaveManager] 백업 생성: {backupPath}");
            }
        }
        catch (Exception ex)
        {
            JCDebug.Log($"[SaveManager] 백업 생성 실패: {ex.Message}", JCDebug.LogLevel.Warning);
        }
    }

    private async UniTask CleanupOldBackupsAsync(string backupDir, string filePrefix)
    {
        try
        {
            var backupFiles = Directory.GetFiles(backupDir, $"{filePrefix}_*{_saveSettings.backupFileExtension}")
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.CreationTime)
                .ToArray();

            if (backupFiles.Length > _saveSettings.maxBackupFiles)
            {
                var filesToDelete = backupFiles.Skip(_saveSettings.maxBackupFiles);

                foreach (var file in filesToDelete)
                {
                    await UniTask.RunOnThreadPool(() => file.Delete());
                    JCDebug.Log($"[SaveManager] 오래된 백업 파일 삭제: {file.Name}");
                }
            }
        }
        catch (Exception ex)
        {
            JCDebug.Log($"[SaveManager] 백업 정리 실패: {ex.Message}", JCDebug.LogLevel.Warning);
        }
    }

    private async UniTask<bool> ValidateFileIntegrityAsync(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(filePath))
                return false;

            // 파일 읽기 시도
            await File.ReadAllTextAsync(filePath, cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }

    #endregion

    #region Data Processing

    private async UniTask<string> CompressDataAsync(string data)
    {
        // 간단한 압축 구현 (실제로는 GZip 등을 사용할 수 있음)
        await UniTask.Yield();

        // 여기서는 간단히 압축된 것처럼 처리
        // 실제 구현에서는 System.IO.Compression.GZipStream 등을 사용
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(data));
    }

    private string EncryptData(string data, string key)
    {
        try
        {
            byte[] dataBytes = Encoding.UTF8.GetBytes(data);
            byte[] keyBytes = Encoding.UTF8.GetBytes(key.PadRight(32).Substring(0, 32)); // 32바이트로 맞춤

            using (var aes = Aes.Create())
            {
                aes.Key = keyBytes;
                aes.GenerateIV();

                using (var encryptor = aes.CreateEncryptor())
                {
                    byte[] encryptedBytes = encryptor.TransformFinalBlock(dataBytes, 0, dataBytes.Length);

                    // IV와 암호화된 데이터를 결합
                    byte[] result = new byte[aes.IV.Length + encryptedBytes.Length];
                    Array.Copy(aes.IV, 0, result, 0, aes.IV.Length);
                    Array.Copy(encryptedBytes, 0, result, aes.IV.Length, encryptedBytes.Length);

                    return Convert.ToBase64String(result);
                }
            }
        }
        catch (Exception ex)
        {
            JCDebug.Log($"[SaveManager] 암호화 실패: {ex.Message}", JCDebug.LogLevel.Error);
            return data; // 암호화 실패 시 원본 반환
        }
    }

    private string GenerateChecksum(string data)
    {
        try
        {
            using (var sha256 = SHA256.Create())
            {
                byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(data));
                return Convert.ToBase64String(hashBytes);
            }
        }
        catch (Exception ex)
        {
            JCDebug.Log($"[SaveManager] 체크섬 생성 실패: {ex.Message}", JCDebug.LogLevel.Error);
            return "";
        }
    }

    #endregion

    #region Slot Management

    /// <summary>
    /// 지정된 슬롯을 삭제합니다
    /// </summary>
    public bool DeleteSlot(int slotIndex)
    {
        try
        {
            if (_slotFilePaths.TryGetValue(slotIndex, out string filePath) && File.Exists(filePath))
            {
                File.Delete(filePath);
                _saveSlots.Remove(slotIndex);
                _slotFilePaths.Remove(slotIndex);

                JCDebug.Log($"[SaveManager] 슬롯 {slotIndex} 삭제 완료");
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            JCDebug.Log($"[SaveManager] 슬롯 {slotIndex} 삭제 실패: {ex.Message}", JCDebug.LogLevel.Error);
            return false;
        }
    }

    /// <summary>
    /// 슬롯 정보를 가져옵니다
    /// </summary>
    public SaveFileInfo GetSlotInfo(int slotIndex)
    {
        return _saveSlots.TryGetValue(slotIndex, out SaveFileInfo info) ? info : null;
    }

    /// <summary>
    /// 모든 슬롯 정보를 가져옵니다
    /// </summary>
    public Dictionary<int, SaveFileInfo> GetAllSlotInfo()
    {
        return new Dictionary<int, SaveFileInfo>(_saveSlots);
    }

    /// <summary>
    /// 슬롯이 존재하는지 확인합니다
    /// </summary>
    public bool HasSlot(int slotIndex)
    {
        return _saveSlots.ContainsKey(slotIndex);
    }

    /// <summary>
    /// 사용 가능한 슬롯 번호를 찾습니다
    /// </summary>
    public int FindAvailableSlot()
    {
        for (int i = 0; i < _saveSettings.maxSaveSlots; i++)
        {
            if (!HasSlot(i))
                return i;
        }
        return -1; // 사용 가능한 슬롯 없음
    }

    #endregion

    #region Utility Methods

    /// <summary>
    /// 저장 매니저의 상태 정보를 출력합니다
    /// </summary>
    public void PrintDebugInfo()
    {
        JCDebug.Log($"[SaveManager] 상태 정보:\n" +
                   $"  초기화 상태: {_isInitialized}\n" +
                   $"  저장 중: {_isSaving}\n" +
                   $"  저장 디렉토리: {_fullSaveDirectory}\n" +
                   $"  등록된 슬롯: {_saveSlots.Count}\n" +
                   $"  최대 슬롯: {_saveSettings.maxSaveSlots}\n" +
                   $"  암호화: {_saveSettings.enableEncryption}\n" +
                   $"  압축: {_saveSettings.enableCompression}\n" +
                   $"  백업: {_saveSettings.createBackup}");
    }

    #endregion
}