// Assets/Scripts/Level/Data/GeneratedRoom.cs
using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public class GeneratedRoom
{
    [Header("Room Instance")]
    public GameObject roomObject;
    public Room roomComponent;
    public RoomData roomData;

    [Header("Position Data")]
    public Vector2Int gridPosition;
    public Vector3 worldPosition;
    public Bounds roomBounds;

    [Header("Connection Data")]
    public List<GeneratedRoom> connectedRooms = new List<GeneratedRoom>();
    public List<Vector2Int> connectionPoints = new List<Vector2Int>();
    public List<Corridor> corridors = new List<Corridor>();

    [Header("Room State")]
    public bool isVisited = false;
    public bool isCleared = false;
    public bool isActive = false;
    public bool isLocked = false;

    [Header("Content Data")]
    public List<GameObject> spawnedEnemies = new List<GameObject>();
    public List<GameObject> spawnedItems = new List<GameObject>();
    public int enemyCount = 0;
    public int itemCount = 0;

    [Header("Distance Info")]
    public int distanceFromStart = -1; // A* ���ã���
    public float pathCost = 0f;

    // ������
    public GeneratedRoom()
    {
        connectedRooms = new List<GeneratedRoom>();
        connectionPoints = new List<Vector2Int>();
        corridors = new List<Corridor>();
        spawnedEnemies = new List<GameObject>();
        spawnedItems = new List<GameObject>();
    }

    public GeneratedRoom(RoomData data, Vector2Int gridPos, Vector3 worldPos) : this()
    {
        roomData = data;
        gridPosition = gridPos;
        worldPosition = worldPos;

        // Bounds ���
        if (data != null)
        {
            Vector3 size = new Vector3(data.roomSize.x, data.roomSize.y, 1f);
            roomBounds = new Bounds(worldPosition, size);
        }
    }

    /// <summary>
    /// �ٸ� ��� ����
    /// </summary>
    public void ConnectToRoom(GeneratedRoom otherRoom, Vector2Int connectionPoint)
    {
        if (otherRoom == null || connectedRooms.Contains(otherRoom)) return;

        connectedRooms.Add(otherRoom);
        connectionPoints.Add(connectionPoint);

        // ����� ����
        if (!otherRoom.connectedRooms.Contains(this))
        {
            otherRoom.connectedRooms.Add(this);
            otherRoom.connectionPoints.Add(connectionPoint);
        }
    }

    /// <summary>
    /// Ư�� ����� ���� ����
    /// </summary>
    public void DisconnectFromRoom(GeneratedRoom otherRoom)
    {
        if (otherRoom == null) return;

        int index = connectedRooms.IndexOf(otherRoom);
        if (index >= 0)
        {
            connectedRooms.RemoveAt(index);
            if (index < connectionPoints.Count)
            {
                connectionPoints.RemoveAt(index);
            }
        }

        // ����� ���� ����
        if (otherRoom.connectedRooms.Contains(this))
        {
            otherRoom.DisconnectFromRoom(this);
        }
    }

    /// <summary>
    /// ���� �ٸ� ��� ����Ǿ� �ִ��� Ȯ��
    /// </summary>
    public bool IsConnectedTo(GeneratedRoom otherRoom)
    {
        return connectedRooms.Contains(otherRoom);
    }

    /// <summary>
    /// ���� �߽������� Ư�� ���������� �Ÿ�
    /// </summary>
    public float GetDistanceTo(Vector2Int targetPos)
    {
        return Vector2Int.Distance(gridPosition, targetPos);
    }

    public float GetDistanceTo(GeneratedRoom otherRoom)
    {
        if (otherRoom == null) return float.MaxValue;
        return GetDistanceTo(otherRoom.gridPosition);
    }

    /// <summary>
    /// �� ���� (�޸� ����)
    /// </summary>
    public void Cleanup()
    {
        if (roomObject != null)
        {
            Object.DestroyImmediate(roomObject);
        }

        connectedRooms.Clear();
        connectionPoints.Clear();
        corridors.Clear();
        spawnedEnemies.Clear();
        spawnedItems.Clear();
    }

    /// <summary>
    /// ����� ���� ���
    /// </summary>
    public override string ToString()
    {
        return $"Room[{roomData?.roomName}] at Grid({gridPosition.x},{gridPosition.y}) " +
               $"Connections:{connectedRooms.Count} Visited:{isVisited} Cleared:{isCleared}";
    }
}