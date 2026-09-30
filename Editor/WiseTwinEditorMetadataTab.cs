using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

#if UNITY_EDITOR

namespace WiseTwin.Editor
{
    /// <summary>
    /// Metadata Config tab for WiseTwinEditor
    /// </summary>
    public static class WiseTwinEditorMetadataTab
    {
        public static void Draw(WiseTwinEditorData data)
        {
            // Basic Settings
            EditorGUILayout.LabelField("📋 Basic Configuration", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("box");

            // Scene ID (read-only, based on active scene)
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.TextField("Scene Name", data.sceneId);
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.HelpBox(data.isMultiScene
                ? $"Metadata will be saved as: {data.MetadataFileScene}-metadata.json (single file of the multi-scene training)"
                : $"Metadata will be saved as: {data.MetadataFileScene}-metadata.json", MessageType.Info);

            // Title
            EditorGUILayout.Space(5);
            data.projectTitle = EditorGUILayout.TextField("Title", data.projectTitle);

            // Description
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Description", EditorStyles.miniLabel);
            data.projectDescription = EditorGUILayout.TextArea(data.projectDescription, GUILayout.Height(60));

            data.projectVersion = EditorGUILayout.TextField("Version", data.projectVersion);

            // Duration in minutes (numeric field)
            EditorGUILayout.BeginHorizontal();
            data.durationMinutes = EditorGUILayout.IntField("Duration (minutes)", data.durationMinutes);
            EditorGUILayout.LabelField($"→ {data.durationMinutes} minutes", EditorStyles.miniLabel, GUILayout.Width(100));
            EditorGUILayout.EndHorizontal();

            // Difficulty (dropdown)
            data.difficultyIndex = EditorGUILayout.Popup("Difficulty", data.difficultyIndex, data.difficultyOptions);

            // Language (mono-language per build). Each build has a single language;
            // for multilingual training, duplicate the build and translate the copy.
            data.languageIndex = EditorGUILayout.Popup("Language", data.languageIndex, data.languageOptions);

            data.imageUrl = EditorGUILayout.TextField("Image URL", data.imageUrl);

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space();

            // Advanced Settings
            EditorGUILayout.LabelField("⚙️ Advanced Settings", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("box");

            DrawStringList("🏷️ Tags", data.tags);

            EditorGUILayout.EndVertical();
        }

        private static void DrawStringList(string label, List<string> list)
        {
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);

            for (int i = 0; i < list.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                list[i] = EditorGUILayout.TextField($"  [{i}]", list[i]);
                if (GUILayout.Button("❌", GUILayout.Width(25)))
                {
                    list.RemoveAt(i);
                    break;
                }
                EditorGUILayout.EndHorizontal();
            }

            if (GUILayout.Button($"➕ Add {label}"))
            {
                list.Add("");
            }

            EditorGUILayout.Space();
        }
    }
}

#endif
