using UnityEngine;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

namespace WiseTwin
{
    /// <summary>
    /// Main entry point for WiseTwin training system.
    /// Centralizes access to MetadataLoader and TrainingCompletionNotifier.
    /// </summary>
    public class WiseTwinManager : MonoBehaviour
    {
        [Header("🎯 WiseTwin Manager")]
        [SerializeField, Tooltip("Enable debug logs for this component")]
        private bool enableDebugLogs = true;
        [SerializeField, Tooltip("Log prefix for easy filtering")]
        private string logPrefix = "[WiseTwinManager]";
        
        [SerializeField, Tooltip("Use production mode (Azure API + web notifications)")]
        private bool useProductionMode = false;

        [Header("🎮 Player Controls")]
        // Stored as "disable" flags (default false = enabled) so that adding these fields to an
        // already-serialized scene/prefab keeps both modes ON (Unity deserializes missing bools as
        // false). The editor window exposes them as positive "Allow ..." toggles.
        [SerializeField, Tooltip("Disable keyboard + mouse (WASD) navigation offered at training start")]
        private bool disableKeyboardControl = false;
        [SerializeField, Tooltip("Disable mouse-only (click-to-move) navigation offered at training start")]
        private bool disableMouseControl = false;

        [Header("📋 References")]
        [SerializeField, Tooltip("MetadataLoader component (auto-found if empty)")]
        private MetadataLoader metadataLoader;
        
        [SerializeField, Tooltip("TrainingCompletionNotifier component (auto-found if empty)")]
        private TrainingCompletionNotifier completionNotifier;
        
        
        // Singleton
        public static WiseTwinManager Instance { get; private set; }
        
        // Public Properties
        public MetadataLoader MetadataLoader => metadataLoader;
        public TrainingCompletionNotifier CompletionNotifier => completionNotifier;
        
        // Public Properties for settings
        public bool EnableDebugLogs => enableDebugLogs;
        public bool IsProductionMode() => useProductionMode;

        // Control modes offered at training start (drive the tutorial choice / auto-apply)
        public bool AllowKeyboardControl => !disableKeyboardControl;
        public bool AllowMouseControl => !disableMouseControl;
        
        // Quick access properties
        public bool IsMetadataLoaded => metadataLoader != null && metadataLoader.IsLoaded;
        public string SceneName => GetSceneName();
        
        // Events
        public System.Action<Dictionary<string, object>> OnMetadataReady;
        public System.Action<string> OnMetadataError;
        public System.Action OnTrainingCompleted;

        // Player spawn tracking
        private Vector3 initialPlayerPosition;
        private Quaternion initialPlayerRotation;
        private bool playerSpawnPositionSaved = false;
        
        void Awake()
        {
            // Singleton setup
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
                InitializeComponents();
                EnsureAnalyticsInstance();
            }
            else
            {
                DebugLog("WiseTwinManager instance already exists. Destroying duplicate.");
                Destroy(gameObject);
            }
        }

        void EnsureAnalyticsInstance()
        {
            if (Analytics.TrainingAnalytics.Instance != null) return;

            var analyticsGO = new GameObject("TrainingAnalytics");
            analyticsGO.transform.SetParent(transform);
            analyticsGO.AddComponent<Analytics.TrainingAnalytics>();
            DebugLog("✅ TrainingAnalytics created");
        }
        
        void Start()
        {
            DebugLog("🎯 WiseTwin Manager initialized successfully");

            // Save player's initial spawn position
            SavePlayerSpawnPosition();
        }
        
        void InitializeComponents()
        {
            DebugLog("🔍 Searching for WiseTwin components...");
            
            // Find MetadataLoader
            if (metadataLoader == null)
            {
                metadataLoader = FindFirstObjectByType<MetadataLoader>();
            }

            // Subscribe to events early (in Awake) to catch metadata loaded event
            if (metadataLoader != null)
            {
                DebugLog("✅ MetadataLoader found and linked");
                metadataLoader.OnMetadataLoaded += OnMetadataLoaded;
                metadataLoader.OnLoadError += OnMetadataLoadError;
            }
            else
            {
                DebugLog("⚠️ MetadataLoader not found in scene");
            }
            
            // Find TrainingCompletionNotifier
            if (completionNotifier == null)
            {
                completionNotifier = FindFirstObjectByType<TrainingCompletionNotifier>();
                if (completionNotifier != null)
                {
                    DebugLog("✅ TrainingCompletionNotifier found and linked");
                }
                else
                {
                    DebugLog("⚠️ TrainingCompletionNotifier not found in scene");
                }
            }
            
            // Update component settings
            UpdateComponentSettings();
        }
        
