using UnityEngine;
using UnityEngine.UIElements;
using System.Collections;
using System.Collections.Generic;
using WiseTwin.UI;

namespace WiseTwin
{
    /// <summary>
    /// Training onboarding UI shown before the first scenario starts.
    /// Two panels:
    ///   1. Welcome — formation title + description (from metadata).
    ///   2. Tutorial — control mode selection (keyboard+mouse / mouse only) + Play button.
    ///
    /// Fires <see cref="OnTutorialCompleted"/> when the user clicks Play. The
    /// ProgressionManager subscribes to that event to actually launch the training.
    /// </summary>
    public class TutorialUI : MonoBehaviour
    {
        [Header("Configuration")]
        [SerializeField] private float animationDuration = 0.3f;

        [Header("Debug")]
        [SerializeField] private bool debugMode = false;

        private UIDocument uiDocument;
        private VisualElement root;
        private VisualElement overlay;

        private VisualElement welcomePanel;
        private VisualElement tutorialPanel;

        private ControlMode selectedMode = ControlMode.KeyboardMouse;
        public bool IsDisplaying { get; private set; } = false;

        // Which control modes are offered (set via Configure before Show).
        // Both → the player chooses; exactly one → shown pre-selected, no choice;
        // neither → no control section, WiseTwin does not apply any controller.
        private bool allowKeyboard = true;
        private bool allowMouse = true;

        private VisualElement keyboardCard;
        private VisualElement mouseCard;
        private Label explanationLabel;

        const string ExplanationKeyboard =
            "WASD or arrow keys to move.\nHold right-click and drag to look around.\nScroll wheel to zoom.";
        const string ExplanationMouse =
            "Left-click on the ground to walk there.\nHold right-click and drag to look around.\nScroll wheel to zoom.";

        public System.Action OnTutorialCompleted;

        public static TutorialUI Instance { get; private set; }

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

            uiDocument = GetComponent<UIDocument>();
            if (uiDocument == null)
            {
                uiDocument = gameObject.AddComponent<UIDocument>();
            }

            if (uiDocument.visualTreeAsset != null)
            {
                uiDocument.visualTreeAsset = null;
            }
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void SetPanelSettings(PanelSettings settings)
        {
            if (uiDocument != null && settings != null)
            {
                uiDocument.panelSettings = settings;
            }
        }

        /// <summary>
        /// Configure which control modes are offered. Call before <see cref="Show"/>.
        /// Both → choice cards; exactly one → that mode shown pre-selected (no choice);
        /// neither → no control section (host project manages the player controller).
        /// </summary>
        public void Configure(bool keyboard, bool mouse)
        {
            allowKeyboard = keyboard;
            allowMouse = mouse;
        }

        public void Show(string languageCode = "")
        {
            // Default to the only available mode when a single one is offered
            selectedMode = (allowMouse && !allowKeyboard) ? ControlMode.MouseOnly : ControlMode.KeyboardMouse;
            ControlModeSettings.SetMode(selectedMode);

            if (uiDocument != null && !uiDocument.enabled)
            {
                uiDocument.enabled = true;
            }

            if (root == null)
            {
                root = uiDocument.rootVisualElement;
                if (root == null)
                {
                    Debug.LogError("[TutorialUI] Root visual element is null — missing PanelSettings?");
                    return;
                }
                root.pickingMode = PickingMode.Position;
                root.style.flexGrow = 1;
            }

            if (IsDisplaying && overlay != null && overlay.style.display == DisplayStyle.Flex)
            {
                return;
            }

            root.Clear();

            PlayerControls.SetEnabled(false);

            CreateOverlay();
            ShowWelcomePanel();

            IsDisplaying = true;
            StartCoroutine(FadeIn(overlay));

            if (debugMode) Debug.Log("[TutorialUI] Shown");
        }

        public void Hide()
        {
            StartCoroutine(FadeOutAndHide());
        }

        // ==========================================================================
        // Overlay setup
        // ==========================================================================

