// Assets/Scripts/Level/Data/GeneratedRoom.cs
using UnityEngine;
using System.Collections.Generic;
using CustomDebug;

namespace Metamorph.Level.Generation
{
    /// <summary>
    /// 생성된 방의 정보를 관리하는 컴포넌트
    /// 기존 Room 시스템과 연계
    /// </summary>
    public class GeneratedRoom : MonoBehaviour
    {
        [Header("Room Info")]
        [SerializeField] private string _roomName;
        [SerializeField] private Vector2Int _gridPosition;
        [SerializeField] private RoomType _roomType;
        [SerializeField] private Vector3 _worldPosition;
        [SerializeField] private bool _isCleared = false;
        [SerializeField] private bool _isVisited = false;
        [SerializeField] private bool _isCompleted = false; // 완전 완료 여부

        [Header("Room Content")]
        [SerializeField] private List<GameObject> _enemies = new List<GameObject>();
        [SerializeField] private List<GameObject> _rewards = new List<GameObject>();
        [SerializeField] private List<GameObject> _interactables = new List<GameObject>();

        [Header("Visual")]
        [SerializeField] private SpriteRenderer _roomVisual;
        [SerializeField] private Color _completedColor = Color.blue;  // 완전 완료
        [SerializeField] private Color _clearedColor = Color.green;   // 클리어됨
        [SerializeField] private Color _currentColor = Color.yellow;  // 방문함
        [SerializeField] private Color _unvisitedColor = Color.gray;  // 미방문

        // 이벤트
        public System.Action<GeneratedRoom> OnRoomEntered;
        public System.Action<GeneratedRoom> OnRoomCleared;
        public System.Action<GeneratedRoom> OnRoomCompleted;

        // Properties
        public string RoomName => _roomName;
        public Vector2Int GridPosition => _gridPosition;
        public RoomType Type => _roomType;
        public Vector3 WorldPosition => _worldPosition;
        public bool IsCleared => _isCleared;
        public bool IsVisited => _isVisited;
        public bool IsCompleted => _isCompleted;
        public bool HasEnemies => _enemies.Count > 0;
        public bool HasRewards => _rewards.Count > 0;

        #region Initialization

        /// <summary>
        /// 방 초기화
        /// </summary>
        public void Initialize(Vector2Int gridPosition, RoomType roomType, Vector3 worldPosition)
        {
            _gridPosition = gridPosition;
            _roomType = roomType;
            _worldPosition = worldPosition;

            SetupRoomContent();
            UpdateVisual();

            JCDebug.Log($"[GeneratedRoom] 방 초기화: {roomType} at {gridPosition}");
        }

        /// <summary>
        /// 방 타입에 따른 콘텐츠 설정
        /// </summary>
        private void SetupRoomContent()
        {
            switch (_roomType)
            {
                case RoomType.Normal:
                    SetupCombatRoom();
                    break;
                case RoomType.Elite:
                    SetupEliteRoom();
                    break;
                case RoomType.Reward:
                    SetupRewardRoom();
                    break;
                case RoomType.Boss:
                    SetupBossRoom();
                    break;
                case RoomType.Start:
                    SetupStartRoom();
                    break;
                case RoomType.Shop:
                    SetupShopRoom();
                    break;
            }
        }

        private void SetupCombatRoom()
        {
            // 일반 전투방: 적 2-4마리
            int enemyCount = Random.Range(2, 5);
            SpawnEnemies("CommonEnemy", enemyCount);
            SpawnRandomReward();
        }

        private void SetupEliteRoom()
        {
            // 엘리트방: 강한 적 1-2마리
            int eliteCount = Random.Range(1, 3);
            SpawnEnemies("EliteEnemy", eliteCount);
            SpawnGuaranteedReward("EliteReward");
        }

        private void SetupRewardRoom()
        {
            // 보상방: 적 없음, 보상 확정
            SpawnGuaranteedReward("TreasureChest");
            _isCleared = true; // 보상방은 자동으로 클리어
        }

        private void SetupBossRoom()
        {
            // 보스방: 보스 1마리
            SpawnEnemies("Boss", 1);
            SpawnGuaranteedReward("BossReward");
        }

        private void SetupStartRoom()
        {
            // 시작방: 적 없음, 특별한 상호작용 없음
            _isCleared = true;
            _isVisited = true;
        }

        private void SetupShopRoom()
        {
            // 상점방: 상점 NPC
            SpawnInteractable("ShopKeeper");
            _isCleared = true;
        }

        #endregion

        #region Content Spawning

        private void SpawnEnemies(string enemyType, int count)
        {
            for (int i = 0; i < count; i++)
            {
                Vector3 spawnPos = GetRandomPositionInRoom();
                GameObject enemy = CreatePlaceholder(enemyType, spawnPos, Color.red);
                _enemies.Add(enemy);
            }
        }

        private void SpawnRandomReward()
        {
            if (Random.Range(0f, 1f) < 0.7f) // 70% 확률
            {
                SpawnGuaranteedReward("CommonReward");
            }
        }