        void OnMetadataLoaded(Dictionary<string, object> metadata)
        {
            var scenarios = metadataLoader?.GetScenarios();
            int count = scenarios?.Count ?? 0;
            DebugLog($"📦 Metadata loaded successfully. Scenarios: {count}");

            // Initialize video triggers if present
            InitializeVideoTriggers();

            OnMetadataReady?.Invoke(metadata);
        }

        void InitializeVideoTriggers()
        {
            if (metadataLoader == null || !metadataLoader.HasVideoTriggers())
            {
                return;
            }

            DebugLog("🎬 Initializing video triggers...");

            // Create VideoDisplayer if not exists
            if (VideoDisplayer.Instance == null)
            {
                var displayerGO = new GameObject("VideoDisplayer");
                displayerGO.transform.SetParent(transform);
                displayerGO.AddComponent<VideoDisplayer>();
                DebugLog("✅ VideoDisplayer created");
            }
            else
            {
                DebugLog("✅ VideoDisplayer already exists");
            }

            // Create VideoTriggerManager if not exists
            if (VideoTriggerManager.Instance == null)
            {
                var managerGO = new GameObject("VideoTriggerManager");
                managerGO.transform.SetParent(transform);
                managerGO.AddComponent<VideoTriggerManager>();
                DebugLog("✅ VideoTriggerManager created");
            }

            // Initialize triggers from metadata
            VideoTriggerManager.Instance?.InitializeFromMetadata(metadataLoader.GetVideoTriggers());
        }
        
        void OnMetadataLoadError(string error)
        {
            DebugLog($"❌ Metadata load error: {error}");
            OnMetadataError?.Invoke(error);
        }
        
        #region Public API - Easy access methods
        
        /// <summary>
        /// Get data for a specific Unity object (Legacy - use scenario-based system instead)
        /// </summary>
        /// <param name="objectId">Unity object identifier</param>
        /// <returns>Object data dictionary or null if not found</returns>
        [System.Obsolete("This method is for legacy InteractableObject system. Use ProgressionManager with scenario-based metadata instead.")]
        public Dictionary<string, object> GetDataForObject(string objectId)
        {
            if (metadataLoader == null)
            {
                DebugLog($"❌ Cannot get data for '{objectId}': MetadataLoader not available");
                return null;
            }
            
            return metadataLoader.GetDataForObject(objectId);
        }
        
        /// <summary>
        /// Get typed content for a specific Unity object (Legacy - use scenario-based system instead)
        /// </summary>
        /// <typeparam name="T">Type to deserialize to</typeparam>
        /// <param name="objectId">Unity object identifier</param>
        /// <param name="contentKey">Optional content key within object</param>
        /// <returns>Typed content or null if not found</returns>
        [System.Obsolete("This method is for legacy InteractableObject system. Use ProgressionManager with scenario-based metadata instead.")]
        public T GetContentForObject<T>(string objectId, string contentKey = null) where T : class
        {
            if (metadataLoader == null)
            {
                DebugLog($"❌ Cannot get content for '{objectId}': MetadataLoader not available");
                return null;
            }
            
            return metadataLoader.GetContentForObject<T>(objectId, contentKey);
        }
        
        /// <summary>
        /// Get all available Unity object IDs (Legacy - use scenario-based system instead)
        /// </summary>
        /// <returns>List of object identifiers</returns>
        [System.Obsolete("This method is for legacy InteractableObject system. Use MetadataLoader.GetScenarios() for scenario-based training instead.")]
        public List<string> GetAvailableObjectIds()
        {
            if (metadataLoader == null)
            {
                DebugLog("❌ Cannot get object IDs: MetadataLoader not available");
                return new List<string>();
            }
            
            return metadataLoader.GetAvailableObjectIds();
        }
        
