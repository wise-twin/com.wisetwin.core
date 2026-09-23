using UnityEngine;
using UnityEngine.UIElements;
using System;
using System.Collections;
using System.Collections.Generic;
using WiseTwin.UI;

namespace WiseTwin
{
    /// <summary>
    /// Panneau de transition entre les scénarios.
    /// Affiche un écran centré avec titre, sous-titre et bouton d'action.
    /// Deux modes : Start (démarrage) et Transition (entre scénarios).
    /// </summary>
    public class ScenarioTransitionPanel : MonoBehaviour
    {
        public static ScenarioTransitionPanel Instance { get; private set; }

        public event Action OnActionButtonClicked;

        // UI
        private UIDocument uiDocument;
        private VisualElement root;
        private VisualElement backdrop;
        private VisualElement panel;
        private Label titleLabel;
        private Label subtitleLabel;
        private Button actionButton;

        // State
        private bool isVisible = false;
        public bool IsVisible => isVisible;

        // Animation
        private const float FadeDuration = 0.3f;
        private Coroutine fadeCoroutine;

        void Awake()
        {
            if (Instance != null)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            SetupUIDocument();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void SetPanelSettings(PanelSettings settings)
        {
            if (uiDocument != null)
            {
                uiDocument.panelSettings = settings;
                Debug.Log($"[ScenarioTransitionPanel] PanelSettings set to: {settings?.name}");

                // Re-initialize root element now that we have valid PanelSettings
                root = uiDocument.rootVisualElement;
                if (root != null)
                {
                    CreatePanel();
                    Debug.Log("[ScenarioTransitionPanel] Panel recreated after PanelSettings assignment");
                }
                else
                {
                    Debug.LogError("[ScenarioTransitionPanel] Root still null after setting PanelSettings!");
                }
            }
        }

        void SetupUIDocument()
        {
            uiDocument = GetComponent<UIDocument>();
            if (uiDocument == null)
            {
                uiDocument = gameObject.AddComponent<UIDocument>();
            }
            uiDocument.visualTreeAsset = null;

            Debug.Log($"[ScenarioTransitionPanel] SetupUIDocument - PanelSettings: {(uiDocument.panelSettings != null ? uiDocument.panelSettings.name : "NULL")}");

            root = uiDocument.rootVisualElement;
            if (root == null)
            {
                Debug.LogError("[ScenarioTransitionPanel] Root visual element is null!");
                return;
            }

            CreatePanel();
        }

        void CreatePanel()
        {
            root.Clear();
            root.pickingMode = PickingMode.Ignore;

            // Backdrop plein écran
            backdrop = new VisualElement();
            backdrop.name = "transition-backdrop";
            UIStyles.ApplyBackdropStyle(backdrop);
            backdrop.style.display = DisplayStyle.None;

            // Panneau central
            panel = new VisualElement();
            panel.name = "transition-panel";
            panel.style.width = 500;
            panel.style.maxWidth = Length.Percent(90);
            UIStyles.ApplyCardStyle(panel, UIStyles.RadiusXL);
            UIStyles.SetBorderWidth(panel, 2);
            UIStyles.SetBorderColor(panel, UIStyles.Accent);
            UIStyles.SetPadding(panel, UIStyles.Space3XL);
            panel.style.alignItems = Align.Center;

            // Titre
            titleLabel = UIStyles.CreateTitle("", UIStyles.Font2XL);
            titleLabel.name = "transition-title";
            titleLabel.style.marginBottom = UIStyles.SpaceLG;
            panel.Add(titleLabel);

            // Sous-titre
            subtitleLabel = UIStyles.CreateSubtitle("", UIStyles.FontBase);
            subtitleLabel.name = "transition-subtitle";
            subtitleLabel.style.color = UIStyles.TextMuted;
            subtitleLabel.style.marginBottom = UIStyles.Space2XL;
            panel.Add(subtitleLabel);

            // Bouton d'action
            actionButton = UIStyles.CreatePrimaryButton("");
            actionButton.name = "transition-action-button";
            actionButton.style.width = 320;
            actionButton.style.maxWidth = Length.Percent(100);
            actionButton.style.height = 56;
            actionButton.style.fontSize = UIStyles.FontLG;
            actionButton.clicked += OnButtonClicked;
            panel.Add(actionButton);

            backdrop.Add(panel);
            root.Add(backdrop);
        }

        public void ShowStartPanel(int totalScenarios)
        {
            titleLabel.text = "";
            subtitleLabel.text = "";
            UIStyles.SetButtonIcon(actionButton, WiseTwinIcons.PlayTriangle(22, UIStyles.TextOnAccent));

            ShowInternal();
        }

        public void ShowTransitionPanel(int completedIndex, int totalScenarios, string nextScenarioName = "")
        {
            Debug.Log($"[ScenarioTransitionPanel] ShowTransitionPanel called - PanelSettings: {(uiDocument?.panelSettings != null ? uiDocument.panelSettings.name : "NULL")}");

            titleLabel.text = $"{completedIndex + 1} / {totalScenarios}";
            subtitleLabel.text = nextScenarioName ?? "";
            UIStyles.SetButtonIcon(actionButton, WiseTwinIcons.ArrowRight(22, UIStyles.TextOnAccent));

            ShowInternal();
        }

        public void Hide()
        {
            if (!isVisible) return;
            isVisible = false;

            SetPlayerControlsEnabled(true);

            if (fadeCoroutine != null) StopCoroutine(fadeCoroutine);
            fadeCoroutine = StartCoroutine(FadeOutAndHide());
        }

        void ShowInternal()
        {
            isVisible = true;

            SetPlayerControlsEnabled(false);

            if (backdrop != null)
            {
                backdrop.style.display = DisplayStyle.Flex;
            }

            if (fadeCoroutine != null) StopCoroutine(fadeCoroutine);
            fadeCoroutine = StartCoroutine(FadeIn());
        }

        void OnButtonClicked()
        {
            Debug.Log("[ScenarioTransitionPanel] Button clicked!");
            if (!isVisible)
            {
                Debug.Log("[ScenarioTransitionPanel] But panel is not visible, ignoring");
                return;
            }
            Debug.Log("[ScenarioTransitionPanel] Invoking OnActionButtonClicked event");
            OnActionButtonClicked?.Invoke();
        }

        void SetPlayerControlsEnabled(bool enabled)
        {
            PlayerControls.SetEnabled(enabled);
        }

        IEnumerator FadeIn()
        {
            if (backdrop == null) yield break;

            backdrop.style.opacity = 0;
            float elapsed = 0f;

            while (elapsed < FadeDuration)
            {
                elapsed += Time.deltaTime;
                backdrop.style.opacity = Mathf.Lerp(0f, 1f, elapsed / FadeDuration);
                yield return null;
            }

            backdrop.style.opacity = 1f;
            fadeCoroutine = null;
        }

        IEnumerator FadeOutAndHide()
        {
            if (backdrop == null) yield break;

            float elapsed = 0f;

            while (elapsed < FadeDuration)
            {
                elapsed += Time.deltaTime;
                backdrop.style.opacity = Mathf.Lerp(1f, 0f, elapsed / FadeDuration);
                yield return null;
            }

            backdrop.style.opacity = 0f;
            backdrop.style.display = DisplayStyle.None;
            fadeCoroutine = null;
        }

    }
}
