using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;
using System.Collections;
using System.Collections.Generic;
using WiseTwin.UI;

namespace WiseTwin
{
    /// <summary>
    /// HUD minimaliste pour afficher le timer, la progression et le titre du scénario.
    /// Layout: [restart] [scenario title] [progress bar + timer] [help (?)]
    /// </summary>
    public class TrainingHUD : MonoBehaviour
    {
        [Header("Configuration")]
        [SerializeField] private bool showOnStart = false;
        [SerializeField] private float fadeInDuration = 0.5f;

        [Header("Debug")]
        [SerializeField] private bool debugMode = false;

        // UI Elements
        private UIDocument uiDocument;
        private VisualElement root;
        private VisualElement hudContainer;
        private Label timerLabel;
        private Label progressLabel;
        private Label scenarioTitleLabel;
        private VisualElement progressBar;
        private VisualElement progressFill;
        private Button resetButton;
        private VisualElement confirmationOverlay;

        // State
        private float startTime;
        private int currentProgress = 0;
        private int totalObjects = 0;
        private bool isVisible = false;
        private HashSet<string> completedObjects = new HashSet<string>();

        // Singleton
        public static TrainingHUD Instance { get; private set; }

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

            SetupUIDocument();
        }

        void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        void Start()
        {
            // Only auto-show if we actually have scenarios to track. API-only trainings
            // (no scenarios in metadata) shouldn't display the progress bar — there's
            // nothing to progress through.
            if (!showOnStart) return;

            var loader = MetadataLoader.Instance;
            if (loader == null)
            {
                if (debugMode) Debug.Log("[TrainingHUD] MetadataLoader missing — skipping auto-show");
                return;
            }

            if (loader.IsLoaded)
            {
                ShowIfScenariosExist();
            }
            else
            {
                // Defer until metadata loads, then check
                loader.OnMetadataLoaded += HandleMetadataLoadedForAutoShow;
            }
        }

        void HandleMetadataLoadedForAutoShow(Dictionary<string, object> _)
        {
            var loader = MetadataLoader.Instance;
            if (loader != null) loader.OnMetadataLoaded -= HandleMetadataLoadedForAutoShow;
            ShowIfScenariosExist();
        }

        void ShowIfScenariosExist()
        {
            var scenarios = MetadataLoader.Instance?.GetScenarios();
            if (scenarios != null && scenarios.Count > 0)
            {
                Show();
            }
            else if (debugMode)
            {
                Debug.Log("[TrainingHUD] No scenarios in metadata — HUD stays hidden");
            }
        }

        void SetupUIDocument()
        {
            uiDocument = GetComponent<UIDocument>();
            if (uiDocument == null)
            {
                uiDocument = gameObject.AddComponent<UIDocument>();
            }

            if (uiDocument.panelSettings == null)
            {
                Debug.LogWarning("[TrainingHUD] PanelSettings is null! Please assign it in the inspector.");
            }

            // Le HUD doit être au-dessus de tous les autres UIDocuments
            // pour que le bouton reset soit toujours cliquable
            uiDocument.sortingOrder = 100;

            root = uiDocument.rootVisualElement;
            if (root == null)
            {
                Debug.LogError("[TrainingHUD] Root visual element is null!");
                return;
            }

            CreateHUD();
        }

