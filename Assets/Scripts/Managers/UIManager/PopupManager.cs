using CustomDebug;
using Cysharp.Threading.Tasks;
using Metamorph.Initialization;
using Metamorph.Managers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Metamorph.UI
{
    /// <summary>
    /// 스컬 게임 팝업 관리 시스템
    /// 모든 팝업 창의 생성, 표시, 관리를 담당
    /// </summary>
    public class PopupManager : SingletonManager<PopupManager>, IInitializableAsync
    {
        [Header("Popup Settings")]
        [SerializeField] private PopupSettings _popupSettings = new PopupSettings();
        [SerializeField] private bool _pauseGameWhenPopupOpen = true;
        [SerializeField] private bool _closeWithEscapeKey = true;

        [Header("Popup Prefabs")]
        [SerializeField] private GameObject _alertPopupPrefab;
        [SerializeField] private GameObject _confirmPopupPrefab;
        [SerializeField] private GameObject _itemPopupPrefab;
        [SerializeField] private GameObject _levelUpPopupPrefab;
        [SerializeField] private GameObject _gameOverPopupPrefab;
        [SerializeField] private GameObject _rewardPopupPrefab;

        [Header("Canvas References")]
        [SerializeField] private Canvas _popupCanvas;
        [SerializeField] private GameObject _backgroundBlocker;

        // IManagerInitializable 구현
        public string Name => "PopupManager";
        public InitializationPriority Priority { get; set; } = InitializationPriority.Low;
        public bool IsInitialized { get; private set; } = false;

        // 팝업 관리
        private Stack<BasePopup> _popupStack = new Stack<BasePopup>();
        private Dictionary<PopupType, Queue<BasePopup>> _popupPools = new Dictionary<PopupType, Queue<BasePopup>>();
        private Dictionary<PopupType, GameObject> _popupPrefabs = new Dictionary<PopupType, GameObject>();

        // 상태 관리
        private bool _isAnyPopupOpen = false;
        private float _originalTimeScale = 1f;
        private CancellationTokenSource _globalCancellationTokenSource;

        // 이벤트 시스템 (옵저버 패턴)
        public event Action<PopupType> OnPopupOpened;
        public event Action<PopupType> OnPopupClosed;
        public event Action<PopupType, PopupResult> OnPopupCompleted;
        public event Action OnAllPopupsClosed;
        public event Action<bool> OnPopupStateChanged; // isAnyOpen

        // 프로퍼티
        public bool IsAnyPopupOpen => _isAnyPopupOpen;
        public int PopupCount => _popupStack.Count;
        public BasePopup CurrentPopup => _popupStack.Count > 0 ? _popupStack.Peek() : null;

        #region 헬퍼 메서드
        private static readonly PopupType[] _allPopupTypes =
        {
            PopupType.Alert,
            PopupType.Confirm,
            PopupType.ItemReward,
            PopupType.LevelUp,
            PopupType.GameOver,
            PopupType.Reward,
            PopupType.Custom
        };

        private static PopupType[] GetAllPopupTypes() => _allPopupTypes;
        #endregion

        #region IManagerInitializable 구현

        public async UniTask InitializeAsync(CancellationToken cancellationToken = default)
        {
            if (IsInitialized) return;

            try
            {
                JCDebug.Log("[PopupManager] 팝업 시스템 초기화 시작");

                // 1. Canvas 시스템 설정
                await SetupPopupCanvasAsync(cancellationToken);

                // 2. 팝업 프리팹 로드
                await LoadPopupPrefabsAsync(cancellationToken);

                // 3. 팝업 풀 시스템 초기화
                await InitializePopupPoolsAsync(cancellationToken);

                // 4. UI 이벤트 연결
                SetupEventConnections();

                // 5. 다른 매니저와 연동
                SetupManagerIntegrations();

                _globalCancellationTokenSource = new CancellationTokenSource();

                IsInitialized = true;
                JCDebug.Log("[PopupManager] 팝업 시스템 초기화 완료");
            }
            catch (OperationCanceledException)
            {
                JCDebug.Log("[PopupManager] 초기화가 취소됨", JCDebug.LogLevel.Warning);
                throw;
            }
            catch (Exception ex)
            {
                JCDebug.Log($"[PopupManager] 초기화 실패: {ex.Message}", JCDebug.LogLevel.Error);
                throw;
            }
        }

        #endregion

        #region 초기화 메서드들

        private async UniTask SetupPopupCanvasAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 팝업 전용 Canvas 생성 또는 찾기
            if (_popupCanvas == null)
            {
                GameObject canvasObj = new GameObject("PopupCanvas");
                _popupCanvas = canvasObj.AddComponent<Canvas>();
                _popupCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                _popupCanvas.sortingOrder = 100; // 최상위 레이어

                canvasObj.AddComponent<CanvasScaler>();
                canvasObj.AddComponent<GraphicRaycaster>();
                DontDestroyOnLoad(canvasObj);
            }

            // 배경 블로커 생성
            if (_backgroundBlocker == null)
            {
                _backgroundBlocker = CreateBackgroundBlocker();
            }

            await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
        }

        private GameObject CreateBackgroundBlocker()
        {
            GameObject blocker = new GameObject("BackgroundBlocker");
            blocker.transform.SetParent(_popupCanvas.transform, false);

            // 전체 화면을 덮는 이미지
            Image blockerImage = blocker.AddComponent<Image>();
            blockerImage.color = new Color(0, 0, 0, _popupSettings.backgroundAlpha);
            blockerImage.raycastTarget = true;

            // 화면 전체 크기로 설정
            RectTransform rectTransform = blocker.GetComponent<RectTransform>();
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.sizeDelta = Vector2.zero;
            rectTransform.anchoredPosition = Vector2.zero;

            blocker.SetActive(false);
            return blocker;
        }

        private async UniTask LoadPopupPrefabsAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Resources 폴더에서 팝업 프리팹들 로드
            var prefabsToLoad = new[]
            {
                (PopupType.Alert, "UI/Popups/AlertPopup"),
                (PopupType.Confirm, "UI/Popups/ConfirmPopup"),
                (PopupType.ItemReward, "UI/Popups/ItemPopup"),
                (PopupType.LevelUp, "UI/Popups/LevelUpPopup"),
                (PopupType.GameOver, "UI/Popups/GameOverPopup"),
                (PopupType.Reward, "UI/Popups/RewardPopup")
            };

            foreach (var (popupType, path) in prefabsToLoad)
            {
                cancellationToken.ThrowIfCancellationRequested();

                GameObject prefab = Resources.Load<GameObject>(path);
                if (prefab != null)
                {
                    _popupPrefabs[popupType] = prefab;
                }
                else
                {
                    // 프리팹이 없으면 기본 프리팹 생성
                    _popupPrefabs[popupType] = CreateDefaultPopupPrefab(popupType);
                    JCDebug.Log($"[PopupManager] {popupType} 기본 프리팹 생성됨", JCDebug.LogLevel.Warning);
                }

                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }

            JCDebug.Log($"[PopupManager] {_popupPrefabs.Count}개 팝업 프리팹 로드 완료");
        }

        private GameObject CreateDefaultPopupPrefab(PopupType popupType)
        {
            // 기본 팝업 프리팹 생성 (프리팹이 없을 때)
            GameObject popup = new GameObject($"Default_{popupType}Popup");

            // 기본 컴포넌트 추가
            Canvas canvas = popup.AddComponent<Canvas>();
            CanvasGroup canvasGroup = popup.AddComponent<CanvasGroup>();

            // 팝업 스크립트 추가
            BasePopup popupScript = popup.AddComponent<BasePopup>();

            return popup;
        }

        private async UniTask InitializePopupPoolsAsync(CancellationToken cancellationToken)
        {
            foreach (var popupType in GetAllPopupTypes())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!_popupPrefabs.ContainsKey(popupType)) continue;

                var pool = new Queue<BasePopup>();

                // 팝업 타입별로 풀 크기 결정
                int poolSize = GetPoolSizeForPopupType(popupType);

                for (int i = 0; i < poolSize; i++)
                {
                    BasePopup popup = CreatePopupInstance(popupType);
                    if (popup != null)
                    {
                        popup.gameObject.SetActive(false);
                        pool.Enqueue(popup);
                    }
                }

                _popupPools[popupType] = pool;

                if (pool.Count % 3 == 0) // 3개마다 프레임 대기
                {
                    await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
                }
            }

            JCDebug.Log($"[PopupManager] 팝업 풀 초기화 완료");
        }

        private int GetPoolSizeForPopupType(PopupType popupType)
        {
            return popupType switch
            {
                PopupType.Alert => 3,
                PopupType.Confirm => 2,
                PopupType.ItemReward => 5,
                PopupType.LevelUp => 1,
                PopupType.GameOver => 1,
                PopupType.Reward => 3,
                _ => 2
            };
        }

        private void SetupEventConnections()
        {
            // 입력 이벤트 연결
            if (_closeWithEscapeKey)
            {
                // ESC 키 입력 처리는 Update에서 처리
            }
        }

        private void SetupManagerIntegrations()
        {
            // AudioManager와 연동
            OnPopupOpened += (popupType) =>
            {
                if (AudioManager.Instance != null)
                {
                    AudioManager.Instance.PlaySFX(GetPopupOpenSound(popupType));
                }
            };

            OnPopupClosed += (popupType) =>
            {
                if (AudioManager.Instance != null)
                {
                    AudioManager.Instance.PlaySFX(GetPopupCloseSound(popupType));
                }
            };

            // UIManager와 연동
            OnPopupStateChanged += (isAnyOpen) =>
            {
                UIManager.Instance?.NotifyPanelStateChanged(UIPanel.PopupSystem, isAnyOpen);
            };
        }

        #endregion

        #region 팝업 생성 및 표시

        /// <summary>
        /// 알림 팝업 표시
        /// </summary>
        public async UniTask<PopupResult> ShowAlertAsync(string title, string message, string buttonText = "확인")
        {
            var data = new PopupData
            {
                Title = title,
                Message = message,
                ButtonTexts = new[] { buttonText }
            };

            return await ShowPopupAsync(PopupType.Alert, data);
        }

        /// <summary>
        /// 확인/취소 팝업 표시
        /// </summary>
        public async UniTask<PopupResult> ShowConfirmAsync(string title, string message, string confirmText = "확인", string cancelText = "취소")
        {
            var data = new PopupData
            {
                Title = title,
                Message = message,
                ButtonTexts = new[] { confirmText, cancelText }
            };

            return await ShowPopupAsync(PopupType.Confirm, data);
        }

        /// <summary>
        /// 아이템 획득 팝업 표시
        /// </summary>
        public async UniTask<PopupResult> ShowItemRewardAsync(string itemName, Sprite itemIcon, int quantity = 1)
        {
            var data = new PopupData
            {
                Title = "아이템 획득!",
                Message = $"{itemName} x{quantity}",
                Icon = itemIcon,
                ButtonTexts = new[] { "확인" }
            };

            return await ShowPopupAsync(PopupType.ItemReward, data);
        }

        /// <summary>
        /// 레벨업 팝업 표시
        /// </summary>
        public async UniTask<PopupResult> ShowLevelUpAsync(int newLevel, int gainedStatPoints = 0)
        {
            var data = new PopupData
            {
                Title = "레벨 업!",
                Message = $"레벨 {newLevel} 달성!\n스탯 포인트 +{gainedStatPoints}",
                ButtonTexts = new[] { "확인" }
            };

            return await ShowPopupAsync(PopupType.LevelUp, data);
        }

        /// <summary>
        /// 게임오버 팝업 표시
        /// </summary>
        public async UniTask<PopupResult> ShowGameOverAsync(string reason = "")
        {
            var data = new PopupData
            {
                Title = "게임 오버",
                Message = string.IsNullOrEmpty(reason) ? "다시 도전하시겠습니까?" : reason,
                ButtonTexts = new[] { "재시작", "메뉴로" }
            };

            return await ShowPopupAsync(PopupType.GameOver, data);
        }

        /// <summary>
        /// 범용 팝업 표시
        /// </summary>
        public async UniTask<PopupResult> ShowPopupAsync(PopupType popupType, PopupData data)
        {
            if (!IsInitialized)
            {
                JCDebug.Log("[PopupManager] 아직 초기화되지 않았습니다.", JCDebug.LogLevel.Warning);
                return PopupResult.Cancelled;
            }

            BasePopup popup = GetPopupFromPool(popupType);
            if (popup == null)
            {
                JCDebug.Log($"[PopupManager] {popupType} 팝업 생성 실패", JCDebug.LogLevel.Error);
                return PopupResult.Cancelled;
            }

            // 팝업 스택에 추가
            _popupStack.Push(popup);
            UpdatePopupState(true);

            try
            {
                // 팝업 초기화 및 표시
                popup.Initialize(data);
                popup.gameObject.SetActive(true);

                // 애니메이션 재생
                await AnimatePopupInAsync(popup);

                // 이벤트 발생
                OnPopupOpened?.Invoke(popupType);

                // 사용자 입력 대기
                PopupResult result = await popup.WaitForResultAsync(_globalCancellationTokenSource.Token);

                // 애니메이션 재생
                await AnimatePopupOutAsync(popup);

                // 이벤트 발생
                OnPopupClosed?.Invoke(popupType);
                OnPopupCompleted?.Invoke(popupType, result);

                return result;
            }
            catch (OperationCanceledException)
            {
                JCDebug.Log($"[PopupManager] {popupType} 팝업이 취소됨", JCDebug.LogLevel.Warning);
                return PopupResult.Cancelled;
            }
            finally
            {
                // 팝업 정리
                CleanupPopup(popup, popupType);
            }
        }

        #endregion

        #region 팝업 스택 관리

        /// <summary>
        /// 현재 팝업 닫기
        /// </summary>
        public async UniTask CloseCurrentPopupAsync(PopupResult result = PopupResult.Cancelled)
        {
            if (_popupStack.Count == 0) return;

            BasePopup currentPopup = _popupStack.Peek();
            currentPopup.SetResult(result);
        }

        /// <summary>
        /// 모든 팝업 닫기
        /// </summary>
        public async UniTask CloseAllPopupsAsync()
        {
            while (_popupStack.Count > 0)
            {
                await CloseCurrentPopupAsync(PopupResult.Cancelled);
                await UniTask.DelayFrame(1); // 한 프레임 대기
            }
        }

        /// <summary>
        /// 특정 타입의 팝업이 열려있는지 확인
        /// </summary>
        public bool IsPopupOpen(PopupType popupType)
        {
            return _popupStack.Any(popup => popup.PopupType == popupType);
        }

        private void UpdatePopupState(bool isOpening)
        {
            bool wasAnyOpen = _isAnyPopupOpen;
            _isAnyPopupOpen = _popupStack.Count > 0;

            if (wasAnyOpen != _isAnyPopupOpen)
            {
                OnPopupStateChanged?.Invoke(_isAnyPopupOpen);

                // 게임 일시정지 처리
                if (_pauseGameWhenPopupOpen)
                {
                    if (_isAnyPopupOpen)
                    {
                        _originalTimeScale = Time.timeScale;
                        Time.timeScale = 0f;
                    }
                    else
                    {
                        Time.timeScale = _originalTimeScale;
                    }
                }

                // 배경 블로커 처리
                if (_backgroundBlocker != null)
                {
                    _backgroundBlocker.SetActive(_isAnyPopupOpen);

                    if (_isAnyPopupOpen)
                    {
                        _backgroundBlocker.transform.SetAsLastSibling();
                        if (_popupStack.Count > 0)
                        {
                            _popupStack.Peek().transform.SetAsLastSibling();
                        }
                    }
                }
            }

            if (!_isAnyPopupOpen)
            {
                OnAllPopupsClosed?.Invoke();
            }
        }

        private void CleanupPopup(BasePopup popup, PopupType popupType)
        {
            // 스택에서 제거
            if (_popupStack.Count > 0 && _popupStack.Peek() == popup)
            {
                _popupStack.Pop();
            }

            // 팝업 정리
            popup.gameObject.SetActive(false);

            // 풀로 반환
            ReturnPopupToPool(popup, popupType);

            // 상태 업데이트
            UpdatePopupState(false);
        }

        #endregion

        #region 오브젝트 풀링

        private BasePopup GetPopupFromPool(PopupType popupType)
        {
            if (_popupPools.TryGetValue(popupType, out Queue<BasePopup> pool) && pool.Count > 0)
            {
                return pool.Dequeue();
            }

            // 풀이 비어있으면 새로 생성
            return CreatePopupInstance(popupType);
        }

        private void ReturnPopupToPool(BasePopup popup, PopupType popupType)
        {
            if (popup == null) return;

            popup.Reset();

            if (_popupPools.TryGetValue(popupType, out Queue<BasePopup> pool))
            {
                pool.Enqueue(popup);
            }
        }

        private BasePopup CreatePopupInstance(PopupType popupType)
        {
            if (!_popupPrefabs.TryGetValue(popupType, out GameObject prefab))
            {
                JCDebug.Log($"[PopupManager] {popupType} 프리팹을 찾을 수 없습니다.", JCDebug.LogLevel.Warning);
                return null;
            }

            GameObject popupObj = Instantiate(prefab, _popupCanvas.transform);
            BasePopup popup = popupObj.GetComponent<BasePopup>();

            if (popup == null)
            {
                popup = popupObj.AddComponent<BasePopup>();
            }

            popup.PopupType = popupType;
            popup.PopupManager = this;

            return popup;
        }

        #endregion

        #region 애니메이션

        private async UniTask AnimatePopupInAsync(BasePopup popup)
        {
            if (popup == null) return;

            Transform popupTransform = popup.transform;
            CanvasGroup canvasGroup = popup.GetComponent<CanvasGroup>();

            if (canvasGroup == null)
                canvasGroup = popup.gameObject.AddComponent<CanvasGroup>();

            // 초기 상태 설정
            popupTransform.localScale = Vector3.one * 0.8f;
            canvasGroup.alpha = 0f;

            float duration = _popupSettings.animationDuration;
            float elapsedTime = 0f;

            while (elapsedTime < duration)
            {
                elapsedTime += Time.unscaledDeltaTime;
                float progress = elapsedTime / duration;

                // 이징 적용
                float easedProgress = _popupSettings.animationCurve.Evaluate(progress);

                // 스케일 애니메이션
                popupTransform.localScale = Vector3.Lerp(Vector3.one * 0.8f, Vector3.one, easedProgress);

                // 페이드 애니메이션
                canvasGroup.alpha = easedProgress;

                await UniTask.Yield(PlayerLoopTiming.LastUpdate);
            }

            // 최종 상태
            popupTransform.localScale = Vector3.one;
            canvasGroup.alpha = 1f;
        }

        private async UniTask AnimatePopupOutAsync(BasePopup popup)
        {
            if (popup == null) return;

            Transform popupTransform = popup.transform;
            CanvasGroup canvasGroup = popup.GetComponent<CanvasGroup>();

            if (canvasGroup == null) return;

            float duration = _popupSettings.animationDuration * 0.8f; // 닫기는 조금 더 빠르게
            float elapsedTime = 0f;

            Vector3 startScale = popupTransform.localScale;
            float startAlpha = canvasGroup.alpha;

            while (elapsedTime < duration)
            {
                elapsedTime += Time.unscaledDeltaTime;
                float progress = elapsedTime / duration;

                // 이징 적용
                float easedProgress = _popupSettings.animationCurve.Evaluate(progress);

                // 스케일 애니메이션
                popupTransform.localScale = Vector3.Lerp(startScale, Vector3.one * 0.8f, easedProgress);

                // 페이드 애니메이션
                canvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, easedProgress);

                await UniTask.Yield(PlayerLoopTiming.LastUpdate);
            }

            // 최종 상태
            popupTransform.localScale = Vector3.one * 0.8f;
            canvasGroup.alpha = 0f;
        }

        #endregion

        #region 유틸리티 메서드

        private AudioClip GetPopupOpenSound(PopupType popupType)
        {
            // 팝업 타입별 열기 사운드 반환
            return popupType switch
            {
                PopupType.Alert => null, // Resources.Load<AudioClip>("Audio/UI/PopupOpen"),
                PopupType.GameOver => null, // Resources.Load<AudioClip>("Audio/UI/GameOver"),
                _ => null // Resources.Load<AudioClip>("Audio/UI/PopupOpen")
            };
        }

        private AudioClip GetPopupCloseSound(PopupType popupType)
        {
            // 팝업 타입별 닫기 사운드 반환
            return null; // Resources.Load<AudioClip>("Audio/UI/PopupClose");
        }

        #endregion

        #region Update 및 생명주기

        private void Update()
        {
            if (!IsInitialized) return;

            // ESC 키로 팝업 닫기
            if (_closeWithEscapeKey && Input.GetKeyDown(KeyCode.Escape) && _popupStack.Count > 0)
            {
                CloseCurrentPopupAsync(PopupResult.Cancelled).Forget();
            }
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();

            // 취소 토큰 정리
            _globalCancellationTokenSource?.Cancel();
            _globalCancellationTokenSource?.Dispose();

            // 모든 팝업 닫기
            CloseAllPopupsAsync().Forget();

            // 타임스케일 복원
            if (_pauseGameWhenPopupOpen)
            {
                Time.timeScale = _originalTimeScale;
            }

            // 풀 정리
            foreach (var pool in _popupPools.Values)
            {
                pool.Clear();
            }
            _popupPools.Clear();
        }

        #endregion

        #region 디버그 메서드

        [ContextMenu("Show Test Alert")]
        private void ShowTestAlert()
        {
            ShowAlertAsync("테스트", "이것은 테스트 알림입니다.").Forget();
        }

        [ContextMenu("Show Test Confirm")]
        private void ShowTestConfirm()
        {
            ShowConfirmAsync("확인", "정말로 실행하시겠습니까?").Forget();
        }

        [ContextMenu("Close All Popups")]
        private void CloseAllPopupsForDebug()
        {
            CloseAllPopupsAsync().Forget();
        }

        public void DebugPopupState()
        {
            JCDebug.Log($"=== Popup Manager State ===");
            JCDebug.Log($"Is Any Popup Open: {_isAnyPopupOpen}");
            JCDebug.Log($"Popup Count: {_popupStack.Count}");
            JCDebug.Log($"Current Time Scale: {Time.timeScale}");

            foreach (var pool in _popupPools)
            {
                JCDebug.Log($"{pool.Key} Pool: {pool.Value.Count} available");
            }
        }

        #endregion
    }

    // ===== 보조 클래스들 및 열거형 =====

    public enum PopupType
    {
        Alert,
        Confirm,
        ItemReward,
        LevelUp,
        GameOver,
        Reward,
        Custom
    }

    public enum PopupResult
    {
        None,
        Confirm,
        Cancel,
        Button1,
        Button2,
        Button3,
        Cancelled,
        TimedOut
    }

    [System.Serializable]
    public class PopupSettings
    {
        [Header("Animation")]
        public float animationDuration = 0.3f;
        public AnimationCurve animationCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

        [Header("Background")]
        public float backgroundAlpha = 0.5f;

        [Header("Auto Close")]
        public bool enableAutoClose = false;
        public float autoCloseTime = 5f;

        [Header("Audio")]
        public bool enableSounds = true;
    }

    [System.Serializable]
    public class PopupData
    {
        public string Title;
        public string Message;
        public Sprite Icon;
        public string[] ButtonTexts;
        public float AutoCloseTime = 0f; // 0이면 자동 닫기 안함
        public bool UseUnscaledTime = true; // 게임 일시정지 상태에서도 동작

        public PopupData()
        {
            ButtonTexts = new[] { "확인" };
        }
    }

    /// <summary>
    /// 팝업 기본 클래스
    /// </summary>
    public class BasePopup : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] protected TextMeshProUGUI titleText;
        [SerializeField] protected TextMeshProUGUI messageText;
        [SerializeField] protected Image iconImage;
        [SerializeField] protected Button[] buttons;
        [SerializeField] protected TextMeshProUGUI[] buttonTexts;

        // 상태 관리
        public PopupType PopupType { get; set; }
        public PopupManager PopupManager { get; set; }

        protected PopupResult _result = PopupResult.None;
        protected bool _isWaitingForResult = false;
        protected CancellationTokenSource _popupCancellationTokenSource;

        /// <summary>
        /// 팝업 초기화
        /// </summary>
        public virtual void Initialize(PopupData data)
        {
            _result = PopupResult.None;
            _isWaitingForResult = true;
            _popupCancellationTokenSource = new CancellationTokenSource();

            // UI 업데이트
            if (titleText != null)
                titleText.text = data.Title;

            if (messageText != null)
                messageText.text = data.Message;

            if (iconImage != null)
            {
                iconImage.gameObject.SetActive(data.Icon != null);
                if (data.Icon != null)
                    iconImage.sprite = data.Icon;
            }

            // 버튼 설정
            SetupButtons(data.ButtonTexts);

            // 자동 닫기 설정
            if (data.AutoCloseTime > 0)
            {
                SetupAutoClose(data.AutoCloseTime, data.UseUnscaledTime);
            }
        }

        /// <summary>
        /// 사용자 입력 대기
        /// </summary>
        public async UniTask<PopupResult> WaitForResultAsync(CancellationToken cancellationToken)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _popupCancellationTokenSource.Token
            );

            try
            {
                while (_isWaitingForResult && _result == PopupResult.None)
                {
                    linkedCts.Token.ThrowIfCancellationRequested();
                    await UniTask.Yield(PlayerLoopTiming.LastUpdate);
                }
            }
            catch (OperationCanceledException)
            {
                _result = PopupResult.Cancelled;
            }

            return _result;
        }

        /// <summary>
        /// 결과 설정
        /// </summary>
        public void SetResult(PopupResult result)
        {
            _result = result;
            _isWaitingForResult = false;
            _popupCancellationTokenSource?.Cancel();
        }

        /// <summary>
        /// 팝업 리셋 (풀로 반환 시)
        /// </summary>
        public virtual void Reset()
        {
            _result = PopupResult.None;
            _isWaitingForResult = false;
            _popupCancellationTokenSource?.Cancel();
            _popupCancellationTokenSource?.Dispose();
            _popupCancellationTokenSource = null;
        }

        protected virtual void SetupButtons(string[] buttonLabels)
        {
            if (buttons == null || buttonLabels == null) return;

            for (int i = 0; i < buttons.Length; i++)
            {
                if (i < buttonLabels.Length)
                {
                    buttons[i].gameObject.SetActive(true);

                    if (buttonTexts != null && i < buttonTexts.Length)
                    {
                        buttonTexts[i].text = buttonLabels[i];
                    }

                    // 버튼 이벤트 연결
                    int buttonIndex = i;
                    buttons[i].onClick.RemoveAllListeners();
                    buttons[i].onClick.AddListener(() => OnButtonClicked(buttonIndex));
                }
                else
                {
                    buttons[i].gameObject.SetActive(false);
                }
            }
        }

        protected virtual void OnButtonClicked(int buttonIndex)
        {
            PopupResult result = buttonIndex switch
            {
                0 => PopupResult.Confirm,
                1 => PopupResult.Cancel,
                2 => PopupResult.Button3,
                _ => PopupResult.Button1
            };

            SetResult(result);
        }

        protected virtual void SetupAutoClose(float autoCloseTime, bool useUnscaledTime)
        {
            AutoCloseAsync(autoCloseTime, useUnscaledTime).Forget();
        }

        protected virtual async UniTaskVoid AutoCloseAsync(float delay, bool useUnscaledTime)
        {
            try
            {
                if (useUnscaledTime)
                {
                    await UniTask.Delay(TimeSpan.FromSeconds(delay).Milliseconds, false, PlayerLoopTiming.LastUpdate, _popupCancellationTokenSource.Token);
                }
                else
                {
                    await UniTask.Delay(TimeSpan.FromSeconds(delay).Milliseconds, false, PlayerLoopTiming.Update, _popupCancellationTokenSource.Token);
                }

                if (_isWaitingForResult)
                {
                    SetResult(PopupResult.TimedOut);
                }
            }
            catch (OperationCanceledException)
            {
                // 정상적인 취소
            }
        }
    }
}

