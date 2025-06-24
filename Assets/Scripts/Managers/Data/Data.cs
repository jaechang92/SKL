using PlayerData;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace PlayerData
{
    /// <summary>
    /// 전체 플레이어 데이터를 포함하는 루트 클래스
    /// </summary>
    [Serializable]
    public class PlayerGameData
    {
        [Header("Core Data")]
        public PlayerProfile profile;
        public GameProgress progress;
        public SkillConfiguration skills;
        public InventoryData inventory;
        public GameStatistics stats;

        [Header("System Data")]
        public string saveVersion;
        public long saveTimestamp;
        public Vector3 lastSavePosition;
        public string lastSaveSceneName;

        public PlayerGameData()
        {
            profile = new PlayerProfile();
            progress = new GameProgress();
            skills = new SkillConfiguration();
            inventory = new InventoryData();
            stats = new GameStatistics();

            saveVersion = Application.version;
            saveTimestamp = DateTime.Now.ToBinary();
            lastSavePosition = Vector3.zero;
            lastSaveSceneName = "";
        }
    }

    /// <summary>
    /// 플레이어 기본 프로필 정보
    /// </summary>
    [Serializable]
    public class PlayerProfile
    {
        [Header("Basic Info")]
        public string playerName;
        public int level;
        public long experience;
        public long experienceToNext;

        [Header("Health & Status")]
        public int currentHealth;
        public int maxHealth;
        public int currentMana;
        public int maxMana;

        [Header("Attributes")]
        public int strength;
        public int agility;
        public int intelligence;
        public int vitality;
        public int availableAttributePoints;

        public PlayerProfile()
        {
            playerName = "Unknown";
            level = 1;
            experience = 0;
            experienceToNext = 100;

            currentHealth = maxHealth = 100;
            currentMana = maxMana = 50;

            strength = agility = intelligence = vitality = 10;
            availableAttributePoints = 0;
        }
    }

    /// <summary>
    /// 게임 진행 상황 데이터
    /// </summary>
    [Serializable]
    public class GameProgress
    {
        [Header("Story Progress")]
        public int currentChapter;
        public int currentStage;
        public List<string> completedStages;
        public List<string> unlockedAreas;

        [Header("Save Points")]
        public List<string> discoveredSavePoints;
        public string lastActiveSavePoint;

        [Header("Collectibles")]
        public List<string> collectedItems;
        public List<string> foundSecrets;
        public int totalSecretsFound;

        [Header("Boss Progress")]
        public List<string> defeatedBosses;
        public Dictionary<string, int> bossAttempts;

        public GameProgress()
        {
            currentChapter = 1;
            currentStage = 1;
            completedStages = new List<string>();
            unlockedAreas = new List<string>();

            discoveredSavePoints = new List<string>();
            lastActiveSavePoint = "";

            collectedItems = new List<string>();
            foundSecrets = new List<string>();
            totalSecretsFound = 0;

            defeatedBosses = new List<string>();
            bossAttempts = new Dictionary<string, int>();
        }
    }

    /// <summary>
    /// 스킬 구성 및 업그레이드 정보
    /// </summary>
    [Serializable]
    public class SkillConfiguration
    {
        [Header("Active Skills")]
        public List<EquippedSkill> equippedSkills;
        public int maxActiveSkills;

        [Header("Learned Skills")]
        public List<string> learnedSkillIds;
        public Dictionary<string, int> skillLevels;
        public Dictionary<string, float> skillCooldowns;

        [Header("Skill Points")]
        public int availableSkillPoints;
        public int totalSkillPointsEarned;

        [Header("Skill Trees")]
        public Dictionary<string, SkillTreeProgress> skillTreesProgress;

        public SkillConfiguration()
        {
            equippedSkills = new List<EquippedSkill>();
            maxActiveSkills = 4;

            learnedSkillIds = new List<string>();
            skillLevels = new Dictionary<string, int>();
            skillCooldowns = new Dictionary<string, float>();

            availableSkillPoints = 0;
            totalSkillPointsEarned = 0;

            skillTreesProgress = new Dictionary<string, SkillTreeProgress>();
        }
    }

    /// <summary>
    /// 장착된 스킬 정보
    /// </summary>
    [Serializable]
    public class EquippedSkill
    {
        public string skillId;
        public int slotIndex;
        public int skillLevel;
        public KeyCode keyBinding;

        public EquippedSkill(string id, int slot, int level = 1, KeyCode key = KeyCode.None)
        {
            skillId = id;
            slotIndex = slot;
            skillLevel = level;
            keyBinding = key;
        }
    }

    /// <summary>
    /// 스킬 트리 진행 상황
    /// </summary>
    [Serializable]
    public class SkillTreeProgress
    {
        public string treeId;
        public List<string> unlockedNodes;
        public int totalPointsInvested;

        public SkillTreeProgress(string id)
        {
            treeId = id;
            unlockedNodes = new List<string>();
            totalPointsInvested = 0;
        }
    }

    /// <summary>
    /// 인벤토리 및 아이템 데이터
    /// </summary>
    [Serializable]
    public class InventoryData
    {
        [Header("Inventory")]
        public List<ItemData> items;
        public int maxInventorySlots;

        [Header("Equipment")]
        public Dictionary<string, string> equippedItems; // slotType -> itemId

        [Header("Currency")]
        public int souls; // 스컬 게임의 주요 화폐
        public int gems;
        public Dictionary<string, int> otherCurrencies;

        [Header("Consumables")]
        public Dictionary<string, int> consumableItems;

        public InventoryData()
        {
            items = new List<ItemData>();
            maxInventorySlots = 20;

            equippedItems = new Dictionary<string, string>();

            souls = 0;
            gems = 0;
            otherCurrencies = new Dictionary<string, int>();

            consumableItems = new Dictionary<string, int>();
        }
    }

    /// <summary>
    /// 아이템 정보
    /// </summary>
    [Serializable]
    public class ItemData
    {
        public string itemId;
        public int quantity;
        public Dictionary<string, object> customProperties;

        public ItemData(string id, int qty = 1)
        {
            itemId = id;
            quantity = qty;
            customProperties = new Dictionary<string, object>();
        }
    }

    /// <summary>
    /// 게임 통계 데이터
    /// </summary>
    [Serializable]
    public class GameStatistics
    {
        [Header("Time")]
        public float totalPlayTime;
        public float currentSessionTime;
        public DateTime firstPlayDate;
        public DateTime lastPlayDate;

        [Header("Combat")]
        public int totalEnemiesKilled;
        public int totalDeaths;
        public int totalDamageDealt;
        public int totalDamageTaken;
        public Dictionary<string, int> enemyKillCounts;

        [Header("Exploration")]
        public float totalDistanceTraveled;
        public int totalJumps;
        public int totalDashes;
        public int secretsFoundPercentage;

        [Header("Performance")]
        public int bestSpeedrunTime;
        public int longestSurvivalTime;
        public int highestCombo;
        public Dictionary<string, object> customStats;

        public GameStatistics()
        {
            totalPlayTime = 0f;
            currentSessionTime = 0f;
            firstPlayDate = DateTime.Now;
            lastPlayDate = DateTime.Now;

            totalEnemiesKilled = totalDeaths = totalDamageDealt = totalDamageTaken = 0;
            enemyKillCounts = new Dictionary<string, int>();

            totalDistanceTraveled = 0f;
            totalJumps = totalDashes = 0;
            secretsFoundPercentage = 0;

            bestSpeedrunTime = longestSurvivalTime = highestCombo = 0;
            customStats = new Dictionary<string, object>();
        }
    }

    /// <summary>
    /// 데이터 변경 이벤트 인자
    /// </summary>
    public class PlayerDataChangedEventArgs : EventArgs
    {
        public string ChangedProperty { get; }
        public object OldValue { get; }
        public object NewValue { get; }

        public PlayerDataChangedEventArgs(string property, object oldValue, object newValue)
        {
            ChangedProperty = property;
            OldValue = oldValue;
            NewValue = newValue;
        }
    }
}