        void CreateHUD()
        {
            root.Clear();
            root.pickingMode = PickingMode.Ignore;

            // Container principal - barre horizontale en haut, responsive
            hudContainer = new VisualElement();
            hudContainer.name = "training-hud";
            hudContainer.style.position = Position.Absolute;
            hudContainer.style.top = UIStyles.SpaceSM;
            hudContainer.style.left = Length.Percent(50);
            hudContainer.style.width = Length.Percent(50);
            hudContainer.style.maxWidth = 720;
            hudContainer.style.minWidth = 380;
            hudContainer.style.translate = new Translate(Length.Percent(-50), 0);
            hudContainer.style.height = 52;
            hudContainer.style.backgroundColor = UIStyles.BgBase;
            UIStyles.SetBorderRadius(hudContainer, UIStyles.Radius2XL);
            UIStyles.SetBorderWidth(hudContainer, 1);
            UIStyles.SetBorderColor(hudContainer, UIStyles.BorderSubtle);
            hudContainer.style.flexDirection = FlexDirection.Row;
            hudContainer.style.alignItems = Align.Center;
            hudContainer.style.paddingLeft = UIStyles.SpaceMD;
            hudContainer.style.paddingRight = UIStyles.SpaceMD;
            hudContainer.style.display = DisplayStyle.None;
            hudContainer.pickingMode = PickingMode.Position;

            // ===== Bouton Restart (gauche) =====
            resetButton = UIStyles.CreateIconButton("", 36, UIStyles.Danger, () => OnResetButtonClicked());
            UIStyles.SetButtonIcon(resetButton, WiseTwinIcons.Reset(20, UIStyles.TextOnAccent));
            resetButton.name = "reset-button";
            resetButton.style.marginRight = UIStyles.SpaceMD;
            resetButton.style.flexShrink = 0;
            hudContainer.Add(resetButton);

            // ===== Section centrale (titre + progress) =====
            var centerSection = new VisualElement();
            centerSection.style.flexGrow = 1;
            centerSection.style.flexDirection = FlexDirection.Column;
            centerSection.style.justifyContent = Justify.Center;
            centerSection.style.overflow = Overflow.Hidden;

            // Titre du scénario
            scenarioTitleLabel = new Label();
            scenarioTitleLabel.name = "scenario-title";
            scenarioTitleLabel.style.fontSize = UIStyles.FontXS;
            scenarioTitleLabel.style.color = UIStyles.TextMuted;
            scenarioTitleLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            scenarioTitleLabel.style.overflow = Overflow.Hidden;
            scenarioTitleLabel.style.textOverflow = TextOverflow.Ellipsis;
            scenarioTitleLabel.style.whiteSpace = WhiteSpace.NoWrap;
            scenarioTitleLabel.style.display = DisplayStyle.None;
            centerSection.Add(scenarioTitleLabel);

            // Row: progress label + bar
            var progressRow = new VisualElement();
            progressRow.style.flexDirection = FlexDirection.Row;
            progressRow.style.alignItems = Align.Center;

            // Label de progression
            progressLabel = new Label("0 / 0");
            progressLabel.style.fontSize = UIStyles.FontXS;
            progressLabel.style.color = UIStyles.TextSecondary;
            progressLabel.style.marginRight = UIStyles.SpaceSM;
            progressLabel.style.flexShrink = 0;
            progressRow.Add(progressLabel);

            // Barre de progression
            var (bar, fill) = UIStyles.CreateProgressBar(6, UIStyles.SpaceXS);
            progressBar = bar;
            progressFill = fill;
            bar.style.flexGrow = 1;
            progressRow.Add(bar);

            centerSection.Add(progressRow);
            hudContainer.Add(centerSection);

            // ===== Timer (droite) =====
            timerLabel = new Label("00:00");
            timerLabel.name = "timer-label";
            timerLabel.style.fontSize = UIStyles.FontSM;
            timerLabel.style.color = UIStyles.TextMuted;
            timerLabel.style.marginLeft = UIStyles.SpaceMD;
            timerLabel.style.flexShrink = 0;
            hudContainer.Add(timerLabel);

            root.Add(hudContainer);

            // Confirmation dialog
            CreateRestartConfirmationDialog();

            if (debugMode) Debug.Log("[TrainingHUD] HUD created");
        }

