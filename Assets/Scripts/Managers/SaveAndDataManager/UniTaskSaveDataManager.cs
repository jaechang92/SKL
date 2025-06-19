using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
using Metamorph.Initialization;
using CustomDebug;
using Metamorph.Data;

namespace Metamorph.Managers
{
    /// <summary>
    /// UniTask 기반 세이브 데이터 매니저 (싱글톤)
    /// </summary>
    public class UniTaskSaveDataManager : SingletonManager<UniTaskSaveDataManager>, IInitializableAsync
    {
        public string Name => "Save Data Manager";

        public InitializationPriority Priority { get; set; } = InitializationPriority.Critical;


        public bool IsInitialized { get; private set; }

        private PlayerData currentPlayerData;
        private const string SAVE_FILE_NAME = "PlayerSave.json";
        private const string BACKUP_FILE_NAME = "PlayerSave_Backup.json";

        [Header("Save Configuration")]
        [SerializeField] private bool _autoSave = true;
        [SerializeField] private float _autoSaveInterval = 300f; // 5분
        [SerializeField] private bool _createBackups = true;
        [SerializeField] private int _maxBackups = 3;
        [SerializeField] private bool _encryptSaveData = false;

        private CancellationTokenSource _autoSaveCts;
        private bool _isDirty = false; // 데이터 변경 여부

        public PlayerData PlayerData => currentPlayerData;

        // 이벤트
        public event Action<PlayerData> OnPlayerDataLoaded;
        public event Action<PlayerData> OnPlayerDataSaved;
        public event Action<PlayerData> OnPlayerDataChanged;
        public event Action<string> OnSaveError;
        public event Action OnAutoSaveTriggered;

        public async UniTask InitializeAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                JCDebug.Log("[UniTaskSaveDataManager] 세이브 데이터 매니저 초기화 시작");

                // 플레이어 데이터 로드
                await LoadPlayerDataAsync(cancellationToken);

                // 자동 저장 시작
                if (_autoSave)
                {
                    StartAutoSave();
                }

