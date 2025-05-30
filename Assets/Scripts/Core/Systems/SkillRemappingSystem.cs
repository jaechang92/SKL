using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;
using System;
using Metamorph.Forms.Base;

/// <summary>
/// 스킬 키 리매핑 및 슬롯 관리 시스템
/// FormManager와 SkillManager와 연동하여 동적 스킬 관리 제공
/// </summary>
public class SkillRemappingSystem : SingletonManager<SkillRemappingSystem>
{
    [Header("Input System")]
    [SerializeField] private PlayerInputActions inputActions;

    [Header("스킬 슬롯 설정")]
    [SerializeField] private SkillSlotConfig[] skillSlots = new SkillSlotConfig[4];

    [Header("Form 연동 설정")]
    [SerializeField] private bool autoUpdateOnFormChange = true;
    [SerializeField] private bool preserveCustomMappings = true;

    [Header("저장/로드 설정")]
    [SerializeField] private string saveKey = "SkillRemappingData";
    [SerializeField] private bool autoSave = true;
    [SerializeField] private bool loadOnStart = true;

    // Input Actions (키 리매핑용)
    private InputAction[] slotActions = new InputAction[4];
    private static readonly string[] ACTION_NAMES = { "BasicAttack", "Skill1", "Skill2", "Skill3" };

    // 키 리매핑 상태
    private bool isRemapping = false;
    private int remappingSlotIndex = -1;
    private InputActionRebindingExtensions.RebindingOperation currentRebind;

    // 현재 폼과 스킬 정보 캐시
    private FormData currentForm;
    private Dictionary<string, SkillSlotConfig[]> formSkillPresets = new Dictionary<string, SkillSlotConfig[]>();

    // 이벤트 시스템
    public static event Action<int, SkillData> OnSkillUsed;           // 슬롯, 스킬데이터
    public static event Action<int, SkillData, SkillData> OnSkillChanged; // 슬롯, 이전스킬, 새스킬
    public static event Action<int, string> OnKeyRemapped;           // 슬롯, 새키이름
    public static event Action<string> OnRemappingStarted;           // 안내메시지
    public static event Action OnRemappingCanceled;                  // 리매핑 취소
    public static event Action OnDataSaved;                         // 데이터 저장됨
    public static event Action OnDataLoaded;                        // 데이터 로드됨

    [Serializable]
    public class SkillSlotConfig
    {
        [Header("슬롯 정보")]
        public string slotName = "";                    // "기본 공격", "스킬 1" 등
        public SkillSlotType slotType;                  // 슬롯 타입
        public SkillData assignedSkill = null;          // 할당된 스킬 데이터
        public bool isLocked = false;                   // 잠긴 슬롯인지

        [Header("기본 키 설정")]
        public string defaultKey = "";                  // 기본 키
        public string currentKey = "";                  // 현재 키 (리매핑된 키)

        [Header("UI 정보")]
        public string displayName = "";                 // UI에 표시될 이름
        public Color slotColor = Color.white;           // 슬롯 색상

        public bool IsEmpty => assignedSkill == null;
        public bool CanUse => !IsEmpty && !isLocked;
        public string GetCurrentKey() => !string.IsNullOrEmpty(currentKey) ? currentKey : defaultKey;
    }

    public enum SkillSlotType
    {
        BasicAttack,    // 기본 공격
        Skill1,         // 스킬 1
        Skill2,         // 스킬 2
        Ultimate        // 궁극기
    }

    // 저장용 데이터 구조
    [Serializable]
    public class RemappingData
    {
        public SlotData[] slots = new SlotData[4];
        public FormPresetData[] formPresets;
        public string saveVersion = "1.0";
        public long saveTimestamp;

        [Serializable]
        public class SlotData
        {
            public string skillName;  // 스킬 이름으로 저장 (SkillData에 skillName이 있다고 가정)
            public string remappedKey;
            public bool isLocked;
            public SkillSlotType slotType;
        }

        [Serializable]
        public class FormPresetData
        {
            public string formId;
            public SlotData[] formSlots = new SlotData[4];
        }
    }

    protected override void OnCreated()
    {
        base.OnCreated();

        InitializeInputActions();
        InitializeSlots();
        SetupInputActions();
        RegisterFormManagerEvents();
    }

