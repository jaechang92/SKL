using CustomDebug;
using Cysharp.Threading.Tasks;
using Metamorph.Core;
using Metamorph.Initialization;
using PlayerData;
using SaveSystem;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using UnityEngine;

/// <summary>
/// 플레이어 데이터의 로드를 담당하는 매니저
/// 암호 해독, 압축 해제, 유효성 검증, 버전 호환성 체크 기능 제공
/// </summary>
public class LoadManager : SingletonManager<LoadManager>, IInitializableAsync
{
    #region Fields

    [Header("Load Settings")]
    [SerializeField] private LoadSettings _loadSettings;
    [SerializeField] private bool _logLoadOperations = true;
    [SerializeField] private bool _autoRepairCorrupted = true;

    [Header("Cache Settings")]
    [SerializeField] private bool _enableCaching = false;
    [SerializeField] private int _maxCacheSize = 3;

    // 로드 상태
    private bool _isInitialized = false;
    private bool _isLoading = false;

    // 캐시 시스템
    private readonly Dictionary<int, PlayerGameData> _loadCache = new();
    private readonly Dictionary<int, DateTime> _cacheTimestamps = new();

    // 파일 정보 캐시
    private Dictionary<int, SaveFileInfo> _fileInfoCache = new();
    private DateTime _lastFileListUpdate;

    // SaveManager 참조
    private SaveManager _saveManager;

    #endregion

    #region Properties

    public bool IsInitialized => _isInitialized;
    public string Name => nameof(LoadManager);
    public InitializationPriority Priority => InitializationPriority.Gameplay;
    public bool IsLoading => _isLoading;
    public LoadSettings Settings => _loadSettings;


    #endregion

    #region Events

    public event Action<SaveLoadEventArgs> OnLoadEvent;
    public event Action<int, PlayerGameData> OnSlotLoaded;
    public event Action<int, string> OnLoadFailed;
    public event Action<int, string> OnFileCorrupted;

    #endregion

    #region IInitializableAsync Implementation

    public async UniTask InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            JCDebug.Log("[LoadManager] 초기화 시작");

            // 기본 설정 초기화
            InitializeDefaultSettings();

            // SaveManager 참조 설정
            await SetupSaveManagerReferenceAsync(cancellationToken);

            // 파일 목록 미리 로드
            if (_loadSettings.preloadFileList)
            {
                await RefreshFileListAsync(cancellationToken);
            }

