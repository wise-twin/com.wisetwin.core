using System;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR

namespace WiseTwin.Editor
{
    /// <summary>
    /// Shared data container for WiseTwinEditor
    /// All editor tabs access this data
    /// </summary>
    [Serializable]
    public class WiseTwinEditorData
    {
        // ============= GENERAL SETTINGS =============
        public bool useLocalMode = true;
        public bool useAzureStorageDirect = false;
        public string azureStorageUrl = "https://yourstorage.blob.core.windows.net/";
        public string azureApiUrl = "https://your-domain.com/api/unity/metadata";
        public string containerId = "";
        public string buildType = "wisetrainer";

        // Player control modes offered at training start
        public bool allowKeyboardControl = true;
        public bool allowMouseControl = true;

        // ============= METADATA CONFIG =============
        public string projectTitle = "Training Test";
        public string projectDescription = "Training description";
        public string projectVersion = "1.0.0";
        public int durationMinutes = 30;
        public int difficultyIndex = 1;
        public int languageIndex = 0; // index dans languageOptions ci-dessous
        public string imageUrl = "";
        public List<string> tags = new List<string> { "training", "interactive" };
        public bool includeTimestamp = true;

        // Unity content (deprecated but kept for backwards compatibility)
        public string unityContentJSON = "";
        public bool isUnityContentValid = true;

        // ============= SCENARIO CONFIGURATION =============
        public List<ScenarioConfiguration> scenarios = new List<ScenarioConfiguration>();
        public int selectedScenarioIndex = -1;
        public int draggingScenarioIndex = -1; // For drag and drop reordering

        // ============= DIALOGUES =============
        public List<DialogueScenarioData> dialogues = new List<DialogueScenarioData>();
        public int selectedDialogueIndex = -1;

        // ============= VIDEO TRIGGERS =============
        public List<VideoTriggerConfiguration> videoTriggers = new List<VideoTriggerConfiguration>();
        public int selectedVideoTriggerIndex = -1;

        // ============= UI STATE =============
        public Vector2 scrollPosition;
        public Vector2 unityContentScrollPosition;
        public bool hasLoadedExistingJSON = false;
        public string currentLoadedFile = "";
        public string sceneId = "";

        // Difficulty options (en français)
        public readonly string[] difficultyOptions = { "Facile", "Intermédiaire", "Avancé", "Expert" };

        // Language options : code ISO 639-1. Couverture Europe élargie (industrie B2B).
        // Ordre : langues principales d'abord, puis pays nordiques, puis Europe centrale.
        public readonly string[] languageOptions = {
            "fr", "en", "es", "de", "it", "pt", "nl",
            "sv", "da", "no", "fi",
            "pl", "cs", "ro", "hu"
        };

        public WiseTwinEditorData()
        {
            scenarios = new List<ScenarioConfiguration>();
            dialogues = new List<DialogueScenarioData>();
            videoTriggers = new List<VideoTriggerConfiguration>();
            tags = new List<string> { "training", "interactive" };
        }
    }
}

#endif