    void Start()
    {
        if (loadOnStart)
        {
            LoadSettings();
        }

        // 현재 폼 정보 가져오기
        UpdateCurrentForm();
        LogSystemInfo();
    }

    void OnEnable()
    {
        EnableInputActions();
    }

    void OnDisable()
    {
        DisableInputActions();
        CancelRemapping();
    }

    void Update()
    {
        // ESC 키로 리매핑 취소
        if (isRemapping && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            CancelRemapping();
        }

        // 디버그 키들
        HandleDebugInput();
    }

    #region 초기화 및 Form Manager 연동

    /// <summary>
    /// FormManager 이벤트 등록
    /// </summary>
    private void RegisterFormManagerEvents()
    {
        if (FormManager.Instance != null)
        {
            FormManager.OnFormChanged += OnFormChanged;
        }
        else
        {
            Debug.LogWarning("FormManager를 찾을 수 없습니다. Form 변경 이벤트를 구독할 수 없습니다.");
        }
    }

    /// <summary>
    /// Form 변경 시 호출되는 이벤트 핸들러
    /// </summary>
    private void OnFormChanged(FormData newForm)
    {
        if (!autoUpdateOnFormChange) return;

        Debug.Log($"🔄 Form 변경 감지: {newForm.formName}");

        // 현재 커스텀 매핑 저장 (필요시)
        if (preserveCustomMappings && currentForm != null)
        {
            SaveFormPreset(currentForm.formId);
        }

        // 새 폼 적용
        currentForm = newForm;
        UpdateSkillSlotsFromForm(newForm);

        // 저장된 프리셋이 있다면 로드
        if (preserveCustomMappings)
        {
            LoadFormPreset(newForm.formId);
        }
    }

    /// <summary>
    /// 현재 폼 정보 업데이트
    /// </summary>
    private void UpdateCurrentForm()
    {
        if (FormManager.Instance != null)
        {
            // FormManager에서 현재 폼을 가져오는 프로퍼티나 메서드가 있어야 함
            // 예시: currentForm = FormManager.Instance.GetCurrentForm();
            // 실제 FormManager 구조에 맞게 수정 필요

            // 임시로 주석 처리 - 실제 FormManager API에 맞게 수정 필요
            currentForm = FormManager.Instance.GetCurrentForm();

            if (currentForm != null)
            {
                UpdateSkillSlotsFromForm(currentForm);
            }
        }
    }

    /// <summary>
    /// Form 데이터를 기반으로 스킬 슬롯 업데이트
    /// </summary>
    private void UpdateSkillSlotsFromForm(FormData form)
    {
        if (form == null) return;

        // Form의 스킬들을 슬롯에 할당
        if (skillSlots.Length >= 4)
        {
            skillSlots[0].assignedSkill = form.basicAttack;
            skillSlots[0].slotType = SkillSlotType.BasicAttack;

            skillSlots[1].assignedSkill = form.skillOne;
            skillSlots[1].slotType = SkillSlotType.Skill1;

            skillSlots[2].assignedSkill = form.skillTwo;
            skillSlots[2].slotType = SkillSlotType.Skill2;

            skillSlots[3].assignedSkill = form.ultimateSkill;
            skillSlots[3].slotType = SkillSlotType.Ultimate;
        }

        Debug.Log($"✅ {form.formName}의 스킬들이 슬롯에 할당되었습니다.");
    }

    /// <summary>
    /// Form 프리셋 저장
    /// </summary>
    private void SaveFormPreset(string formId)
    {
        if (string.IsNullOrEmpty(formId)) return;

        SkillSlotConfig[] preset = new SkillSlotConfig[skillSlots.Length];
        for (int i = 0; i < skillSlots.Length; i++)
        {
            preset[i] = new SkillSlotConfig
            {
                slotName = skillSlots[i].slotName,
                slotType = skillSlots[i].slotType,
                assignedSkill = skillSlots[i].assignedSkill,
                isLocked = skillSlots[i].isLocked,
                defaultKey = skillSlots[i].defaultKey,
                currentKey = skillSlots[i].currentKey,
                displayName = skillSlots[i].displayName,
                slotColor = skillSlots[i].slotColor
            };
        }

        formSkillPresets[formId] = preset;
    }

