// Assets/Scripts/Level/Room/RoomChoiceManager.cs
using System;
using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;
using CustomDebug;
using Metamorph.Level.Generation;

namespace Metamorph.Level.Room
{
    /// <summary>
    /// 방 선택 시스템 관리 (CustomTween 사용)
    /// </summary>
    public class RoomChoiceManager : SingletonManager<RoomChoiceManager>
    {
        [Header("UI References")]
        [SerializeField] private RoomChoiceUI _roomChoiceUI;
        [SerializeField] private Canvas _uiCanvas;

        [Header("Choice Settings")]
        [SerializeField] private float _choiceDisplayDelay = 1f;
        [SerializeField] private bool _pauseGameDuringChoice = true;
        [SerializeField] private bool _showChoicePreview = true;

        // 현재 선택 상태
        private List<RoomChoicePortal> _currentChoices = new List<RoomChoicePortal>();
        private bool _isChoosingRoom = false;

        // 이벤트
        public event Action<RoomType, int> OnRoomChosen;
        public event Action OnChoiceStarted;
        public event Action OnChoiceEnded;

        public bool IsChoosingRoom => _isChoosingRoom;

        protected override void OnCreated()
        {
            base.OnCreated();
            InitializeRoomChoiceManager();
        }

        private void InitializeRoomChoiceManager()
        {
            // UI 캔버스 찾기
            if (_uiCanvas == null)
            {
                _uiCanvas = FindAnyObjectByType<Canvas>();
            }

            // UI 생성
            if (_roomChoiceUI == null)
            {
                CreateRoomChoiceUI();
            }

            JCDebug.Log("[RoomChoiceManager] 방 선택 시스템 초기화 완료");
        }

        /// <summary>
        /// 방 선택 UI 생성
        /// </summary>
        private void CreateRoomChoiceUI()
        {
            var uiPrefab = Resources.Load<GameObject>("UI/RoomChoiceUI");
            if (uiPrefab != null && _uiCanvas != null)
            {
                var uiInstance = Instantiate(uiPrefab, _uiCanvas.transform);
                _roomChoiceUI = uiInstance.GetComponent<RoomChoiceUI>();
                _roomChoiceUI.Initialize(this);
            }
        }

        /// <summary>
        /// 방 선택지 표시
        /// </summary>
        public async UniTask ShowRoomChoices(params GameObject[] choicePortals)
        {
            if (_isChoosingRoom) return;

            _isChoosingRoom = true;
            OnChoiceStarted?.Invoke();

            try
            {
                // 포털들을 RoomChoicePortal로 변환
                _currentChoices.Clear();
                foreach (var portal in choicePortals)
                {
                    var choicePortal = portal.GetComponent<RoomChoicePortal>();
                    if (choicePortal != null)
                    {
                        _currentChoices.Add(choicePortal);
                    }
                }

                // 게임 일시정지
                if (_pauseGameDuringChoice)
                {
                    Time.timeScale = 0f;
                }

                // 딜레이 후 UI 표시
                await UniTask.Delay(TimeSpan.FromSeconds(_choiceDisplayDelay).Milliseconds, true);

                // UI 활성화
                if (_roomChoiceUI != null)
                {
                    await _roomChoiceUI.ShowChoices(_currentChoices);
                }

                JCDebug.Log($"[RoomChoiceManager] 방 선택 UI 표시 - {_currentChoices.Count}개 선택지");
            }
            catch (Exception ex)
            {
                JCDebug.Log($"[RoomChoiceManager] 방 선택 표시 실패: {ex.Message}", JCDebug.LogLevel.Error);
                EndRoomChoice();
            }
        }

        /// <summary>
        /// 방 선택 처리
        /// </summary>
        public async UniTask ChooseRoom(RoomChoicePortal chosenPortal)
        {
            if (!_isChoosingRoom || chosenPortal == null) return;

            try
            {
                JCDebug.Log($"[RoomChoiceManager] 방 선택됨: {chosenPortal.RoomType}, 층: {chosenPortal.TargetFloor}");

                // 선택 효과 재생
                await PlayChoiceEffectAsync(chosenPortal);

                // UI 숨기기
                if (_roomChoiceUI != null)
                {
                    await _roomChoiceUI.HideChoices();
                }

                // 선택되지 않은 포털들 제거
                await RemoveUnselectedPortalsAsync(chosenPortal);

                // 새 맵 생성
                await MapGenerator.Instance.GenerateMap();

                // 이벤트 발생
                OnRoomChosen?.Invoke(chosenPortal.RoomType, chosenPortal.TargetFloor);
            }
            catch (Exception ex)
            {
                JCDebug.Log($"[RoomChoiceManager] 방 선택 처리 실패: {ex.Message}", JCDebug.LogLevel.Error);
            }
            finally
            {
                EndRoomChoice();
            }
        }

        /// <summary>
        /// 방 선택 종료
        /// </summary>
        private void EndRoomChoice()
        {
            _isChoosingRoom = false;

            // 게임 재개
            if (_pauseGameDuringChoice)
            {
                Time.timeScale = 1f;
            }

            // 정리
            _currentChoices.Clear();

            OnChoiceEnded?.Invoke();
        }

        /// <summary>
        /// 선택 효과 재생
        /// </summary>
        private async UniTask PlayChoiceEffectAsync(RoomChoicePortal chosenPortal)
        {
            if (chosenPortal.ChoiceEffect != null)
            {
                var effect = Instantiate(chosenPortal.ChoiceEffect, chosenPortal.transform.position, Quaternion.identity);
                Destroy(effect, 2f);
            }

            if (chosenPortal.ChoiceSound != null)
            {
                AudioSource.PlayClipAtPoint(chosenPortal.ChoiceSound, chosenPortal.transform.position);
            }

            await UniTask.Delay(500, true); // 0.5초 대기
        }

        /// <summary>
        /// 선택되지 않은 포털들 제거
        /// </summary>
        private async UniTask RemoveUnselectedPortalsAsync(RoomChoicePortal chosenPortal)
        {
            foreach (var portal in _currentChoices)
            {
                if (portal != chosenPortal)
                {
                    // 페이드 아웃 효과
                    await portal.FadeOutAsync();
                    Destroy(portal.gameObject);
                }
            }
        }
    }
}