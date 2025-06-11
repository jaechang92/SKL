using System.Collections.Generic;
using UnityEditor;
using UnityEngine;


#if UNITY_EDITOR
[CustomEditor(typeof(CustomTween))]
public class CustomTweenEditor : Editor
{
    private CustomTween tweenTarget;

    // 기본 프로퍼티들
    private SerializedProperty tweenFlagsProp;
    private SerializedProperty typeSettingsListProp;  // 새로운 프로퍼티

    // Transform 프로퍼티들 (동일)
    private SerializedProperty startPositionProp;
    private SerializedProperty endPositionProp;
    private SerializedProperty useCurrentAsStartPosProp;

    private SerializedProperty startRotationProp;
    private SerializedProperty endRotationProp;
    private SerializedProperty useCurrentAsStartRotProp;

    private SerializedProperty startScaleProp;
    private SerializedProperty endScaleProp;
    private SerializedProperty useCurrentAsStartScaleProp;

    private SerializedProperty startColorProp;
    private SerializedProperty endColorProp;
    private SerializedProperty useCurrentAsStartColorProp;

    private SerializedProperty startAlphaProp;
    private SerializedProperty endAlphaProp;
    private SerializedProperty useCurrentAsStartAlphaProp;

    // UI 스타일
    private GUIStyle _addButtonStyle;
    private GUIStyle _removeButtonStyle;
    private GUIStyle _headerStyle;

    private GUIStyle AddButtonStyle
    {
        get
        {
            if (_addButtonStyle == null)
            {
                _addButtonStyle = new GUIStyle(GUI.skin.button)
                {
                    normal = { textColor = Color.green },
                    fontStyle = FontStyle.Bold
                };
            }
            return _addButtonStyle;
        }
    }

    private GUIStyle RemoveButtonStyle
    {
        get
        {
            if (_removeButtonStyle == null)
            {
                _removeButtonStyle = new GUIStyle(GUI.skin.button)
                {
                    normal = { textColor = Color.red },
                    fontStyle = FontStyle.Bold
                };
            }
            return _removeButtonStyle;
        }
    }