        void CreateOverlay()
        {
            overlay = new VisualElement();
            overlay.name = "tutorial-overlay";
            overlay.style.position = Position.Absolute;
            overlay.style.width = Length.Percent(100);
            overlay.style.height = Length.Percent(100);
            overlay.style.backgroundColor = UIStyles.BgDeep;
            overlay.style.alignItems = Align.Center;
            overlay.style.justifyContent = Justify.Center;
            overlay.pickingMode = PickingMode.Position;

            welcomePanel = BuildWelcomePanel();
            tutorialPanel = BuildTutorialPanel();

            welcomePanel.style.display = DisplayStyle.None;
            tutorialPanel.style.display = DisplayStyle.None;

            overlay.Add(welcomePanel);
            overlay.Add(tutorialPanel);
            root.Add(overlay);
        }

        // ==========================================================================
        // Panel 1: Welcome (title + description from metadata)
        // ==========================================================================

        VisualElement BuildWelcomePanel()
        {
            var panel = new VisualElement();
            panel.name = "welcome-panel";
            panel.style.width = 640;
            panel.style.maxWidth = Length.Percent(90);
            panel.style.maxHeight = Length.Percent(90);
            UIStyles.ApplyCardStyle(panel, UIStyles.RadiusXL);
            UIStyles.SetPadding(panel, UIStyles.Space3XL);
            panel.style.alignItems = Align.Center;

            string title = "";
            string description = "";
            var loader = MetadataLoader.Instance;
            if (loader != null && loader.IsLoaded)
            {
                var meta = loader.GetMetadata();
                if (meta != null)
                {
                    if (meta.TryGetValue("title", out var t)) title = LocalizedValueReader.Flatten(t);
                    if (meta.TryGetValue("description", out var d)) description = LocalizedValueReader.Flatten(d);
                }
            }

            var titleLabel = UIStyles.CreateTitle(title, UIStyles.Font3XL);
            titleLabel.style.marginBottom = UIStyles.SpaceLG;
            titleLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            panel.Add(titleLabel);

            var descLabel = UIStyles.CreateBodyText(description, UIStyles.FontMD);
            descLabel.style.color = UIStyles.TextSecondary;
            descLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            descLabel.style.whiteSpace = WhiteSpace.Normal;
            descLabel.style.marginBottom = UIStyles.Space2XL;
            panel.Add(descLabel);

            var nextButton = UIStyles.CreatePrimaryButton("", ShowTutorialPanel);
            UIStyles.SetButtonIcon(nextButton, WiseTwinIcons.ArrowRight(28, UIStyles.TextOnAccent));
            nextButton.style.width = 140;
            nextButton.style.height = 52;
            panel.Add(nextButton);

            return panel;
        }

        void ShowWelcomePanel()
        {
            if (welcomePanel != null) welcomePanel.style.display = DisplayStyle.Flex;
            if (tutorialPanel != null) tutorialPanel.style.display = DisplayStyle.None;
        }

        // ==========================================================================
        // Panel 2: Tutorial (control mode + Play button)
        // ==========================================================================