        void CreateRestartConfirmationDialog()
        {
            confirmationOverlay = new VisualElement();
            confirmationOverlay.name = "restart-confirmation";
            UIStyles.ApplyBackdropStyle(confirmationOverlay);
            confirmationOverlay.style.display = DisplayStyle.None;

            var dialog = new VisualElement();
            dialog.style.width = 420;
            dialog.style.maxWidth = Length.Percent(90);
            UIStyles.ApplyElevatedCardStyle(dialog, UIStyles.RadiusXL);
            UIStyles.SetBorderWidth(dialog, 2);
            UIStyles.SetBorderColor(dialog, new Color(UIStyles.Danger.r, UIStyles.Danger.g, UIStyles.Danger.b, 0.5f));
            UIStyles.SetPadding(dialog, UIStyles.Space2XL);
            dialog.style.alignItems = Align.Center;

            // Warning icon at the top of the dialog
            var warningIcon = WiseTwinIcons.Warning(48, UIStyles.Danger);
            warningIcon.style.marginBottom = UIStyles.SpaceMD;
            dialog.Add(warningIcon);

            // Title — kept short and in English (intentional fallback for destructive actions)
            var titleLabel = new Label("Restart Training?");
            titleLabel.name = "restart-title";
            titleLabel.style.fontSize = UIStyles.FontLG;
            titleLabel.style.color = UIStyles.TextPrimary;
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            titleLabel.style.marginBottom = UIStyles.SpaceXS;
            dialog.Add(titleLabel);

            var messageLabel = new Label("Your progress will be lost.");
            messageLabel.name = "restart-message";
            messageLabel.style.fontSize = UIStyles.FontSM;
            messageLabel.style.color = UIStyles.TextSecondary;
            messageLabel.style.whiteSpace = WhiteSpace.Normal;
            messageLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            messageLabel.style.marginBottom = UIStyles.SpaceXL;
            dialog.Add(messageLabel);

            var buttonRow = new VisualElement();
            buttonRow.style.flexDirection = FlexDirection.Row;
            buttonRow.style.justifyContent = Justify.Center;

            var cancelButton = UIStyles.CreateSecondaryButton("", () => HideRestartConfirmation());
            cancelButton.name = "cancel-restart-button";
            cancelButton.style.width = 140;
            cancelButton.style.marginRight = UIStyles.SpaceLG;
            buttonRow.Add(cancelButton);

            var restartButton = UIStyles.CreateDangerButton("", () => ConfirmRestart());
            restartButton.name = "confirm-restart-button";
            restartButton.style.width = 140;
            buttonRow.Add(restartButton);

            dialog.Add(buttonRow);
            confirmationOverlay.Add(dialog);
            root.Add(confirmationOverlay);

            UpdateConfirmationTexts();
        }

        void UpdateConfirmationTexts()
        {
            if (confirmationOverlay == null) return;

            // Title and message are now static English text set at construction time \u2014
            // see CreateRestartConfirmationDialog. Buttons get drawn icons (WebGL-safe).
            var cancelBtn = confirmationOverlay.Q<Button>("cancel-restart-button");
            if (cancelBtn != null)
            {
                UIStyles.SetButtonIcon(cancelBtn, WiseTwinIcons.CloseX(18, UIStyles.TextSecondary));
            }

            var restartBtn = confirmationOverlay.Q<Button>("confirm-restart-button");
            if (restartBtn != null)
            {
                UIStyles.SetButtonIcon(restartBtn, WiseTwinIcons.Reset(20, UIStyles.TextOnAccent));
            }
        }

        public void Show()
        {
            if (hudContainer == null) return;

            isVisible = true;
            hudContainer.style.display = DisplayStyle.Flex;
            StartCoroutine(FadeIn());
            startTime = Time.time;

            if (debugMode) Debug.Log("[TrainingHUD] HUD shown");
        }

        public void Hide()
        {
            if (hudContainer == null) return;

            isVisible = false;
            StartCoroutine(FadeOut());

            if (debugMode) Debug.Log("[TrainingHUD] HUD hidden");
        }

