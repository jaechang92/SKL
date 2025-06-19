using System.Collections.Generic;

namespace Metamorph.Data
{
    // ==================================================
    // 데이터 모델 클래스들 (기존 유지)
    // ==================================================
    [System.Serializable]
    public class PlayerData
    {
        public string playerName = "New Player";
        public int level = 1;
        public int experience = 0;
        public int gold = 100;
        public int currentStageIndex = 0;
        public List<int> unlockedStages = new List<int> { 0 };
        public List<ItemData> inventory = new List<ItemData>();
        public long lastPlayTime;
        public float totalPlayTime = 0f;

        // 게임 특화 데이터
        public int highScore = 0;
        public int totalDeaths = 0;
        public int totalKills = 0;
        public Dictionary<string, bool> achievements = new Dictionary<string, bool>();
        public Dictionary<string, int> statistics = new Dictionary<string, int>();
    }

    [System.Serializable]
    public class ItemData
    {
        public string itemId;
        public int quantity;
        public Dictionary<string, object> properties = new Dictionary<string, object>();

        public ItemData(string id, int qty)
        {
            itemId = id;
            quantity = qty;
        }
    }
}