        private void SpawnGuaranteedReward(string rewardType)
        {
            Vector3 spawnPos = GetRandomPositionInRoom();
            GameObject reward = CreatePlaceholder(rewardType, spawnPos, Color.yellow);
            _rewards.Add(reward);
        }

        private void SpawnInteractable(string interactableType)
        {
            Vector3 spawnPos = GetCenterPosition();
            GameObject interactable = CreatePlaceholder(interactableType, spawnPos, Color.blue);
            _interactables.Add(interactable);
        }

        private GameObject CreatePlaceholder(string objectType, Vector3 position, Color color)
        {
            GameObject placeholder = GameObject.CreatePrimitive(PrimitiveType.Cube);
            placeholder.name = objectType;
            placeholder.transform.position = position;
            placeholder.transform.SetParent(transform);

            var renderer = placeholder.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material.color = color;
            }

            return placeholder;
        }

        #endregion

        #region Room State Management

        /// <summary>
        /// 플레이어가 방에 입장했을 때
        /// </summary>
        public void OnPlayerEntered()
        {
            if (!_isVisited)
            {
                _isVisited = true;
                UpdateVisual();
            }

            OnRoomEntered?.Invoke(this);
            JCDebug.Log($"[GeneratedRoom] 플레이어 입장: {_roomType} at {_gridPosition}");
        }

        /// <summary>
        /// 방의 모든 적이 처치되었을 때
        /// </summary>
        public void OnAllEnemiesDefeated()
        {
            if (!_isCleared && _enemies.Count > 0)
            {
                _isCleared = true;
                ActivateRewards();
                UpdateVisual();

                OnRoomCleared?.Invoke(this);
                JCDebug.Log($"[GeneratedRoom] 방 클리어: {_roomType} at {_gridPosition}");
            }
        }

        /// <summary>
        /// 보상을 활성화
        /// </summary>
        private void ActivateRewards()
        {
            foreach (var reward in _rewards)
            {
                if (reward != null)
                {
                    // 보상 활성화 로직
                    reward.SetActive(true);
                }
            }
        }

        /// <summary>
        /// 방을 완전히 완료 상태로 설정 (보상까지 모두 획득했을 때)
        /// </summary>
        public void CompleteRoom()
        {
            if (_isCleared && !HasActiveRewards())
            {
                // 모든 보상이 획득되었을 때만 완료 처리
                _isCompleted = true;
                UpdateVisual();

                OnRoomCompleted?.Invoke(this);
                JCDebug.Log($"[GeneratedRoom] 방 완전 완료: {_roomType} at {_gridPosition}");
            }
        }

        /// <summary>
        /// 플레이어가 보상을 획득했을 때 호출
        /// </summary>
        public void OnRewardCollected(GameObject reward)
        {
            if (_rewards.Contains(reward))
            {
                _rewards.Remove(reward);
                JCDebug.Log($"[GeneratedRoom] 보상 획득: {reward.name}");

                // 모든 보상을 획득했다면 방 완료
                if (_isCleared && !HasActiveRewards())
                {
                    CompleteRoom();
                }
            }
        }

        /// <summary>
        /// 아직 획득하지 않은 활성 보상이 있는지 확인
        /// </summary>
        private bool HasActiveRewards()
        {
            return _rewards.Count > 0 && _rewards.Exists(reward => reward != null && reward.activeInHierarchy);
        }

        #endregion

        #region Utility Methods

        private Vector3 GetRandomPositionInRoom()
        {
            float range = 2f; // 방 크기에 맞게 조정
            return _worldPosition + new Vector3(
                Random.Range(-range, range),
                Random.Range(-range, range),
                0
            );
        }

        private Vector3 GetCenterPosition()
        {
            return _worldPosition;
        }

        private void UpdateVisual()
        {
            if (_roomVisual == null) return;

            if (_isCompleted)
                _roomVisual.color = _completedColor;      // 파란색: 완전 완료
            else if (_isCleared)
                _roomVisual.color = _clearedColor;        // 초록색: 적만 처치
            else if (_isVisited)
                _roomVisual.color = _currentColor;        // 노란색: 방문함
            else
                _roomVisual.color = _unvisitedColor;      // 회색: 미방문
        }

        #endregion

        #region Debug

        private void OnDrawGizmos()
        {
            // 방 영역 표시 (상태에 따라 색상 변경)
            if (_isCompleted)
                Gizmos.color = Color.blue;      // 완전 완료
            else if (_isCleared)
                Gizmos.color = Color.green;     // 클리어됨
            else if (_isVisited)
                Gizmos.color = Color.yellow;    // 방문함
            else
                Gizmos.color = Color.white;     // 미방문

            Gizmos.DrawWireCube(transform.position, Vector3.one * 3f);

            // 방 타입 텍스트 표시 (Scene뷰에서)
#if UNITY_EDITOR
            string statusText = _isCompleted ? " (완료)" : _isCleared ? " (클리어)" : _isVisited ? " (방문)" : "";
            UnityEditor.Handles.Label(transform.position + Vector3.up * 2f, _roomType.ToString() + statusText);
#endif
        }

        #endregion
    }
}