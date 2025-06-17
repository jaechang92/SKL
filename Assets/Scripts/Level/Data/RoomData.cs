// Assets/Scripts/Level/Data/RoomData.cs ¼öÁ¤
using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "NewRoom", menuName = "Metamorph/Level/Room Data")]
public class RoomData : ScriptableObject
{
    [Header("Room Info")]
    public string roomId;
    public string roomName;
    public RoomType roomType;

    [Header("Layout")]
    public GameObject roomPrefab;
    public Vector2Int roomSize = new Vector2Int(10, 10);
    public List<Vector2Int> entrancePositions = new List<Vector2Int>();
    public List<Vector2Int> exitPositions = new List<Vector2Int>();

    [Header("Content")]
    public List<SpawnPoint> enemySpawnPoints = new List<SpawnPoint>();
    public List<SpawnPoint> itemSpawnPoints = new List<SpawnPoint>();
    public bool hasShop = false;
    public bool hasBoss = false;

    public enum RoomType
    {
        Normal,
        Start,
        Boss,
        Treasure,
        Shop,
        Secret
    }

    [System.Serializable]
    public class SpawnPoint
    {
        public Vector2Int position;
        public float spawnChance = 1f;
        public string spawnTag = "";
    }
}