    /// <summary>
    /// Form 프리셋 로드
    /// </summary>
    private void LoadFormPreset(string formId)
    {
        if (string.IsNullOrEmpty(formId) || !formSkillPresets.ContainsKey(formId)) return;

        SkillSlotConfig[] preset = formSkillPresets[formId];
        for (int i = 0; i < Mathf.Min(preset.Length, skillSlots.Length); i++)
        {
            // 키 매핑만 복원 (스킬은 Form에서 가져옴)
            skillSlots[i].currentKey = preset[i].currentKey;
            skillSlots[i].isLocked = preset[i].isLocked;
        }

        Debug.Log($"📂 {formId} Form의 키 설정을 로드했습니다.");
    }

    #endregion

    #region 초기화 메서드들

    private void InitializeInputActions()
    {
        if (inputActions == null)
        {
            inputActions = new PlayerInputActions();
            Debug.LogWarning("🔧 PlayerInputActions가 설정되지 않았습니다. 기본 Input Actions를 생성합니다.");
        }

        inputActions.Enable();
    }

    private void InitializeSlots()
    {
        if (skillSlots.Length < 4)
        {
            Array.Resize(ref skillSlots, 4);
        }

        for (int i = 0; i < skillSlots.Length; i++)
        {
            if (skillSlots[i] == null)
            {
                skillSlots[i] = new SkillSlotConfig();
            }

            var slot = skillSlots[i];

            // 슬롯 타입 설정
            slot.slotType = (SkillSlotType)i;

            // 기본값 설정
            if (string.IsNullOrEmpty(slot.slotName))
                slot.slotName = GetDefaultSlotName(i);

            if (string.IsNullOrEmpty(slot.defaultKey))
                slot.defaultKey = GetDefaultKeyPath(i);

            if (string.IsNullOrEmpty(slot.displayName))
                slot.displayName = GetDefaultDisplayName(i);

            // 현재 키가 설정되지 않았으면 기본 키 사용
            if (string.IsNullOrEmpty(slot.currentKey))
                slot.currentKey = slot.defaultKey;
        }

        Debug.Log("🔧 스킬 슬롯 초기화 완료");
    }

    private void SetupInputActions()
    {
        for (int i = 0; i < ACTION_NAMES.Length; i++)
        {
            slotActions[i] = inputActions.FindAction(ACTION_NAMES[i]);

            if (slotActions[i] == null)
            {
                Debug.LogError($"❌ Action '{ACTION_NAMES[i]}'를 찾을 수 없습니다! Input Action Asset을 확인해주세요.");
            }
        }
    }

    private void EnableInputActions()
    {
        for (int i = 0; i < slotActions.Length; i++)
        {
            if (slotActions[i] != null)
            {
                slotActions[i].Enable();

                // 스킬 사용 이벤트 구독
                int slotIndex = i; // 클로저 캡처용
                slotActions[i].performed += _ => OnSlotActionPerformed(slotIndex);
            }
        }
    }

    private void DisableInputActions()
    {
        for (int i = 0; i < slotActions.Length; i++)
        {
            if (slotActions[i] != null)
            {
                slotActions[i].Disable();
                slotActions[i].performed -= _ => OnSlotActionPerformed(i);
            }
        }
    }

    #endregion

    #region 스킬 사용 (SkillManager 연동)

    private void OnSlotActionPerformed(int slotIndex)
    {
        if (isRemapping) return;
        UseSkillInSlot(slotIndex);
    }

    public void UseSkillInSlot(int slotIndex)
    {
        if (!IsValidSlotIndex(slotIndex)) return;

        var slot = skillSlots[slotIndex];

        if (!slot.CanUse)
        {
            if (slot.IsEmpty)
                Debug.Log($"⚪ {slot.displayName} 슬롯이 비어있습니다.");
            else if (slot.isLocked)
                Debug.Log($"🔒 {slot.displayName} 슬롯이 잠겨있습니다.");
            return;
        }

        // SkillManager를 통해 스킬 사용
        if (SkillManager.Instance != null)
        {
            switch (slot.slotType)
            {
                case SkillSlotType.BasicAttack:
                    SkillManager.Instance.UseBasicAttack();
                    break;
                case SkillSlotType.Skill1:
                    SkillManager.Instance.UseSkill(1);
                    break;
                case SkillSlotType.Skill2:
                    SkillManager.Instance.UseSkill(2);
                    break;
                case SkillSlotType.Ultimate:
                    SkillManager.Instance.UseUltimateSkill();
                    break;
            }

            string keyDisplay = GetKeyDisplayName(slot.GetCurrentKey());
            Debug.Log($"🎯 {slot.displayName} ({keyDisplay}): '{slot.assignedSkill?.skillName}' 사용!");

            // 이벤트 발생
            OnSkillUsed?.Invoke(slotIndex, slot.assignedSkill);
        }
        else
        {
            Debug.LogError("SkillManager를 찾을 수 없습니다!");
        }
    }

