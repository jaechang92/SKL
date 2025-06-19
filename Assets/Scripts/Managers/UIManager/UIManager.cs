using CustomDebug;
using Cysharp.Threading.Tasks;
using Metamorph.Core;
using Metamorph.Core.Interfaces;
using Metamorph.Forms.Base;
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
    /// 스컬 게임 UI 관리 시스템
    /// 모든 UI 패널, 애니메이션, 이벤트를 통합 관리
    /// </summary>
    public class UIManager : SingletonManager<UIManager>, IInitializableAsync
    {
        [Header("Canvas References")]
        [SerializeField] private Canvas _mainCanvas;
        [SerializeField] private Canvas _hudCanvas;
        [SerializeField] private Canvas _menuCanvas;
        [SerializeField] private Canvas _overlayCanvas;
        [SerializeField] private CanvasScaler _canvasScaler;

        [Header("UI Prefabs")]
        [SerializeField] private GameObject _hudPrefab;
        [SerializeField] private GameObject _inventoryPrefab;
        [SerializeField] private GameObject _settingsPrefab;
        [SerializeField] private GameObject _pauseMenuPrefab;
        [SerializeField] private GameObject _damageNumberPrefab;
        [SerializeField] private GameObject _statusEffectPrefab;

        [Header("UI Settings")]
        [SerializeField] private UISettings _uiSettings = new UISettings();
        [SerializeField] private bool _autoCreateCanvases = true;
        [SerializeField] private bool _preloadAllPanels = false;

        [Header("Animation Settings")]
        [SerializeField] private float _defaultFadeTime = 0.3f;
        [SerializeField] private float _defaultSlideTime = 0.4f;
        [SerializeField] private AnimationCurve _defaultEaseInOut = AnimationCurve.EaseInOut(0, 0, 1, 1);

        // IManagerInitializable 구현
        public string Name => "UIManager";
        public InitializationPriority Priority { get; set; } = InitializationPriority.Low;
        public bool IsInitialized { get; private set; } = false;

        // UI 패널 관리
        private Dictionary<UIPanel, GameObject> _activePanels = new Dictionary<UIPanel, GameObject>();
        private Dictionary<UIPanel, GameObject> _pooledPanels = new Dictionary<UIPanel, GameObject>();
        private Dictionary<UIPanel, bool> _panelStates = new Dictionary<UIPanel, bool>();

        // UI 컴포넌트 참조
        private PlayerHUD _playerHUD;
        private InventoryUI _inventoryUI;
        private SettingsUI _settingsUI;
        private PauseMenuUI _pauseMenuUI;

        // 데미지 넘버 풀링
        private Queue<DamageNumber> _damageNumberPool = new Queue<DamageNumber>();
        private List<DamageNumber> _activeDamageNumbers = new List<DamageNumber>();

        // 상태 효과 UI 풀링
        private Queue<StatusEffectUI> _statusEffectPool = new Queue<StatusEffectUI>();
        private List<StatusEffectUI> _activeStatusEffects = new List<StatusEffectUI>();

        // 이벤트 시스템 (옵저버 패턴)
        public event Action<UIPanel> OnPanelOpened;
        public event Action<UIPanel> OnPanelClosed;
        public event Action<UIPanel, bool> OnPanelStateChanged;
        public event Action OnUIInitialized;
        public event Action OnHUDUpdated;

        // 프로퍼티
        public bool IsAnyMenuOpen => _activePanels.Values.Any(panel => panel != null && panel.activeInHierarchy);
        public PlayerHUD PlayerHUD => _playerHUD;
        public bool IsInitializing { get; private set; } = false;

        #region IManagerInitializable 구현

        public async UniTask InitializeAsync(CancellationToken cancellationToken = default)
        {
            if (IsInitialized || IsInitializing) return;

            IsInitializing = true;

            try
            {
                JCDebug.Log("[UIManager] UI 시스템 초기화 시작");

                // 1. Canvas 시스템 설정
                await SetupCanvasSystemAsync(cancellationToken);

                // 2. UI 프리팹 로드
                await LoadUIPrefabsAsync(cancellationToken);

                // 3. 핵심 UI 패널 생성
                await CreateCoreUIPanelsAsync(cancellationToken);

                // 4. UI 풀 시스템 초기화
                await InitializeUIPoolsAsync(cancellationToken);

                // 5. UI 이벤트 연결
                SetupUIEventConnections();

                // 6. 다른 매니저와 연동
                SetupManagerIntegrations();

                IsInitialized = true;
                OnUIInitialized?.Invoke();
                JCDebug.Log("[UIManager] UI 시스템 초기화 완료");
            }
            catch (OperationCanceledException)
            {
                JCDebug.Log("[UIManager] 초기화가 취소됨", JCDebug.LogLevel.Warning);
                throw;
            }
            catch (Exception ex)
            {
                JCDebug.Log($"[UIManager] 초기화 실패: {ex.Message}", JCDebug.LogLevel.Error);
                throw;
            }
            finally
            {
                IsInitializing = false;
            }
        }

        #endregion

        #region 초기화 메서드들

        private async UniTask SetupCanvasSystemAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_autoCreateCanvases)
            {
                await CreateCanvasHierarchyAsync(cancellationToken);
            }
            else
            {
                FindExistingCanvases();
            }

            SetupCanvasSettings();
            await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
        }

        private async UniTask CreateCanvasHierarchyAsync(CancellationToken cancellationToken)
        {
            // Main Canvas (UI Root)
            if (_mainCanvas == null)
            {
                GameObject mainCanvasObj = new GameObject("MainCanvas");
                _mainCanvas = mainCanvasObj.AddComponent<Canvas>();
                _mainCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                _mainCanvas.sortingOrder = 0;

                _canvasScaler = mainCanvasObj.AddComponent<CanvasScaler>();
                mainCanvasObj.AddComponent<GraphicRaycaster>();
                DontDestroyOnLoad(mainCanvasObj);
            }

            await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);

            // HUD Canvas (항상 표시되는 UI)
            if (_hudCanvas == null)
            {
                GameObject hudCanvasObj = new GameObject("HUDCanvas");
                hudCanvasObj.transform.SetParent(_mainCanvas.transform, false);
                _hudCanvas = hudCanvasObj.AddComponent<Canvas>();
                _hudCanvas.overrideSorting = true;
                _hudCanvas.sortingOrder = 1;
                hudCanvasObj.AddComponent<GraphicRaycaster>();
            }

            await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);

            // Menu Canvas (메뉴 UI)
            if (_menuCanvas == null)
            {
                GameObject menuCanvasObj = new GameObject("MenuCanvas");
                menuCanvasObj.transform.SetParent(_mainCanvas.transform, false);
                _menuCanvas = menuCanvasObj.AddComponent<Canvas>();
                _menuCanvas.overrideSorting = true;
                _menuCanvas.sortingOrder = 2;
                menuCanvasObj.AddComponent<GraphicRaycaster>();
            }

            await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);

            // Overlay Canvas (최상위 UI - 데미지 넘버, 이펙트 등)
            if (_overlayCanvas == null)
            {
                GameObject overlayCanvasObj = new GameObject("OverlayCanvas");
                overlayCanvasObj.transform.SetParent(_mainCanvas.transform, false);
                _overlayCanvas = overlayCanvasObj.AddComponent<Canvas>();
                _overlayCanvas.overrideSorting = true;
                _overlayCanvas.sortingOrder = 3;
                overlayCanvasObj.AddComponent<GraphicRaycaster>();
            }

            JCDebug.Log("[UIManager] Canvas 계층 구조 생성 완료");
        }

        private void FindExistingCanvases()
        {
            if (_mainCanvas == null)
                _mainCanvas = FindAnyObjectByType<Canvas>();

            if (_canvasScaler == null && _mainCanvas != null)
                _canvasScaler = _mainCanvas.GetComponent<CanvasScaler>();
        }

        private void SetupCanvasSettings()
        {
            if (_canvasScaler != null)
            {
                _canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                _canvasScaler.referenceResolution = new Vector2(1920, 1080);
                _canvasScaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                _canvasScaler.matchWidthOrHeight = 0.5f;
            }
        }

        private async UniTask LoadUIPrefabsAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 메인 스레드에서 직접 로드 (Unity API는 메인 스레드 전용)
            if (_hudPrefab == null)
                _hudPrefab = Resources.Load<GameObject>("UI/HUD/PlayerHUD");

            if (_inventoryPrefab == null)
                _inventoryPrefab = Resources.Load<GameObject>("UI/Menus/InventoryUI");

            if (_settingsPrefab == null)
                _settingsPrefab = Resources.Load<GameObject>("UI/Menus/SettingsUI");

            if (_pauseMenuPrefab == null)
                _pauseMenuPrefab = Resources.Load<GameObject>("UI/Menus/PauseMenuUI");

            if (_damageNumberPrefab == null)
                _damageNumberPrefab = Resources.Load<GameObject>("UI/Effects/DamageNumber");

            if (_statusEffectPrefab == null)
                _statusEffectPrefab = Resources.Load<GameObject>("UI/Effects/StatusEffect");

            // 프레임 드롭 방지를 위해 잠시 대기 (선택사항)
            await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);

            JCDebug.Log("[UIManager] UI 프리팹 로드 완료");
        }

        private async UniTask CreateCoreUIPanelsAsync(CancellationToken cancellationToken)
        {
            // Player HUD 생성 (항상 활성화)
            if (_hudPrefab != null && _hudCanvas != null)
            {
                GameObject hudObj = Instantiate(_hudPrefab, _hudCanvas.transform);
                _playerHUD = hudObj.GetComponent<PlayerHUD>();

                if (_playerHUD != null)
                {
                    await _playerHUD.InitializeAsync(cancellationToken);
                    _activePanels[UIPanel.HUD] = hudObj;
                    _panelStates[UIPanel.HUD] = true;
                }
            }

            await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);

            // 미리 로드할 패널들 (옵션)
            if (_preloadAllPanels)
            {
                await PreloadAllPanelsAsync(cancellationToken);
            }

            JCDebug.Log("[UIManager] 핵심 UI 패널 생성 완료");
        }

        private async UniTask PreloadAllPanelsAsync(CancellationToken cancellationToken)
        {
            var panelsToPreload = new[]
            {
                (UIPanel.Inventory, _inventoryPrefab),
                (UIPanel.Settings, _settingsPrefab),
                (UIPanel.PauseMenu, _pauseMenuPrefab)
            };

            foreach (var (panel, prefab) in panelsToPreload)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (prefab != null)
                {
                    GameObject panelObj = Instantiate(prefab, _menuCanvas.transform);
                    panelObj.SetActive(false);
                    _pooledPanels[panel] = panelObj;
                }

                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }
        }

        private async UniTask InitializeUIPoolsAsync(CancellationToken cancellationToken)
        {
            // 데미지 넘버 풀 초기화
            if (_damageNumberPrefab != null)
            {
                for (int i = 0; i < _uiSettings.damageNumberPoolSize; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    GameObject damageNumObj = Instantiate(_damageNumberPrefab, _overlayCanvas.transform);
                    DamageNumber damageNumber = damageNumObj.GetComponent<DamageNumber>();

                    if (damageNumber == null)
                        damageNumber = damageNumObj.AddComponent<DamageNumber>();

                    damageNumber.Initialize();
                    damageNumObj.SetActive(false);
                    _damageNumberPool.Enqueue(damageNumber);

                    if (i % 5 == 0) // 5개마다 한 프레임 대기
                        await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
                }
            }

            // 상태 효과 UI 풀 초기화
            if (_statusEffectPrefab != null)
            {
                for (int i = 0; i < _uiSettings.statusEffectPoolSize; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    GameObject statusEffectObj = Instantiate(_statusEffectPrefab, _hudCanvas.transform);
                    StatusEffectUI statusEffect = statusEffectObj.GetComponent<StatusEffectUI>();

                    if (statusEffect == null)
                        statusEffect = statusEffectObj.AddComponent<StatusEffectUI>();

                    statusEffect.Initialize();
                    statusEffectObj.SetActive(false);
                    _statusEffectPool.Enqueue(statusEffect);

                    if (i % 3 == 0) // 3개마다 한 프레임 대기
                        await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
                }
            }

            JCDebug.Log($"[UIManager] UI 풀 초기화 완료 (데미지: {_damageNumberPool.Count}, 상태효과: {_statusEffectPool.Count})");
        }

        private void SetupUIEventConnections()
        {
            // UI 패널들의 이벤트 연결
            if (_playerHUD != null)
            {
                _playerHUD.OnHealthChanged += OnPlayerHealthChanged;
                _playerHUD.OnManaChanged += OnPlayerManaChanged;
                _playerHUD.OnSkillCooldownChanged += OnSkillCooldownChanged;
            }

            // Input 이벤트 연결
            SetupInputEventConnections();
        }

        private void SetupInputEventConnections()
        {
            // 키보드 입력 이벤트 연결 (예시)
            // 실제 Input System과 연동 필요
        }

        private void SetupManagerIntegrations()
        {
            // SkillManager와 연동
            if (SkillManager.Instance != null)
            {
                SkillManager.Instance.OnSkillCooldownChanged.AddListener(OnSkillCooldownUpdate);
                SkillManager.Instance.OnSkillUsed.AddListener(OnSkillUsed);
            }

            // FormManager와 연동
            if (FormManager.Instance != null)
            {
                FormManager.Instance.RegisterPlayer(OnPlayerFormChanged);
            }

            // AudioManager와 연동
            if (AudioManager.Instance != null)
            {
                OnPanelOpened += (panel) => AudioManager.Instance.PlaySFX(GetPanelOpenSound(panel));
                OnPanelClosed += (panel) => AudioManager.Instance.PlaySFX(GetPanelCloseSound(panel));
            }
        }

        #endregion

        #region UI 패널 관리

        /// <summary>
        /// UI 패널 열기
        /// </summary>
        public async UniTask ShowPanelAsync(UIPanel panel, bool animate = true)
        {
            GameObject panelObj = GetOrCreatePanel(panel);
            if (panelObj == null) return;

            // 이미 활성화되어 있으면 무시
            if (_panelStates.TryGetValue(panel, out bool isActive) && isActive) return;

            panelObj.SetActive(true);
            _activePanels[panel] = panelObj;
            _panelStates[panel] = true;

            if (animate)
            {
                await AnimatePanelInAsync(panelObj);
            }

            OnPanelOpened?.Invoke(panel);
            OnPanelStateChanged?.Invoke(panel, true);

            JCDebug.Log($"[UIManager] 패널 열림: {panel}");
        }

        /// <summary>
        /// UI 패널 닫기
        /// </summary>
        public async UniTask HidePanelAsync(UIPanel panel, bool animate = true)
        {
            if (!_activePanels.TryGetValue(panel, out GameObject panelObj) || panelObj == null) return;

            if (animate)
            {
                await AnimatePanelOutAsync(panelObj);
            }

            panelObj.SetActive(false);
            _activePanels.Remove(panel);
            _panelStates[panel] = false;

            // 풀로 반환 (필요시)
            if (_uiSettings.usePooling && panel != UIPanel.HUD)
            {
                _pooledPanels[panel] = panelObj;
            }

            OnPanelClosed?.Invoke(panel);
            OnPanelStateChanged?.Invoke(panel, false);

            JCDebug.Log($"[UIManager] 패널 닫힘: {panel}");
        }

        /// <summary>
        /// UI 패널 토글
        /// </summary>
        public async UniTask TogglePanelAsync(UIPanel panel, bool animate = true)
        {
            bool isOpen = _panelStates.TryGetValue(panel, out bool state) && state;

            if (isOpen)
            {
                await HidePanelAsync(panel, animate);
            }
            else
            {
                await ShowPanelAsync(panel, animate);
            }
        }

        /// <summary>
        /// 모든 메뉴 패널 닫기
        /// </summary>
        public async UniTask CloseAllMenusAsync(bool animate = true)
        {
            var menuPanels = new[] { UIPanel.Inventory, UIPanel.Settings, UIPanel.PauseMenu };
            var tasks = new List<UniTask>();

            foreach (var panel in menuPanels)
            {
                if (_panelStates.TryGetValue(panel, out bool isOpen) && isOpen)
                {
                    tasks.Add(HidePanelAsync(panel, animate));
                }
            }

            await UniTask.WhenAll(tasks);
        }

        private GameObject GetOrCreatePanel(UIPanel panel)
        {
            // 이미 활성화된 패널이 있으면 반환
            if (_activePanels.TryGetValue(panel, out GameObject activePanel) && activePanel != null)
            {
                return activePanel;
            }

            // 풀에서 가져오기
            if (_pooledPanels.TryGetValue(panel, out GameObject pooledPanel) && pooledPanel != null)
            {
                _pooledPanels.Remove(panel);
                return pooledPanel;
            }

            // 새로 생성
            return CreatePanel(panel);
        }

        private GameObject CreatePanel(UIPanel panel)
        {
            GameObject prefab = panel switch
            {
                UIPanel.Inventory => _inventoryPrefab,
                UIPanel.Settings => _settingsPrefab,
                UIPanel.PauseMenu => _pauseMenuPrefab,
                _ => null
            };

            if (prefab == null)
            {
                JCDebug.Log($"[UIManager] {panel} 프리팹을 찾을 수 없습니다.", JCDebug.LogLevel.Warning);
                return null;
            }

            Canvas targetCanvas = GetCanvasForPanel(panel);
            GameObject panelObj = Instantiate(prefab, targetCanvas.transform);
            panelObj.SetActive(false);

            // 패널별 특수 초기화
            InitializePanelComponent(panel, panelObj);

            return panelObj;
        }

        private Canvas GetCanvasForPanel(UIPanel panel)
        {
            return panel switch
            {
                UIPanel.HUD => _hudCanvas,
                UIPanel.Inventory => _menuCanvas,
                UIPanel.Settings => _menuCanvas,
                UIPanel.PauseMenu => _menuCanvas,
                _ => _mainCanvas
            };
        }

        private void InitializePanelComponent(UIPanel panel, GameObject panelObj)
        {
            switch (panel)
            {
                case UIPanel.Inventory:
                    _inventoryUI = panelObj.GetComponent<InventoryUI>();
                    break;
                case UIPanel.Settings:
                    _settingsUI = panelObj.GetComponent<SettingsUI>();
                    break;
                case UIPanel.PauseMenu:
                    _pauseMenuUI = panelObj.GetComponent<PauseMenuUI>();
                    break;
            }
        }

        /// <summary>
        /// 외부에서 패널 상태 변경 알림 (PopupManager용)
        /// </summary>
        public void NotifyPanelStateChanged(UIPanel panel, bool isOpen)
        {
            OnPanelStateChanged?.Invoke(panel, isOpen);
        }

        #endregion

        #region UI 애니메이션

        private async UniTask AnimatePanelInAsync(GameObject panel)
        {
            var canvasGroup = panel.GetComponent<CanvasGroup>();
            if (canvasGroup == null)
                canvasGroup = panel.AddComponent<CanvasGroup>();

            // 페이드 인 애니메이션
            canvasGroup.alpha = 0f;
            float elapsedTime = 0f;

            while (elapsedTime < _defaultFadeTime)
            {
                elapsedTime += Time.deltaTime;
                float progress = elapsedTime / _defaultFadeTime;
                canvasGroup.alpha = _defaultEaseInOut.Evaluate(progress);
                await UniTask.Yield();
            }

            canvasGroup.alpha = 1f;
        }

        private async UniTask AnimatePanelOutAsync(GameObject panel)
        {
            var canvasGroup = panel.GetComponent<CanvasGroup>();
            if (canvasGroup == null) return;

            // 페이드 아웃 애니메이션
            float startAlpha = canvasGroup.alpha;
            float elapsedTime = 0f;

            while (elapsedTime < _defaultFadeTime)
            {
                elapsedTime += Time.deltaTime;
                float progress = elapsedTime / _defaultFadeTime;
                canvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, _defaultEaseInOut.Evaluate(progress));
                await UniTask.Yield();
            }

            canvasGroup.alpha = 0f;
        }

        #endregion

        #region 데미지 넘버 시스템

        /// <summary>
        /// 데미지 넘버 표시
        /// </summary>
        public void ShowDamageNumber(Vector3 worldPosition, float damage, DamageType damageType = DamageType.Normal)
        {
            DamageNumber damageNumber = GetDamageNumberFromPool();
            if (damageNumber == null) return;

            // 월드 좌표를 UI 좌표로 변환
            Vector2 screenPosition = Camera.main.WorldToScreenPoint(worldPosition);
            Vector2 uiPosition;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _overlayCanvas.transform as RectTransform,
                screenPosition,
                _overlayCanvas.worldCamera,
                out uiPosition
            );

            damageNumber.Show(uiPosition, damage, damageType);
            _activeDamageNumbers.Add(damageNumber);
        }

        private DamageNumber GetDamageNumberFromPool()
        {
            if (_damageNumberPool.Count > 0)
            {
                return _damageNumberPool.Dequeue();
            }

            // 풀이 비어있으면 새로 생성
            if (_damageNumberPrefab != null)
            {
                GameObject damageNumObj = Instantiate(_damageNumberPrefab, _overlayCanvas.transform);
                DamageNumber damageNumber = damageNumObj.GetComponent<DamageNumber>();

                if (damageNumber == null)
                    damageNumber = damageNumObj.AddComponent<DamageNumber>();

                damageNumber.Initialize();
                return damageNumber;
            }

            return null;
        }

        /// <summary>
        /// 데미지 넘버를 풀로 반환
        /// </summary>
        public void ReturnDamageNumberToPool(DamageNumber damageNumber)
        {
            if (damageNumber == null) return;

            _activeDamageNumbers.Remove(damageNumber);
            damageNumber.gameObject.SetActive(false);
            _damageNumberPool.Enqueue(damageNumber);
        }

        #endregion

        #region 상태 효과 UI 시스템

        /// <summary>
        /// 상태 효과 UI 표시
        /// </summary>
        public void ShowStatusEffect(StatusEffectData statusEffect)
        {
            StatusEffectUI statusEffectUI = GetStatusEffectFromPool();
            if (statusEffectUI == null) return;

            statusEffectUI.Show(statusEffect);
            _activeStatusEffects.Add(statusEffectUI);
        }

        /// <summary>
        /// 상태 효과 UI 제거
        /// </summary>
        public void HideStatusEffect(string statusEffectId)
        {
            var statusEffectUI = _activeStatusEffects.FirstOrDefault(se => se.StatusEffectId == statusEffectId);
            if (statusEffectUI != null)
            {
                statusEffectUI.Hide();
                ReturnStatusEffectToPool(statusEffectUI);
            }
        }

        private StatusEffectUI GetStatusEffectFromPool()
        {
            if (_statusEffectPool.Count > 0)
            {
                return _statusEffectPool.Dequeue();
            }

            // 풀이 비어있으면 새로 생성
            if (_statusEffectPrefab != null)
            {
                GameObject statusEffectObj = Instantiate(_statusEffectPrefab, _hudCanvas.transform);
                StatusEffectUI statusEffect = statusEffectObj.GetComponent<StatusEffectUI>();

                if (statusEffect == null)
                    statusEffect = statusEffectObj.AddComponent<StatusEffectUI>();

                statusEffect.Initialize();
                return statusEffect;
            }

            return null;
        }

        /// <summary>
        /// 상태 효과 UI를 풀로 반환
        /// </summary>
        public void ReturnStatusEffectToPool(StatusEffectUI statusEffect)
        {
            if (statusEffect == null) return;

            _activeStatusEffects.Remove(statusEffect);
            statusEffect.gameObject.SetActive(false);
            _statusEffectPool.Enqueue(statusEffect);
        }

        #endregion

        #region 이벤트 핸들러

        private void OnPlayerHealthChanged(float currentHealth, float maxHealth)
        {
            // 추가 UI 업데이트 처리
            OnHUDUpdated?.Invoke();
        }

        private void OnPlayerManaChanged(float currentMana, float maxMana)
        {
            // 추가 UI 업데이트 처리
            OnHUDUpdated?.Invoke();
        }

        private void OnSkillCooldownChanged(int skillIndex, float currentCooldown, float maxCooldown)
        {
            // 스킬 쿨다운 UI 업데이트
            _playerHUD?.UpdateSkillCooldown(skillIndex, currentCooldown, maxCooldown);
        }

        private void OnSkillCooldownUpdate(int skillIndex, float currentCooldown, float maxCooldown)
        {
            OnSkillCooldownChanged(skillIndex, currentCooldown, maxCooldown);
        }

        private void OnSkillUsed(ISkill skill)
        {
            // 스킬 사용 UI 효과
            _playerHUD?.PlaySkillUseEffect(skill);
        }

        private void OnPlayerFormChanged(FormData newForm)
        {
            // 형태 변경 시 UI 업데이트
            _playerHUD?.UpdateFormDisplay(newForm);
        }

        private AudioClip GetPanelOpenSound(UIPanel panel)
        {
            // 패널별 열기 사운드 반환
            return null; // 실제 구현에서는 사운드 클립 반환
        }

        private AudioClip GetPanelCloseSound(UIPanel panel)
        {
            // 패널별 닫기 사운드 반환
            return null; // 실제 구현에서는 사운드 클립 반환
        }

        #endregion

        #region 공개 API

        /// <summary>
        /// HUD 업데이트 (외부에서 호출)
        /// </summary>
        public void UpdateHUD(float health, float maxHealth, float mana, float maxMana)
        {
            _playerHUD?.UpdateHealthBar(health, maxHealth);
            _playerHUD?.UpdateManaBar(mana, maxMana);
        }

        /// <summary>
        /// 패널 상태 확인
        /// </summary>
        public bool IsPanelOpen(UIPanel panel)
        {
            return _panelStates.TryGetValue(panel, out bool isOpen) && isOpen;
        }

        /// <summary>
        /// UI 전체 표시/숨김
        /// </summary>
        public void SetUIVisibility(bool visible)
        {
            if (_mainCanvas != null)
            {
                _mainCanvas.gameObject.SetActive(visible);
            }
        }

        #endregion

        #region Update 및 생명주기

        private void Update()
        {
            if (!IsInitialized) return;

            // 활성 데미지 넘버 업데이트
            UpdateActiveDamageNumbers();

            // 활성 상태 효과 UI 업데이트
            UpdateActiveStatusEffects();
        }

        private void UpdateActiveDamageNumbers()
        {
            for (int i = _activeDamageNumbers.Count - 1; i >= 0; i--)
            {
                var damageNumber = _activeDamageNumbers[i];
                if (damageNumber == null || !damageNumber.IsActive)
                {
                    if (damageNumber != null)
                    {
                        ReturnDamageNumberToPool(damageNumber);
                    }
                    else
                    {
                        _activeDamageNumbers.RemoveAt(i);
                    }
                }
            }
        }

        private void UpdateActiveStatusEffects()
        {
            for (int i = _activeStatusEffects.Count - 1; i >= 0; i--)
            {
                var statusEffect = _activeStatusEffects[i];
                if (statusEffect == null || !statusEffect.IsActive)
                {
                    if (statusEffect != null)
                    {
                        ReturnStatusEffectToPool(statusEffect);
                    }
                    else
                    {
                        _activeStatusEffects.RemoveAt(i);
                    }
                }
            }
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();

            // 이벤트 구독 해제
            if (SkillManager.Instance != null)
            {
                SkillManager.Instance.OnSkillCooldownChanged.RemoveListener(OnSkillCooldownUpdate);
                SkillManager.Instance.OnSkillUsed.RemoveListener(OnSkillUsed);
            }

            if (FormManager.Instance != null)
            {
                FormManager.Instance.UnregisterPlayer(OnPlayerFormChanged);
            }

            // 풀 정리
            _damageNumberPool.Clear();
            _statusEffectPool.Clear();
            _activeDamageNumbers.Clear();
            _activeStatusEffects.Clear();
        }

        #endregion
    }

    // ===== 보조 클래스들 및 열거형 =====

    public enum UIPanel
    {
        HUD,
        Inventory,
        Settings,
        PauseMenu,
        DeathScreen,
        LevelComplete,
        BossHealthBar,
        Minimap,
        FormSwitch,
        SkillTree,
        PopupSystem
    }

    public enum DamageType
    {
        Normal,
        Critical,
        Heal,
        Mana,
        Experience
    }

    [System.Serializable]
    public class UISettings
    {
        [Header("Pool Settings")]
        public bool usePooling = true;
        public int damageNumberPoolSize = 20;
        public int statusEffectPoolSize = 10;

        [Header("Animation Settings")]
        public bool useAnimations = true;
        public float defaultAnimationSpeed = 1f;

        [Header("Performance")]
        public bool enableUIOptimization = true;
        public int maxUIUpdatesPerFrame = 5;
    }

    // ===== UI 컴포넌트 인터페이스들 =====

    public abstract class UIComponent : MonoBehaviour
    {
        public abstract void Initialize();
        public virtual async UniTask InitializeAsync(CancellationToken cancellationToken = default)
        {
            Initialize();
            await UniTask.CompletedTask;
        }
    }

    public class PlayerHUD : UIComponent
    {
        public event Action<float, float> OnHealthChanged;
        public event Action<float, float> OnManaChanged;
        public event Action<int, float, float> OnSkillCooldownChanged;

        public override void Initialize() { }
        public void UpdateHealthBar(float current, float max) { }
        public void UpdateManaBar(float current, float max) { }
        public void UpdateSkillCooldown(int index, float current, float max) { }
        public void PlaySkillUseEffect(ISkill skill) { }
        public void UpdateFormDisplay(FormData form) { }
    }

    public class InventoryUI : UIComponent
    {
        public override void Initialize() { }
    }

    public class SettingsUI : UIComponent
    {
        public override void Initialize() { }
    }

    public class PauseMenuUI : UIComponent
    {
        public override void Initialize() { }
    }

    public class DamageNumber : UIComponent
    {
        public bool IsActive { get; private set; }
        public override void Initialize() { }
        public void Show(Vector2 position, float damage, DamageType type) { IsActive = true; }
    }

    public class StatusEffectUI : UIComponent
    {
        public string StatusEffectId { get; private set; }
        public bool IsActive { get; private set; }
        public override void Initialize() { }
        public void Show(StatusEffectData data) { IsActive = true; }
        public void Hide() { IsActive = false; }
    }

    [System.Serializable]
    public class StatusEffectData
    {
        public string id;
        public string name;
        public Sprite icon;
        public float duration;
        public string description;
    }
}