        /// <summary>
        /// Get project metadata information
        /// </summary>
        /// <param name="key">Metadata key (title, description, version, etc.)</param>
        /// <returns>Metadata value or empty string</returns>
        public string GetProjectInfo(string key)
        {
            if (metadataLoader == null)
            {
                DebugLog($"❌ Cannot get project info '{key}': MetadataLoader not available");
                return "";
            }
            
            return metadataLoader.GetProjectInfo(key);
        }
        
        /// <summary>
        /// Complete the training: show the completion screen (with score + analytics) and
        /// notify the web application. Safe to call from external scripts via WiseTwinAPI.
        ///
        /// The completion screen handles the analytics export and the WebGL bridge call
        /// internally (TrainingCompletionUI.NotifyTrainingCompletion). When the screen
        /// can't be shown (no UIDocument / panel settings available), we fall back to a
        /// direct notification so the SaaS still receives the completion event.
        /// </summary>
        /// <param name="trainingName">Optional training name</param>
        public void CompleteTraining(string trainingName = null)
        {
            DebugLog($"🎉 Training completed: {trainingName ?? SceneName}");

            var completionUI = EnsureCompletionUI();
            if (completionUI != null)
            {
                float elapsed = Analytics.TrainingAnalytics.Instance != null
                    ? Analytics.TrainingAnalytics.Instance.SessionDurationSeconds
                    : 0f;
                int modules = Analytics.TrainingAnalytics.Instance != null
                    ? Analytics.TrainingAnalytics.Instance.GetTotalInteractions()
                    : 0;
                completionUI.ShowCompletionScreen(elapsed, modules);
            }
            else if (completionNotifier != null)
            {
                completionNotifier.FormationCompleted(trainingName);
            }
            else
            {
                DebugLog("⚠️ Cannot complete training: no UI nor completion notifier available");
            }

            OnTrainingCompleted?.Invoke();
            WiseTwinAPI.RaiseTrainingCompleted();
        }

        /// <summary>
        /// Find or create the TrainingCompletionUI instance. Borrows the panel settings
        /// from the TrainingHUD UIDocument so the completion screen renders correctly.
        /// </summary>
        UI.TrainingCompletionUI EnsureCompletionUI()
        {
            var existing = UI.TrainingCompletionUI.Instance;
            if (existing != null) return existing;

            // Need panel settings from somewhere — try TrainingHUD, then any UIDocument in the scene
            UnityEngine.UIElements.PanelSettings panelSettings = null;
            var hud = TrainingHUD.Instance;
            if (hud != null)
            {
                var hudDoc = hud.GetComponent<UnityEngine.UIElements.UIDocument>();
                if (hudDoc != null) panelSettings = hudDoc.panelSettings;
            }
            if (panelSettings == null)
            {
                var anyDoc = FindFirstObjectByType<UnityEngine.UIElements.UIDocument>();
                if (anyDoc != null) panelSettings = anyDoc.panelSettings;
            }
            if (panelSettings == null)
            {
                DebugLog("⚠️ Cannot create TrainingCompletionUI: no PanelSettings found in scene");
                return null;
            }

            var go = new GameObject("TrainingCompletionUI");
            go.transform.SetParent(transform);
            var ui = go.AddComponent<UI.TrainingCompletionUI>();
            var doc = go.AddComponent<UnityEngine.UIElements.UIDocument>();
            doc.panelSettings = panelSettings;
            return ui;
        }
        
        /// <summary>
        /// Test training completion (development only)
        /// </summary>
        public void TestCompletion()
        {
            if (completionNotifier != null)
            {
                completionNotifier.TestCompletion();
            }
            else
            {
                DebugLog("❌ Cannot test completion: TrainingCompletionNotifier not available");
            }
        }
        
        /// <summary>
        /// Reload metadata from source
        /// </summary>
        public void ReloadMetadata()
        {
            if (metadataLoader == null)
            {
                DebugLog("❌ Cannot reload metadata: MetadataLoader not available");
                return;
            }
            
            DebugLog("🔄 Reloading metadata...");
            metadataLoader.ReloadMetadata();
        }
        
