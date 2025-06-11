


using CustomDebug;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public enum TweenType
{
    Position,
    Rotation,
    Scale,
    Color,
    Alpha
}

[System.Flags]
public enum TweenFlags
{
    None = 0,
    Position = 1 << 0,    // 1
    Rotation = 1 << 1,    // 2
    Scale = 1 << 2,       // 4
    Color = 1 << 3,       // 8
    Alpha = 1 << 4        // 16
}

[System.Serializable]
public class TweenTypeSettings
{
    public float duration = 1f;
    public AnimationCurve curve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    public bool playOnStart = false;
    public bool loop = false;
    public bool pingpong = false;

    public TweenTypeSettings()
    {
        duration = 1f;
        curve = AnimationCurve.EaseInOut(0, 0, 1, 1);
        playOnStart = false;
        loop = false;
        pingpong = false;
    }

    public TweenTypeSettings(float duration, AnimationCurve curve, bool playOnStart, bool loop, bool pingpong)
    {
        this.duration = duration;
        this.curve = curve != null ? curve : AnimationCurve.EaseInOut(0, 0, 1, 1);
        this.playOnStart = playOnStart;
        this.loop = loop;
        this.pingpong = pingpong;
    }
}

[System.Serializable]
public class CustomTween : MonoBehaviour
{
    [Header("활성화된 타입")]
    public TweenFlags tweenFlags = TweenFlags.Position;

    [Header("타입별 설정")]
    [SerializeField] private List<TweenTypeSettingsPair> typeSettingsList = new List<TweenTypeSettingsPair>();

    [Header("Position 설정")]
    [SerializeField] private Vector3 startPosition;
    [SerializeField] private Vector3 endPosition;
    [SerializeField] private bool useCurrentAsStartPos = true;

    [Header("Rotation 설정")]
    [SerializeField] private Vector3 startRotation;
    [SerializeField] private Vector3 endRotation;
    [SerializeField] private bool useCurrentAsStartRot = true;

    [Header("Scale 설정")]
    [SerializeField] private Vector3 startScale = Vector3.one;
    [SerializeField] private Vector3 endScale = Vector3.one;
    [SerializeField] private bool useCurrentAsStartScale = true;

    [Header("Color 설정")]
    [SerializeField] private Color startColor = Color.white;
    [SerializeField] private Color endColor = Color.white;
    [SerializeField] private bool useCurrentAsStartColor = true;

    [Header("Alpha 설정")]
    [SerializeField] private float startAlpha = 1f;
    [SerializeField] private float endAlpha = 0f;
    [SerializeField] private bool useCurrentAsStartAlpha = true;

    // 컴포넌트 참조
    private Renderer targetRenderer;
    private SpriteRenderer targetSpriteRenderer;
    private CanvasGroup targetCanvasGroup;
    private RectTransform rectTransform;  // 새로운 추가
    private bool isCanvasElement;         // 새로운 추가: Canvas 요소인지 여부

    // 트윈 상태 관리
    private Dictionary<TweenType, bool> isPlayingDict = new Dictionary<TweenType, bool>();
    private Dictionary<TweenType, Coroutine> activeCoroutines = new Dictionary<TweenType, Coroutine>();
    private Dictionary<TweenType, bool> isForwardDict = new Dictionary<TweenType, bool>();  // 새로운 추가: Pingpong 방향 추적

    // 이벤트
    public event Action<TweenType> OnTweenStart;
    public event Action<TweenType> OnTweenComplete;
    public event Action<TweenType, float> OnTweenUpdate;
    public event Action OnAllTweensComplete;
    public event Action<TweenType, bool> OnPingpongDirectionChange;  // 새로운 이벤트: Pingpong 방향 변경

    // 기존 코드들...
    [System.Serializable]
    public class TweenTypeSettingsPair
    {
        public TweenType tweenType;
        public TweenTypeSettings settings;

        public TweenTypeSettingsPair(TweenType type, TweenTypeSettings settings)
        {
            this.tweenType = type;
            this.settings = settings;
        }
    }

    private void Start()
    {
        InitializeComponents();
        InitializeTypeSettings();

        foreach (TweenType type in GetActiveTweenTypes())
        {
            var settings = GetTypeSettings(type);
            if (settings.playOnStart)
            {
                PlayTween(type);
            }
        }
    }

