using UnityEngine;
using UnityEngine.UIElements;
using System;
using UnityEngine.SceneManagement;

namespace WiseTwin.UI
{
    /// <summary>
    /// Interface de complétion de formation affichée quand l'utilisateur termine tous les modules
    /// </summary>
    public class TrainingCompletionUI : MonoBehaviour
    {
        [Header("UI Settings")]
        [SerializeField] private PanelSettings panelSettings;

        private UIDocument uiDocument;
        private VisualElement rootElement;
        private VisualElement modalContainer;

        private float totalTime = 0f;
        private int totalInteractions = 0;

        public static TrainingCompletionUI Instance { get; private set; }

        void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else
            {
                Destroy(gameObject);
                return;
            }

            SetupUIDocument();
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
                if (panelSettings != null)
                {
                    try
                    {
                        uiDocument.panelSettings = panelSettings;
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogWarning($"[TrainingCompletionUI] Could not assign PanelSettings: {e.Message}");
                    }
                }
                else
                {
                    Debug.LogWarning("[TrainingCompletionUI] PanelSettings is null! Please assign it in the inspector.");
                }
            }

            // Must render above the TrainingHUD (sortingOrder 1000) so the backdrop + card cover it.
            uiDocument.sortingOrder = 2000;

            rootElement = uiDocument.rootVisualElement;