/* 
=== UnifiedGameManager에 등록 ===
RegisterManager<PopupManager>("UI", InitializationPriority.Low);

=== 주요 특징 ===
1. 스컬 게임에 최적화된 팝업 시스템
2. 팝업 스택 관리 (여러 팝업 동시 지원)
3. 오브젝트 풀링으로 성능 최적화
4. 애니메이션 시스템 (스케일 + 페이드)
5. 게임 일시정지 자동 처리
6. 취소 토큰 지원으로 안전한 비동기 처리
7. 다양한 팝업 타입 지원
8. UIManager와 AudioManager 연동
9. 자동 닫기 기능
10. ESC 키로 팝업 닫기

=== 사용 예시 ===
// 알림 팝업
await PopupManager.Instance.ShowAlertAsync("알림", "아이템을 획득했습니다!");

// 확인/취소 팝업
PopupResult result = await PopupManager.Instance.ShowConfirmAsync("확인", "정말 삭제하시겠습니까?");
if (result == PopupResult.Confirm) {
    // 확인 버튼 클릭됨
}

// 아이템 획득 팝업
await PopupManager.Instance.ShowItemRewardAsync("마법 검", itemIcon, 1);

// 레벨업 팝업
await PopupManager.Instance.ShowLevelUpAsync(15, 5);

// 게임오버 팝업
PopupResult gameOverResult = await PopupManager.Instance.ShowGameOverAsync();

=== 프리팹 구조 예시 ===
UI/Popups/AlertPopup.prefab:
- Canvas
  - Background (Image)
  - Panel (Image)
    - Title (TextMeshPro)
    - Message (TextMeshPro)
    - ButtonContainer
      - ConfirmButton (Button + TextMeshPro)
*/