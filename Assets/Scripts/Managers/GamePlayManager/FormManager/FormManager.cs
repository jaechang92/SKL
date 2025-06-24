// Assets/Scripts/Managers/GamePlayManager/FormManager/FormManager.cs
using CustomDebug;
using Cysharp.Threading.Tasks;
using Metamorph.Core.Interfaces;
using Metamorph.Forms.Base;
using Metamorph.Forms.Data;
using Metamorph.Initialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;

namespace Metamorph.Managers
{
    /// <summary>
    /// 플레이어의 형태 변환 시스템을 관리하는 매니저
    /// 폼 수집, 장착, 전환, 잠금해제 등을 담당
    /// </summary>
    public class FormManager : SingletonManager<FormManager>, IFormManager, IInitializableAsync
    {
        #region Fields

        [Header("Form Database")]
        [SerializeField] private FormDatabase _formDatabase;
        [SerializeField] private bool _autoLoadDatabase = true;

        [Header("Starting Forms")]
        [SerializeField] private FormData _defaultStartingForm;
        [SerializeField] private List<FormData> _unlockedForms = new List<FormData>();

        [Header("Settings")]
        [SerializeField] private bool _allowInstantFormSwitch = true;
        [SerializeField] private float _formSwitchCooldown = 1.0f;
        [SerializeField] private bool _saveFormProgress = true;
        [SerializeField] private bool _logFormChanges = true;

        [Header("Debug")]
        [SerializeField] private bool _showDebugInfo = false;

        // 현재 장착된 폼들
        private FormData _primaryForm;
        private FormData _secondaryForm;
        private FormData _currentActiveForm;

        // 폼 변경 리스너들
        private readonly List<System.Action<FormData>> _formChangeCallbacks = new();
        private readonly List<System.Action<IForm>> _interfaceCallbacks = new();

        // 발견된 폼들과 수집 상태
        private readonly HashSet<string> _discoveredFormIds = new();
        private readonly HashSet<string> _unlockedFormIds = new();
        private readonly Dictionary<string, FormData> _formCache = new();

        // 폼 전환 상태
        private bool _isFormSwitching = false;
        private float _lastSwitchTime = 0f;
        private bool _isInitialized = false;

        #endregion

        #region Properties

        public bool IsInitialized => _isInitialized;
        public string Name => nameof(FormManager);
        public InitializationPriority Priority => InitializationPriority.Gameplay;
        public FormDatabase Database => _formDatabase;

        // 현재 상태
        public FormData PrimaryForm => _primaryForm;
        public FormData SecondaryForm => _secondaryForm;
        public FormData CurrentActiveForm => _currentActiveForm;
        public bool IsFormSwitching => _isFormSwitching;

        // 수집 현황
        public int DiscoveredFormsCount => _discoveredFormIds.Count;
        public int UnlockedFormsCount => _unlockedFormIds.Count;
        public int TotalFormsCount => _formDatabase?.GetAllForms()?.Count ?? 0;

        #endregion

        #region Events

        public event Action<FormData, FormData> OnFormSwitched; // oldForm, newForm
        public event Action<FormData> OnFormUnlocked;
        public event Action<FormData> OnFormDiscovered;
        public event Action<FormData> OnFormEquipped;
        public event Action<string> OnFormCollectionUpdated; // collection type: "discovered", "unlocked"

        #endregion

        #region IInitializableAsync Implementation

        public async UniTask InitializeAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                JCDebug.Log("[FormManager] 초기화 시작");

                // 폼 데이터베이스 로드
                if (_autoLoadDatabase)
                {
                    await LoadFormDatabaseAsync(cancellationToken);
                }

                // 폼 캐시 구축
                await BuildFormCacheAsync(cancellationToken);

                // 기본 폼 설정
                await SetupDefaultFormsAsync(cancellationToken);

                // 저장된 폼 진행상황 로드
                if (_saveFormProgress)
                {
                    await LoadFormProgressAsync(cancellationToken);
                }