    #endregion

    #region 스킬 할당

    public bool AssignSkillToSlot(int slotIndex, SkillData skill)
    {
        if (!IsValidSlotIndex(slotIndex)) return false;

        var slot = skillSlots[slotIndex];

        if (slot.isLocked)
        {
            Debug.LogWarning($"⚠️ {slot.displayName} 슬롯이 잠겨있어 스킬을 할당할 수 없습니다.");
            return false;
        }

        SkillData previousSkill = slot.assignedSkill;
        slot.assignedSkill = skill;

        string skillName = skill?.skillName ?? "없음";
        string prevSkillName = previousSkill?.skillName ?? "없음";
        Debug.Log($"🔄 {slot.displayName}: '{prevSkillName}' → '{skillName}'");

        // 이벤트 발생
        OnSkillChanged?.Invoke(slotIndex, previousSkill, skill);

        // 자동 저장
        if (autoSave)
        {
            SaveSettings();
        }

        return true;
    }

    public bool ClearSlot(int slotIndex)
    {
        return AssignSkillToSlot(slotIndex, null);
    }

    #endregion

    #region 유틸리티 메서드들

    private bool IsValidSlotIndex(int slotIndex)
    {
        bool isValid = slotIndex >= 0 && slotIndex < skillSlots.Length;
        if (!isValid)
        {
            Debug.LogWarning($"⚠️ 잘못된 슬롯 인덱스: {slotIndex}");
        }
        return isValid;
    }

    private string GetDefaultSlotName(int slotIndex)
    {
        string[] slotNames = { "기본 공격", "스킬 1", "스킬 2", "궁극기" };
        return slotIndex < slotNames.Length ? slotNames[slotIndex] : $"슬롯 {slotIndex + 1}";
    }

    private string GetDefaultKeyPath(int slotIndex)
    {
        string[] defaultKeys = { "<Keyboard>/a", "<Keyboard>/s", "<Keyboard>/d", "<Keyboard>/f" };
        return slotIndex < defaultKeys.Length ? defaultKeys[slotIndex] : "<Keyboard>/space";
    }

    private string GetDefaultDisplayName(int slotIndex)
    {
        string[] displayNames = { "A키", "S키", "D키", "F키" };
        return slotIndex < displayNames.Length ? displayNames[slotIndex] : $"슬롯 {slotIndex + 1}";
    }

    private string GetKeyDisplayName(string keyPath)
    {
        if (string.IsNullOrEmpty(keyPath)) return "없음";

        return keyPath switch
        {
            "<Mouse>/leftButton" => "좌클릭",
            "<Mouse>/rightButton" => "우클릭",
            "<Keyboard>/a" => "A",
            "<Keyboard>/s" => "S",
            "<Keyboard>/d" => "D",
            "<Keyboard>/f" => "F",
            "<Keyboard>/space" => "Space",
            "<Keyboard>/leftShift" => "Shift",
            "<Keyboard>/leftCtrl" => "Ctrl",
            _ => InputControlPath.ToHumanReadableString(keyPath, InputControlPath.HumanReadableStringOptions.OmitDevice)
        };
    }

    private void LogSystemInfo()
    {
        Debug.Log("🚀 스킬 & 키 리매핑 시스템 초기화 완료");
        Debug.Log($"   - 슬롯 수: {skillSlots.Length}");
        Debug.Log($"   - 자동 저장: {autoSave}");
        Debug.Log($"   - Form 연동: {autoUpdateOnFormChange}");
    }

    #endregion

    #region 디버그 입력