    private GUIStyle HeaderStyle
    {
        get
        {
            if (_headerStyle == null)
            {
                _headerStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 12
                };
            }
            return _headerStyle;
        }
    }

    private void OnEnable()
    {
        tweenTarget = (CustomTween)target;

        tweenFlagsProp = serializedObject.FindProperty("tweenFlags");
        typeSettingsListProp = serializedObject.FindProperty("typeSettingsList");  // 새로운 프로퍼티

        startPositionProp = serializedObject.FindProperty("startPosition");
        endPositionProp = serializedObject.FindProperty("endPosition");
        useCurrentAsStartPosProp = serializedObject.FindProperty("useCurrentAsStartPos");

        startRotationProp = serializedObject.FindProperty("startRotation");
        endRotationProp = serializedObject.FindProperty("endRotation");
        useCurrentAsStartRotProp = serializedObject.FindProperty("useCurrentAsStartRot");

        startScaleProp = serializedObject.FindProperty("startScale");
        endScaleProp = serializedObject.FindProperty("endScale");
        useCurrentAsStartScaleProp = serializedObject.FindProperty("useCurrentAsStartScale");

        startColorProp = serializedObject.FindProperty("startColor");
        endColorProp = serializedObject.FindProperty("endColor");
        useCurrentAsStartColorProp = serializedObject.FindProperty("useCurrentAsStartColor");

        startAlphaProp = serializedObject.FindProperty("startAlpha");
        endAlphaProp = serializedObject.FindProperty("endAlpha");
        useCurrentAsStartAlphaProp = serializedObject.FindProperty("useCurrentAsStartAlpha");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        DrawTweenTypeManager();
        EditorGUILayout.Space();
        DrawActiveTypeSettings();
        EditorGUILayout.Space();
        DrawPreviewButtons();

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawTweenTypeManager()
    {
        EditorGUILayout.LabelField("Tween 타입 관리", HeaderStyle);

        // 현재 활성화된 타입들 표시
        List<TweenType> activeTypes = tweenTarget.GetActiveTweenTypes();

        if (activeTypes.Count == 0)
        {
            EditorGUILayout.HelpBox("활성화된 Tween 타입이 없습니다. 아래에서 타입을 추가하세요.", MessageType.Info);
        }
        else
        {
            EditorGUILayout.LabelField($"활성 타입: {string.Join(", ", activeTypes)}", EditorStyles.helpBox);
        }

        EditorGUILayout.Space(5);

        // Add 버튼들
        EditorGUILayout.LabelField("타입 추가:", EditorStyles.miniLabel);
        EditorGUILayout.BeginHorizontal();

        DrawAddButton(TweenType.Position);
        DrawAddButton(TweenType.Rotation);
        DrawAddButton(TweenType.Scale);

        EditorGUILayout.EndHorizontal();
        EditorGUILayout.BeginHorizontal();

        DrawAddButton(TweenType.Color);
        DrawAddButton(TweenType.Alpha);

        EditorGUILayout.EndHorizontal();

        // Remove 버튼들 (활성화된 타입만)
        if (activeTypes.Count > 0)
        {
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("타입 제거:", EditorStyles.miniLabel);

            int buttonsPerRow = 3;
            for (int i = 0; i < activeTypes.Count; i += buttonsPerRow)
            {
                EditorGUILayout.BeginHorizontal();

                for (int j = 0; j < buttonsPerRow && i + j < activeTypes.Count; j++)
                {
                    DrawRemoveButton(activeTypes[i + j]);
                }

                EditorGUILayout.EndHorizontal();
            }
        }

        // 전체 제거 버튼
        if (activeTypes.Count > 0)
        {
            EditorGUILayout.Space(5);
            if (GUILayout.Button("모든 타입 제거", RemoveButtonStyle))
            {
                tweenFlagsProp.intValue = (int)TweenFlags.None;
                typeSettingsListProp.ClearArray();
            }
        }
    }

    private void DrawAddButton(TweenType type)
    {
        bool isActive = tweenTarget.HasTweenType(type);

        using (new EditorGUI.DisabledScope(isActive))
        {
            if (GUILayout.Button($"+ {type}", AddButtonStyle))
            {
                tweenTarget.AddTweenType(type);
                EditorUtility.SetDirty(tweenTarget);
            }
        }
    }

    private void DrawRemoveButton(TweenType type)
    {
        if (GUILayout.Button($"- {type}", RemoveButtonStyle))
        {
            tweenTarget.RemoveTweenType(type);
            EditorUtility.SetDirty(tweenTarget);
        }
    }

    private void DrawActiveTypeSettings()
    {
        List<TweenType> activeTypes = tweenTarget.GetActiveTweenTypes();

        if (activeTypes.Count == 0) return;

        EditorGUILayout.LabelField("타입별 설정", HeaderStyle);

        foreach (TweenType type in activeTypes)
        {
            DrawTypeSettings(type);
        }
    }

    private void DrawTypeSettings(TweenType type)
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        // 타입 헤더
        EditorGUILayout.LabelField($"{type} 설정", EditorStyles.boldLabel);

        // 타입별 기본 설정 (duration, curve, playOnStart, loop)
        DrawTypeBasicSettings(type);

        EditorGUILayout.Space(5);

        // 타입별 Transform 설정
        switch (type)
        {
            case TweenType.Position:
                DrawPositionTransformSettings();
                break;
            case TweenType.Rotation:
                DrawRotationTransformSettings();
                break;
            case TweenType.Scale:
                DrawScaleTransformSettings();
                break;
            case TweenType.Color:
                DrawColorTransformSettings();
                break;
            case TweenType.Alpha:
                DrawAlphaTransformSettings();
                break;
        }

        EditorGUILayout.EndVertical();
        EditorGUILayout.Space();
    }

    // 새로운 메서드: 타입별 기본 설정 그리기
    private void DrawTypeBasicSettings(TweenType type)
    {
        TweenTypeSettings settings = tweenTarget.GetTypeSettings(type);

        EditorGUI.BeginChangeCheck();

        // Duration
        float newDuration = EditorGUILayout.FloatField("Duration", settings.duration);

        // Curve
        AnimationCurve newCurve = EditorGUILayout.CurveField("Animation Curve", settings.curve);

        // Play On Start
        bool newPlayOnStart = EditorGUILayout.Toggle("Play On Start", settings.playOnStart);

        // Loop and Pingpong options
        EditorGUILayout.Space(5);
        EditorGUILayout.LabelField("반복 옵션", EditorStyles.boldLabel);

        bool newLoop = EditorGUILayout.Toggle("Loop", settings.loop);
        bool newPingpong = EditorGUILayout.Toggle("Pingpong", settings.pingpong);

        // Pingpong 설명
        if (newPingpong)
        {
            EditorGUILayout.HelpBox(
                newLoop ?
                "Pingpong + Loop: 0→1→0→1→0... 무한 반복" :
                "Pingpong Only: 0→1→0 한 번의 왕복 후 정지",
                MessageType.Info
            );
        }
        else if (newLoop)
        {
            EditorGUILayout.HelpBox("Loop Only: 0→1→0→1→0... (매번 시작점에서 재시작)", MessageType.Info);
        }

        // 런타임 상태 표시
        if (Application.isPlaying && tweenTarget.IsPlaying(type))
        {
            EditorGUILayout.Space(3);
            string directionText = tweenTarget.IsForward(type) ? "Forward (0→1)" : "Backward (1→0)";
            EditorGUILayout.LabelField($"현재 방향: {directionText}", EditorStyles.helpBox);

            if (newPingpong && GUILayout.Button("방향 바꾸기"))
            {
                tweenTarget.FlipDirection(type);
            }
        }

        if (EditorGUI.EndChangeCheck())
        {
            TweenTypeSettings newSettings = new TweenTypeSettings(newDuration, newCurve, newPlayOnStart, newLoop, newPingpong);
            tweenTarget.SetTypeSettings(type, newSettings);
            EditorUtility.SetDirty(tweenTarget);
        }
    }

    private void DrawPositionTransformSettings()
    {
        EditorGUILayout.LabelField("Transform 설정", EditorStyles.miniLabel);
        EditorGUILayout.PropertyField(useCurrentAsStartPosProp, new GUIContent("현재 위치를 시작점으로"));

        if (!useCurrentAsStartPosProp.boolValue)
        {
            EditorGUILayout.PropertyField(startPositionProp);
        }

        EditorGUILayout.PropertyField(endPositionProp);
    }

    private void DrawRotationTransformSettings()
    {
        EditorGUILayout.LabelField("Transform 설정", EditorStyles.miniLabel);
        EditorGUILayout.PropertyField(useCurrentAsStartRotProp, new GUIContent("현재 회전을 시작점으로"));

        if (!useCurrentAsStartRotProp.boolValue)
        {
            EditorGUILayout.PropertyField(startRotationProp);
        }

        EditorGUILayout.PropertyField(endRotationProp);
    }

    private void DrawScaleTransformSettings()
    {
        EditorGUILayout.LabelField("Transform 설정", EditorStyles.miniLabel);
        EditorGUILayout.PropertyField(useCurrentAsStartScaleProp, new GUIContent("현재 크기를 시작점으로"));

        if (!useCurrentAsStartScaleProp.boolValue)
        {
            EditorGUILayout.PropertyField(startScaleProp);
        }

        EditorGUILayout.PropertyField(endScaleProp);
    }

    private void DrawColorTransformSettings()
    {
        EditorGUILayout.LabelField("Transform 설정", EditorStyles.miniLabel);
        EditorGUILayout.PropertyField(useCurrentAsStartColorProp, new GUIContent("현재 색상을 시작점으로"));

        if (!useCurrentAsStartColorProp.boolValue)
        {
            EditorGUILayout.PropertyField(startColorProp);
        }

        EditorGUILayout.PropertyField(endColorProp);

        // 컴포넌트 체크
        if (tweenTarget.GetComponent<Renderer>() == null &&
            tweenTarget.GetComponent<SpriteRenderer>() == null)
        {
            EditorGUILayout.HelpBox("Renderer 또는 SpriteRenderer 컴포넌트가 필요합니다.", MessageType.Warning);
        }
    }

    private void DrawAlphaTransformSettings()
    {
        EditorGUILayout.LabelField("Transform 설정", EditorStyles.miniLabel);
        EditorGUILayout.PropertyField(useCurrentAsStartAlphaProp, new GUIContent("현재 알파를 시작점으로"));

        if (!useCurrentAsStartAlphaProp.boolValue)
        {
            EditorGUILayout.PropertyField(startAlphaProp);
        }

        EditorGUILayout.PropertyField(endAlphaProp);

        // 컴포넌트 체크
        if (tweenTarget.GetComponent<CanvasGroup>() == null &&
            tweenTarget.GetComponent<SpriteRenderer>() == null &&
            tweenTarget.GetComponent<Renderer>() == null)
        {
            EditorGUILayout.HelpBox("CanvasGroup, SpriteRenderer 또는 Renderer 컴포넌트가 필요합니다.", MessageType.Warning);
        }
    }

    private void DrawPreviewButtons()
    {
        EditorGUILayout.LabelField("미리보기", HeaderStyle);

        // 전체 제어 버튼
        EditorGUILayout.LabelField("전체 제어:", EditorStyles.miniLabel);
        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("Play All"))
        {
            if (Application.isPlaying)
            {
                tweenTarget.PlayTween();
            }
        }

        if (GUILayout.Button("Stop All"))
        {
            if (Application.isPlaying)
            {
                tweenTarget.StopTween();
            }
        }

        if (GUILayout.Button("Reset All"))
        {
            if (Application.isPlaying)
            {
                tweenTarget.ResetTween();
            }
        }

        EditorGUILayout.EndHorizontal();

        // 개별 타입 제어 버튼
        List<TweenType> activeTypes = tweenTarget.GetActiveTweenTypes();
        if (activeTypes.Count > 0)
        {
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("개별 제어:", EditorStyles.miniLabel);

            foreach (TweenType type in activeTypes)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(type.ToString(), GUILayout.Width(80));

                if (GUILayout.Button("Play"))
                {
                    if (Application.isPlaying)
                    {
                        tweenTarget.PlayTween(type);
                    }
                }

                if (GUILayout.Button("Stop"))
                {
                    if (Application.isPlaying)
                    {
                        tweenTarget.StopTween(type);
                    }
                }

                if (GUILayout.Button("Reset"))
                {
                    if (Application.isPlaying)
                    {
                        tweenTarget.ResetTween(type);
                    }
                }

                EditorGUILayout.EndHorizontal();
            }
        }
    }
}
#endif