        VisualElement BuildTutorialPanel()
        {
            var panel = new VisualElement();
            panel.name = "tutorial-panel-content";
            panel.style.width = 720;
            panel.style.maxWidth = Length.Percent(90);
            panel.style.maxHeight = Length.Percent(90);
            UIStyles.ApplyCardStyle(panel, UIStyles.RadiusXL);
            UIStyles.SetPadding(panel, UIStyles.Space3XL);
            panel.style.alignItems = Align.Center;

            bool bothModes = allowKeyboard && allowMouse;
            bool anyMode = allowKeyboard || allowMouse;

            string headerText = bothModes ? "Choose your controls" : (anyMode ? "Controls" : "Ready");
            var header = UIStyles.CreateTitle(headerText, UIStyles.FontXL);
            header.style.marginBottom = UIStyles.SpaceLG;
            header.style.unityTextAlign = TextAnchor.MiddleCenter;
            panel.Add(header);

            // Control section — only shown when WiseTwin manages at least one mode.
            // Clickable choice cards only when BOTH modes are offered; otherwise the
            // single available mode is shown pre-selected (informational, no choice).
            if (anyMode)
            {
                var cardsRow = new VisualElement();
                cardsRow.style.flexDirection = FlexDirection.Row;
                cardsRow.style.justifyContent = Justify.Center;
                cardsRow.style.marginBottom = UIStyles.SpaceLG;

                if (allowKeyboard)
                {
                    keyboardCard = BuildKeyboardCard(selectedMode == ControlMode.KeyboardMouse);
                    if (bothModes)
                    {
                        keyboardCard.RegisterCallback<ClickEvent>(evt =>
                        {
                            evt.StopPropagation();
                            SelectControlMode(ControlMode.KeyboardMouse);
                        });
                    }
                    cardsRow.Add(keyboardCard);
                }

                if (bothModes)
                {
                    var spacer = new VisualElement();
                    spacer.style.width = UIStyles.Space2XL;
                    cardsRow.Add(spacer);
                }

                if (allowMouse)
                {
                    mouseCard = BuildMouseCard(selectedMode == ControlMode.MouseOnly);
                    if (bothModes)
                    {
                        mouseCard.RegisterCallback<ClickEvent>(evt =>
                        {
                            evt.StopPropagation();
                            SelectControlMode(ControlMode.MouseOnly);
                        });
                    }
                    cardsRow.Add(mouseCard);
                }

                panel.Add(cardsRow);

                // Explanation label (updates when a card is selected, when a choice is offered)
                explanationLabel = new Label(selectedMode == ControlMode.MouseOnly ? ExplanationMouse : ExplanationKeyboard);
                explanationLabel.style.fontSize = UIStyles.FontBase;
                explanationLabel.style.color = UIStyles.TextSecondary;
                explanationLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                explanationLabel.style.whiteSpace = WhiteSpace.Normal;
                explanationLabel.style.marginTop = UIStyles.SpaceLG;
                explanationLabel.style.marginBottom = UIStyles.SpaceXL;
                explanationLabel.style.paddingTop = UIStyles.SpaceMD;
                explanationLabel.style.paddingBottom = UIStyles.SpaceMD;
                explanationLabel.style.paddingLeft = UIStyles.SpaceLG;
                explanationLabel.style.paddingRight = UIStyles.SpaceLG;
                explanationLabel.style.backgroundColor = UIStyles.BgElevated;
                UIStyles.SetBorderRadius(explanationLabel, UIStyles.RadiusSM);
                explanationLabel.style.minWidth = 520;
                panel.Add(explanationLabel);
            }

            // Back + Play buttons
            var buttonRow = new VisualElement();
            buttonRow.style.flexDirection = FlexDirection.Row;
            buttonRow.style.justifyContent = Justify.Center;
            buttonRow.style.marginTop = UIStyles.SpaceLG;

            var backButton = UIStyles.CreateSecondaryButton("", ShowWelcomePanel);
            UIStyles.SetButtonIcon(backButton, WiseTwinIcons.ArrowLeft(22, UIStyles.TextSecondary));
            backButton.style.width = 100;
            backButton.style.height = 48;
            backButton.style.marginRight = UIStyles.SpaceLG;
            buttonRow.Add(backButton);

            var playButton = UIStyles.CreatePrimaryButton("", OnPlayButtonClicked);
            UIStyles.SetButtonIcon(playButton, WiseTwinIcons.PlayTriangle(24, UIStyles.TextOnAccent));
            playButton.style.width = 140;
            playButton.style.height = 52;
            buttonRow.Add(playButton);

            panel.Add(buttonRow);

            return panel;
        }

        // ==========================================================================
        // Visual control mode cards (keyboard keys + mouse shape drawn with VisualElements)
        // ==========================================================================