                _isInitialized = true;
                JCDebug.Log("[FormManager] 초기화 완료", JCDebug.LogLevel.Success);
            }
            catch (Exception ex)
            {
                JCDebug.Log($"[FormManager] 초기화 실패: {ex.Message}", JCDebug.LogLevel.Error);
                throw;
            }
        }

        public async UniTask CleanupAsync()
        {
            JCDebug.Log("[FormManager] 정리 시작");

            // 진행상황 저장
            if (_saveFormProgress)
            {
                await SaveFormProgressAsync();
            }

            // 리스너 정리
            _formChangeCallbacks.Clear();
            _interfaceCallbacks.Clear();

            // 캐시 정리
            _formCache.Clear();
            _discoveredFormIds.Clear();
            _unlockedFormIds.Clear();

            _isInitialized = false;
            JCDebug.Log("[FormManager] 정리 완료");
        }

        #endregion

        #region Initialization Methods

        private async UniTask LoadFormDatabaseAsync(CancellationToken cancellationToken)
        {
            if (_formDatabase == null)
            {
                _formDatabase = Resources.Load<FormDatabase>("Data/FormDatabase");

                if (_formDatabase == null)
                {
                    JCDebug.Log("[FormManager] FormDatabase를 찾을 수 없습니다.", JCDebug.LogLevel.Warning);
                    await CreateDefaultDatabaseAsync(cancellationToken);
                }
            }

            await UniTask.Yield(cancellationToken);
            JCDebug.Log("[FormManager] 폼 데이터베이스 로드 완료");
        }

        private async UniTask CreateDefaultDatabaseAsync(CancellationToken cancellationToken)
        {
            // 기본 데이터베이스 생성 로직
            await UniTask.Yield(cancellationToken);
            JCDebug.Log("[FormManager] 기본 폼 데이터베이스 생성");
        }

        private async UniTask BuildFormCacheAsync(CancellationToken cancellationToken)
        {
            if (_formDatabase == null) return;

            var allForms = _formDatabase.GetAllForms();
            foreach (var form in allForms)
            {
                if (form != null && !string.IsNullOrEmpty(form.formId))
                {
                    _formCache[form.formId] = form;
                }
            }

            await UniTask.Yield(cancellationToken);
            JCDebug.Log($"[FormManager] 폼 캐시 구축 완료: {_formCache.Count}개");
        }

        private async UniTask SetupDefaultFormsAsync(CancellationToken cancellationToken)
        {
            // 기본 시작 폼 설정
            if (_defaultStartingForm == null && _formDatabase != null)
            {
                _defaultStartingForm = _formDatabase.GetDefaultForm();
            }

            if (_defaultStartingForm != null)
            {
                _primaryForm = _defaultStartingForm;
                _currentActiveForm = _primaryForm;

                // 기본 폼은 자동으로 언락
                UnlockFormInternal(_defaultStartingForm, false);
            }

            // 시작 폼들 언락
            foreach (var form in _unlockedForms)
            {
                if (form != null)
                {
                    UnlockFormInternal(form, false);
                }
            }

            await UniTask.Yield(cancellationToken);
            JCDebug.Log("[FormManager] 기본 폼 설정 완료");
        }

        private async UniTask LoadFormProgressAsync(CancellationToken cancellationToken)
        {
            // PlayerDataManager에서 폼 진행상황 로드
            var playerDataManager = PlayerDataManager.Instance;
            if (playerDataManager?.IsInitialized == true)
            {
                // TODO: 저장된 폼 진행상황 로드
                await UniTask.Yield(cancellationToken);
                JCDebug.Log("[FormManager] 폼 진행상황 로드 완료");
            }
        }

        private async UniTask SaveFormProgressAsync()
        {
            // PlayerDataManager에 폼 진행상황 저장
            var playerDataManager = PlayerDataManager.Instance;
            if (playerDataManager?.IsInitialized == true)
            {
                // TODO: 폼 진행상황 저장
                await UniTask.Yield();
                JCDebug.Log("[FormManager] 폼 진행상황 저장 완료");
            }
        }

        #endregion

        #region Player Registration (PlayerController 연동)

        /// <summary>
        /// 플레이어 컨트롤러를 등록하고 폼 변경 콜백을 설정합니다
        /// </summary>
        public void RegisterPlayer(System.Action<FormData> onFormChanged)
        {
            if (onFormChanged == null) return;

            if (!_formChangeCallbacks.Contains(onFormChanged))
            {
                _formChangeCallbacks.Add(onFormChanged);

                // 현재 활성 폼을 즉시 전달
                if (_currentActiveForm != null)
                {
                    onFormChanged.Invoke(_currentActiveForm);
                }

                if (_logFormChanges)
                {
                    JCDebug.Log("[FormManager] 플레이어 등록 완료");
                }
            }
        }

        /// <summary>
        /// 플레이어 컨트롤러 등록을 해제합니다
        /// </summary>
        public void UnregisterPlayer(System.Action<FormData> onFormChanged)
        {
            if (onFormChanged != null && _formChangeCallbacks.Remove(onFormChanged))
            {
                if (_logFormChanges)
                {
                    JCDebug.Log("[FormManager] 플레이어 등록 해제");
                }
            }
        }

        #endregion

        #region IFormManager Implementation

        public void RegisterFormChangeListener(Action<IForm> callback)
        {
            if (callback != null && !_interfaceCallbacks.Contains(callback))
            {
                _interfaceCallbacks.Add(callback);

                // 현재 활성 폼을 즉시 전달
                if (_currentActiveForm != null)
                {
                    callback.Invoke(_currentActiveForm);
                }
            }
        }

        public void UnregisterFormChangeListener(Action<IForm> callback)
        {
            if (callback != null)
            {
                _interfaceCallbacks.Remove(callback);
            }
        }

        public void SwitchToSecondaryForm()
        {
            if (_secondaryForm != null)
            {
                SwitchToForm(_secondaryForm);
            }
            else
            {
                if (_logFormChanges)
                {
                    JCDebug.Log("[FormManager] 보조 폼이 설정되지 않음", JCDebug.LogLevel.Warning);
                }
            }
        }

        public IForm GetCurrentForm()
        {
            return _currentActiveForm;
        }

        public void EquipForm(IForm form, bool asPrimary)
        {
            if (form is FormData formData)
            {
                EquipForm(formData, asPrimary);
            }
        }

        public void UnlockForm(IForm form)
        {
            if (form is FormData formData)
            {
                UnlockForm(formData);
            }
        }

        #endregion

        #region Form Management

        /// <summary>
        /// 폼을 장착합니다
        /// </summary>
        public void EquipForm(FormData form, bool asPrimary = true)
        {
            if (form == null)
            {
                JCDebug.Log("[FormManager] 장착할 폼이 null입니다.", JCDebug.LogLevel.Warning);
                return;
            }

            if (!IsFormUnlocked(form.formId))
            {
                JCDebug.Log($"[FormManager] 잠겨있는 폼입니다: {form.formName}", JCDebug.LogLevel.Warning);
                return;
            }

            if (asPrimary)
            {
                _primaryForm = form;

                // 주 폼 변경 시 현재 활성 폼도 변경
                if (_currentActiveForm == null || _currentActiveForm == _primaryForm)
                {
                    SwitchToForm(form);
                }
            }
            else
            {
                _secondaryForm = form;
            }

            OnFormEquipped?.Invoke(form);

            if (_logFormChanges)
            {
                JCDebug.Log($"[FormManager] 폼 장착: {form.formName} ({(asPrimary ? "주" : "보조")})");
            }
        }

        /// <summary>
        /// 지정된 폼으로 전환합니다
        /// </summary>
        public void SwitchToForm(FormData newForm)
        {
            if (newForm == null || newForm == _currentActiveForm)
                return;

            if (!IsFormUnlocked(newForm.formId))
            {
                JCDebug.Log($"[FormManager] 잠겨있는 폼으로 전환 시도: {newForm.formName}", JCDebug.LogLevel.Warning);
                return;
            }

            if (!CanSwitchForm())
                return;

            PerformFormSwitch(newForm);
        }

        /// <summary>
        /// 폼 ID로 전환합니다
        /// </summary>
        public void SwitchToFormById(string formId)
        {
            if (_formCache.TryGetValue(formId, out FormData form))
            {
                SwitchToForm(form);
            }
            else
            {
                JCDebug.Log($"[FormManager] 존재하지 않는 폼 ID: {formId}", JCDebug.LogLevel.Warning);
            }
        }

        /// <summary>
        /// 다음 장착된 폼으로 순환 전환
        /// </summary>
        public void CycleToNextForm()
        {
            if (_primaryForm != null && _secondaryForm != null)
            {
                var targetForm = (_currentActiveForm == _primaryForm) ? _secondaryForm : _primaryForm;
                SwitchToForm(targetForm);
            }
        }

        private bool CanSwitchForm()
        {
            if (_isFormSwitching)
            {
                JCDebug.Log("[FormManager] 이미 폼 전환 중", JCDebug.LogLevel.Info);
                return false;
            }

            if (!_allowInstantFormSwitch && Time.time - _lastSwitchTime < _formSwitchCooldown)
            {
                JCDebug.Log($"[FormManager] 폼 전환 쿨다운 중 ({_formSwitchCooldown - (Time.time - _lastSwitchTime):F1}초 남음)", JCDebug.LogLevel.Info);
                return false;
            }

            return true;
        }

        private void PerformFormSwitch(FormData newForm)
        {
            var oldForm = _currentActiveForm;
            _isFormSwitching = true;
            _lastSwitchTime = Time.time;

            // 폼 변경
            _currentActiveForm = newForm;

            // 모든 콜백 호출
            NotifyFormChange(newForm);

            // 이벤트 발생
            OnFormSwitched?.Invoke(oldForm, newForm);

            _isFormSwitching = false;

            if (_logFormChanges)
            {
                JCDebug.Log($"[FormManager] 폼 전환 완료: {oldForm?.formName ?? "없음"} → {newForm.formName}");
            }
        }

        private void NotifyFormChange(FormData newForm)
        {
            // FormData 콜백들 (PlayerController용)
            foreach (var callback in _formChangeCallbacks)
            {
                try
                {
                    callback.Invoke(newForm);
                }
                catch (Exception ex)
                {
                    JCDebug.Log($"[FormManager] 폼 변경 콜백 오류: {ex.Message}", JCDebug.LogLevel.Error);
                }
            }

            // IForm 콜백들 (다른 시스템용)
            foreach (var callback in _interfaceCallbacks)
            {
                try
                {
                    callback.Invoke(newForm);
                }
                catch (Exception ex)
                {
                    JCDebug.Log($"[FormManager] IForm 콜백 오류: {ex.Message}", JCDebug.LogLevel.Error);
                }
            }
        }

        #endregion

        #region Form Discovery & Unlocking

        /// <summary>
        /// 폼을 발견 상태로 표시합니다
        /// </summary>
        public void DiscoverForm(FormData form)
        {
            if (form == null || string.IsNullOrEmpty(form.formId))
                return;

            if (_discoveredFormIds.Add(form.formId))
            {
                OnFormDiscovered?.Invoke(form);
                OnFormCollectionUpdated?.Invoke("discovered");

                if (_logFormChanges)
                {
                    JCDebug.Log($"[FormManager] 폼 발견: {form.formName}");
                }
            }
        }

        /// <summary>
        /// 폼을 잠금 해제합니다
        /// </summary>
        public void UnlockForm(FormData form)
        {
            UnlockFormInternal(form, true);
        }

        private void UnlockFormInternal(FormData form, bool notify)
        {
            if (form == null || string.IsNullOrEmpty(form.formId))
                return;

            // 먼저 발견 상태로 표시
            _discoveredFormIds.Add(form.formId);

            if (_unlockedFormIds.Add(form.formId))
            {
                if (notify)
                {
                    OnFormUnlocked?.Invoke(form);
                    OnFormCollectionUpdated?.Invoke("unlocked");
                }

                if (_logFormChanges)
                {
                    JCDebug.Log($"[FormManager] 폼 잠금해제: {form.formName}");
                }
            }
        }

        /// <summary>
        /// 폼 ID로 잠금 해제
        /// </summary>
        public void UnlockFormById(string formId)
        {
            if (_formCache.TryGetValue(formId, out FormData form))
            {
                UnlockForm(form);
            }
            else
            {
                JCDebug.Log($"[FormManager] 존재하지 않는 폼 ID로 잠금해제 시도: {formId}", JCDebug.LogLevel.Warning);
            }
        }

        /// <summary>
        /// 폼이 발견되었는지 확인
        /// </summary>
        public bool IsFormDiscovered(string formId)
        {
            return _discoveredFormIds.Contains(formId);
        }

        /// <summary>
        /// 폼이 잠금해제되었는지 확인
        /// </summary>
        public bool IsFormUnlocked(string formId)
        {
            return _unlockedFormIds.Contains(formId);
        }

        #endregion

        #region Query Methods

        /// <summary>
        /// 잠금해제된 모든 폼을 가져옵니다
        /// </summary>
        public List<FormData> GetUnlockedForms()
        {
            return _unlockedFormIds
                .Where(id => _formCache.ContainsKey(id))
                .Select(id => _formCache[id])
                .ToList();
        }

        /// <summary>
        /// 발견된 모든 폼을 가져옵니다
        /// </summary>
        public List<FormData> GetDiscoveredForms()
        {
            return _discoveredFormIds
                .Where(id => _formCache.ContainsKey(id))
                .Select(id => _formCache[id])
                .ToList();
        }

        /// <summary>
        /// 수집 진행률을 가져옵니다 (0-1)
        /// </summary>
        public float GetCollectionProgress()
        {
            int total = TotalFormsCount;
            return total > 0 ? (float)UnlockedFormsCount / total : 0f;
        }

        /// <summary>
        /// 타입별 잠금해제된 폼들을 가져옵니다
        /// </summary>
        public List<FormData> GetUnlockedFormsByType(FormData.FormType type)
        {
            return GetUnlockedForms()
                .Where(form => form.type == type)
                .ToList();
        }

        #endregion

        #region Utility Methods

        /// <summary>
        /// 폼 매니저의 상태 정보를 출력합니다
        /// </summary>
        public void PrintDebugInfo()
        {
            JCDebug.Log($"[FormManager] 상태 정보:\n" +
                       $"  초기화 상태: {_isInitialized}\n" +
                       $"  현재 활성 폼: {_currentActiveForm?.formName ?? "없음"}\n" +
                       $"  주 폼: {_primaryForm?.formName ?? "없음"}\n" +
                       $"  보조 폼: {_secondaryForm?.formName ?? "없음"}\n" +
                       $"  발견된 폼: {DiscoveredFormsCount}/{TotalFormsCount}\n" +
                       $"  잠금해제된 폼: {UnlockedFormsCount}/{TotalFormsCount}\n" +
                       $"  수집 진행률: {GetCollectionProgress():P1}\n" +
                       $"  등록된 콜백: {_formChangeCallbacks.Count + _interfaceCallbacks.Count}개");
        }

        #endregion

        #region Context Menu (에디터 전용)

        [ContextMenu("다음 폼으로 전환")]
        private void ContextMenuSwitchToNext()
        {
            if (Application.isPlaying)
            {
                CycleToNextForm();
            }
        }

        [ContextMenu("보조 폼으로 전환")]
        private void ContextMenuSwitchToSecondary()
        {
            if (Application.isPlaying)
            {
                SwitchToSecondaryForm();
            }
        }

        [ContextMenu("상태 정보 출력")]
        private void ContextMenuPrintDebugInfo()
        {
            PrintDebugInfo();
        }

        #endregion
    }
}