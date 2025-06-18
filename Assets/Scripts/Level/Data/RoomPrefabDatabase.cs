// ===== RoomPrefabDatabase.cs =====
using Metamorph.Level.Generation;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 방 프리팹들을 관리하는 데이터베이스
/// </summary>
[CreateAssetMenu(fileName = "RoomPrefabDatabase", menuName = "Metamorph/Level/Room Prefab Database")]
public class RoomPrefabDatabase : ScriptableObject
{
    [Header("Room Prefabs")]
    [SerializeField] private RoomPrefabEntry[] _roomPrefabs;

    [Header("Default Prefabs")]
    [SerializeField] private GameObject _defaultRoomPrefab;

    private Dictionary<RoomType, List<GameObject>> _prefabCache;

    private void OnEnable()
    {
        BuildPrefabCache();
    }

    /// <summary>
    /// 프리팹 캐시 구축
    /// </summary>
    private void BuildPrefabCache()
    {
        _prefabCache = new Dictionary<RoomType, List<GameObject>>();

        foreach (var entry in _roomPrefabs)
        {
            if (!_prefabCache.ContainsKey(entry.roomType))
            {
                _prefabCache[entry.roomType] = new List<GameObject>();
            }

            if (entry.prefab != null)
            {
                _prefabCache[entry.roomType].Add(entry.prefab);
            }
        }
    }

    /// <summary>
    /// 방 타입에 맞는 프리팹 가져오기
    /// </summary>
    public GameObject GetRoomPrefab(RoomType roomType)
    {
        if (_prefabCache == null) BuildPrefabCache();

        if (_prefabCache.ContainsKey(roomType) && _prefabCache[roomType].Count > 0)
        {
            var prefabs = _prefabCache[roomType];
            return prefabs[Random.Range(0, prefabs.Count)];
        }

        // 기본 프리팹 반환
        return _defaultRoomPrefab;
    }

    /// <summary>
    /// 특정 인덱스의 프리팹 가져오기
    /// </summary>
    public GameObject GetRoomPrefab(RoomType roomType, int index)
    {
        if (_prefabCache == null) BuildPrefabCache();

        if (_prefabCache.ContainsKey(roomType) &&
            _prefabCache[roomType].Count > index &&
            index >= 0)
        {
            return _prefabCache[roomType][index];
        }

        return _defaultRoomPrefab;
    }

    /// <summary>
    /// 특정 타입의 프리팹 개수
    /// </summary>
    public int GetPrefabCount(RoomType roomType)
    {
        if (_prefabCache == null) BuildPrefabCache();

        if (_prefabCache.ContainsKey(roomType))
        {
            return _prefabCache[roomType].Count;
        }

        return 0;
    }

    [System.Serializable]
    public class RoomPrefabEntry
    {
        public RoomType roomType;
        public GameObject prefab;
        [Range(0f, 1f)] public float spawnWeight = 1f;
        public string description;
    }
}