/* 
=== UnifiedGameManager에 등록 ===
RegisterManager<UIManager>("UI", InitializationPriority.Low);

=== 주요 특징 ===
1. 스컬 게임에 최적화된 UI 시스템
2. Canvas 계층적 관리 (HUD/Menu/Overlay)
3. UI 패널 생명주기 관리 (Show/Hide/Toggle)
4. 데미지 넘버 풀링 시스템
5. 상태 효과 UI 관리
6. UI 애니메이션 시스템
7. 다른 매니저들과 완벽 연동
8. 성능 최적화 (풀링, 업데이트 최적화)
9. 이벤트 기반 UI 업데이트
10. 확장 가능한 구조

=== 사용 예시 ===
// UI 패널 제어
await UIManager.Instance.ShowPanelAsync(UIPanel.Inventory);
await UIManager.Instance.HidePanelAsync(UIPanel.Settings);
await UIManager.Instance.TogglePanelAsync(UIPanel.PauseMenu);

// 데미지 넘버 표시
UIManager.Instance.ShowDamageNumber(enemyPosition, 150f, DamageType.Critical);

// HUD 업데이트
UIManager.Instance.UpdateHUD(currentHP, maxHP, currentMP, maxMP);

// 상태 효과 표시
UIManager.Instance.ShowStatusEffect(poisonEffect);
*/