                IsInitialized = true;
                JCDebug.Log("[UniTaskSaveDataManager] 세이브 데이터 매니저 초기화 완료");
            }
            catch (OperationCanceledException)
            {
                JCDebug.Log("[UniTaskSaveDataManager] 세이브 데이터 초기화가 취소됨",JCDebug.LogLevel.Warning);
                throw;
            }
            catch (Exception ex)
            {
                JCDebug.Log($"[UniTaskSaveDataManager] 세이브 데이터 초기화 실패: {ex.Message}",JCDebug.LogLevel.Error);
                throw;
            }
        }

        public async UniTask LoadPlayerDataAsync(CancellationToken cancellationToken = default)
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

                    currentPlayerData = JsonUtility.FromJson<PlayerData>(jsonData);

                    // 데이터 유효성 검증
                    await ValidatePlayerDataAsync(cancellationToken);

                    JCDebug.Log("[UniTaskSaveDataManager] 플레이어 데이터 로드 완료");
                }
                else
                {
                    // 백업 파일 확인
                    string backupPath = Path.Combine(Application.persistentDataPath, BACKUP_FILE_NAME);
                    if (File.Exists(backupPath))
                    {
                        JCDebug.Log("[UniTaskSaveDataManager] 메인 세이브 파일이 없어 백업에서 복원",JCDebug.LogLevel.Warning);
                        string backupData = await ReadFileAsync(backupPath, cancellationToken);

                        if (_encryptSaveData)
                        {
                            backupData = DecryptData(backupData);
                        }

                        currentPlayerData = JsonUtility.FromJson<PlayerData>(backupData);
                    }
                    else
                    {
                        // 새 플레이어 데이터 생성
                        currentPlayerData = CreateDefaultPlayerData();
                        JCDebug.Log("[UniTaskSaveDataManager] 새 플레이어 데이터 생성");
                    }
                }

                _isDirty = false;
                OnPlayerDataLoaded?.Invoke(currentPlayerData);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                JCDebug.Log($"[UniTaskSaveDataManager] 플레이어 데이터 로드 실패: {ex.Message}",JCDebug.LogLevel.Error);

                // 기본 데이터로 폴백
                currentPlayerData = CreateDefaultPlayerData();
                OnSaveError?.Invoke($"데이터 로드 실패, 기본 데이터로 시작: {ex.Message}");
                OnPlayerDataLoaded?.Invoke(currentPlayerData);
            }
        }

        public async UniTask SavePlayerDataAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                if (currentPlayerData == null)
                {
                    JCDebug.Log("[UniTaskSaveDataManager] 저장할 플레이어 데이터가 없습니다.",JCDebug.LogLevel.Warning);
                    return;
                }

                // 현재 플레이 시간 업데이트
                currentPlayerData.lastPlayTime = DateTime.Now.ToBinary();

                // 백업 생성
                if (_createBackups)
                {
                    await CreateBackupAsync(cancellationToken);
                }

                string jsonData = JsonUtility.ToJson(currentPlayerData, true);

                if (_encryptSaveData)
                {
                    jsonData = EncryptData(jsonData);
                }

                string savePath = Path.Combine(Application.persistentDataPath, SAVE_FILE_NAME);
                await WriteFileAsync(savePath, jsonData, cancellationToken);

                _isDirty = false;
                OnPlayerDataSaved?.Invoke(currentPlayerData);
                JCDebug.Log("[UniTaskSaveDataManager] 플레이어 데이터 저장 완료");
            }
            catch (OperationCanceledException)
            {
                JCDebug.Log("[UniTaskSaveDataManager] 플레이어 데이터 저장이 취소됨", JCDebug.LogLevel.Warning);
                throw;
            }
            catch (Exception ex)
            {
                JCDebug.Log($"[UniTaskSaveDataManager] 플레이어 데이터 저장 실패: {ex.Message}",JCDebug.LogLevel.Error);
                OnSaveError?.Invoke($"데이터 저장 실패: {ex.Message}");
                throw;
            }
        }

        private async UniTask ValidatePlayerDataAsync(CancellationToken cancellationToken)
        {
            await UniTask.Delay(TimeSpan.FromSeconds(0.1f).Milliseconds, false, PlayerLoopTiming.Update, cancellationToken);

            if (currentPlayerData == null)
            {
                throw new Exception("플레이어 데이터가 null입니다.");
            }

            // 기본값 검증 및 수정
            if (string.IsNullOrEmpty(currentPlayerData.playerName))
            {
                currentPlayerData.playerName = "Unknown Player";
            }

            if (currentPlayerData.level < 1)
            {
                currentPlayerData.level = 1;
            }

            if (currentPlayerData.experience < 0)
            {
                currentPlayerData.experience = 0;
            }

            if (currentPlayerData.gold < 0)
            {
                currentPlayerData.gold = 0;
            }

            if (currentPlayerData.unlockedStages == null)
            {
                currentPlayerData.unlockedStages = new List<int> { 0 };
            }

            if (currentPlayerData.inventory == null)
            {
                currentPlayerData.inventory = new List<ItemData>();
            }

            if (currentPlayerData.achievements == null)
            {
                currentPlayerData.achievements = new Dictionary<string, bool>();
            }

            if (currentPlayerData.statistics == null)
            {
                currentPlayerData.statistics = new Dictionary<string, int>();
            }

            JCDebug.Log("[UniTaskSaveDataManager] 플레이어 데이터 검증 완료");
        }

        private PlayerData CreateDefaultPlayerData()
        {
            return new PlayerData
            {
                playerName = "New Player",
                level = 1,
                experience = 0,
                gold = 100,
                currentStageIndex = 0,
                unlockedStages = new List<int> { 0 },
                inventory = new List<ItemData>(),
                lastPlayTime = DateTime.Now.ToBinary(),
                totalPlayTime = 0f,
                highScore = 0,
                totalDeaths = 0,
                totalKills = 0,
                achievements = new Dictionary<string, bool>(),
                statistics = new Dictionary<string, int>()
            };
        }

        private void StartAutoSave()
        {
            _autoSaveCts = new CancellationTokenSource();
            AutoSaveLoop(_autoSaveCts.Token).Forget();
        }

        private async UniTaskVoid AutoSaveLoop(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    await UniTask.Delay(TimeSpan.FromSeconds(_autoSaveInterval).Milliseconds, false, PlayerLoopTiming.Update, cancellationToken);

                    if (_isDirty)
                    {
                        OnAutoSaveTriggered?.Invoke();
                        await SavePlayerDataAsync(cancellationToken);
                        JCDebug.Log("[UniTaskSaveDataManager] 자동 저장 완료");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                JCDebug.Log("[UniTaskSaveDataManager] 자동 저장 루프가 취소됨");
            }
            catch (Exception ex)
            {
                JCDebug.Log($"[UniTaskSaveDataManager] 자동 저장 오류: {ex.Message}",JCDebug.LogLevel.Error);
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
                JCDebug.Log($"[UniTaskSaveDataManager] 백업 생성 실패: {ex.Message}", JCDebug.LogLevel.Warning);
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

        // 편의 메서드들
        public void MarkDirty()
        {
            _isDirty = true;
            OnPlayerDataChanged?.Invoke(currentPlayerData);
        }

        public void AddExperience(int amount)
        {
            if (currentPlayerData != null)
            {
                currentPlayerData.experience += amount;
                MarkDirty();
            }
        }

        public void AddGold(int amount)
        {
            if (currentPlayerData != null)
            {
                currentPlayerData.gold = Mathf.Max(0, currentPlayerData.gold + amount);
                MarkDirty();
            }
        }

        public void UnlockStage(int stageIndex)
        {
            if (currentPlayerData != null && !currentPlayerData.unlockedStages.Contains(stageIndex))
            {
                currentPlayerData.unlockedStages.Add(stageIndex);
                MarkDirty();
            }
        }

        public void AddItem(string itemId, int quantity)
        {
            if (currentPlayerData?.inventory == null) return;

            var existingItem = currentPlayerData.inventory.Find(item => item.itemId == itemId);
            if (existingItem != null)
            {
                existingItem.quantity += quantity;
            }
            else
            {
                currentPlayerData.inventory.Add(new ItemData(itemId, quantity));
            }
            MarkDirty();
        }

        public bool HasItem(string itemId, int requiredQuantity = 1)
        {
            if (currentPlayerData?.inventory == null) return false;

            var item = currentPlayerData.inventory.Find(i => i.itemId == itemId);
            return item != null && item.quantity >= requiredQuantity;
        }

        public void SetAchievement(string achievementId, bool unlocked = true)
        {
            if (currentPlayerData?.achievements == null) return;

            currentPlayerData.achievements[achievementId] = unlocked;
            MarkDirty();
        }

        public bool HasAchievement(string achievementId)
        {
            return currentPlayerData?.achievements?.ContainsKey(achievementId) == true &&
                   currentPlayerData.achievements[achievementId];
        }

        protected override void OnDestroy()
        {
            _autoSaveCts?.Cancel();
            _autoSaveCts?.Dispose();
            base.OnDestroy();
        }
    }
}