        /// <summary>
        /// Met à jour le titre du scénario affiché dans le HUD.
        /// </summary>
        public void SetScenarioTitle(string title)
        {
            if (scenarioTitleLabel == null) return;

            if (string.IsNullOrEmpty(title))
            {
                scenarioTitleLabel.style.display = DisplayStyle.None;
            }
            else
            {
                scenarioTitleLabel.text = title;
                scenarioTitleLabel.style.display = DisplayStyle.Flex;
            }
        }

        public void SetTotalObjects(int total)
        {
            totalObjects = total;
            UpdateProgressDisplay();

            if (debugMode) Debug.Log($"[TrainingHUD] Total objects set to {total}");
        }

        public void UpdateProgress(int completed)
        {
            currentProgress = completed;
            UpdateProgressDisplay();
        }

        public void IncrementProgress()
        {
            IncrementProgressForObject(null);
        }

        public void IncrementProgressForObject(string objectId)
        {
            if (!string.IsNullOrEmpty(objectId))
            {
                if (completedObjects.Contains(objectId))
                {
                    Debug.LogWarning($"[TrainingHUD] Object {objectId} already completed - ignoring to prevent cheating");
                    return;
                }
                completedObjects.Add(objectId);
            }

            if (currentProgress >= totalObjects)
            {
                Debug.LogWarning($"[TrainingHUD] Progress already at maximum ({currentProgress}/{totalObjects})");
                return;
            }

            currentProgress++;
            UpdateProgressDisplay();

            if (debugMode)
            {
                Debug.Log($"[TrainingHUD] Progress: {currentProgress}/{totalObjects} (Object: {objectId ?? "unknown"})");
            }

            if (currentProgress >= totalObjects && totalObjects > 0)
            {
                OnTrainingCompleted();
            }
        }

        void UpdateProgressDisplay()
        {
            if (progressLabel != null)
            {
                progressLabel.text = $"{currentProgress} / {totalObjects}";
            }

            if (progressFill != null && totalObjects > 0)
            {
                float percentage = Mathf.Clamp((float)currentProgress / totalObjects * 100f, 0f, 100f);
                progressFill.style.width = Length.Percent(percentage);

                if (currentProgress >= totalObjects)
                {
                    progressFill.style.backgroundColor = UIStyles.Success;
                }
                else
                {
                    progressFill.style.backgroundColor = UIStyles.Accent;
                }
            }
        }

        void Update()
        {
            if (!isVisible || timerLabel == null) return;

            float elapsed = Time.time - startTime;
            int minutes = Mathf.FloorToInt(elapsed / 60);
            int seconds = Mathf.FloorToInt(elapsed % 60);
            timerLabel.text = $"{minutes:00}:{seconds:00}";
        }

        IEnumerator FadeIn()
        {
            hudContainer.style.opacity = 0;
            float elapsed = 0;

            while (elapsed < fadeInDuration)
            {
                elapsed += Time.deltaTime;
                hudContainer.style.opacity = Mathf.Lerp(0, 1, elapsed / fadeInDuration);
                yield return null;
            }

            hudContainer.style.opacity = 1;
        }

        IEnumerator FadeOut()
        {
            float elapsed = 0;

            while (elapsed < fadeInDuration)
            {
                elapsed += Time.deltaTime;
                hudContainer.style.opacity = Mathf.Lerp(1, 0, elapsed / fadeInDuration);
                yield return null;
            }

            hudContainer.style.opacity = 0;
            hudContainer.style.display = DisplayStyle.None;
        }

        /// <summary>
        /// DEPRECATED: Auto-detection is no longer needed - ProgressionManager automatically sets the total from metadata
        /// </summary>
        [System.Obsolete("AutoDetectInteractables is deprecated. ProgressionManager automatically initializes the total from metadata scenarios.")]
        public void AutoDetectInteractables()
        {
            Debug.LogWarning("[TrainingHUD] AutoDetectInteractables is deprecated. The total is automatically set by ProgressionManager from metadata.");
        }