namespace SaveSystem
{
    /// <summary>
    /// 저장 파일 정보를 담는 클래스
    /// </summary>
    [Serializable]
    public class SaveFileInfo
    {
        [Header("File Info")]
        public int slotIndex;
        public string fileName;
        public string filePath;
        public long fileSize;

        [Header("Game Info")]
        public string playerName;
        public int playerLevel;
        public int currentChapter;
        public int currentStage;
        public string lastSaveSceneName;
        public Vector3 lastSavePosition;

        [Header("Timestamps")]
        public long saveTimestamp;
        public long playTime;
        public string saveVersion;

        [Header("Validation")]
        public string checksum;
        public bool isValid;
        public bool isEncrypted;

        public SaveFileInfo()
        {
            slotIndex = 0;
            fileName = "";
            filePath = "";
            fileSize = 0;

            playerName = "Unknown";
            playerLevel = 1;
            currentChapter = 1;
            currentStage = 1;
            lastSaveSceneName = "";
            lastSavePosition = Vector3.zero;

            saveTimestamp = DateTime.Now.ToBinary();
            playTime = 0;
            saveVersion = Application.version;

            checksum = "";
            isValid = true;
            isEncrypted = false;
        }

        /// <summary>
        /// PlayerGameData에서 SaveFileInfo를 생성합니다
        /// </summary>
        public static SaveFileInfo CreateFromPlayerData(PlayerGameData data, int slot)
        {
            var info = new SaveFileInfo
            {
                slotIndex = slot,
                playerName = data.profile?.playerName ?? "Unknown",
                playerLevel = data.profile?.level ?? 1,
                currentChapter = data.progress?.currentChapter ?? 1,
                currentStage = data.progress?.currentStage ?? 1,
                lastSaveSceneName = data.lastSaveSceneName,
                lastSavePosition = data.lastSavePosition,
                saveTimestamp = data.saveTimestamp,
                playTime = (long)(data.stats?.totalPlayTime ?? 0),
                saveVersion = data.saveVersion
            };

            return info;
        }

