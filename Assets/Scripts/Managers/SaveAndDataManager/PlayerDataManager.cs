using CustomDebug;
using Cysharp.Threading.Tasks;
using Metamorph.Core.Interfaces;
using Metamorph.Initialization;
using Metamorph.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;

namespace Metamorph.Managers
{
    // ==================================================
    // PlayerDataManager - 데이터 관리 전용
    // ==================================================
    public class PlayerDataManager : SingletonManager<PlayerDataManager>, IDataManager, IInitializableAsync
    {
        public string Name => "Player Data Manager";

        public InitializationPriority Priority { get; set; } = InitializationPriority.High;


        public bool IsInitialized { get; private set; }

        private PlayerData _currentPlayerData;
        private bool _isDirty = false;

        public PlayerData PlayerData => _currentPlayerData;
        public bool IsDirty => _isDirty;

        Data.PlayerData IDataManager.PlayerData => throw new NotImplementedException();

        // 이벤트
        public event Action<PlayerData> OnDataChanged;
        public event Action<string> OnDataError;

        event Action<Data.PlayerData> IDataManager.OnDataChanged
        {
            add
            {
                throw new NotImplementedException();
            }

            remove
            {
                throw new NotImplementedException();
            }
        }

        public async UniTask InitializeAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                JCDebug.Log("[PlayerDataManager] 플레이어 데이터 매니저 초기화 시작");

                // 기본 데이터 생성 (SaveManager에서 로드된 데이터로 교체될 예정)
                _currentPlayerData = CreateDefaultData();
                _isDirty = false;

                IsInitialized = true;
                JCDebug.Log("[PlayerDataManager] 플레이어 데이터 매니저 초기화 완료");
            }
            catch (Exception ex)
            {
                JCDebug.Log($"[PlayerDataManager] 초기화 실패: {ex.Message}", JCDebug.LogLevel.Error);
                throw;
            }
        }

        public void SetPlayerData(PlayerData data)
        {
            if (data == null)
            {
                OnDataError?.Invoke("설정하려는 플레이어 데이터가 null입니다.");
                return;
            }

            if (!ValidateData(data))
            {
                OnDataError?.Invoke("유효하지 않은 플레이어 데이터입니다.");
                return;
            }

            _currentPlayerData = data;
            _isDirty = false;
            OnDataChanged?.Invoke(_currentPlayerData);
            JCDebug.Log("[PlayerDataManager] 플레이어 데이터 설정 완료");
        }

        public void MarkDirty()
        {
            _isDirty = true;
            OnDataChanged?.Invoke(_currentPlayerData);
        }

        public void ResetDirtyFlag()
        {
            _isDirty = false;
        }

        public PlayerData CreateDefaultData()
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

        public bool ValidateData(PlayerData data)
        {
            if (data == null) return false;

            // 기본값 검증 및 수정
            if (string.IsNullOrEmpty(data.playerName))
            {
                data.playerName = "Unknown Player";
            }

            if (data.level < 1) data.level = 1;
            if (data.experience < 0) data.experience = 0;
            if (data.gold < 0) data.gold = 0;

            data.unlockedStages ??= new List<int> { 0 };
            data.inventory ??= new List<ItemData>();
            data.achievements ??= new Dictionary<string, bool>();
            data.statistics ??= new Dictionary<string, int>();

            return true;
        }

        // ===== 편의 메서드들 =====
        public void AddExperience(int amount)
        {
            if (_currentPlayerData != null)
            {
                _currentPlayerData.experience += amount;
                MarkDirty();
            }
        }

        public void AddGold(int amount)
        {
            if (_currentPlayerData != null)
            {
                _currentPlayerData.gold = Mathf.Max(0, _currentPlayerData.gold + amount);
                MarkDirty();
            }
        }

        public void SetLevel(int level)
        {
            if (_currentPlayerData != null)
            {
                _currentPlayerData.level = Mathf.Max(1, level);
                MarkDirty();
            }
        }

        public void UnlockStage(int stageIndex)
        {
            if (_currentPlayerData != null && !_currentPlayerData.unlockedStages.Contains(stageIndex))
            {
                _currentPlayerData.unlockedStages.Add(stageIndex);
                MarkDirty();
            }
        }

        public void AddItem(string itemId, int quantity)
        {
            if (_currentPlayerData?.inventory == null) return;

            var existingItem = _currentPlayerData.inventory.Find(item => item.itemId == itemId);
            if (existingItem != null)
            {
                existingItem.quantity += quantity;
            }
            else
            {
                _currentPlayerData.inventory.Add(new ItemData(itemId, quantity));
            }
            MarkDirty();
        }

        public bool HasItem(string itemId, int requiredQuantity = 1)
        {
            if (_currentPlayerData?.inventory == null) return false;

            var item = _currentPlayerData.inventory.Find(i => i.itemId == itemId);
            return item != null && item.quantity >= requiredQuantity;
        }

        public bool RemoveItem(string itemId, int quantity)
        {
            if (!HasItem(itemId, quantity)) return false;

            var item = _currentPlayerData.inventory.Find(i => i.itemId == itemId);
            item.quantity -= quantity;

            if (item.quantity <= 0)
            {
                _currentPlayerData.inventory.Remove(item);
            }

            MarkDirty();
            return true;
        }

        public void SetAchievement(string achievementId, bool unlocked = true)
        {
            if (_currentPlayerData?.achievements == null) return;

            _currentPlayerData.achievements[achievementId] = unlocked;
            MarkDirty();
        }

        public bool HasAchievement(string achievementId)
        {
            return _currentPlayerData?.achievements?.ContainsKey(achievementId) == true &&
                   _currentPlayerData.achievements[achievementId];
        }

        public void IncrementStatistic(string statId, int amount = 1)
        {
            if (_currentPlayerData?.statistics == null) return;

            if (_currentPlayerData.statistics.ContainsKey(statId))
            {
                _currentPlayerData.statistics[statId] += amount;
            }
            else
            {
                _currentPlayerData.statistics[statId] = amount;
            }
            MarkDirty();
        }

        public int GetStatistic(string statId)
        {
            return _currentPlayerData?.statistics?.ContainsKey(statId) == true
                ? _currentPlayerData.statistics[statId]
                : 0;
        }

        public void UpdatePlayTime(float deltaTime)
        {
            if (_currentPlayerData != null)
            {
                _currentPlayerData.totalPlayTime += deltaTime;
                _currentPlayerData.lastPlayTime = DateTime.Now.ToBinary();
                MarkDirty();
            }
        }

        public void SetHighScore(int score)
        {
            if (_currentPlayerData != null && score > _currentPlayerData.highScore)
            {
                _currentPlayerData.highScore = score;
                MarkDirty();
            }
        }

        public void IncrementDeaths()
        {
            if (_currentPlayerData != null)
            {
                _currentPlayerData.totalDeaths++;
                MarkDirty();
            }
        }

        public void IncrementKills()
        {
            if (_currentPlayerData != null)
            {
                _currentPlayerData.totalKills++;
                MarkDirty();
            }
        }
    }
}

// ==================================================
// 필요한 using 문들
// ==================================================
/*
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;
using Metamorph.Initialization;
using CustomDebug;
*/