        /// <summary>
        /// Save the player's current position as the spawn point
        /// Called automatically on Start, but can be called manually to update
        /// </summary>
        public void SavePlayerSpawnPosition()
        {
            var player = FindFirstObjectByType<FirstPersonCharacter>();
            if (player != null)
            {
                initialPlayerPosition = player.transform.position;
                initialPlayerRotation = player.transform.rotation;
                playerSpawnPositionSaved = true;
                DebugLog($"💾 Player spawn position saved: {initialPlayerPosition}");
            }
            else
            {
                DebugLog("⚠️ Cannot save spawn position: FirstPersonCharacter not found in scene");
            }
        }

        /// <summary>
        /// Reset the player to their initial spawn position
        /// Useful if player gets stuck or wants to restart positioning
        /// </summary>
        public void ResetPlayerPosition()
        {
            if (!playerSpawnPositionSaved)
            {
                DebugLog("⚠️ Cannot reset player: spawn position not saved yet");
                SavePlayerSpawnPosition(); // Try to save it now
                return;
            }

            var player = FindFirstObjectByType<FirstPersonCharacter>();
            if (player != null)
            {
                var characterController = player.GetComponent<CharacterController>();
                if (characterController != null)
                {
                    // Disable CharacterController to teleport properly
                    characterController.enabled = false;
                    player.transform.position = initialPlayerPosition;
                    player.transform.rotation = initialPlayerRotation;
                    characterController.enabled = true;

                    DebugLog($"↻ Player reset to spawn position: {initialPlayerPosition}");
                }
                else
                {
                    // No CharacterController, just move directly
                    player.transform.position = initialPlayerPosition;
                    player.transform.rotation = initialPlayerRotation;
                    DebugLog($"↻ Player reset to spawn position (no CharacterController): {initialPlayerPosition}");
                }
            }
            else
            {
                DebugLog("❌ Cannot reset player: FirstPersonCharacter not found in scene");
            }
        }

        /// <summary>
        /// Fully resets the training: destroys all WiseTwin singletons and reloads the active
        /// scene from scratch. This is the exact same behavior as the red restart button in the
        /// HUD, but WITHOUT the confirmation dialog — call it only when you already have user
        /// intent (or none is required).
        ///
        /// All in-memory state is discarded: analytics session, scenario progression, UI,
        /// player position, control mode. Everything is re-instantiated fresh by the scene reload.
        /// </summary>
        public void RestartTraining()
        {
            DebugLog("↻ Restart training requested - reloading scene");

            ControlModeSettings.Reset();

            // Détruire les singletons standalone (pas sous WiseTwinSystem)
            var transitionPanel = FindFirstObjectByType<ScenarioTransitionPanel>();
            if (transitionPanel != null) Destroy(transitionPanel.gameObject);

            var tutorialUI = FindFirstObjectByType<TutorialUI>();
            if (tutorialUI != null) Destroy(tutorialUI.gameObject);

            // Détruire le HUD s'il est un objet root séparé
            var hud = FindFirstObjectByType<TrainingHUD>();
            if (hud != null) Destroy(hud.transform.root.gameObject);

            int buildIndex = SceneManager.GetActiveScene().buildIndex;

            // Détruire le WiseTwinSystem root en dernier (contient les singletons principaux,
            // dont ce manager) puis recharger la scène.
            Destroy(transform.root.gameObject);

            SceneManager.LoadScene(buildIndex);

            WiseTwinAPI.RaiseTrainingRestarted();
        }

        #endregion

        #region Development Helpers
        