    private void HandleDebugInput()
    {
        // F1-F4: 각 슬롯 키 리매핑 시작 (키 리매핑 기능이 구현되어 있다면)
        if (Keyboard.current.f1Key.wasPressedThisFrame) Debug.Log("F1: 기본 공격 키 리매핑");
        if (Keyboard.current.f2Key.wasPressedThisFrame) Debug.Log("F2: 스킬1 키 리매핑");
        if (Keyboard.current.f3Key.wasPressedThisFrame) Debug.Log("F3: 스킬2 키 리매핑");
        if (Keyboard.current.f4Key.wasPressedThisFrame) Debug.Log("F4: 궁극기 키 리매핑");

        // 기타 디버그 키들
        if (Keyboard.current.f5Key.wasPressedThisFrame) PrintCurrentConfiguration();
        if (Keyboard.current.f9Key.wasPressedThisFrame) SaveSettings();
        if (Keyboard.current.f10Key.wasPressedThisFrame) LoadSettings();
    }

    public void PrintCurrentConfiguration()
    {
        Debug.Log("=== 현재 스킬 & 키 설정 ===");

        for (int i = 0; i < skillSlots.Length; i++)
        {
            var slot = skillSlots[i];
            string keyDisplay = GetKeyDisplayName(slot.GetCurrentKey());
            string skillName = slot.assignedSkill?.skillName ?? "없음";
            string status = slot.isLocked ? " (잠김)" : "";

            Debug.Log($"{slot.displayName}: {keyDisplay} → {skillName}{status}");
        }
    }

    #endregion

    #region 저장/로드

    public void SaveSettings()
    {
        RemappingData data = new RemappingData();
        data.saveTimestamp = DateTimeOffset.Now.ToUnixTimeSeconds();

        // 슬롯 데이터 저장
        for (int i = 0; i < skillSlots.Length; i++)
        {
            var slot = skillSlots[i];
            data.slots[i] = new RemappingData.SlotData
            {
                skillName = slot.assignedSkill?.skillName ?? "",
                remappedKey = slot.currentKey,
                isLocked = slot.isLocked,
                slotType = slot.slotType
            };
        }

        string json = JsonUtility.ToJson(data, true);
        PlayerPrefs.SetString(saveKey, json);
        PlayerPrefs.Save();

        Debug.Log("💾 스킬 설정이 저장되었습니다.");
        OnDataSaved?.Invoke();
    }

    public void LoadSettings()
    {
        if (!PlayerPrefs.HasKey(saveKey))
        {
            Debug.Log("📁 저장된 스킬 설정이 없습니다. 기본 설정을 사용합니다.");
            return;
        }

        try
        {
            string json = PlayerPrefs.GetString(saveKey);
            RemappingData data = JsonUtility.FromJson<RemappingData>(json);

            if (data?.slots == null) return;

            // 슬롯 데이터 적용 (키 설정만)
            for (int i = 0; i < Mathf.Min(data.slots.Length, skillSlots.Length); i++)
            {
                var slotData = data.slots[i];
                var slot = skillSlots[i];

                // 키 설정만 복원 (스킬은 FormManager에서 가져옴)
                slot.currentKey = slotData.remappedKey;
                slot.isLocked = slotData.isLocked;
                slot.slotType = slotData.slotType;

                // 키 바인딩 적용 (실제 키 리매핑 기능이 구현되어 있다면)
                if (!string.IsNullOrEmpty(slotData.remappedKey) && slotActions[i] != null)
                {
                    slotActions[i].ApplyBindingOverride(0, slotData.remappedKey);
                }
            }

            Debug.Log($"📂 스킬 설정이 로드되었습니다.");
            OnDataLoaded?.Invoke();
        }
        catch (Exception ex)
        {
            Debug.LogError($"❌ 스킬 설정 로드 중 오류 발생: {ex.Message}");
        }
    }

    #endregion

    

    protected override void OnDestroy()
    {
        // 정리
        CancelRemapping();

        if (autoSave)
        {
            SaveSettings();
        }

        base.OnDestroy();
    }

    // 키 리매핑 관련 메서드들은 필요시 구현
    public void StartKeyRemapping(int slotIndex) { /* 구현 필요 */ }
    public void CancelRemapping() { /* 구현 필요 */ }
}