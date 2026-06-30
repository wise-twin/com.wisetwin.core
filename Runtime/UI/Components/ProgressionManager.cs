using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;
using System;
using WiseTwin.UI;

namespace WiseTwin
{
    /// <summary>
    /// Manages progression through training scenarios
    /// Displays scenarios sequentially from metadata.json and handles completion
    /// NEW SYSTEM: Works directly with metadata scenarios (no more InteractableObjects)
    /// </summary>
    public class ProgressionManager : MonoBehaviour
    {
        [Header("Configuration")]
        [Tooltip("Auto-start the first scenario when training begins")]
        [SerializeField] private bool autoStartFirstScenario = true;

        [Tooltip("Reset progression on Start")]
        [SerializeField] private bool resetOnStart = true;

        [Header("Debug")]
        [SerializeField] private bool debugMode = false;

        // Progression state
        private List<ScenarioData> scenarios;
        private int currentScenarioIndex = -1;
        private HashSet<string> completedScenarioIds = new HashSet<string>();
        private Dictionary<string, int> attemptCounts = new Dictionary<string, int>();
        private bool isProgressionActive = false;
        private bool isWaitingForCompletion = false;

        // References
        private MetadataLoader metadataLoader;
        private ContentDisplayManager contentDisplayManager;
        private ScenarioTransitionPanel transitionPanel;

        // Singleton
        public static ProgressionManager Instance { get; private set; }

        // Events
        public event Action<int, ScenarioData> OnScenarioStarted; // index, scenario
        public event Action<int, ScenarioData, bool> OnScenarioCompleted; // index, scenario, success
        public event Action OnAllScenariosCompleted;
        public event Action OnProgressionReset;

        // Public properties
        public int CurrentScenarioIndex => currentScenarioIndex;
        public int TotalScenarios => scenarios?.Count ?? 0;
        public bool IsProgressionActive => isProgressionActive;
        public bool IsWaitingForCompletion => isWaitingForCompletion;
        public float ProgressPercentage => TotalScenarios > 0 ? (float)(currentScenarioIndex + 1) / TotalScenarios * 100f : 0f;
        public ScenarioData CurrentScenario => scenarios != null && currentScenarioIndex >= 0 && currentScenarioIndex < scenarios.Count
            ? scenarios[currentScenarioIndex]
            : null;
        public bool CanMoveToNext => isWaitingForCompletion && !contentDisplayManager.IsDisplaying;