    private void InitializeComponents()
    {
        targetRenderer = GetComponent<Renderer>();
        targetSpriteRenderer = GetComponent<SpriteRenderer>();
        targetCanvasGroup = GetComponent<CanvasGroup>();
        rectTransform = GetComponent<RectTransform>();

        isCanvasElement = targetCanvasGroup == null ? false : true;

        SetCurrentAsStartValues();
    }

    private void InitializeTypeSettings()
    {
        List<TweenType> activeTypes = GetActiveTweenTypes();

        foreach (TweenType type in activeTypes)
        {
            if (!HasTypeSettings(type))
            {
                AddDefaultTypeSettings(type);
            }
        }

        typeSettingsList.RemoveAll(pair => !activeTypes.Contains(pair.tweenType));
    }

    private bool HasTypeSettings(TweenType type)
    {
        return typeSettingsList.Any(pair => pair.tweenType == type);
    }

    private void AddDefaultTypeSettings(TweenType type)
    {
        TweenTypeSettings defaultSettings = GetDefaultSettingsForType(type);
        typeSettingsList.Add(new TweenTypeSettingsPair(type, defaultSettings));
    }

    // 수정된 기본 설정 (Pingpong 포함)
    private TweenTypeSettings GetDefaultSettingsForType(TweenType type)
    {
        switch (type)
        {
            case TweenType.Position:
                return new TweenTypeSettings(1f, AnimationCurve.EaseInOut(0, 0, 1, 1), false, false, false);
            case TweenType.Rotation:
                return new TweenTypeSettings(1f, AnimationCurve.Linear(0, 0, 1, 1), false, false, true); // Rotation은 기본 Pingpong
            case TweenType.Scale:
                return new TweenTypeSettings(0.5f, AnimationCurve.EaseInOut(0, 0, 1, 1), false, true, true); // Scale은 Loop + Pingpong
            case TweenType.Color:
                return new TweenTypeSettings(2f, AnimationCurve.EaseInOut(0, 0, 1, 1), false, false, false);
            case TweenType.Alpha:
                return new TweenTypeSettings(1.5f, AnimationCurve.EaseInOut(0, 0, 1, 1), false, true, true); // Alpha는 Loop + Pingpong
            default:
                return new TweenTypeSettings();
        }
    }

    public TweenTypeSettings GetTypeSettings(TweenType type)
    {
        var pair = typeSettingsList.FirstOrDefault(p => p.tweenType == type);
        return pair?.settings ?? new TweenTypeSettings();
    }

    public void SetTypeSettings(TweenType type, TweenTypeSettings settings)
    {
        var existingPair = typeSettingsList.FirstOrDefault(p => p.tweenType == type);
        if (existingPair != null)
        {
            existingPair.settings = settings;
        }
        else
        {
            typeSettingsList.Add(new TweenTypeSettingsPair(type, settings));
        }
    }

    // 기존 메서드들...
    public List<TweenType> GetActiveTweenTypes()
    {
        List<TweenType> activeTypes = new List<TweenType>();

        if (HasFlag(TweenFlags.Position)) activeTypes.Add(TweenType.Position);
        if (HasFlag(TweenFlags.Rotation)) activeTypes.Add(TweenType.Rotation);
        if (HasFlag(TweenFlags.Scale)) activeTypes.Add(TweenType.Scale);
        if (HasFlag(TweenFlags.Color)) activeTypes.Add(TweenType.Color);
        if (HasFlag(TweenFlags.Alpha)) activeTypes.Add(TweenType.Alpha);

        return activeTypes;
    }

    public void AddTweenType(TweenType type)
    {
        TweenFlags flag = ConvertToFlag(type);
        tweenFlags |= flag;

        if (!HasTypeSettings(type))
        {
            AddDefaultTypeSettings(type);
        }
    }

    public void RemoveTweenType(TweenType type)
    {
        TweenFlags flag = ConvertToFlag(type);
        tweenFlags &= ~flag;

        StopTween(type);
        typeSettingsList.RemoveAll(pair => pair.tweenType == type);
    }

    public bool HasTweenType(TweenType type)
    {
        TweenFlags flag = ConvertToFlag(type);
        return HasFlag(flag);
    }

    private TweenFlags ConvertToFlag(TweenType type)
    {
        switch (type)
        {
            case TweenType.Position: return TweenFlags.Position;
            case TweenType.Rotation: return TweenFlags.Rotation;
            case TweenType.Scale: return TweenFlags.Scale;
            case TweenType.Color: return TweenFlags.Color;
            case TweenType.Alpha: return TweenFlags.Alpha;
            default: return TweenFlags.None;
        }
    }