        VisualElement BuildKeyboardCard(bool isSelected)
        {
            var card = MakeCardShell(isSelected);

            var visual = new VisualElement();
            visual.style.alignItems = Align.Center;
            visual.style.justifyContent = Justify.Center;
            visual.style.marginBottom = UIStyles.SpaceMD;

            // Top row: [W]
            var topRow = new VisualElement();
            topRow.style.flexDirection = FlexDirection.Row;
            topRow.style.justifyContent = Justify.Center;
            topRow.style.marginBottom = 4;
            topRow.Add(BuildKey("W"));
            visual.Add(topRow);

            // Bottom row: [A] [S] [D]
            var bottomRow = new VisualElement();
            bottomRow.style.flexDirection = FlexDirection.Row;
            bottomRow.style.justifyContent = Justify.Center;
            bottomRow.Add(BuildKey("A"));
            bottomRow.Add(BuildKey("S"));
            bottomRow.Add(BuildKey("D"));
            visual.Add(bottomRow);

            card.Add(visual);

            var label = new Label("Keyboard + Mouse");
            label.style.fontSize = UIStyles.FontBase;
            label.style.color = UIStyles.TextPrimary;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.pickingMode = PickingMode.Ignore;
            card.Add(label);

            return card;
        }

        VisualElement BuildMouseCard(bool isSelected)
        {
            var card = MakeCardShell(isSelected);

            // Mouse silhouette: a rounded vertical capsule. The vertical divider runs from
            // the top of the body down to about the middle (button line), with the scroll
            // wheel as a coloured accent pill straddling that divider near the top — every
            // measurement is derived from bodyW/bodyH so the shape stays symmetric.
            const float bodyW = 64f;
            const float bodyH = 94f;

            var mouseBody = new VisualElement();
            mouseBody.style.width = bodyW;
            mouseBody.style.height = bodyH;
            mouseBody.style.backgroundColor = UIStyles.BgDeep;
            UIStyles.SetBorderRadius(mouseBody, 26);
            UIStyles.SetBorderWidth(mouseBody, 2);
            UIStyles.SetBorderColor(mouseBody, UIStyles.TextSecondary);
            mouseBody.style.marginBottom = UIStyles.SpaceMD;
            mouseBody.pickingMode = PickingMode.Ignore;

            // Vertical divider: top → mid-body. Centered horizontally on the body.
            const float dividerW = 2f;
            var divider = new VisualElement();
            divider.style.position = Position.Absolute;
            divider.style.left = (bodyW - dividerW) * 0.5f;
            divider.style.top = 6f;
            divider.style.width = dividerW;
            divider.style.height = bodyH * 0.50f - 6f;
            divider.style.backgroundColor = UIStyles.TextSecondary;
            mouseBody.Add(divider);

            // Scroll wheel: coloured pill straddling the divider, near the top.
            const float wheelW = 10f;
            const float wheelH = 18f;
            var wheel = new VisualElement();
            wheel.style.position = Position.Absolute;
            wheel.style.left = (bodyW - wheelW) * 0.5f;
            wheel.style.top = 14f;
            wheel.style.width = wheelW;
            wheel.style.height = wheelH;
            wheel.style.backgroundColor = UIStyles.Accent;
            UIStyles.SetBorderRadius(wheel, 5);
            mouseBody.Add(wheel);

            card.Add(mouseBody);

            var label = new Label("Mouse only");
            label.style.fontSize = UIStyles.FontBase;
            label.style.color = UIStyles.TextPrimary;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.pickingMode = PickingMode.Ignore;
            card.Add(label);

            return card;
        }

        VisualElement MakeCardShell(bool isSelected)
        {
            var card = new VisualElement();
            card.style.width = 220;
            card.style.height = 200;
            UIStyles.SetPadding(card, UIStyles.SpaceLG);
            UIStyles.SetBorderRadius(card, UIStyles.RadiusMD);
            UIStyles.SetBorderWidth(card, 2);
            card.style.alignItems = Align.Center;
            card.style.justifyContent = Justify.Center;
            card.pickingMode = PickingMode.Position;
            ApplyControlCardStyle(card, isSelected);
            return card;
        }

