using UnityEngine;
using UnityEditor;

#if UNITY_EDITOR

namespace WiseTwin.Editor
{
    /// <summary>
    /// General Settings tab for WiseTwinEditor
    /// </summary>
    public static class WiseTwinEditorGeneralTab
    {
        public static void Draw(WiseTwinEditorData data, EditorWindow window)
        {
            EditorGUILayout.LabelField("🔧 General Settings", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("box");

            // Environment Mode
            EditorGUILayout.LabelField("Environment Configuration", EditorStyles.boldLabel);
            bool newUseLocalMode = EditorGUILayout.Toggle("Use Local Mode", data.useLocalMode);
            if (newUseLocalMode != data.useLocalMode)
            {
                data.useLocalMode = newUseLocalMode;
                EditorUtility.SetDirty(window);
                // Appliquer immédiatement au WiseTwinManager dans la scène
                ApplyLocalModeToManager(data.useLocalMode);
            }

            EditorGUILayout.HelpBox(
                data.useLocalMode ?
                "🏠 Local Mode: Will load metadata from StreamingAssets folder\n⚠️ Les changements sont appliqués automatiquement à la scène" :
                "☁️ Production Mode: Will load metadata from Azure API\n✅ Les changements sont appliqués automatiquement à la scène",
                MessageType.Info);

            // Afficher l'état actuel du WiseTwinManager
            WiseTwin.WiseTwinManager currentManager = Object.FindFirstObjectByType<WiseTwin.WiseTwinManager>();
            if (currentManager != null)
            {
                bool currentProdMode = currentManager.IsProductionMode();
                if (currentProdMode == data.useLocalMode) // Si désynchronisé
                {
                    EditorGUILayout.HelpBox(
                        "⚠️ Synchronisation en cours avec WiseTwinManager...",
                        MessageType.Warning);
                }
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "⚠️ WiseTwinManager non trouvé dans la scène. Ajoutez-le via 'Setup Scene' ci-dessous.",
                    MessageType.Warning);
            }

            EditorGUILayout.Space(10);

            // Bouton pour appliquer les settings aux GameObjects de la scène
            GUI.backgroundColor = new Color(0.2f, 0.8f, 0.5f);
            if (GUILayout.Button("🔧 Apply Settings to Scene Objects", GUILayout.Height(30)))
            {
                ApplySettingsToScene(data);
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.Space(10);

            // Azure Configuration (only show if not in local mode)
            if (!data.useLocalMode)
            {
                EditorGUILayout.LabelField("☁️ Azure Configuration", EditorStyles.boldLabel);

                // Toggle pour choisir entre API et Azure Storage direct
                data.useAzureStorageDirect = EditorGUILayout.Toggle("Use Azure Storage Direct", data.useAzureStorageDirect);

                if (data.useAzureStorageDirect)
                {
                    EditorGUILayout.HelpBox("☁️ Direct Azure Storage access (bypass API)", MessageType.Info);
                    data.azureStorageUrl = EditorGUILayout.TextField("Storage URL", data.azureStorageUrl);
                    if (GUILayout.Button("Example: https://yourstorage.blob.core.windows.net/", EditorStyles.miniLabel))
                    {
                        data.azureStorageUrl = "https://yourstorage.blob.core.windows.net/";
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox("🌐 Using Next.js API endpoint", MessageType.Info);
                    data.azureApiUrl = EditorGUILayout.TextField("API Base URL", data.azureApiUrl);
                }

                data.containerId = EditorGUILayout.TextField("Container ID", data.containerId);
                data.buildType = EditorGUILayout.TextField("Build Type", data.buildType);

                EditorGUILayout.Space(10);
            }

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space();

            // Player Controls
            EditorGUILayout.LabelField("🎮 Player Controls", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("box");

            bool newAllowKeyboard = EditorGUILayout.Toggle("Allow Keyboard (WASD)", data.allowKeyboardControl);
            bool newAllowMouse = EditorGUILayout.Toggle("Allow Mouse (click-to-move)", data.allowMouseControl);

            if (newAllowKeyboard != data.allowKeyboardControl || newAllowMouse != data.allowMouseControl)
            {
                data.allowKeyboardControl = newAllowKeyboard;
                data.allowMouseControl = newAllowMouse;
                EditorUtility.SetDirty(window);
                ApplyControlModesToManager(data.allowKeyboardControl, data.allowMouseControl);
            }

            string controlsHelp;
            if (data.allowKeyboardControl && data.allowMouseControl)
                controlsHelp = "🎮 Both modes: the player is asked to choose keyboard or mouse at the start of the training (tutorial choice).";
            else if (data.allowKeyboardControl)
                controlsHelp = "⌨️ Keyboard only: no choice UI — WASD navigation is applied automatically.";
            else if (data.allowMouseControl)
                controlsHelp = "🖱️ Mouse only: no choice UI — click-to-move navigation is applied automatically.";
            else
                controlsHelp = "🚫 None: WiseTwin does NOT manage player controls. The host Unity project is responsible for the player controller (disable WiseTwin's FirstPersonCharacter if unused).";

            EditorGUILayout.HelpBox(controlsHelp, MessageType.Info);

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space();

            // Project Information
            EditorGUILayout.LabelField("📋 Project Information", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("box");

            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.TextField("Scene Name", data.sceneId);
            EditorGUILayout.TextField("Company Name", Application.companyName);
            EditorGUILayout.TextField("Unity Version", Application.unityVersion);
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space();
        }

        private static void ApplyLocalModeToManager(bool useLocalMode)
        {
            WiseTwin.WiseTwinManager manager = Object.FindFirstObjectByType<WiseTwin.WiseTwinManager>();
            if (manager != null)
            {
                // Appliquer le mode Production/Local
                SerializedObject managerSO = new SerializedObject(manager);
                SerializedProperty prodModeProp = managerSO.FindProperty("useProductionMode");
                if (prodModeProp != null)
                {
                    prodModeProp.boolValue = !useLocalMode;  // Inverser car useLocalMode est l'opposé de useProductionMode
                    managerSO.ApplyModifiedProperties();
                    EditorUtility.SetDirty(manager);

                    // Marquer la scène comme modifiée pour forcer la sauvegarde
                    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);

                    Debug.Log($"✅ WiseTwinManager: Mode {(useLocalMode ? "Local" : "Production")} appliqué automatiquement");
                }
            }
            else
            {
                Debug.LogWarning("❌ WiseTwinManager not found in scene!");
            }
        }

        private static void ApplyControlModesToManager(bool allowKeyboard, bool allowMouse)
        {
            WiseTwin.WiseTwinManager manager = Object.FindFirstObjectByType<WiseTwin.WiseTwinManager>();
            if (manager != null)
            {
                SerializedObject managerSO = new SerializedObject(manager);
                // Serialized as inverted "disable" flags (see WiseTwinManager)
                SerializedProperty kbProp = managerSO.FindProperty("disableKeyboardControl");
                SerializedProperty mouseProp = managerSO.FindProperty("disableMouseControl");
                if (kbProp != null && mouseProp != null)
                {
                    kbProp.boolValue = !allowKeyboard;
                    mouseProp.boolValue = !allowMouse;
                    managerSO.ApplyModifiedProperties();
                    EditorUtility.SetDirty(manager);
                    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);

                    Debug.Log($"✅ WiseTwinManager: Control modes appliqués (keyboard={allowKeyboard}, mouse={allowMouse})");
                }
            }
            else
            {
                Debug.LogWarning("❌ WiseTwinManager not found in scene!");
            }
        }

        private static void ApplySettingsToScene(WiseTwinEditorData data)
        {
            // Appliquer le mode local/production
            ApplyLocalModeToManager(data.useLocalMode);

            // Appliquer les modes de contrôle joueur
            ApplyControlModesToManager(data.allowKeyboardControl, data.allowMouseControl);

            // Chercher le MetadataLoader dans la scène
            MetadataLoader loader = Object.FindFirstObjectByType<MetadataLoader>();
            if (loader != null)
            {
                // Appliquer les paramètres API
                loader.useAzureStorageDirect = data.useAzureStorageDirect;
                loader.azureStorageUrl = data.azureStorageUrl;
                loader.apiBaseUrl = data.azureApiUrl;
                loader.containerId = data.containerId;
                loader.buildType = data.buildType;

                Debug.Log($"✅ MetadataLoader configured:");
                Debug.Log($"   - Mode: {(data.useLocalMode ? "Local" : "Production")}");
                Debug.Log($"   - Azure Direct: {data.useAzureStorageDirect}");
            }

            EditorUtility.DisplayDialog(
                "Settings Applied",
                "Configuration applied to scene GameObjects successfully!",
                "OK");
        }
    }
}

#endif
