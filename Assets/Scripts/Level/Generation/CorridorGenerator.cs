// Assets/Scripts/Level/Generation/CorridorGenerator.cs
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using CustomDebug;

public class CorridorGenerator
{
    private GridSystem gridSystem;
    private List<GeneratedRoom> rooms;
    private List<Corridor> generatedCorridors;
    private readonly GameObject corridorPrefab;

    // ���� ���� ����
    private readonly int corridorWidth = 1;
    private readonly float corridorHeight = 2f;

    public CorridorGenerator(GridSystem grid, List<GeneratedRoom> roomList)
    {
        gridSystem = grid;
        rooms = roomList;
        generatedCorridors = new List<Corridor>();

        // ���� ������ �ε�
        corridorPrefab = Resources.Load<GameObject>("Prefabs/Corridor");
    }

    /// <summary>
    /// ��� ���� ����
    /// </summary>
    public void GenerateAllCorridors()
    {
        JCDebug.Log("[CorridorGenerator] ���� ���� ����");

        // 1. �ּ� ���д� Ʈ���� ��� �� ����
        GenerateMinimumSpanningTree();

        // 2. �߰� ����� ��ȯ ��� ����
        GenerateAdditionalConnections();

        // 3. ���� ���� ������Ʈ ����
        CreateCorridorObjects();

        JCDebug.Log($"���� ���� �Ϸ�: {generatedCorridors.Count}��");
    }

    /// <summary>
    /// �ּ� ���д� Ʈ���� �⺻ ���� ����
    /// </summary>
    private void GenerateMinimumSpanningTree()
    {
        if (rooms.Count < 2) return;

        var unconnectedRooms = new List<GeneratedRoom>(rooms);
        var connectedRooms = new List<GeneratedRoom>();

        // ���� ����� ����
        var startRoom = rooms.FirstOrDefault(r => r.roomData?.roomType == RoomData.RoomType.Start);
        if (startRoom == null) startRoom = rooms[0];

        connectedRooms.Add(startRoom);
        unconnectedRooms.Remove(startRoom);

        while (unconnectedRooms.Count > 0)
        {
            var bestConnection = FindShortestConnection(connectedRooms, unconnectedRooms);

            if (bestConnection.Item1 != null && bestConnection.Item2 != null)
            {
                CreateConnection(bestConnection.Item1, bestConnection.Item2);
                connectedRooms.Add(bestConnection.Item2);
                unconnectedRooms.Remove(bestConnection.Item2);
            }
            else
            {
                // ������ �� ���� ���� ������ ������ ���� ����� ��� ����
                var nearestRoom = FindNearestRoom(connectedRooms[0], unconnectedRooms);
                if (nearestRoom != null)
                {
                    CreateConnection(connectedRooms[0], nearestRoom);
                    connectedRooms.Add(nearestRoom);
                    unconnectedRooms.Remove(nearestRoom);
                }
                else break;
            }
        }
    }

    /// <summary>
    /// �߰� ����� ���� ���� (�� ��̷ο� ���� ����)
    /// </summary>
    private void GenerateAdditionalConnections()
    {
        int additionalConnections = Mathf.RoundToInt(rooms.Count * 0.3f); // 30% �߰� ����

        for (int i = 0; i < additionalConnections; i++)
        {
            var roomPair = FindBestAdditionalConnection();
            if (roomPair.Item1 != null && roomPair.Item2 != null)
            {
                CreateConnection(roomPair.Item1, roomPair.Item2);
            }
        }
    }

    /// <summary>
    /// ���� ���� ���ӿ�����Ʈ ����
    /// </summary>
    private void CreateCorridorObjects()
    {
        foreach (var corridor in generatedCorridors)
        {
            CreateCorridorSegments(corridor);
        }
    }

    private (GeneratedRoom, GeneratedRoom) FindShortestConnection(
        List<GeneratedRoom> connectedRooms,
        List<GeneratedRoom> unconnectedRooms)
    {
        float shortestDistance = float.MaxValue;
        GeneratedRoom bestConnected = null;
        GeneratedRoom bestUnconnected = null;

        foreach (var connected in connectedRooms)
        {
            foreach (var unconnected in unconnectedRooms)
            {
                float distance = connected.GetDistanceTo(unconnected);
                if (distance < shortestDistance)
                {
                    shortestDistance = distance;
                    bestConnected = connected;
                    bestUnconnected = unconnected;
                }
            }
        }

        return (bestConnected, bestUnconnected);
    }