    private void SetCurrentAsStartValues()
    {
        if (HasFlag(TweenFlags.Position) && useCurrentAsStartPos)
        {
            if (isCanvasElement && rectTransform != null)
            {
                startPosition = rectTransform.anchoredPosition;
            }
            else
            {
                startPosition = transform.position;
            }
        }

        if (HasFlag(TweenFlags.Rotation) && useCurrentAsStartRot)
        {
            if (isCanvasElement && rectTransform != null)
            {
                startRotation = rectTransform.eulerAngles;
            }
            else
            {
                startRotation = transform.eulerAngles;
            }
        }

        if (HasFlag(TweenFlags.Scale) && useCurrentAsStartScale)
        {
            startScale = transform.localScale;
        }

        if (HasFlag(TweenFlags.Color) && useCurrentAsStartColor)
        {
            if (targetSpriteRenderer != null)
                startColor = targetSpriteRenderer.color;
            else if (targetRenderer != null)
                startColor = targetRenderer.material.color;
        }

        if (HasFlag(TweenFlags.Alpha) && useCurrentAsStartAlpha)
        {
            if (targetCanvasGroup != null)
                startAlpha = targetCanvasGroup.alpha;
            else if (targetSpriteRenderer != null)
                startAlpha = targetSpriteRenderer.color.a;
        }
    }

    private bool HasFlag(TweenFlags flag)
    {
        return (tweenFlags & flag) == flag;
    }

    public void PlayTween()
    {
        List<TweenType> activeTypes = GetActiveTweenTypes();
        foreach (TweenType type in activeTypes)
        {
            PlayTween(type);
        }
    }

    public void PlayTween(TweenType type)
    {
        if (!HasTweenType(type)) return;

        TweenTypeSettings settings = GetTypeSettings(type);

        if (IsPlaying(type) && !settings.loop && !settings.pingpong) return;

        StopTween(type);

        isPlayingDict[type] = true;
        isForwardDict[type] = true;  // 새로운 추가: 항상 forward로 시작
        OnTweenStart?.Invoke(type);

        Coroutine coroutine = StartCoroutine(TweenCoroutine(type, settings));
        activeCoroutines[type] = coroutine;
    }

    // 수정된 Tween 코루틴 (Pingpong 로직 포함)
    private IEnumerator TweenCoroutine(TweenType type, TweenTypeSettings settings)
    {
        float currentTime = 0f;
        bool shouldContinue = true;

        while (shouldContinue)
        {
            currentTime = 0f;
            bool isForward = isForwardDict.ContainsKey(type) ? isForwardDict[type] : true;

            while (currentTime < settings.duration)
            {
                currentTime += Time.deltaTime;
                float normalizedTime = currentTime / settings.duration;

                // Pingpong일 때 방향에 따라 t 값 조정
                float t = normalizedTime;
                if (settings.pingpong && !isForward)
                {
                    t = 1f - normalizedTime;  // 역방향일 때는 1에서 0으로
                }

                float curveValue = settings.curve.Evaluate(t);

                ApplyTween(type, curveValue);
                OnTweenUpdate?.Invoke(type, normalizedTime);

                yield return null;
            }

            // 한 사이클 완료
            float finalT = settings.pingpong && !isForward ? 0f : 1f;
            ApplyTween(type, finalT);

            // Pingpong 또는 Loop 처리
            if (settings.pingpong)
            {
                // Pingpong: 방향 변경
                isForwardDict[type] = !isForwardDict[type];
                OnPingpongDirectionChange?.Invoke(type, isForwardDict[type]);

                // Loop가 false이고 한 번의 왕복이 끝났다면 (다시 forward가 됐다면) 정지
                if (!settings.loop && isForwardDict[type])
                {
                    shouldContinue = false;
                }
            }
            else if (settings.loop)
            {
                // 일반 Loop: 계속 반복
                shouldContinue = true;
            }
            else
            {
                // Loop도 Pingpong도 아니면 한 번만 실행
                shouldContinue = false;
            }
        }

        // 트윈 완료 처리
        isPlayingDict[type] = false;
        if (activeCoroutines.ContainsKey(type))
        {
            activeCoroutines.Remove(type);
        }

        OnTweenComplete?.Invoke(type);

        if (activeCoroutines.Count == 0)
        {
            OnAllTweensComplete?.Invoke();
        }
    }

