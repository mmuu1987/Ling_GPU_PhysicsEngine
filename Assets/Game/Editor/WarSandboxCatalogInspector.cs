using UnityEditor;
using UnityEngine;

namespace MassEngine.Game.Editor
{
    [CustomEditor(typeof(WarSandboxBattlefieldCatalog))]
    public sealed class WarSandboxCatalogInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUI.BeginDisabledGroup(EditorApplication.isPlayingOrWillChangePlaymode);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("defaultEntryId"));
            SerializedProperty entries = serializedObject.FindProperty("entries");
            for (int i = 0; i < entries.arraySize; i++)
            {
                var item = entries.GetArrayElementAtIndex(i);
                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField("战场 " + (i + 1), EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(item.FindPropertyRelative("id"));
                EditorGUILayout.PropertyField(item.FindPropertyRelative("displayName"));
                var path = item.FindPropertyRelative("scenePath");
                SceneAsset current = AssetDatabase.LoadAssetAtPath<SceneAsset>(path.stringValue);
                EditorGUI.BeginChangeCheck();
                var scene = EditorGUILayout.ObjectField("Scene", current, typeof(SceneAsset), false) as SceneAsset;
                if (EditorGUI.EndChangeCheck()) path.stringValue = scene != null ? AssetDatabase.GetAssetPath(scene) : string.Empty;
                EditorGUILayout.PropertyField(item.FindPropertyRelative("rules"));
                EditorGUILayout.PropertyField(item.FindPropertyRelative("preview"));
                EditorGUILayout.PropertyField(item.FindPropertyRelative("contentVersion"));
                EditorGUILayout.PropertyField(item.FindPropertyRelative("terrainId"));
                EditorGUILayout.PropertyField(item.FindPropertyRelative("terrainVersion"));
                if (GUILayout.Button("移除条目")) { entries.DeleteArrayElementAtIndex(i); break; }
            }
            if (GUILayout.Button("新增条目"))
            {
                int index = entries.arraySize++;
                var item = entries.GetArrayElementAtIndex(index);
                item.FindPropertyRelative("id").stringValue = string.Empty;
                item.FindPropertyRelative("displayName").stringValue = string.Empty;
                item.FindPropertyRelative("scenePath").stringValue = string.Empty;
                item.FindPropertyRelative("rules").objectReferenceValue = null;
                item.FindPropertyRelative("preview").objectReferenceValue = null;
                item.FindPropertyRelative("contentVersion").intValue = 1;
                item.FindPropertyRelative("terrainId").stringValue = "flat-ground";
                item.FindPropertyRelative("terrainVersion").intValue = 1;
            }
            EditorGUILayout.PropertyField(serializedObject.FindProperty("templates"), true);
            serializedObject.ApplyModifiedProperties();
            var catalog = (WarSandboxBattlefieldCatalog)target;
            if (GUILayout.Button("加入构建场景")) WarSandboxEntryBuilder.AddBuildScenes(catalog);
            EditorGUI.EndDisabledGroup();
            if (!catalog.TryValidate(WarSandboxEntryBuilder.IsIncludedScene, out string error))
                EditorGUILayout.HelpBox(error, MessageType.Error);
            if (!catalog.TryValidateTemplates(out error)) EditorGUILayout.HelpBox(error, MessageType.Error);
        }
    }
}