        VisualElement BuildKey(string letter)
        {
            const int keySize = 42;

            var key = new VisualElement();
            key.style.width = keySize;
            key.style.height = keySize;
            key.style.marginLeft = 4;
            key.style.marginRight = 4;
            key.style.backgroundColor = UIStyles.BgDeep;
            UIStyles.SetBorderRadius(key, UIStyles.RadiusSM);
            UIStyles.SetBorderWidth(key, 2);
            UIStyles.SetBorderColor(key, UIStyles.TextSecondary);
            key.pickingMode = PickingMode.Ignore;

            // Label fills the key (absolute, all sides 0) and centers the glyph in both axes.
            // All padding / margin explicitly zeroed so the line-height box doesn't push the
            // letter off-center. Explicit fontSize (instead of FontMD) keeps the glyph from
            // being cropped at small key sizes.
            var label = new Label(letter);
            label.style.position = Position.Absolute;
            label.style.left = 0;
            label.style.right = 0;
            label.style.top = 0;
            label.style.bottom = 0;
            label.style.paddingTop = 0;
            label.style.paddingBottom = 0;
            label.style.paddingLeft = 0;
            label.style.paddingRight = 0;
            label.style.marginTop = 0;
            label.style.marginBottom = 0;
            label.style.marginLeft = 0;
            label.style.marginRight = 0;
            label.style.fontSize = 18;
            label.style.color = UIStyles.TextPrimary;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.pickingMode = PickingMode.Ignore;
            key.Add(label);

            return key;
        }

        void ShowTutorialPanel()
        {
            if (welcomePanel != null) welcomePanel.style.display = DisplayStyle.None;
            if (tutorialPanel != null) tutorialPanel.style.display = DisplayStyle.Flex;
        }

        void ApplyControlCardStyle(VisualElement card, bool isSelected)
        {
            Color borderColor = isSelected ? UIStyles.Accent : UIStyles.BorderSubtle;
            UIStyles.SetBorderColor(card, borderColor);
            card.style.backgroundColor = isSelected
                ? new Color(UIStyles.Accent.r, UIStyles.Accent.g, UIStyles.Accent.b, 0.12f)
                : UIStyles.BgInput;
        }

        void SelectControlMode(ControlMode mode)
        {
            selectedMode = mode;
            ControlModeSettings.SetMode(mode);

            if (keyboardCard != null) ApplyControlCardStyle(keyboardCard, mode == ControlMode.KeyboardMouse);
            if (mouseCard != null) ApplyControlCardStyle(mouseCard, mode == ControlMode.MouseOnly);
            if (explanationLabel != null)
            {
                explanationLabel.text = mode == ControlMode.KeyboardMouse ? ExplanationKeyboard : ExplanationMouse;
            }

            if (debugMode) Debug.Log($"[TutorialUI] Control mode: {mode}");
        }

        // ==========================================================================
        // Actions
        // ==========================================================================

        void OnPlayButtonClicked()
        {
            if (debugMode) Debug.Log("[TutorialUI] Play clicked → completing tutorial");
            // Only apply a WiseTwin controller when at least one mode is offered;
            // otherwise the host project owns the player controller.
            if (allowKeyboard || allowMouse)
            {
                ControlModeSettings.ApplyToPlayer();
            }
            OnTutorialCompleted?.Invoke();
            Hide();
        }

        IEnumerator FadeIn(VisualElement element)
        {
            if (element == null) yield break;
            element.style.opacity = 0;
            element.style.display = DisplayStyle.Flex;

            float elapsed = 0;
            while (elapsed < animationDuration)
            {
                elapsed += Time.deltaTime;
                element.style.opacity = Mathf.Lerp(0, 1, elapsed / animationDuration);
                yield return null;
            }
            element.style.opacity = 1;
        }

        IEnumerator FadeOutAndHide()
        {
            if (overlay == null) yield break;

            float elapsed = 0;
            while (elapsed < animationDuration)
            {
                elapsed += Time.deltaTime;
                overlay.style.opacity = Mathf.Lerp(1, 0, elapsed / animationDuration);
                yield return null;
            }

            overlay.style.display = DisplayStyle.None;
            IsDisplaying = false;

            if (root != null)
            {
                root.style.display = DisplayStyle.None;
                root.pickingMode = PickingMode.Ignore;
            }

            if (uiDocument != null)
            {
                uiDocument.enabled = false;
            }

            PlayerControls.SetEnabled(true);

            if (debugMode) Debug.Log("[TutorialUI] Hidden");
        }
    }
}
