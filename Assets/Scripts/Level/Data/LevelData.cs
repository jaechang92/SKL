// Assets/Scripts/Level/Data/LevelData.cs
using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "NewLevel", menuName = "Metamorph/Level/Level Data")]
public class LevelData : ScriptableObject
{
    [Header("Level Info")]
    public string levelId;
    public string levelName;
    public int levelIndex; // Ãþ¼ö
    public Sprite levelIcon;

    [Header("Generation Settings")]
    public Vector2Int mapSize = new Vector2Int(20, 20);
    public int minRooms = 5;
    public int maxRooms = 10;
    public float roomSpacing = 2f;

    [Header("Difficulty")]
    [Range(1, 10)]
    public int difficultyLevel = 1;
    public float enemySpawnRate = 1f;
    public float itemSpawnRate = 1f;

    [Header("Room Types")]
    public List<RoomData> possibleRooms = new List<RoomData>();
    public RoomData bossRoom;
    public RoomData treasureRoom;

    [Header("Spawn Data")]
    public List<EnemySpawnData> enemySpawns = new List<EnemySpawnData>();
    public List<ItemSpawnData> itemSpawns = new List<ItemSpawnData>();
}