    private void ApplyTween(TweenType type, float t)
    {
        switch (type)
        {
            case TweenType.Position:
                if (isCanvasElement && rectTransform != null)
                {
                    // UI Canvas 요소: anchoredPosition 사용
                    Vector2 lerpedPosition = Vector2.Lerp(startPosition, startPosition + endPosition, t);
                    rectTransform.anchoredPosition = lerpedPosition;
                }
                else
                {
                    // 일반 3D 오브젝트: position 사용
                    transform.position = Vector3.Lerp(startPosition, endPosition, t);

                }
                break;

            case TweenType.Rotation:
                // 새로운 수정: Canvas 요소 체크
                if (isCanvasElement && rectTransform != null)
                {
                    // UI Canvas 요소: RectTransform.eulerAngles 사용
                    rectTransform.eulerAngles = Vector3.Lerp(startRotation, endRotation, t);
                }
                else
                {
                    // 일반 3D 오브젝트: Transform.eulerAngles 사용
                    transform.eulerAngles = Vector3.Lerp(startRotation, endRotation, t);
                }
                break;

            case TweenType.Scale:
                transform.localScale = Vector3.Lerp(startScale, endScale, t);
                break;

            case TweenType.Color:
                Color lerpedColor = Color.Lerp(startColor, endColor, t);
                ApplyColor(lerpedColor);
                break;

            case TweenType.Alpha:
                float lerpedAlpha = Mathf.Lerp(startAlpha, endAlpha, t);
                ApplyAlpha(lerpedAlpha);
                break;
        }
    }

    private void ApplyColor(Color color)
    {
        if (targetSpriteRenderer != null)
        {
            targetSpriteRenderer.color = color;
        }
        else if (targetRenderer != null)
        {
            targetRenderer.material.color = color;
        }
    }

    private void ApplyAlpha(float alpha)
    {
        if (targetCanvasGroup != null)
        {
            targetCanvasGroup.alpha = alpha;
        }
        else if (targetSpriteRenderer != null)
        {
            Color color = targetSpriteRenderer.color;
            color.a = alpha;
            targetSpriteRenderer.color = color;
        }
        else if (targetRenderer != null)
        {
            Color color = targetRenderer.material.color;
            color.a = alpha;
            targetRenderer.material.color = color;
        }
    }

    // 수정된 정지 메서드들
    public void StopTween()
    {
        List<TweenType> activeTypes = new List<TweenType>(activeCoroutines.Keys);
        foreach (TweenType type in activeTypes)
        {
            StopTween(type);
        }
    }

    public void StopTween(TweenType type)
    {
        if (activeCoroutines.ContainsKey(type))
        {
            StopCoroutine(activeCoroutines[type]);
            activeCoroutines.Remove(type);
        }

        if (isPlayingDict.ContainsKey(type))
        {
            isPlayingDict[type] = false;
        }

        // Pingpong 상태도 초기화
        if (isForwardDict.ContainsKey(type))
        {
            isForwardDict[type] = true;
        }
    }

    public void ResetTween()
    {
        StopTween();

        List<TweenType> activeTypes = GetActiveTweenTypes();
        foreach (TweenType type in activeTypes)
        {
            ApplyTween(type, 0f);
        }
    }

    public void ResetTween(TweenType type)
    {
        StopTween(type);
        ApplyTween(type, 0f);
    }

    // 새로운 메서드들
    public bool IsPlaying(TweenType type)
    {
        return isPlayingDict.ContainsKey(type) && isPlayingDict[type];
    }

    public bool IsAnyPlaying()
    {
        return activeCoroutines.Count > 0;
    }

    public bool IsForward(TweenType type)
    {
        return isForwardDict.ContainsKey(type) ? isForwardDict[type] : true;
    }

    // Pingpong 방향 수동 변경
    public void FlipDirection(TweenType type)
    {
        if (isForwardDict.ContainsKey(type))
        {
            isForwardDict[type] = !isForwardDict[type];
            OnPingpongDirectionChange?.Invoke(type, isForwardDict[type]);
        }
    }

    public bool IsCanvasBasedTween()
    {
        return isCanvasElement;
    }

    public Vector2 GetCurrentAnchoredPosition()
    {
        if (isCanvasElement && rectTransform != null)
        {
            return rectTransform.anchoredPosition;
        }
        return Vector2.zero;
    }

    public Vector3 GetCurrentWorldPosition()
    {
        if (isCanvasElement && rectTransform != null)
        {
            return rectTransform.position; // World position
        }
        return transform.position;
    }

}