        /// <summary>
        /// Get system status for debugging
        /// </summary>
        /// <returns>Status information</returns>
        public string GetSystemStatus()
        {
            var status = new System.Text.StringBuilder();
            status.AppendLine($"🎯 WiseTwin Manager Status");
            status.AppendLine($"Scene: {SceneName}");
            status.AppendLine($"MetadataLoader: {(metadataLoader != null ? "✅" : "❌")}");
            status.AppendLine($"CompletionNotifier: {(completionNotifier != null ? "✅" : "❌")}");
            status.AppendLine($"Test Mode: {(IsProductionMode() ? "❌ Production" : "✅ Local")}");
            status.AppendLine($"Metadata Loaded: {(IsMetadataLoaded ? "✅" : "❌")}");
            
            if (IsMetadataLoaded)
            {
                var scenarios = metadataLoader.GetScenarios();
                if (scenarios != null && scenarios.Count > 0)
                {
                    status.AppendLine($"Scenarios: {scenarios.Count}");
                    foreach (var scenario in scenarios)
                    {
                        status.AppendLine($"  - {scenario.id} ({scenario.type})");
                    }
                }
                else
                {
                    status.AppendLine("Scenarios: None");
                }
            }
            
            return status.ToString();
        }
        
        /// <summary>
        /// Force component refresh (useful after scene changes)
        /// </summary>
        public void RefreshComponents()
        {
            DebugLog("🔄 Refreshing WiseTwin components...");
            
            // Clear current references
            metadataLoader = null;
            completionNotifier = null;
            
            // Find components again
            InitializeComponents();
            
            // Re-subscribe to events
            if (metadataLoader != null)
            {
                metadataLoader.OnMetadataLoaded += OnMetadataLoaded;
                metadataLoader.OnLoadError += OnMetadataLoadError;
            }
        }
        
        /// <summary>
        /// Get current scene name
        /// </summary>
        /// <returns>Scene name</returns>
        string GetSceneName()
        {
            // Use metadata loader's scene name if available
            if (metadataLoader != null && !string.IsNullOrEmpty(metadataLoader.SceneName))
            {
                return metadataLoader.SceneName;
            }

            // Fallback to current active scene
            string sceneName = SceneManager.GetActiveScene().name;
            return !string.IsNullOrEmpty(sceneName) ? sceneName : "default-scene";
        }
        
        #endregion
        
        /// <summary>
        /// Update settings on all components
        /// </summary>
        void UpdateComponentSettings()
        {
            // Les composants gèrent maintenant leurs propres settings
            // Cette méthode est conservée pour compatibilité future

            if (metadataLoader != null)
            {
                metadataLoader.UpdateSettingsFromManager();
            }
        }
        
        void DebugLog(string message)
        {
            if (enableDebugLogs)
            {
                Debug.Log($"{logPrefix} {message}");
            }
        }

        /// <summary>
        /// Toggle debug logs for this component
        /// </summary>
        public void SetDebugEnabled(bool enabled)
        {
            enableDebugLogs = enabled;
            DebugLog($"Debug logs {(enabled ? "enabled" : "disabled")}");
        }
        
        void OnDestroy()
        {
            // Unsubscribe from events
            if (metadataLoader != null)
            {
                metadataLoader.OnMetadataLoaded -= OnMetadataLoaded;
                metadataLoader.OnLoadError -= OnMetadataLoadError;
            }

            if (Instance == this)
            {
                Instance = null;
            }
        }
        
        #region Inspector GUI (Development)
        
        void OnGUI()
        {
            if (!enableDebugLogs) return;
            
            // Simple debug overlay
            GUILayout.BeginArea(new Rect(10, 10, 300, 150));
            GUILayout.BeginVertical("box");
            
            GUIStyle boldStyle = new GUIStyle(GUI.skin.label);
            boldStyle.fontStyle = FontStyle.Bold;
            
            GUILayout.Label("🎯 WiseTwin Manager", boldStyle);
            GUILayout.Label($"Scene: {SceneName}");
            GUILayout.Label($"Metadata: {(IsMetadataLoaded ? "✅ Loaded" : "❌ Loading...")}");
            
            if (IsMetadataLoaded)
            {
                var scenarios = metadataLoader?.GetScenarios();
                int count = scenarios?.Count ?? 0;
                GUILayout.Label($"Scenarios: {count}");
                GUILayout.Label($"Title: {GetProjectInfo("title")}");
            }
            
            GUILayout.Space(5);
            
            if (GUILayout.Button("🎉 Test Completion"))
            {
                TestCompletion();
            }
            
            if (GUILayout.Button("🔄 Reload Data"))
            {
                ReloadMetadata();
            }
            
            GUILayout.EndVertical();
            GUILayout.EndArea();
        }
        
        #endregion
    }
}