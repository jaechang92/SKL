using CustomDebug;
using Cysharp.Threading.Tasks;
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
    // 형태 관리자 - 싱글톤 패턴 적용
    public class FormManager : SingletonManager<FormManager>, IInitializableAsync
    {
        [SerializeField] private FormDatabase _formDatabase;
        // 현재 장착된 형태들
        private FormData _primaryForm;
        private FormData _secondaryForm;
        private FormData _currentForm;

        // 소유한 모든 형태
        private Dictionary<string, FormData> _unlockedForms = new Dictionary<string, FormData>();

        // 형태 변경 이벤트 - 옵저버 패턴
        public event Action<FormData> OnFormChanged;

        // 플레이어 컴포넌트 참조
        private PlayerController _playerController;

        [Header("Debug")]
        [SerializeField] private bool _showDebugInfo = false;

        public string Name => "Form Manager";

        public InitializationPriority Priority { get; set; } = InitializationPriority.Normal;

        public bool IsInitialized { get; private set; }

        protected override void OnCreated()
        {
            base.OnCreated();
            // Resources 폴더에서 FormDatabase 로드
            _formDatabase = Resources.Load<FormDatabase>("Databases/FormDatabase");
            _currentForm = _formDatabase.GetDefaultForm(); // 기본 폼 설정

            if (_formDatabase != null && _showDebugInfo)
            {
                JCDebug.Log("FormDatabase 성공적으로 로드됨");
                InitializeDefaultForms();
            }
            else
            {
                JCDebug.Log("FormDatabase를 Resources 폴더에서 찾을 수 없습니다!",JCDebug.LogLevel.Error);
            }
        }

        private void Start()
        {
            OnCreated();
        }

        public void RegisterPlayer(Action<FormData> formChangedCallback)
        {
            OnFormChanged += formChangedCallback;

            // 현재 장착된 폼이 있다면 즉시 콜백 호출
            if (_currentForm != null)
            {
                formChangedCallback.Invoke(_currentForm);
            }
        }

        public void UnregisterPlayer(Action<FormData> formChangedCallback)
        {
            OnFormChanged -= formChangedCallback;
        }

        private void InitializeDefaultForms()
        {
            // 기본 폼 가져오기
            FormData defaultForm = _formDatabase.GetDefaultForm();

            // 기본 폼 잠금 해제 및 장착
            if (defaultForm != null)
            {
                UnlockForm(defaultForm);
                EquipForm(defaultForm, true);
            }
            else
            {
                JCDebug.Log("기본 폼이 FormDatabase에 정의되지 않았습니다!", JCDebug.LogLevel.Error);
            }

            // 시작 폼 잠금 해제 (있다면)
            foreach (var starterForm in _formDatabase.GetStarterForms())
            {
                UnlockForm(starterForm);
            }

            // 저장된 게임에서 로드 (있다면)
            LoadUnlockedForms();
        }

        // FormManager - 이벤트만 발생
        public void SwitchForm()
        {
            // 폼 전환 로직...
            OnFormChanged.Invoke(_currentForm);
        }

        // 형태 획득
        public void UnlockForm(FormData form)
        {
            if (form == null || string.IsNullOrEmpty(form.formId))
            {
                JCDebug.Log("잘못된 폼을 잠금 해제하려고 시도했습니다.", JCDebug.LogLevel.Error);
                return;
            }

            if (!_unlockedForms.ContainsKey(form.formId))
            {
                _unlockedForms.Add(form.formId, form);

                // 획득 이벤트 발생
                JCDebug.Log($"새 폼 획득: {form.formName}");
                // 여기에 이벤트 시스템 호출 추가
            }
        }

        public void UnlockFormById(string formId)
        {
            if (string.IsNullOrEmpty(formId))
            {
                JCDebug.Log("빈 formId로 폼을 잠금 해제하려고 시도했습니다.", JCDebug.LogLevel.Error);
                return;
            }

            // 이미 잠금 해제되었는지 확인
            if (_unlockedForms.ContainsKey(formId))
            {
                JCDebug.Log($"폼 '{formId}'는 이미 잠금 해제되었습니다.");
                return;
            }

            // 데이터베이스에서 폼 가져오기
            FormData form = _formDatabase.GetFormById(formId);

            if (form != null)
            {
                UnlockForm(form);
            }
            else
            {
                JCDebug.Log($"ID '{formId}'의 폼을 찾을 수 없습니다.", JCDebug.LogLevel.Error);
            }
        }

        // 형태 장착
        public void EquipForm(FormData form, bool asPrimary)
        {
            // form이 null이 아니고 잠금 해제되었는지 확인
            if (form == null || !_unlockedForms.ContainsKey(form.formId))
            {
                JCDebug.Log("장착할 수 없는 폼입니다: 없거나 잠금 해제되지 않았습니다.",JCDebug.LogLevel.Warning);
                return;
            }

            if (asPrimary)
            {
                _primaryForm = form;
                _currentForm = _primaryForm; // 기본적으로 주 형태 사용
            }
            else
            {
                _secondaryForm = form;
            }

            // 현재 형태가 변경되었다면 캐릭터 업데이트 // 캐릭터 업데이트는 캐릭터 컨트롤러에서 처리
            //UpdateCharacterWithCurrentForm();

            // 형태 변경 이벤트 발생
            SwitchForm();
        }

        // 보조 형태로 전환
        public void SwitchToSecondaryForm()
        {
            if (_secondaryForm == null)
            {
                JCDebug.Log("No secondary form equipped",JCDebug.LogLevel.Warning);
                return;
            }

            _currentForm = _currentForm == _primaryForm ? _secondaryForm : _primaryForm;

            //UpdateCharacterWithCurrentForm(); // 캐릭터 업데이트는 캐릭터 컨트롤러에서 처리
            // 형태 변경 이벤트 발생
            SwitchForm();

            // 전환 효과 재생
            PlayFormSwitchEffect();
        }

        //// 현재 형태에 따라 캐릭터 업데이트
        //private void UpdateCharacterWithCurrentForm()
        //{
        //    if (_currentForm == null) return;

        //    // 애니메이터 업데이트
        //    _animator.runtimeAnimatorController = _currentForm.animatorController;

        //    // 스프라이트 업데이트 (필요시)
        //    //_spriteRenderer.sprite = _currentForm.formSprite;

        //    // 플레이어 스탯 업데이트
        //    _playerController.UpdateStats(
        //        _currentForm.maxHealth,
        //        _currentForm.moveSpeed,
        //        _currentForm.jumpForce
        //    );

        //    // 스킬 업데이트
        //    SkillManager.Instance.UpdateSkills(
        //        _currentForm.basicAttack,
        //        _currentForm.skillOne,
        //        _currentForm.skillTwo,
        //        _currentForm.ultimateSkill
        //    );

        //    // 패시브 능력 적용
        //    ApplyPassiveAbilities();
        //}


        // 형태 전환 효과
        private void PlayFormSwitchEffect()
        {
            // 여기에 파티클 효과, 사운드 등 추가
            JCDebug.Log("Form switch effect played");

            // 예시: 파티클 시스템 재생
            ParticleSystem switchFx = GetComponent<ParticleSystem>();
            if (switchFx != null)
            {
                switchFx.Play();
            }

            // 예시: 사운드 재생
            AudioSource audioSource = GetComponent<AudioSource>();
            if (audioSource != null)
            {
                audioSource.PlayOneShot(Resources.Load<AudioClip>("Sounds/form_switch"));
            }
        }

        // 저장/로드 관련 메서드

        public List<string> GetUnlockedFormIds()
        {
            return _unlockedForms.Keys.ToList();
        }

        public void LoadUnlockedForms()
        {
            // 여기에 저장 시스템과 연동하여 이전에 획득한 폼 불러오기
            // ...
        }

        internal FormData GetCurrentForm()
        {
            return _currentForm;
        }


        protected override void OnDestroy()
        {
            base.OnDestroy();
            // 이벤트 구독 해제

            OnFormChanged = null;
        }

        public UniTask InitializeAsync(CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }
}