        /// <summary>
        /// 저장 시간을 DateTime으로 반환합니다
        /// </summary>
        public DateTime GetSaveDateTime()
        {
            try
            {
                return DateTime.FromBinary(saveTimestamp);
            }
            catch
            {
                return DateTime.MinValue;
            }
        }

        /// <summary>
        /// 플레이 시간을 시:분:초 형태로 반환합니다
        /// </summary>
        public string GetFormattedPlayTime()
        {
            var timeSpan = TimeSpan.FromSeconds(playTime);
            return $"{timeSpan.Hours:D2}:{timeSpan.Minutes:D2}:{timeSpan.Seconds:D2}";
        }
    }

    /// <summary>
    /// 저장/로드 결과 정보
    /// </summary>
    public class SaveLoadResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public Exception Exception { get; set; }
        public SaveFileInfo FileInfo { get; set; }

        public SaveLoadResult(bool success, string message = "", Exception exception = null)
        {
            Success = success;
            Message = message;
            Exception = exception;
        }

        public static SaveLoadResult CreateSuccess(string message = "")
        {
            return new SaveLoadResult(true, message);
        }

        public static SaveLoadResult CreateFailure(string message, Exception exception = null)
        {
            return new SaveLoadResult(false, message, exception);
        }
    }

    /// <summary>
    /// 저장 설정 옵션
    /// </summary>
    [Serializable]
    public class SaveSettings
    {
        [Header("Encryption")]
        public bool enableEncryption = false;
        public string encryptionKey = "DefaultKey123!";

        [Header("Compression")]
        public bool enableCompression = true;

        [Header("Backup")]
        public bool createBackup = true;
        public int maxBackupFiles = 3;

        [Header("Validation")]
        public bool enableChecksum = true;
        public bool validateOnLoad = true;

        [Header("File Settings")]
        public string saveFileExtension = ".save";
        public string backupFileExtension = ".bak";
        public int maxSaveSlots = 5;
    }

    /// <summary>
    /// 로드 설정 옵션
    /// </summary>
    [Serializable]
    public class LoadSettings
    {
        [Header("Validation")]
        public bool validateOnLoad = true;
        public bool skipCorruptedFiles = true;
        public bool attemptRepair = true;
        public bool loadFromBackup = true;

        [Header("Version Compatibility")]
        public bool allowOlderVersions = true;
        public bool allowNewerVersions = false;
        public List<string> compatibleVersions = new List<string>();

        [Header("Performance")]
        public bool preloadFileList = true;
        public bool cacheLoadedData = false;
    }

    /// <summary>
    /// 저장/로드 이벤트 타입
    /// </summary>
    public enum SaveLoadEventType
    {
        SaveStarted,
        SaveProgress,
        SaveCompleted,
        SaveFailed,
        LoadStarted,
        LoadProgress,
        LoadCompleted,
        LoadFailed,
        ValidationStarted,
        ValidationCompleted,
        BackupCreated,
        FileCorrupted
    }

    /// <summary>
    /// 저장/로드 이벤트 데이터
    /// </summary>
    public class SaveLoadEventArgs : EventArgs
    {
        public SaveLoadEventType EventType { get; }
        public int SlotIndex { get; }
        public float Progress { get; }
        public string Message { get; }
        public Exception Exception { get; }

        public SaveLoadEventArgs(SaveLoadEventType eventType, int slotIndex = -1, float progress = 0f, string message = "", Exception exception = null)
        {
            EventType = eventType;
            SlotIndex = slotIndex;
            Progress = progress;
            Message = message;
            Exception = exception;
        }
    }
}