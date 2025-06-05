using CustomDebug;
using Metamorph.Forms.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Metamorph.Forms.Data
{
    /// <summary>
    /// 게임 내 모든 폼(Forms)을 관리하는 데이터베이스
    /// </summary>
    [CreateAssetMenu(fileName = "FormDatabase", menuName = "Metamorph/Data/FormDatabase")]
    public class FormDatabase : ScriptableObject
    {
        [Header("Default Forms")]
        [Tooltip("게임 시작 시 기본적으로 제공되는 폼")]
        [SerializeField] private FormData _defaultForm;
        [SerializeField] private List<FormData> _starterForms = new List<FormData>();

        [Header("All Forms")]
        [Tooltip("게임 내 모든 폼의 컬렉션")]
        [SerializeField] private List<FormData> _allForms = new List<FormData>();

        [Header("Forms By Rarity")]
        [SerializeField] private List<FormData> _commonForms = new List<FormData>();
        [SerializeField] private List<FormData> _rareForms = new List<FormData>();
        [SerializeField] private List<FormData> _epicForms = new List<FormData>();
        [SerializeField] private List<FormData> _legendaryForms = new List<FormData>();

        [Header("Forms By Type")]
        [SerializeField] private List<FormData> _warriorForms = new List<FormData>();
        [SerializeField] private List<FormData> _mageForms = new List<FormData>();
        [SerializeField] private List<FormData> _assassinForms = new List<FormData>();
        [SerializeField] private List<FormData> _tankForms = new List<FormData>();
        [SerializeField] private List<FormData> _supportForms = new List<FormData>();

        [Header("Special Forms")]
        [SerializeField] private List<FormData> _bossForms = new List<FormData>();
        [SerializeField] private List<FormData> _secretForms = new List<FormData>();

        // 캐싱을 위한 딕셔너리
        private Dictionary<string, FormData> _formLookup;

        /// <summary>
        /// 시작 시 초기화(캐싱)
        /// </summary>
        private void OnEnable()
        {
            InitializeFormLookup();
        }

        /// <summary>
        /// ID로 빠른 액세스를 위한 딕셔너리 초기화
        /// </summary>
        private void InitializeFormLookup()
        {
            _formLookup = new Dictionary<string, FormData>();

            foreach (var form in _allForms)
            {
                if (form != null && !string.IsNullOrEmpty(form.formId))
                {
                    // 중복 ID 확인
                    if (_formLookup.ContainsKey(form.formId))
                    {
                        JCDebug.Log($"중복된 폼 ID가 발견되었습니다: {form.formId}. 이전 항목이 덮어쓰기됩니다.",JCDebug.LogLevel.Warning);
                    }

                    _formLookup[form.formId] = form;
                }
                else if (form != null)
                {
                    JCDebug.Log($"FormID가 없는 폼이 발견되었습니다: {form.name}",JCDebug.LogLevel.Error);
                }
            }
        }

        #region Form Access Methods

        /// <summary>
        /// 기본 폼 반환
        /// </summary>
        public FormData GetDefaultForm()
        {
            return _defaultForm;
        }

        /// <summary>
        /// 게임 시작 시 제공되는 시작 폼 목록 반환
        /// </summary>
        public List<FormData> GetStarterForms()
        {
            return new List<FormData>(_starterForms);
        }

        /// <summary>
        /// 모든 폼 목록 반환
        /// </summary>
        public List<FormData> GetAllForms()
        {
            return new List<FormData>(_allForms);
        }

        /// <summary>
        /// ID로 폼 검색
        /// </summary>
        public FormData GetFormById(string id)
        {
            // 캐시가 초기화되지 않았다면 초기화
            if (_formLookup == null || _formLookup.Count == 0)
            {
                InitializeFormLookup();
            }

            if (_formLookup.TryGetValue(id, out FormData form))
            {
                return form;
            }

            JCDebug.Log($"ID가 {id}인 폼을 찾을 수 없습니다.",JCDebug.LogLevel.Warning);
            return null;
        }

        /// <summary>
        /// 이름으로 폼 검색
        /// </summary>
        public FormData GetFormByName(string formName)
        {
            return _allForms.Find(form => form.formName == formName);
        }

        /// <summary>
        /// 타입별 폼 목록 반환
        /// </summary>
        public List<FormData> GetFormsByType(FormData.FormType type)
        {
            switch (type)
            {
                case FormData.FormType.Warrior:
                    return new List<FormData>(_warriorForms);
                case FormData.FormType.Mage:
                    return new List<FormData>(_mageForms);
                case FormData.FormType.Assassin:
                    return new List<FormData>(_assassinForms);
                case FormData.FormType.Tank:
                    return new List<FormData>(_tankForms);
                case FormData.FormType.Support:
                    return new List<FormData>(_supportForms);
                default:
                    return new List<FormData>();
            }
        }

        /// <summary>
        /// 희귀도별 폼 목록 반환
        /// </summary>
        public List<FormData> GetFormsByRarity(FormData.FormRarity rarity)
        {
            switch (rarity)
            {
                case FormData.FormRarity.Common:
                    return new List<FormData>(_commonForms);
                case FormData.FormRarity.Rare:
                    return new List<FormData>(_rareForms);
                case FormData.FormRarity.Epic:
                    return new List<FormData>(_epicForms);
                case FormData.FormRarity.Legendary:
                    return new List<FormData>(_legendaryForms);
                default:
                    return new List<FormData>();
            }
        }

        /// <summary>
        /// 보스 폼 목록 반환
        /// </summary>
        public List<FormData> GetBossForms()
        {
            return new List<FormData>(_bossForms);
        }

        /// <summary>
        /// 비밀 폼 목록 반환
        /// </summary>
        public List<FormData> GetSecretForms()
        {
            return new List<FormData>(_secretForms);
        }

        /// <summary>
        /// 조건에 맞는 폼 검색 (고급 검색)
        /// </summary>
        public List<FormData> FindForms(Func<FormData, bool> predicate)
        {
            return _allForms.Where(predicate).ToList();
        }

        /// <summary>
        /// 무작위 폼 반환 (희귀도 가중치 적용)
        /// </summary>
        public FormData GetRandomForm(FormData.FormRarity minRarity = FormData.FormRarity.Common)
        {
            // 최소 희귀도를 만족하는 폼만 필터링
            List<FormData> eligibleForms = _allForms.Where(form =>
                (int)form.rarity >= (int)minRarity).ToList();

            if (eligibleForms.Count == 0)
                return null;

            // 희귀도에 따른 가중치 설정
            Dictionary<FormData, float> weightedForms = new Dictionary<FormData, float>();
            float totalWeight = 0;

            foreach (var form in eligibleForms)
            {
                float weight = 0;
                switch (form.rarity)
                {
                    case FormData.FormRarity.Common:
                        weight = 100;
                        break;
                    case FormData.FormRarity.Rare:
                        weight = 40;
                        break;
                    case FormData.FormRarity.Epic:
                        weight = 15;
                        break;
                    case FormData.FormRarity.Legendary:
                        weight = 5;
                        break;
                }

                weightedForms.Add(form, weight);
                totalWeight += weight;
            }

            // 무작위 값 생성 및 폼 선택
            float randomValue = UnityEngine.Random.Range(0, totalWeight);
            float currentWeight = 0;

            foreach (var pair in weightedForms)
            {
                currentWeight += pair.Value;
                if (randomValue <= currentWeight)
                {
                    return pair.Key;
                }
            }

            // 기본값 (여기까지 오면 안됨)
            return eligibleForms[0];
        }

        #endregion

        #region Editor Utilities

#if UNITY_EDITOR
        /// <summary>
        /// 에디터에서 모든 폼 컬렉션 업데이트
        /// </summary>
        public void UpdateCollections()
        {
            // 기존 컬렉션 초기화
            _commonForms.Clear();
            _rareForms.Clear();
            _epicForms.Clear();
            _legendaryForms.Clear();

            _warriorForms.Clear();
            _mageForms.Clear();
            _assassinForms.Clear();
            _tankForms.Clear();
            _supportForms.Clear();

            // 보스 및 비밀 폼은 수동으로 관리하므로 초기화하지 않음

            // 모든 폼 반복하며 분류
            foreach (var form in _allForms)
            {
                if (form == null)
                    continue;

                // 희귀도별 분류
                switch (form.rarity)
                {
                    case FormData.FormRarity.Common:
                        _commonForms.Add(form);
                        break;
                    case FormData.FormRarity.Rare:
                        _rareForms.Add(form);
                        break;
                    case FormData.FormRarity.Epic:
                        _epicForms.Add(form);
                        break;
                    case FormData.FormRarity.Legendary:
                        _legendaryForms.Add(form);
                        break;
                }

                // 타입별 분류
                switch (form.type)
                {
                    case FormData.FormType.Warrior:
                        _warriorForms.Add(form);
                        break;
                    case FormData.FormType.Mage:
                        _mageForms.Add(form);
                        break;
                    case FormData.FormType.Assassin:
                        _assassinForms.Add(form);
                        break;
                    case FormData.FormType.Tank:
                        _tankForms.Add(form);
                        break;
                    case FormData.FormType.Support:
                        _supportForms.Add(form);
                        break;
                }
            }

            // 에디터 업데이트
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif

        #endregion
    }
}