            rootElement.style.position = Position.Absolute;
            rootElement.style.width = Length.Percent(100);
            rootElement.style.height = Length.Percent(100);
            rootElement.pickingMode = PickingMode.Ignore;
        }

        public void ShowCompletionScreen(float trainingTime, int modulesCompleted)
        {
            Debug.Log($"[TrainingCompletionUI] Showing completion screen - Time: {trainingTime}s, Modules: {modulesCompleted}");

            totalTime = trainingTime;
            totalInteractions = modulesCompleted;

            if (TrainingHUD.Instance != null)
            {
                TrainingHUD.Instance.UpdateProgress(modulesCompleted);
            }

            PlayerControls.SetEnabled(false);

            CreateCompletionUI();
            NotifyTrainingCompletion();
        }

        void CreateCompletionUI()
        {
            rootElement.Clear();
            rootElement.pickingMode = PickingMode.Position;

            modalContainer = new VisualElement();
            UIStyles.ApplyBackdropHeavyStyle(modalContainer);

            var card = new VisualElement();
            card.style.width = 580;
            card.style.maxWidth = Length.Percent(90);
            UIStyles.ApplyCardStyle(card, UIStyles.Radius2XL);
            UIStyles.SetBorderWidth(card, 2);
            UIStyles.SetBorderColor(card, new Color(UIStyles.Success.r, UIStyles.Success.g, UIStyles.Success.b, 0.6f));
            card.style.paddingTop = UIStyles.Space4XL;
            card.style.paddingBottom = UIStyles.Space4XL;
            card.style.paddingLeft = UIStyles.Space4XL;
            card.style.paddingRight = UIStyles.Space4XL;

            // Close button
            var closeBtn = UIStyles.CreateIconButton("X", 34, UIStyles.Danger, () => CloseCompletionUI());
            closeBtn.style.position = Position.Absolute;
            closeBtn.style.top = UIStyles.SpaceLG;
            closeBtn.style.right = UIStyles.SpaceLG;
            card.Add(closeBtn);

            // Congrats title (text only, BMP-safe)
            var congratsTitle = UIStyles.CreateTitle("Training Completed", UIStyles.Font3XL);
            congratsTitle.style.color = UIStyles.Success;
            congratsTitle.style.marginBottom = UIStyles.SpaceLG;
            congratsTitle.style.unityTextAlign = TextAnchor.MiddleCenter;
            card.Add(congratsTitle);

            // Separator
            card.Add(UIStyles.CreateSeparator(UIStyles.SpaceLG));

            // Stats section
            var statsContainer = new VisualElement();
            statsContainer.style.marginBottom = UIStyles.SpaceXL;

            var timeLabel = UIStyles.CreateBodyText($"Time: {FormatTime(totalTime)}", UIStyles.FontMD);
            timeLabel.style.color = UIStyles.TextSecondary;
            timeLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            timeLabel.style.marginBottom = UIStyles.SpaceSM;
            statsContainer.Add(timeLabel);

            var modulesLabel = UIStyles.CreateBodyText($"Modules: {totalInteractions}", UIStyles.FontMD);
            modulesLabel.style.color = UIStyles.TextSecondary;
            modulesLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            statsContainer.Add(modulesLabel);

            card.Add(statsContainer);

            // Score + analytics
            float finalScore = 100f;
            int analyticsInteractions = 0;
            int successfulInteractionsCount = 0;
            int failedInteractionsCount = 0;

            if (Analytics.TrainingAnalytics.Instance != null)
            {
                finalScore = Analytics.TrainingAnalytics.Instance.CalculateScore();
                analyticsInteractions = Analytics.TrainingAnalytics.Instance.GetTotalInteractions();
                successfulInteractionsCount = Analytics.TrainingAnalytics.Instance.GetSuccessfulInteractions();
                failedInteractionsCount = Analytics.TrainingAnalytics.Instance.GetFailedInteractions();
            }

            // Interaction details
            if (analyticsInteractions > 0)
            {
                var detailBox = new VisualElement();
                detailBox.style.backgroundColor = UIStyles.BgElevated;
                UIStyles.SetBorderRadius(detailBox, UIStyles.RadiusMD);
                UIStyles.SetPadding(detailBox, UIStyles.SpaceLG);
                detailBox.style.marginBottom = UIStyles.SpaceXL;

                var detailTitle = UIStyles.CreateMutedText("Interaction Details", UIStyles.FontBase);
                detailTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
                detailTitle.style.unityTextAlign = TextAnchor.MiddleCenter;
                detailTitle.style.marginBottom = UIStyles.SpaceSM;
                detailBox.Add(detailTitle);

                var totalLabel = UIStyles.CreateBodyText($"Total: {analyticsInteractions}", UIStyles.FontBase);
                totalLabel.style.color = UIStyles.TextSecondary;
                totalLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                totalLabel.style.marginBottom = UIStyles.SpaceXS;
                detailBox.Add(totalLabel);

                var successLabel = UIStyles.CreateBodyText($"Successful: {successfulInteractionsCount}", UIStyles.FontBase);
                successLabel.style.color = UIStyles.Success;
                successLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                successLabel.style.marginBottom = UIStyles.SpaceXS;
                detailBox.Add(successLabel);

                if (failedInteractionsCount > 0)
                {
                    var failedLabel = UIStyles.CreateBodyText($"Failed: {failedInteractionsCount}", UIStyles.FontBase);
                    failedLabel.style.color = UIStyles.Danger;
                    failedLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                    detailBox.Add(failedLabel);
                }

                card.Add(detailBox);
            }

            // Score display
            var scoreBox = new VisualElement();
            scoreBox.style.backgroundColor = UIStyles.BgElevated;
            UIStyles.SetBorderRadius(scoreBox, UIStyles.RadiusLG);
            scoreBox.style.paddingTop = UIStyles.SpaceXL;
            scoreBox.style.paddingBottom = UIStyles.SpaceXL;
            scoreBox.style.marginBottom = UIStyles.SpaceXL;

            var scoreLabel = new Label($"{Mathf.RoundToInt(finalScore)}%");
            scoreLabel.style.fontSize = UIStyles.Font4XL + 6;
            scoreLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            scoreLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            if (finalScore >= 90f) scoreLabel.style.color = UIStyles.Success;
            else if (finalScore >= 70f) scoreLabel.style.color = UIStyles.Warning;
            else scoreLabel.style.color = UIStyles.Danger;
            scoreBox.Add(scoreLabel);

            string ratingMessage;
            if (finalScore >= 100f) ratingMessage = "Perfect score!";
            else if (finalScore >= 90f) ratingMessage = "Excellent work!";
            else if (finalScore >= 70f) ratingMessage = "Good job!";
            else ratingMessage = "Room for improvement";
            var scoreText = UIStyles.CreateSubtitle(ratingMessage, UIStyles.FontBase);
            scoreText.style.color = UIStyles.TextSecondary;
            scoreText.style.unityTextAlign = TextAnchor.MiddleCenter;
            scoreBox.Add(scoreText);

            card.Add(scoreBox);

            // Info message
            var infoBox = new VisualElement();
            infoBox.style.backgroundColor = UIStyles.BgElevated;
            UIStyles.SetBorderRadius(infoBox, UIStyles.RadiusMD);
            UIStyles.SetPadding(infoBox, UIStyles.SpaceLG);

            var infoText = UIStyles.CreateMutedText(
                "You can now close this window to explore the 3D environment, or quit the training.",
                UIStyles.FontSM);
            infoText.style.unityTextAlign = TextAnchor.MiddleCenter;
            infoText.style.whiteSpace = WhiteSpace.Normal;
            infoBox.Add(infoText);

            card.Add(infoBox);

            modalContainer.Add(card);
            rootElement.Add(modalContainer);

            // Scale-in animation
            card.style.scale = new Scale(new Vector3(0.85f, 0.85f, 1f));
            card.RegisterCallback<GeometryChangedEvent>((evt) =>
            {
                card.style.scale = new Scale(Vector3.one);
            });
        }

        void NotifyTrainingCompletion()
        {
            Debug.Log("[TrainingCompletionUI] Sending training completion notification...");

            if (Analytics.TrainingAnalytics.Instance != null)
            {
                Analytics.TrainingAnalytics.Instance.CompleteTraining("completed");
            }

            var completionNotifier = FindFirstObjectByType<TrainingCompletionNotifier>();
            if (completionNotifier != null)
            {
                completionNotifier.FormationCompleted(SceneManager.GetActiveScene().name);
            }
        }

        string FormatTime(float seconds)
        {
            int minutes = Mathf.FloorToInt(seconds / 60);
            int secs = Mathf.FloorToInt(seconds % 60);
            return $"{minutes:00}:{secs:00}";
        }

        void CloseCompletionUI()
        {
            Debug.Log("[TrainingCompletionUI] Closing completion UI - User can now explore");

            PlayerControls.SetEnabled(true);

            rootElement?.Clear();
            rootElement.pickingMode = PickingMode.Ignore;
        }
    }
}