            _isInitialized = true;
            JCDebug.Log("[LoadManager] 초기화 완료", JCDebug.LogLevel.Success);
        }
        catch (Exception ex)
        {
            JCDebug.Log($"[LoadManager] 초기화 실패: {ex.Message}", JCDebug.LogLevel.Error);
            throw;
        }
    }

    public async UniTask CleanupAsync()
    {
        JCDebug.Log("[LoadManager] 정리 시작");

        // 진행 중인 로드 작업 대기
        while (_isLoading)
        {
            await UniTask.Delay(100);
        }

        // 캐시 정리
        ClearCache();

        _isInitialized = false;
        JCDebug.Log("[LoadManager] 정리 완료");
    }

    #endregion

    #region Initialization

    private void InitializeDefaultSettings()
    {
        if (_loadSettings == null)
        {
            _loadSettings = new LoadSettings();
        }

        // 호환 버전 목록 기본값 설정
        if (_loadSettings.compatibleVersions.Count == 0)
        {
            _loadSettings.compatibleVersions.Add(Application.version);
        }
    }

    private async UniTask SetupSaveManagerReferenceAsync(CancellationToken cancellationToken)
    {
        // SaveManager 참조 대기 (최대 5초)
        int attempts = 0;
        while (_saveManager == null && attempts < 50)
        {
            _saveManager = SaveManager.Instance;
            if (_saveManager == null)
            {
                await UniTask.Delay(100, cancellationToken: cancellationToken);
                attempts++;
            }
        }

        if (_saveManager == null)
        {
            JCDebug.Log("[LoadManager] SaveManager를 찾을 수 없음", JCDebug.LogLevel.Warning);
        }
        else
        {
            JCDebug.Log("[LoadManager] SaveManager 참조 설정 완료");
        }
    }

    private async UniTask RefreshFileListAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (_saveManager?.IsInitialized == true)
            {
                _fileInfoCache = _saveManager.GetAllSlotInfo();
                _lastFileListUpdate = DateTime.Now;

                JCDebug.Log($"[LoadManager] 파일 목록 갱신 완료: {_fileInfoCache.Count}개 파일");
            }

            await UniTask.Yield(cancellationToken);
        }
        catch (Exception ex)
        {
            JCDebug.Log($"[LoadManager] 파일 목록 갱신 실패: {ex.Message}", JCDebug.LogLevel.Error);
        }
    }

    #endregion

    #region Load Operations

    /// <summary>
    /// PlayerGameData를 비동기로 로드합니다
    /// </summary>
    /// <param name="slotIndex">로드할 슬롯 번호 (기본값: 0)</param>
    /// <returns>로드된 플레이어 데이터 또는 null</returns>
    public async UniTask<PlayerGameData> LoadPlayerDataAsync(int slotIndex = 0)
    {
        if (!_isInitialized)
        {
            JCDebug.Log("[LoadManager] 초기화되지 않음", JCDebug.LogLevel.Error);
            return null;
        }

        if (_isLoading)
        {
            JCDebug.Log("[LoadManager] 이미 로드 중", JCDebug.LogLevel.Warning);
            return null;
        }

        // 캐시에서 확인
        if (_enableCaching && _loadCache.TryGetValue(slotIndex, out PlayerGameData cachedData))
        {
            if (_logLoadOperations)
            {
                JCDebug.Log($"[LoadManager] 슬롯 {slotIndex} 캐시에서 로드");
            }
            return cachedData;
        }

        try
        {
            _isLoading = true;
            OnLoadEvent?.Invoke(new SaveLoadEventArgs(SaveLoadEventType.LoadStarted, slotIndex));

            if (_logLoadOperations)
            {
                JCDebug.Log($"[LoadManager] 슬롯 {slotIndex} 로드 시작");
            }

            // 슬롯 정보 확인
            var slotInfo = await GetSlotInfoAsync(slotIndex);
            if (slotInfo == null || !File.Exists(slotInfo.filePath))
            {
                JCDebug.Log($"[LoadManager] 슬롯 {slotIndex} 파일이 존재하지 않음", JCDebug.LogLevel.Warning);
                return null;
            }

            // 파일 유효성 검증
            OnLoadEvent?.Invoke(new SaveLoadEventArgs(SaveLoadEventType.LoadProgress, slotIndex, 0.1f, "파일 유효성 검증 중"));

            if (_loadSettings.validateOnLoad)
            {
                var validationResult = await ValidateFileAsync(slotInfo);
                if (!validationResult.Success)
                {
                    if (_loadSettings.attemptRepair)
                    {
                        var repairResult = await AttemptRepairAsync(slotInfo);
                        if (!repairResult.Success)
                        {
                            OnFileCorrupted?.Invoke(slotIndex, repairResult.Message);

                            if (_loadSettings.skipCorruptedFiles)
                            {
                                JCDebug.Log($"[LoadManager] 손상된 파일 건너뜀: 슬롯 {slotIndex}", JCDebug.LogLevel.Warning);
                                return null;
                            }
                        }
                    }
                    else
                    {
                        OnFileCorrupted?.Invoke(slotIndex, validationResult.Message);
                        return null;
                    }
                }
            }

            // 파일 읽기
            OnLoadEvent?.Invoke(new SaveLoadEventArgs(SaveLoadEventType.LoadProgress, slotIndex, 0.3f, "파일 읽기 중"));
            string fileContent = await File.ReadAllTextAsync(slotInfo.filePath);

            // 암호 해독
            if (slotInfo.isEncrypted && _saveManager?.Settings.enableEncryption == true)
            {
                OnLoadEvent?.Invoke(new SaveLoadEventArgs(SaveLoadEventType.LoadProgress, slotIndex, 0.5f, "데이터 암호 해독 중"));
                fileContent = DecryptData(fileContent, _saveManager.Settings.encryptionKey);

                if (string.IsNullOrEmpty(fileContent))
                {
                    OnLoadFailed?.Invoke(slotIndex, "암호 해독 실패");
                    return null;
                }
            }

            // 압축 해제
            if (_saveManager?.Settings.enableCompression == true)
            {
                OnLoadEvent?.Invoke(new SaveLoadEventArgs(SaveLoadEventType.LoadProgress, slotIndex, 0.7f, "데이터 압축 해제 중"));
                fileContent = await DecompressDataAsync(fileContent);
            }

            // JSON 역직렬화
            OnLoadEvent?.Invoke(new SaveLoadEventArgs(SaveLoadEventType.LoadProgress, slotIndex, 0.9f, "데이터 역직렬화 중"));
            PlayerGameData playerData = JsonUtility.FromJson<PlayerGameData>(fileContent);

            if (playerData == null)
            {
                OnLoadFailed?.Invoke(slotIndex, "데이터 역직렬화 실패");
                return null;
            }

            // 버전 호환성 체크
            if (!IsVersionCompatible(playerData.saveVersion))
            {
                var message = $"호환되지 않는 버전: {playerData.saveVersion}";
                OnLoadFailed?.Invoke(slotIndex, message);

                if (!_loadSettings.allowOlderVersions && !_loadSettings.allowNewerVersions)
                {
                    return null;
                }
            }

            // 데이터 후처리
            await PostProcessLoadedDataAsync(playerData, slotIndex);

            // 캐시에 저장
            if (_enableCaching)
            {
                AddToCache(slotIndex, playerData);
            }

            OnLoadEvent?.Invoke(new SaveLoadEventArgs(SaveLoadEventType.LoadCompleted, slotIndex, 1.0f, "로드 완료"));
            OnSlotLoaded?.Invoke(slotIndex, playerData);

            if (_logLoadOperations)
            {
                JCDebug.Log($"[LoadManager] 슬롯 {slotIndex} 로드 완료", JCDebug.LogLevel.Success);
            }

            return playerData;
        }
        catch (Exception ex)
        {
            var message = $"슬롯 {slotIndex} 로드 실패: {ex.Message}";
            OnLoadEvent?.Invoke(new SaveLoadEventArgs(SaveLoadEventType.LoadFailed, slotIndex, 0f, message, ex));
            OnLoadFailed?.Invoke(slotIndex, message);

            JCDebug.Log($"[LoadManager] {message}", JCDebug.LogLevel.Error);
            return null;
        }
        finally
        {
            _isLoading = false;
        }
    }

    /// <summary>
    /// 모든 슬롯의 데이터를 로드합니다
    /// </summary>
    public async UniTask<Dictionary<int, PlayerGameData>> LoadAllSlotsAsync()
    {
        var result = new Dictionary<int, PlayerGameData>();
        var slotInfos = await GetAllSlotInfoAsync();

        foreach (var kvp in slotInfos)
        {
            var playerData = await LoadPlayerDataAsync(kvp.Key);
            if (playerData != null)
            {
                result[kvp.Key] = playerData;
            }
        }

        JCDebug.Log($"[LoadManager] {result.Count}개 슬롯 로드 완료");
        return result;
    }

    /// <summary>
    /// 가장 최근 저장된 슬롯을 자동으로 로드합니다
    /// </summary>
    public async UniTask<PlayerGameData> LoadMostRecentSlotAsync()
    {
        var slotInfos = await GetAllSlotInfoAsync();

        if (slotInfos.Count == 0)
        {
            JCDebug.Log("[LoadManager] 로드할 슬롯이 없음", JCDebug.LogLevel.Warning);
            return null;
        }

        // 가장 최근 저장 시간 기준으로 정렬
        var mostRecentSlot = slotInfos.Values
            .Where(info => info.isValid)
            .OrderByDescending(info => info.saveTimestamp)
            .First();

        JCDebug.Log($"[LoadManager] 가장 최근 슬롯 로드: {mostRecentSlot.slotIndex}");
        return await LoadPlayerDataAsync(mostRecentSlot.slotIndex);
    }

    #endregion

    #region File Information

    /// <summary>
    /// 슬롯 정보를 비동기로 가져옵니다
    /// </summary>
    public async UniTask<SaveFileInfo> GetSlotInfoAsync(int slotIndex)
    {
        // 캐시 확인
        if (_fileInfoCache.TryGetValue(slotIndex, out SaveFileInfo cachedInfo))
        {
            return cachedInfo;
        }

        // SaveManager에서 가져오기
        if (_saveManager?.IsInitialized == true)
        {
            var info = _saveManager.GetSlotInfo(slotIndex);
            if (info != null)
            {
                _fileInfoCache[slotIndex] = info;
                return info;
            }
        }

        await UniTask.Yield();
        return null;
    }

    /// <summary>
    /// 모든 슬롯 정보를 비동기로 가져옵니다
    /// </summary>
    public async UniTask<Dictionary<int, SaveFileInfo>> GetAllSlotInfoAsync()
    {
        if (_saveManager?.IsInitialized == true)
        {
            _fileInfoCache = _saveManager.GetAllSlotInfo();
        }

        await UniTask.Yield();
        return new Dictionary<int, SaveFileInfo>(_fileInfoCache);
    }

    /// <summary>
    /// 슬롯이 존재하는지 확인합니다
    /// </summary>
    public async UniTask<bool> HasSlotAsync(int slotIndex)
    {
        var slotInfo = await GetSlotInfoAsync(slotIndex);
        return slotInfo != null && File.Exists(slotInfo.filePath);
    }

    #endregion

    #region File Validation

    private async UniTask<SaveLoadResult> ValidateFileAsync(SaveFileInfo fileInfo)
    {
        try
        {
            OnLoadEvent?.Invoke(new SaveLoadEventArgs(SaveLoadEventType.ValidationStarted, fileInfo.slotIndex));

            // 파일 존재 확인
            if (!File.Exists(fileInfo.filePath))
            {
                return SaveLoadResult.CreateFailure("파일이 존재하지 않습니다");
            }

            // 파일 크기 확인
            var fileInfo2 = new FileInfo(fileInfo.filePath);
            if (fileInfo2.Length == 0)
            {
                return SaveLoadResult.CreateFailure("파일이 비어있습니다");
            }

            // 체크섬 검증
            if (_saveManager?.Settings.enableChecksum == true && !string.IsNullOrEmpty(fileInfo.checksum))
            {
                string fileContent = await File.ReadAllTextAsync(fileInfo.filePath);
                string calculatedChecksum = GenerateChecksum(fileContent);

                if (calculatedChecksum != fileInfo.checksum)
                {
                    return SaveLoadResult.CreateFailure("체크섬 불일치 - 파일이 손상되었을 수 있습니다");
                }
            }

            // JSON 형식 간단 검증
            try
            {
                string content = await File.ReadAllTextAsync(fileInfo.filePath);

                // 암호화된 경우 검증 스킵
                if (!fileInfo.isEncrypted)
                {
                    // 압축된 경우 해제 후 검증
                    if (_saveManager?.Settings.enableCompression == true)
                    {
                        content = await DecompressDataAsync(content);
                    }

                    // JSON 파싱 테스트
                    JsonUtility.FromJson<PlayerGameData>(content);
                }
            }
            catch (Exception ex)
            {
                return SaveLoadResult.CreateFailure($"파일 형식 오류: {ex.Message}");
            }

            OnLoadEvent?.Invoke(new SaveLoadEventArgs(SaveLoadEventType.ValidationCompleted, fileInfo.slotIndex));
            return SaveLoadResult.CreateSuccess("파일 유효성 검증 완료");
        }
        catch (Exception ex)
        {
            return SaveLoadResult.CreateFailure($"검증 중 오류 발생: {ex.Message}", ex);
        }
    }

    private async UniTask<SaveLoadResult> AttemptRepairAsync(SaveFileInfo fileInfo)
    {
        try
        {
            JCDebug.Log($"[LoadManager] 슬롯 {fileInfo.slotIndex} 파일 복구 시도");

            // 백업 파일에서 복구 시도
            if (_loadSettings.loadFromBackup)
            {
                var backupResult = await LoadFromBackupAsync(fileInfo);
                if (backupResult.Success)
                {
                    return SaveLoadResult.CreateSuccess("백업에서 복구 완료");
                }
            }

            // 자동 복구 로직 (간단한 JSON 수정 등)
            if (_autoRepairCorrupted)
            {
                var autoRepairResult = await AutoRepairFileAsync(fileInfo);
                if (autoRepairResult.Success)
                {
                    return SaveLoadResult.CreateSuccess("자동 복구 완료");
                }
            }

            return SaveLoadResult.CreateFailure("복구 실패");
        }
        catch (Exception ex)
        {
            return SaveLoadResult.CreateFailure($"복구 중 오류: {ex.Message}", ex);
        }
    }

    private async UniTask<SaveLoadResult> LoadFromBackupAsync(SaveFileInfo fileInfo)
    {
        try
        {
            string backupDir = Path.Combine(Path.GetDirectoryName(fileInfo.filePath), "Backups");
            if (!Directory.Exists(backupDir))
            {
                return SaveLoadResult.CreateFailure("백업 디렉토리가 없습니다");
            }

            string fileName = Path.GetFileNameWithoutExtension(fileInfo.filePath);
            var backupFiles = Directory.GetFiles(backupDir, $"{fileName}_*.bak")
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.CreationTime)
                .ToArray();

            if (backupFiles.Length == 0)
            {
                return SaveLoadResult.CreateFailure("백업 파일이 없습니다");
            }

            // 가장 최근 백업 파일 사용
            var latestBackup = backupFiles[0];
            await UniTask.RunOnThreadPool(() => File.Copy(latestBackup.FullName, fileInfo.filePath, true));

            JCDebug.Log($"[LoadManager] 백업에서 복구: {latestBackup.Name}");
            return SaveLoadResult.CreateSuccess();
        }
        catch (Exception ex)
        {
            return SaveLoadResult.CreateFailure($"백업 로드 실패: {ex.Message}", ex);
        }
    }

    private async UniTask<SaveLoadResult> AutoRepairFileAsync(SaveFileInfo fileInfo)
    {
        // 간단한 자동 복구 로직 (실제로는 더 복잡한 로직 필요)
        await UniTask.Yield();
        return SaveLoadResult.CreateFailure("자동 복구 미구현");
    }

    #endregion

    #region Data Processing

    private async UniTask<string> DecompressDataAsync(string compressedData)
    {
        // 간단한 압축 해제 구현
        await UniTask.Yield();

        try
        {
            byte[] compressedBytes = Convert.FromBase64String(compressedData);
            return Encoding.UTF8.GetString(compressedBytes);
        }
        catch (Exception ex)
        {
            JCDebug.Log($"[LoadManager] 압축 해제 실패: {ex.Message}", JCDebug.LogLevel.Error);
            return compressedData; // 실패 시 원본 반환
        }
    }

    private string DecryptData(string encryptedData, string key)
    {
        try
        {
            byte[] dataBytes = Convert.FromBase64String(encryptedData);
            byte[] keyBytes = Encoding.UTF8.GetBytes(key.PadRight(32).Substring(0, 32));

            using (var aes = Aes.Create())
            {
                aes.Key = keyBytes;

                // IV는 데이터 앞부분에 포함됨
                byte[] iv = new byte[aes.BlockSize / 8];
                byte[] encryptedBytes = new byte[dataBytes.Length - iv.Length];

                Array.Copy(dataBytes, 0, iv, 0, iv.Length);
                Array.Copy(dataBytes, iv.Length, encryptedBytes, 0, encryptedBytes.Length);

                aes.IV = iv;

                using (var decryptor = aes.CreateDecryptor())
                {
                    byte[] decryptedBytes = decryptor.TransformFinalBlock(encryptedBytes, 0, encryptedBytes.Length);
                    return Encoding.UTF8.GetString(decryptedBytes);
                }
            }
        }
        catch (Exception ex)
        {
            JCDebug.Log($"[LoadManager] 암호 해독 실패: {ex.Message}", JCDebug.LogLevel.Error);
            return ""; // 실패 시 빈 문자열 반환
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
            JCDebug.Log($"[LoadManager] 체크섬 생성 실패: {ex.Message}", JCDebug.LogLevel.Error);
            return "";
        }
    }

    private bool IsVersionCompatible(string saveVersion)
    {
        try
        {
            var currentVersion = new Version(Application.version);
            var fileVersion = new Version(saveVersion);

            // 호환 버전 목록에 있는지 확인
            if (_loadSettings.compatibleVersions.Contains(saveVersion))
            {
                return true;
            }

            // 버전 비교
            int comparison = currentVersion.CompareTo(fileVersion);

            if (comparison == 0) return true; // 동일 버전
            if (comparison > 0) return _loadSettings.allowOlderVersions; // 구버전
            if (comparison < 0) return _loadSettings.allowNewerVersions; // 신버전

            return false;
        }
        catch
        {
            // 버전 파싱 실패 시 기본적으로 허용
            return true;
        }
    }

    private async UniTask PostProcessLoadedDataAsync(PlayerGameData playerData, int slotIndex)
    {
        // 로드된 데이터 후처리 (통계 업데이트, 무결성 체크 등)
        if (playerData?.stats != null)
        {
            playerData.stats.lastPlayDate = DateTime.Now;
        }

        await UniTask.Yield();
    }

    #endregion

    #region Cache Management

    private void AddToCache(int slotIndex, PlayerGameData data)
    {
        if (_loadCache.Count >= _maxCacheSize)
        {
            // 가장 오래된 캐시 제거
            var oldestEntry = _cacheTimestamps.OrderBy(kvp => kvp.Value).First();
            _loadCache.Remove(oldestEntry.Key);
            _cacheTimestamps.Remove(oldestEntry.Key);
        }

        _loadCache[slotIndex] = data;
        _cacheTimestamps[slotIndex] = DateTime.Now;
    }

    private void ClearCache()
    {
        _loadCache.Clear();
        _cacheTimestamps.Clear();
        _fileInfoCache.Clear();

        JCDebug.Log("[LoadManager] 캐시 정리 완료");
    }

    /// <summary>
    /// 특정 슬롯의 캐시를 제거합니다
    /// </summary>
    public void InvalidateCache(int slotIndex)
    {
        _loadCache.Remove(slotIndex);
        _cacheTimestamps.Remove(slotIndex);
        _fileInfoCache.Remove(slotIndex);
    }

    #endregion

    #region Utility Methods

    /// <summary>
    /// 로드 매니저의 상태 정보를 출력합니다
    /// </summary>
    public void PrintDebugInfo()
    {
        JCDebug.Log($"[LoadManager] 상태 정보:\n" +
                   $"  초기화 상태: {_isInitialized}\n" +
                   $"  로드 중: {_isLoading}\n" +
                   $"  캐시 활성화: {_enableCaching}\n" +
                   $"  캐시된 슬롯: {_loadCache.Count}/{_maxCacheSize}\n" +
                   $"  파일 정보 캐시: {_fileInfoCache.Count}\n" +
                   $"  자동 복구: {_autoRepairCorrupted}\n" +
                   $"  구버전 허용: {_loadSettings.allowOlderVersions}\n" +
                   $"  신버전 허용: {_loadSettings.allowNewerVersions}");
    }

    #endregion
}