        [ContextMenu("Test Show HUD")]
        public void TestShow()
        {
            SetTotalObjects(5);
            Show();
        }

        [ContextMenu("Test Increment Progress")]
        public void TestIncrement()
        {
            IncrementProgress();
        }

        void OnTrainingCompleted()
        {
            if (debugMode) Debug.Log($"[TrainingHUD] Training completed! {currentProgress}/{totalObjects} modules done");

            float totalTime = Time.time - startTime;

            if (Analytics.TrainingAnalytics.Instance == null)
            {
                var analyticsGO = new GameObject("TrainingAnalytics");
                analyticsGO.AddComponent<Analytics.TrainingAnalytics>();
            }

            var completionUI = FindFirstObjectByType<UI.TrainingCompletionUI>();
            if (completionUI == null)
            {
                GameObject completionGO = new GameObject("TrainingCompletionUI");
                completionUI = completionGO.AddComponent<UI.TrainingCompletionUI>();

                var uiDoc = completionGO.AddComponent<UIDocument>();

                if (uiDocument != null && uiDocument.panelSettings != null)
                {
                    uiDoc.panelSettings = uiDocument.panelSettings;
                }
            }

            if (completionUI != null)
            {
                completionUI.ShowCompletionScreen(totalTime, totalObjects);
            }
            else
            {
                Debug.LogError("[TrainingHUD] Failed to create or find TrainingCompletionUI!");
            }
        }

        #region Scenario Methods

        public void OnScenarioStarted()
        {
            if (debugMode) Debug.Log("[TrainingHUD] Scenario started");
        }

        public void OnScenarioCompleted()
        {
            if (currentProgress < totalObjects)
            {
                currentProgress++;
                UpdateProgressDisplay();

                if (debugMode) Debug.Log($"[TrainingHUD] Progress incremented: {currentProgress}/{totalObjects}");
            }
        }

        /// <summary>
        /// Legacy method - no-op since next button was removed.
        /// </summary>
        public void SetNextButtonVisible(bool visible)
        {
            // No-op
        }

        public void OnAllScenariosCompleted()
        {
            if (debugMode) Debug.Log("[TrainingHUD] All scenarios completed");
            OnTrainingCompleted();
        }

        public void InitializeForScenarios()
        {
            if (ProgressionManager.Instance != null)
            {
                int totalScenarios = ProgressionManager.Instance.TotalScenarios;
                SetTotalObjects(totalScenarios);

                if (debugMode) Debug.Log($"[TrainingHUD] Initialized for {totalScenarios} scenarios");
            }
        }

        #endregion

        #region Button Handlers

        void OnResetButtonClicked()
        {
            if (debugMode) Debug.Log("[TrainingHUD] Restart button clicked - showing confirmation");
            ShowRestartConfirmation();
        }

        void ShowRestartConfirmation()
        {
            UpdateConfirmationTexts();

            if (confirmationOverlay != null)
            {
                confirmationOverlay.style.display = DisplayStyle.Flex;
            }

            PlayerControls.SetEnabled(false);
        }

        void HideRestartConfirmation()
        {
            if (confirmationOverlay != null)
            {
                confirmationOverlay.style.display = DisplayStyle.None;
            }

            PlayerControls.SetEnabled(true);
        }

        void ConfirmRestart()
        {
            if (debugMode) Debug.Log("[TrainingHUD] Restart confirmed - reloading scene");

            // La logique de reset complet vit dans WiseTwinManager (réutilisée par WiseTwinAPI).
            var wiseTwinManager = WiseTwinManager.Instance;
            if (wiseTwinManager != null)
            {
                wiseTwinManager.RestartTraining();
            }
            else
            {
                // Fallback si le manager n'existe pas : recharger la scène directement.
                ControlModeSettings.Reset();
                Destroy(transform.root.gameObject);
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            }
        }

        #endregion

    }
}