    private GeneratedRoom FindNearestRoom(GeneratedRoom target, List<GeneratedRoom> candidates)
    {
        float shortestDistance = float.MaxValue;
        GeneratedRoom nearest = null;

        foreach (var candidate in candidates)
        {
            float distance = target.GetDistanceTo(candidate);
            if (distance < shortestDistance)
            {
                shortestDistance = distance;
                nearest = candidate;
            }
        }

        return nearest;
    }

    private (GeneratedRoom, GeneratedRoom) FindBestAdditionalConnection()
    {
        var unconnectedPairs = new List<(GeneratedRoom, GeneratedRoom, float)>();

        for (int i = 0; i < rooms.Count; i++)
        {
            for (int j = i + 1; j < rooms.Count; j++)
            {
                var room1 = rooms[i];
                var room2 = rooms[j];

                if (!room1.IsConnectedTo(room2))
                {
                    float distance = room1.GetDistanceTo(room2);
                    unconnectedPairs.Add((room1, room2, distance));
                }
            }
        }

        if (unconnectedPairs.Count == 0)
            return (null, null);

        // ������ �Ÿ��� ����� �켱 ���� (�ʹ� �����ų� ���� ����)
        var sortedPairs = unconnectedPairs
            .Where(p => p.Item3 > 2f && p.Item3 < 8f)
            .OrderBy(p => p.Item3)
            .ToList();

        if (sortedPairs.Count == 0)
            sortedPairs = unconnectedPairs.OrderBy(p => p.Item3).ToList();

        var selectedPair = sortedPairs[Random.Range(0, Mathf.Min(3, sortedPairs.Count))];
        return (selectedPair.Item1, selectedPair.Item2);
    }

    private void CreateConnection(GeneratedRoom room1, GeneratedRoom room2)
    {
        if (room1.IsConnectedTo(room2)) return; // �̹� �����

        // A* �˰������� ���� ��� ã��
        var path = FindPath(room1.gridPosition, room2.gridPosition);

        if (path != null && path.Count > 0)
        {
            var corridor = new Corridor(room1, room2, path);
            generatedCorridors.Add(corridor);

            // ��鿡 ���� ���� �߰�
            Vector2Int connectionPoint = path.Count > 1 ? path[1] : room2.gridPosition;
            room1.ConnectToRoom(room2, connectionPoint);
            room1.corridors.Add(corridor);
            room2.corridors.Add(corridor);

            JCDebug.Log($"���� ����: {room1.roomData?.roomName} �� {room2.roomData?.roomName}");
        }
    }

    /// <summary>
    /// A* �˰������� ��� ã��
    /// </summary>
    private List<Vector2Int> FindPath(Vector2Int start, Vector2Int end)
    {
        var openSet = new List<PathNode>();
        var closedSet = new HashSet<Vector2Int>();
        var cameFrom = new Dictionary<Vector2Int, Vector2Int>();

        var startNode = new PathNode(start, 0, GetHeuristic(start, end));
        openSet.Add(startNode);

        while (openSet.Count > 0)
        {
            // F ���� ���� ���� ��� ����
            var current = openSet.OrderBy(n => n.F).First();
            openSet.Remove(current);
            closedSet.Add(current.Position);

            // ��ǥ ����
            if (current.Position == end)
            {
                return ReconstructPath(cameFrom, current.Position);
            }

            // ���� ���� Ȯ��
            foreach (var neighbor in GetNeighbors(current.Position))
            {
                if (closedSet.Contains(neighbor) || !IsValidPathCell(neighbor))
                    continue;

                float tentativeG = current.G + GetMoveCost(current.Position, neighbor);
                var existingNode = openSet.FirstOrDefault(n => n.Position == neighbor);

                if (existingNode == null)
                {
                    var newNode = new PathNode(neighbor, tentativeG, GetHeuristic(neighbor, end));
                    openSet.Add(newNode);
                    cameFrom[neighbor] = current.Position;
                }
                else if (tentativeG < existingNode.G)
                {
                    existingNode.G = tentativeG;
                    cameFrom[neighbor] = current.Position;
                }
            }
        }

        // ��θ� ã�� ���� ��� ���� ��� ����
        return CreateStraightPath(start, end);
    }

    private List<Vector2Int> GetNeighbors(Vector2Int position)
    {
        return new List<Vector2Int>
        {
            position + Vector2Int.up,
            position + Vector2Int.down,
            position + Vector2Int.left,
            position + Vector2Int.right
        };
    }

