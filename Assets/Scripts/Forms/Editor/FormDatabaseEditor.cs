#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using Metamorph.Forms.Data;

namespace Metamorph.Forms.Editor
{
    [CustomEditor(typeof(FormDatabase))]
    public class FormDatabaseEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            FormDatabase formDB = (FormDatabase)target;

            DrawDefaultInspector();

            EditorGUILayout.Space(10);
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("컬렉션 업데이트", GUILayout.Height(30)))
            {
                formDB.UpdateCollections();
                EditorUtility.SetDirty(formDB);
            }

            if (GUILayout.Button("중복 ID 확인", GUILayout.Height(30)))
            {
                CheckForDuplicateIds(formDB);
            }

            EditorGUILayout.EndHorizontal();
        }

        private void CheckForDuplicateIds(FormDatabase formDB)
        {
            var allForms = formDB.GetAllForms();
            var idSet = new System.Collections.Generic.HashSet<string>();
            var duplicates = new System.Collections.Generic.List<string>();

            foreach (var form in allForms)
            {
                if (form == null) continue;

                if (!string.IsNullOrEmpty(form.formId))
                {
                    if (!idSet.Add(form.formId))
                    {
                        duplicates.Add(form.formId);
                    }
                }
                else
                {
                    Debug.LogError($"Form ID가 없는 폼이 있습니다: {form.name}");
                }
            }

            if (duplicates.Count > 0)
            {
                Debug.LogError($"중복된 Form ID가 발견되었습니다: {string.Join(", ", duplicates)}");
            }
            else
            {
                Debug.Log("중복된 ID가 없습니다.");
            }
        }
    }
}
#endif