        void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                if (transform.parent == null)
                {
                    DontDestroyOnLoad(gameObject);
                }
            }
            else
            {
                Destroy(gameObject);
                return;
            }
        }

        void Start()
        {
            // Get references
            metadataLoader = MetadataLoader.Instance;
            contentDisplayManager = ContentDisplayManager.Instance;

            if (metadataLoader == null)
            {
                Debug.LogError("[ProgressionManager] MetadataLoader.Instance is null!");
                return;
            }

            if (contentDisplayManager == null)
            {
                Debug.LogError("[ProgressionManager] ContentDisplayManager.Instance is null!");
                return;
            }

            // Subscribe to content completed event
            contentDisplayManager.OnContentCompleted += HandleContentCompleted;

            if (resetOnStart)
            {
                // Wait for metadata to be loaded before starting
                if (metadataLoader.IsLoaded)
                {
                    InitializeProgression();
                }
                else
                {
                    // Wait for metadata to load
                    metadataLoader.OnMetadataLoaded += OnMetadataLoaded;
                    if (debugMode) Debug.Log("[ProgressionManager] Waiting for metadata to load...");
                }
            }
        }

        void OnDestroy()
        {
            if (contentDisplayManager != null)
            {
                contentDisplayManager.OnContentCompleted -= HandleContentCompleted;
            }

            if (metadataLoader != null)
            {
                metadataLoader.OnMetadataLoaded -= OnMetadataLoaded;
            }

            if (transitionPanel != null)
            {
                transitionPanel.OnActionButtonClicked -= OnTransitionPanelClicked;
            }

            if (Instance == this)
            {
                Instance = null;
            }
        }

        void OnMetadataLoaded(Dictionary<string, object> metadata)
        {
            InitializeProgression();
        }

        /// <summary>
        /// Initialize progression with scenarios from metadata
        /// </summary>
        void InitializeProgression()
        {
            // Load scenarios from metadata
            scenarios = metadataLoader.GetScenarios();

            if (scenarios == null || scenarios.Count == 0)
            {
                Debug.LogError("[ProgressionManager] No scenarios found in metadata!");
                return;
            }

            if (debugMode) Debug.Log($"[ProgressionManager] Loaded {scenarios.Count} scenarios from metadata");

            EnsureTransitionPanel();

            if (autoStartFirstScenario)
            {
                // Plus d'écran d'accueil/tutoriel Unity : l'intro (présentation de
                // la formation) est désormais affichée côté SaaS. On enchaîne
                // directement sur le premier scénario.
                StartTrainingDirectly();
            }
            else
            {
                if (debugMode) Debug.Log("[ProgressionManager] Waiting for external trigger");
            }
        }

        /// <summary>
        /// Lance directement la formation : affiche le HUD, active les contrôles
        /// joueur et démarre le premier scénario. L'écran d'accueil/tutoriel
        /// Unity a été retiré — la présentation de la formation est désormais
        /// affichée côté SaaS (React), et le mode de contrôle (clavier/souris)
        /// est géré par la scène de la formation, plus par le package.
        /// </summary>
        void StartTrainingDirectly()
        {
            // Show the training HUD and hand over to the progression
            if (TrainingHUD.Instance != null)
            {
                TrainingHUD.Instance.InitializeForScenarios();
                TrainingHUD.Instance.Show();
            }
            PlayerControls.SetEnabled(true);

            StartProgression();
        }

        /// <summary>
        /// Start or restart the scenario progression
        /// </summary>
        public void StartProgression()
        {
            if (scenarios == null || scenarios.Count == 0)
            {
                Debug.LogError("[ProgressionManager] Cannot start progression: no scenarios loaded");
                return;
            }

            // Reset state
            currentScenarioIndex = -1;
            completedScenarioIds.Clear();
            attemptCounts.Clear();
            isProgressionActive = true;
            isWaitingForCompletion = false;

            OnProgressionReset?.Invoke();

            if (debugMode) Debug.Log("[ProgressionManager] Progression started");

            // Reset HUD progress
            if (TrainingHUD.Instance != null)
            {
                TrainingHUD.Instance.UpdateProgress(0);
            }

            EnsureTransitionPanel();
            MoveToNextScenario();
        }

        /// <summary>
        /// Display the current scenario
        /// </summary>
        void StartCurrentScenario()
        {
            if (currentScenarioIndex < 0 || currentScenarioIndex >= scenarios.Count)
            {
                Debug.LogError($"[ProgressionManager] Invalid scenario index: {currentScenarioIndex}");
                return;
            }

            var scenario = scenarios[currentScenarioIndex];
            isWaitingForCompletion = false;

            if (debugMode) Debug.Log($"[ProgressionManager] Starting scenario {currentScenarioIndex + 1}/{scenarios.Count}: {scenario.id} ({scenario.type})");

            // Update HUD
            if (TrainingHUD.Instance != null)
            {
                TrainingHUD.Instance.OnScenarioStarted();
                TrainingHUD.Instance.SetScenarioTitle(GetScenarioDisplayTitle(scenario));
            }

            // Display the scenario
            contentDisplayManager.DisplayScenario(scenario);

            // Trigger event
            OnScenarioStarted?.Invoke(currentScenarioIndex, scenario);
            WiseTwinAPI.RaiseScenarioStarted(currentScenarioIndex, scenario);
        }

        /// <summary>
        /// Move to the next scenario (called by button or auto-progression)
        /// </summary>
        public void MoveToNextScenario()
        {
            // If progression not active, check if this is the first click to start
            if (!isProgressionActive)
            {
                if (currentScenarioIndex == -1)
                {
                    // First click - start the progression
                    if (debugMode) Debug.Log("[ProgressionManager] Starting progression from first button click");
                    StartProgression();
                    return;
                }
                else
                {
                    if (debugMode) Debug.LogWarning("[ProgressionManager] Progression is not active");
                    return;
                }
            }

            // If currently displaying content and waiting for completion, ignore
            if (contentDisplayManager.IsDisplaying && !isWaitingForCompletion)
            {
                if (debugMode) Debug.LogWarning("[ProgressionManager] Cannot move to next: current scenario not completed");
                return;
            }

            currentScenarioIndex++;

            // Check if we've completed all scenarios
            if (currentScenarioIndex >= scenarios.Count)
            {
                CompleteProgression();
                return;
            }

            // Start the next scenario
            StartCurrentScenario();
        }

        /// <summary>
        /// Handle content completion from ContentDisplayManager
        /// </summary>
        void HandleContentCompleted(string scenarioId, bool success)
        {
            if (!isProgressionActive)
            {
                return;
            }

            // Verify this is the current scenario
            var currentScenario = CurrentScenario;
            if (currentScenario == null || currentScenario.id != scenarioId)
            {
                if (debugMode) Debug.LogWarning($"[ProgressionManager] Completed scenario {scenarioId} doesn't match current scenario");
                return;
            }

            if (debugMode) Debug.Log($"[ProgressionManager] Scenario completed: {scenarioId} (success: {success})");

            // Mark as completed
            completedScenarioIds.Add(scenarioId);
            isWaitingForCompletion = true;

            // Track attempts
            if (!attemptCounts.ContainsKey(scenarioId))
            {
                attemptCounts[scenarioId] = 0;
            }
            attemptCounts[scenarioId]++;

            // Trigger event
            OnScenarioCompleted?.Invoke(currentScenarioIndex, currentScenario, success);

            // Check if this was the last scenario
            if (currentScenarioIndex >= scenarios.Count - 1)
            {
                // This was the last scenario - complete the progression
                if (debugMode) Debug.Log("[ProgressionManager] Last scenario completed - finishing progression");
                CompleteProgression();
            }
            else
            {
                // Incrémenter la progression dans le HUD
                if (TrainingHUD.Instance != null)
                {
                    TrainingHUD.Instance.OnScenarioCompleted();
                    TrainingHUD.Instance.SetNextButtonVisible(false);
                }

                // Afficher le panneau de transition avec le nom du prochain scénario
                if (transitionPanel != null)
                {
                    int nextIndex = currentScenarioIndex + 1;
                    string nextScenarioName = (nextIndex >= 0 && nextIndex < scenarios.Count) ? scenarios[nextIndex].id : "";
                    transitionPanel.ShowTransitionPanel(currentScenarioIndex, scenarios.Count, nextScenarioName);
                    if (debugMode) Debug.Log($"[ProgressionManager] Showing transition panel: scenario {currentScenarioIndex + 1}/{scenarios.Count} → {nextScenarioName}");
                }
            }
        }

        /// <summary>
        /// Complete the entire progression
        /// </summary>
        void CompleteProgression()
        {
            isProgressionActive = false;
            isWaitingForCompletion = false;

            if (debugMode) Debug.Log("[ProgressionManager] All scenarios completed!");

            OnAllScenariosCompleted?.Invoke();

            // Notify TrainingHUD
            if (TrainingHUD.Instance != null)
            {
                TrainingHUD.Instance.OnAllScenariosCompleted();
            }
        }

        /// <summary>
        /// Get scenario by index
        /// </summary>
        public ScenarioData GetScenario(int index)
        {
            if (scenarios == null || index < 0 || index >= scenarios.Count)
            {
                return null;
            }
            return scenarios[index];
        }

        /// <summary>
        /// Check if a scenario is completed
        /// </summary>
        public bool IsScenarioCompleted(string scenarioId)
        {
            return completedScenarioIds.Contains(scenarioId);
        }

        /// <summary>
        /// Get attempt count for a scenario
        /// </summary>
        public int GetAttemptCount(string scenarioId)
        {
            return attemptCounts.ContainsKey(scenarioId) ? attemptCounts[scenarioId] : 0;
        }

        /// <summary>
        /// Reset progression (can be called manually)
        /// </summary>
        public void ResetProgression()
        {
            if (debugMode) Debug.Log("[ProgressionManager] Resetting progression");
            StartProgression();
        }

        /// <summary>
        /// Extract a display title from a scenario's content data.
        /// For question scenarios, always returns scenario.id (questions have no meaningful title).
        /// Accepts flat strings (mono-language) and legacy {en, fr} objects for other types.
        /// </summary>
        string GetScenarioDisplayTitle(ScenarioData scenario)
        {
            if (scenario == null) return "";

            // Questions: HUD shows the scenario id (questionText is too long/noisy)
            if (scenario.type == "question") return scenario.id;

            var content = scenario.GetContentData();
            if (content == null) return scenario.id;

            var titleToken = content["title"];
            string title = LocalizedValueReader.Flatten(titleToken);
            if (!string.IsNullOrEmpty(title)) return title;

            return scenario.id;
        }

        #region Transition Panel

        /// <summary>
        /// Crée le panneau de transition s'il n'existe pas encore
        /// </summary>
        void EnsureTransitionPanel()
        {
            if (transitionPanel != null) return;

            // Vérifier s'il existe déjà dans la scène
            transitionPanel = ScenarioTransitionPanel.Instance;
            if (transitionPanel != null)
            {
                transitionPanel.OnActionButtonClicked += OnTransitionPanelClicked;
                return;
            }

            // Créer le GameObject avec UIDocument
            var panelGO = new GameObject("ScenarioTransitionPanel");
            transitionPanel = panelGO.AddComponent<ScenarioTransitionPanel>();

            // Assigner le PanelSettings depuis le TrainingHUD s'il existe
            var hudDoc = TrainingHUD.Instance?.GetComponent<UIDocument>();
            if (hudDoc != null && hudDoc.panelSettings != null)
            {
                transitionPanel.SetPanelSettings(hudDoc.panelSettings);
                if (debugMode) Debug.Log("[ProgressionManager] PanelSettings assigned to ScenarioTransitionPanel from TrainingHUD");
            }

            transitionPanel.OnActionButtonClicked += OnTransitionPanelClicked;

            if (debugMode) Debug.Log("[ProgressionManager] ScenarioTransitionPanel created");
        }

        /// <summary>
        /// Appelé quand l'utilisateur clique sur le bouton du panneau de transition
        /// </summary>
        void OnTransitionPanelClicked()
        {
            if (transitionPanel != null)
            {
                transitionPanel.Hide();
            }

            if (debugMode) Debug.Log("[ProgressionManager] Transition panel clicked - moving to next scenario");

            MoveToNextScenario();
        }

        #endregion

        #region Editor Helpers

        [ContextMenu("Start Progression")]
        private void ContextMenu_StartProgression()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[ProgressionManager] Can only start progression in Play Mode");
                return;
            }
            StartProgression();
        }

        [ContextMenu("Reset Progression")]
        private void ContextMenu_ResetProgression()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[ProgressionManager] Can only reset progression in Play Mode");
                return;
            }
            ResetProgression();
        }

        [ContextMenu("Next Scenario")]
        private void ContextMenu_NextScenario()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[ProgressionManager] Can only move to next scenario in Play Mode");
                return;
            }
            MoveToNextScenario();
        }

        [ContextMenu("Show Current Status")]
        private void ContextMenu_ShowStatus()
        {
            Debug.Log($"[ProgressionManager] Status:\n" +
                     $"  Active: {isProgressionActive}\n" +
                     $"  Current: {currentScenarioIndex + 1}/{TotalScenarios}\n" +
                     $"  Waiting: {isWaitingForCompletion}\n" +
                     $"  Progress: {ProgressPercentage:F1}%\n" +
                     $"  Completed: {completedScenarioIds.Count}");
        }

        #endregion
    }
}