    private bool IsValidPathCell(Vector2Int position)
    {
        if (!gridSystem.IsValidPosition(position))
            return false;

        // ���� �ִ� ��ġ�� ���ϱ� (�Ա� ����)
        foreach (var room in rooms)
        {
            Rect roomRect = new Rect(room.gridPosition.x, room.gridPosition.y,
                                   room.roomData.roomSize.x, room.roomData.roomSize.y);

            if (roomRect.Contains(position))
            {
                // ���� �����ڸ�(�Ա� ���� ��ġ)�� ���
                bool isEdge = position.x == roomRect.xMin || position.x == roomRect.xMax - 1 ||
                             position.y == roomRect.yMin || position.y == roomRect.yMax - 1;
                return isEdge;
            }
        }

        return true;
    }

    private float GetHeuristic(Vector2Int from, Vector2Int to)
    {
        return Vector2Int.Distance(from, to);
    }

    private float GetMoveCost(Vector2Int from, Vector2Int to)
    {
        return 1f; // �⺻ �̵� ���
    }

    private List<Vector2Int> ReconstructPath(Dictionary<Vector2Int, Vector2Int> cameFrom, Vector2Int current)
    {
        var path = new List<Vector2Int> { current };

        while (cameFrom.ContainsKey(current))
        {
            current = cameFrom[current];
            path.Insert(0, current);
        }

        return path;
    }

    private List<Vector2Int> CreateStraightPath(Vector2Int start, Vector2Int end)
    {
        var path = new List<Vector2Int>();

        // L�� ��� ���� (���� -> ����)
        Vector2Int current = start;

        // ���� �̵�
        while (current.x != end.x)
        {
            path.Add(current);
            current.x += current.x < end.x ? 1 : -1;
        }

        // ���� �̵�
        while (current.y != end.y)
        {
            path.Add(current);
            current.y += current.y < end.y ? 1 : -1;
        }

        path.Add(end);
        return path;
    }

    private void CreateCorridorSegments(Corridor corridor)
    {
        if (corridorPrefab == null || corridor.Path.Count < 2) return;

        for (int i = 0; i < corridor.Path.Count - 1; i++)
        {
            Vector2Int from = corridor.Path[i];
            Vector2Int to = corridor.Path[i + 1];

            // ���� ���׸�Ʈ ����
            Vector3 worldPos = gridSystem.GridToWorldPosition(from);
            GameObject corridorSegment = Object.Instantiate(corridorPrefab, worldPos, Quaternion.identity);

            // ���⿡ ���� ȸ�� ����
            Vector2Int direction = to - from;
            if (direction == Vector2Int.right || direction == Vector2Int.left)
            {
                corridorSegment.transform.rotation = Quaternion.Euler(0, 0, 90);
            }

            corridorSegment.name = $"Corridor_{from.x}_{from.y}_to_{to.x}_{to.y}";
        }
    }
}

/// <summary>
/// A* �˰���� ��� Ŭ����
/// </summary>
public class PathNode
{
    public Vector2Int Position { get; set; }
    public float G { get; set; } // ������������ �Ÿ�
    public float H { get; set; } // ��ǥ�������� ���� �Ÿ�
    public float F => G + H;     // �� ���

    public PathNode(Vector2Int position, float g, float h)
    {
        Position = position;
        G = g;
        H = h;
    }
}

/// <summary>
/// ���� ������ Ŭ����
/// </summary>
[System.Serializable]
public class Corridor
{
    public GeneratedRoom Room1 { get; set; }
    public GeneratedRoom Room2 { get; set; }
    public List<Vector2Int> Path { get; set; }
    public List<GameObject> CorridorObjects { get; set; }
    public bool IsActive { get; set; } = true;

    public Corridor(GeneratedRoom room1, GeneratedRoom room2, List<Vector2Int> path)
    {
        Room1 = room1;
        Room2 = room2;
        Path = new List<Vector2Int>(path);
        CorridorObjects = new List<GameObject>();
    }

    public float GetLength()
    {
        return Path?.Count ?? 0f;
    }

    public bool ConnectsRoom(GeneratedRoom room)
    {
        return Room1 == room || Room2 == room;
    }

    public GeneratedRoom GetOtherRoom(GeneratedRoom room)
    {
        if (Room1 == room) return Room2;
        if (Room2 == room) return Room1;
        return null;
    }
}