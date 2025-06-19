using Cysharp.Threading.Tasks;
using Metamorph.Level.Room;
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 방 선택 UI 컴포넌트
/// </summary>
public class RoomChoiceUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject _choicePanel;
    [SerializeField] private Transform _choiceContainer;
    [SerializeField] private GameObject _roomChoiceButtonPrefab;

    [Header("Animation Settings")]
    [SerializeField] private float _showDuration = 0.5f;
    [SerializeField] private float _hideDuration = 0.3f;

    private RoomChoiceManager _roomChoiceManager;
    private List<GameObject> _choiceButtons = new List<GameObject>();
    private CustomTween _panelTween;

    public void Initialize(RoomChoiceManager manager)
    {
        _roomChoiceManager = manager;

        if (_choicePanel != null)
        {
            _choicePanel.SetActive(false);

            // CustomTween 컴포넌트 추가
            _panelTween = _choicePanel.GetComponent<CustomTween>();
            if (_panelTween == null)
            {
                _panelTween = _choicePanel.AddComponent<CustomTween>();
            }

            SetupPanelTween();
        }
    }

    /// <summary>
    /// 패널 트윈 설정
    /// </summary>
    private void SetupPanelTween()
    {
        if (_panelTween != null)
        {
            // Scale 트윈 설정
            _panelTween.tweenFlags = TweenFlags.Scale;
            var scaleSettings = _panelTween.GetTypeSettings(TweenType.Scale);
            scaleSettings.duration = _showDuration;
            scaleSettings.curve = AnimationCurve.EaseInOut(0, 0, 1, 1);
            scaleSettings.loop = false;
            scaleSettings.pingpong = false;

            _panelTween.SetTypeSettings(TweenType.Scale, scaleSettings);
        }
    }

    /// <summary>
    /// 선택지 표시
    /// </summary>
    public async UniTask ShowChoices(List<RoomChoicePortal> choices)
    {
        if (_choicePanel == null) return;

        // 기존 버튼들 정리
        ClearChoiceButtons();

        // 새 버튼들 생성
        CreateChoiceButtons(choices);

        // 패널 표시
        _choicePanel.SetActive(true);

        // CustomTween을 사용한 스케일 애니메이션
        _choicePanel.transform.localScale = Vector3.zero;
        if (_panelTween != null)
        {
            _panelTween.PlayTween(TweenType.Scale);
        }

        await UniTask.Delay(TimeSpan.FromSeconds(_showDuration).Milliseconds, true);
    }

    /// <summary>
    /// 선택지 숨기기
    /// </summary>
    public async UniTask HideChoices()
    {
        if (_choicePanel == null) return;

        // 간단한 스케일 다운 애니메이션
        float elapsed = 0f;
        Vector3 startScale = _choicePanel.transform.localScale;

        while (elapsed < _hideDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = elapsed / _hideDuration;
            _choicePanel.transform.localScale = Vector3.Lerp(startScale, Vector3.zero, t);
            await UniTask.Yield();
        }

        _choicePanel.SetActive(false);
        ClearChoiceButtons();
    }

    /// <summary>
    /// 선택 버튼들 생성
    /// </summary>
    private void CreateChoiceButtons(List<RoomChoicePortal> choices)
    {
        if (_roomChoiceButtonPrefab == null || _choiceContainer == null) return;

        foreach (var choice in choices)
        {
            var buttonObj = Instantiate(_roomChoiceButtonPrefab, _choiceContainer);
            var button = buttonObj.GetComponent<RoomChoiceButton>();

            if (button != null)
            {
                button.Initialize(choice, _roomChoiceManager);
                _choiceButtons.Add(buttonObj);
            }
        }
    }

    /// <summary>
    /// 선택 버튼들 정리
    /// </summary>
    private void ClearChoiceButtons()
    {
        foreach (var button in _choiceButtons)
        {
            if (button != null)
            {
                Destroy(button);
            }
        }
        _choiceButtons.Clear();
    }
}

