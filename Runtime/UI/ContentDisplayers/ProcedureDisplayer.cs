using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;
using System;
using System.Linq;
using WiseTwin.Analytics;
using WiseTwin.UI;

namespace WiseTwin.UI
{
    /// <summary>
    /// Afficheur spécialisé pour les procédures séquentielles
    /// Guide l'utilisateur à travers une séquence d'objets 3D à interagir dans le bon ordre
    /// </summary>
    public class ProcedureDisplayer : MonoBehaviour, IContentDisplayer
    {
        public event Action<string> OnClosed;
        public event Action<string, bool> OnCompleted;

        [Header("Visual Settings")]
        [SerializeField] private Color highlightColor = new Color(1f, 0.9f, 0.3f, 1f); // Jaune
        [SerializeField] private float highlightIntensity = 3.5f; // Augmenté pour plus de visibilité
        [SerializeField] private bool pulseHighlight = true; // Pulse jaune quand pas de survol
        [SerializeField] private float pulseSpeed = 3f; // Augmenté pour une pulsation plus visible

        private string currentObjectId;
        private VisualElement rootElement;
        private VisualElement modalContainer;

        // Données de la procédure
        private string procedureKey;        // Clé de la procédure pour tracking
        private string procedureTitle;
        private string procedureDescription;
        private List<ProcedureStep> steps;
        private List<FakeObjectData> fakeObjects; // NEW: Fake objects that show error messages
        private int currentStepIndex = 0;
        private float procedureStartTime;   // Temps de début de la procédure

        // GameObjects de la séquence
        private List<GameObject> allSequenceObjects; // Tous les objets (target + fake) de toutes les étapes
        private Dictionary<Renderer, Material> originalMaterials;
        private List<GameObject> currentHighlightedObjects = new List<GameObject>(); // Objets surlignés à l'étape actuelle
        private GameObject currentCorrectObject; // L'objet correct de l'étape actuelle
        // Used only for "group" steps: tracks which target objects are still pending. The step
        // advances when this set becomes empty (all objects clicked, in any order).
        private HashSet<GameObject> currentGroupRemaining = new HashSet<GameObject>();
        private bool shouldHighlight = true; // Contrôle si on doit surligner ou non
        private bool keepProgressOnOtherClick = false; // Ne pas réinitialiser à 0 si on clique ailleurs

        // UI Elements
        private Label titleLabel;
        private Label descriptionLabel;
        private Label stepLabel;
        private Label progressLabel;
        private Label errorFeedbackLabel;
        private VisualElement progressBar;
        private VisualElement progressFill;
        private Button validateButton; // NEW: Manual validation button
        private VisualElement imageContainer; // NEW: Container for step image
        private VisualElement imageElement; // NEW: The actual image element
        private bool imageZoomed = false; // NEW: Track image zoom state

        // Draggable instruction panel
        private VisualElement instructionPanel;
        private VisualElement dragHandleArea;
        private bool isDraggingPanel;
        private Vector2 dragPointerOffset;
        private int dragPointerId = -1;
        // Static so the panel keeps its position across successive procedures in the same session.
        private static Vector2 savedPanelPosition;
        private static bool hasSavedPanelPosition;

        // Analytics tracking
        private float stepStartTime;
        private int wrongClicksCount = 0; // Erreurs sur l'étape en cours
        private int totalWrongClicksInProcedure = 0; // Compteur global d'erreurs pour toute la procédure
        private List<ProcedureStepData> completedSteps; // Liste des étapes complétées pour tracking

        public class ProcedureStep
        {
            public string targetObjectName; // Object name instead of ID
            public string title;
            public string instruction;
            public string validation;
            public string hint;
            public GameObject targetObject; // renamed from correctObject
            public bool completed = false;
            public Color highlightColor = Color.yellow;
            public bool useBlinking = true;
            public string validationType = "click"; // "click", "manual", "zone", "group"
            public string zoneObjectName; // Name of the zone trigger object (when validationType == "zone")
            public GameObject zoneObject; // Resolved zone trigger GameObject
            public bool requireManualValidation => validationType == "manual"; // Backward compat read-only
            public string imagePath; // Path to the image for this step
            public List<FakeObjectData> fakeObjects = new List<FakeObjectData>(); // Fake objects specific to this step

            // Used only when validationType == "group": every object in this list must be touched.
            public List<string> targetObjectNames = new List<string>();
            public List<GameObject> targetObjects = new List<GameObject>();
        }

        public class FakeObjectData
        {
            public string objectName;
            public string errorMessage;
            public GameObject gameObject;
        }

        public void Display(string objectId, Dictionary<string, object> contentData, VisualElement root)
        {
            currentObjectId = objectId;
            rootElement = root;
            currentStepIndex = 0;
            totalWrongClicksInProcedure = 0; // Réinitialiser le compteur d'erreurs
            procedureStartTime = Time.time; // Enregistrer le temps de début
            completedSteps = new List<ProcedureStepData>(); // Initialiser la liste des étapes

            // Vérifier si on doit activer le highlight
            if (contentData.ContainsKey("enableHighlight"))
            {
                shouldHighlight = contentData["enableHighlight"] is bool highlight ? highlight : true;
                Debug.Log($"[ProcedureDisplayer] Highlight enabled: {shouldHighlight}");
            }
            else
            {
                shouldHighlight = true; // Par défaut, on active le highlight
            }

            // Vérifier si on doit garder la progression lors d'un clic ailleurs
            if (contentData.ContainsKey("keepProgressOnOtherClick"))
            {
                keepProgressOnOtherClick = contentData["keepProgressOnOtherClick"] is bool keepProgress ? keepProgress : false;
                Debug.Log($"[ProcedureDisplayer] Keep progress on other click: {keepProgressOnOtherClick}");
            }
            else
            {
                keepProgressOnOtherClick = false; // Par défaut, on reset
            }

            string lang = "";

            // Trouver la clé de la procédure (la première clé qui commence par "procedure_")
            procedureKey = contentData.Keys.FirstOrDefault(k => k.StartsWith("procedure_"));
            if (string.IsNullOrEmpty(procedureKey))
            {
                // Fallback : chercher n'importe quelle clé de procédure
                procedureKey = "procedure";
                Debug.LogWarning($"[ProcedureDisplayer] No procedure_ key found in contentData, using fallback 'procedure'");
            }

            // Extraire les données de la procédure
            procedureTitle = ExtractLocalizedText(contentData, "title", lang);
            procedureDescription = ExtractLocalizedText(contentData, "description", lang);

            // Extraire les étapes
            steps = ExtractProcedureSteps(contentData, lang);

            if (steps == null || steps.Count == 0)
            {
                Debug.LogError($"[ProcedureDisplayer] No steps found for procedure {objectId}");
                return;
            }

            // Initialiser les matériaux originaux
            originalMaterials = new Dictionary<Renderer, Material>();
            allSequenceObjects = new List<GameObject>();

            // Trouver les GameObjects pour chaque étape par nom
            foreach (var step in steps)
            {
                // Chercher l'objet target par nom
                if (!string.IsNullOrEmpty(step.targetObjectName))
                {
                    step.targetObject = GameObject.Find(step.targetObjectName);

                    if (step.targetObject != null)
                    {
                        allSequenceObjects.Add(step.targetObject);
                        StoreOriginalMaterials(step.targetObject);

                        Debug.Log($"[ProcedureDisplayer] Found target object for step: {step.targetObjectName}");
                    }
                    else
                    {
                        Debug.LogWarning($"[ProcedureDisplayer] Could not find GameObject with name: {step.targetObjectName}");
                    }
                }

                // Find fake objects for this step
                if (step.fakeObjects != null && step.fakeObjects.Count > 0)
                {
                    foreach (var fake in step.fakeObjects)
                    {
                        if (string.IsNullOrEmpty(fake.objectName)) continue;

                        fake.gameObject = GameObject.Find(fake.objectName);

                        if (fake.gameObject != null)
                        {
                            allSequenceObjects.Add(fake.gameObject);
                            StoreOriginalMaterials(fake.gameObject);

                            Debug.Log($"[ProcedureDisplayer] Found step-specific fake object: {fake.objectName}");
                        }
                        else
                        {
                            Debug.LogWarning($"[ProcedureDisplayer] Could not find step-specific fake GameObject with name: {fake.objectName}");
                        }
                    }
                }

                // Find zone trigger object for this step
                if (step.validationType == "zone" && !string.IsNullOrEmpty(step.zoneObjectName))
                {
                    step.zoneObject = GameObject.Find(step.zoneObjectName);
                    if (step.zoneObject != null)
                    {
                        Debug.Log($"[ProcedureDisplayer] Found zone object for step: {step.zoneObjectName}");
                    }
                    else
                    {
                        Debug.LogWarning($"[ProcedureDisplayer] Could not find zone GameObject with name: {step.zoneObjectName}");
                    }
                }

                // Resolve every GameObject in a group step
                if (step.validationType == "group" && step.targetObjectNames != null)
                {
                    step.targetObjects = new List<GameObject>();
                    foreach (var name in step.targetObjectNames)
                    {
                        if (string.IsNullOrEmpty(name)) continue;
                        var go = GameObject.Find(name);
                        if (go != null)
                        {
                            step.targetObjects.Add(go);
                            allSequenceObjects.Add(go);
                            StoreOriginalMaterials(go);
                        }
                        else
                        {
                            Debug.LogWarning($"[ProcedureDisplayer] Group step: GameObject '{name}' not found");
                        }
                    }
                }
            }

            // Trouver les fake objects par nom
            if (fakeObjects != null)
            {
                foreach (var fake in fakeObjects)
                {
                    if (string.IsNullOrEmpty(fake.objectName)) continue;

                    fake.gameObject = GameObject.Find(fake.objectName);

                    if (fake.gameObject != null)
                    {
                        allSequenceObjects.Add(fake.gameObject);
                        StoreOriginalMaterials(fake.gameObject);

                        Debug.Log($"[ProcedureDisplayer] Found fake object: {fake.objectName}");
                    }
                    else
                    {
                        Debug.LogWarning($"[ProcedureDisplayer] Could not find fake GameObject with name: {fake.objectName}");
                    }
                }
            }

            // Démarrer le tracking de la procédure globale
            if (TrainingAnalytics.Instance != null)
            {
                TrainingAnalytics.Instance.StartProcedureInteraction(currentObjectId, procedureKey, steps.Count);
                Debug.Log($"[ProcedureDisplayer] Started procedure tracking: {procedureKey} with {steps.Count} steps");
            }

            // Créer l'UI
            CreateProcedureUI();

            // Commencer la première étape
            StartCurrentStep();
        }

        void CreateProcedureUI()
        {
            rootElement.Clear();

            // Semi-transparent modal (more transparent to see the scene)
            modalContainer = new VisualElement();
            modalContainer.style.position = Position.Absolute;
            modalContainer.style.width = Length.Percent(100);
            modalContainer.style.height = Length.Percent(100);
            modalContainer.style.backgroundColor = UIStyles.BackdropLight;
            modalContainer.style.alignItems = Align.FlexEnd;
            modalContainer.style.justifyContent = Justify.FlexStart;
            // Let 3D clicks pass through the full-screen backdrop so host interaction scripts
            // (and the package's own ProcedureStepClickHandler) can validate steps by clicking
            // the world. The instructionPanel keeps the default Position pickingMode, so its
            // own area stays clickable for buttons inside the panel.
            modalContainer.pickingMode = PickingMode.Ignore;

            // Instruction panel (default: docked right, but draggable from its header so the
            // player can move it anywhere on screen if it covers a relevant 3D object).
            // Auto-sizes vertically to its content so a short instruction like "Turn the valve"
            // produces a compact card instead of a floor-to-ceiling sidebar. The ScrollView
            // inside still takes over if the content exceeds maxHeight.
            instructionPanel = new VisualElement();
            instructionPanel.name = "procedure-instruction-panel";
            instructionPanel.style.width = Length.Percent(28);
            instructionPanel.style.minWidth = 320;
            instructionPanel.style.maxWidth = 420;
            instructionPanel.style.minHeight = 140;
            instructionPanel.style.maxHeight = Length.Percent(85);
            UIStyles.ApplyCardStyle(instructionPanel, UIStyles.RadiusXL);
            instructionPanel.style.borderLeftWidth = 3;
            instructionPanel.style.borderLeftColor = UIStyles.Accent;
            instructionPanel.style.flexDirection = FlexDirection.Column;
            instructionPanel.style.flexShrink = 1;

            if (hasSavedPanelPosition)
            {
                // Restore the position the user dragged the panel to in a previous scenario.
                instructionPanel.style.position = Position.Absolute;
                instructionPanel.style.left = savedPanelPosition.x;
                instructionPanel.style.top = savedPanelPosition.y;
            }
            else
            {
                // Default layout: top-right via parent's flex alignment.
                instructionPanel.style.marginRight = UIStyles.SpaceLG;
                instructionPanel.style.marginTop = UIStyles.SpaceLG;
            }

            // Drag-handle row at the top of the panel (6 grip dots). Hints that the
            // header is draggable. The whole header area below is the actual hit target.
            dragHandleArea = new VisualElement();
            dragHandleArea.style.paddingTop = UIStyles.SpaceSM;
            dragHandleArea.style.paddingBottom = 2;
            dragHandleArea.style.alignItems = Align.Center;
            dragHandleArea.style.justifyContent = Justify.Center;
            dragHandleArea.Add(WiseTwinIcons.DragHandle(22, UIStyles.TextMuted));
            instructionPanel.Add(dragHandleArea);

            // Header
            var headerSection = new VisualElement();
            headerSection.style.paddingTop = UIStyles.SpaceXS;
            headerSection.style.paddingBottom = UIStyles.SpaceMD;
            headerSection.style.paddingLeft = UIStyles.SpaceXL;
            headerSection.style.paddingRight = UIStyles.SpaceXL;
            headerSection.style.borderBottomWidth = 1;
            headerSection.style.borderBottomColor = UIStyles.BorderSubtle;

            titleLabel = new Label(procedureTitle);
            titleLabel.style.fontSize = UIStyles.FontXL;
            titleLabel.style.color = UIStyles.Accent;
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLabel.style.marginBottom = UIStyles.SpaceXS;
            titleLabel.style.whiteSpace = WhiteSpace.Normal;
            headerSection.Add(titleLabel);

            if (!string.IsNullOrEmpty(procedureDescription))
            {
                descriptionLabel = new Label(procedureDescription);
                descriptionLabel.style.fontSize = UIStyles.FontSM;
                descriptionLabel.style.color = UIStyles.TextMuted;
                descriptionLabel.style.whiteSpace = WhiteSpace.Normal;
                headerSection.Add(descriptionLabel);
            }

            instructionPanel.Add(headerSection);

            // Progress section
            var progressSection = new VisualElement();
            progressSection.style.paddingTop = UIStyles.SpaceMD;
            progressSection.style.paddingBottom = UIStyles.SpaceMD;
            progressSection.style.paddingLeft = UIStyles.SpaceXL;
            progressSection.style.paddingRight = UIStyles.SpaceXL;
            progressSection.style.borderBottomWidth = 1;
            progressSection.style.borderBottomColor = UIStyles.BorderSubtle;

            progressLabel = new Label($"\u00c9tape 1 / {steps.Count}");
            progressLabel.style.fontSize = UIStyles.FontBase;
            progressLabel.style.color = UIStyles.TextPrimary;
            progressLabel.style.marginBottom = UIStyles.SpaceSM;
            progressLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            progressSection.Add(progressLabel);

            var (bar, fill) = UIStyles.CreateProgressBar(8, UIStyles.SpaceXS);
            progressBar = bar;
            progressFill = fill;
            progressFill.style.position = Position.Absolute;
            progressFill.style.height = 8;
            progressSection.Add(progressBar);

            instructionPanel.Add(progressSection);

            // Main scrollable section. flexShrink:1 lets it collapse to content size when
            // text is short; flexGrow:1 ensures it takes the remaining space when the panel
            // hits its maxHeight cap (so the scrollbar appears only when needed).
            var mainSection = new ScrollView();
            mainSection.style.flexGrow = 1;
            mainSection.style.flexShrink = 1;
            mainSection.style.paddingTop = UIStyles.SpaceLG;
            mainSection.style.paddingBottom = UIStyles.SpaceLG;
            mainSection.style.paddingLeft = UIStyles.SpaceXL;
            mainSection.style.paddingRight = UIStyles.SpaceXL;

            stepLabel = new Label();
            stepLabel.style.fontSize = UIStyles.FontMD;
            stepLabel.style.color = UIStyles.TextPrimary;
            stepLabel.style.whiteSpace = WhiteSpace.Normal;
            mainSection.Add(stepLabel);

            // Image container for step images
            imageContainer = new VisualElement();
            imageContainer.style.marginTop = UIStyles.SpaceLG;
            imageContainer.style.marginBottom = UIStyles.SpaceLG;
            imageContainer.style.display = DisplayStyle.None;
            imageContainer.style.alignItems = Align.Center;
            imageContainer.style.flexDirection = FlexDirection.Column;

            imageElement = new VisualElement();
            imageElement.style.width = Length.Percent(100);
            imageElement.style.height = 200;
            imageElement.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
            imageElement.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Center);
            imageElement.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Center);
            UIStyles.SetBorderRadius(imageElement, UIStyles.RadiusSM);
            imageElement.pickingMode = PickingMode.Position;
            imageElement.RegisterCallback<ClickEvent>(OnImageClicked);
            imageContainer.Add(imageElement);

            var imageHintLabel = UIStyles.CreateMutedText("+", UIStyles.FontXS);
            imageHintLabel.name = "image-hint";
            imageHintLabel.style.marginTop = UIStyles.SpaceXS;
            imageContainer.Add(imageHintLabel);

            mainSection.Add(imageContainer);

            // Error feedback label
            errorFeedbackLabel = new Label();
            errorFeedbackLabel.style.fontSize = UIStyles.FontBase;
            errorFeedbackLabel.style.color = UIStyles.Danger;
            errorFeedbackLabel.style.backgroundColor = UIStyles.DangerBg;
            UIStyles.SetPadding(errorFeedbackLabel, UIStyles.SpaceMD);
            errorFeedbackLabel.style.marginTop = UIStyles.SpaceLG;
            UIStyles.SetBorderRadius(errorFeedbackLabel, UIStyles.RadiusSM);
            errorFeedbackLabel.style.whiteSpace = WhiteSpace.Normal;
            errorFeedbackLabel.style.display = DisplayStyle.None;
            mainSection.Add(errorFeedbackLabel);

            instructionPanel.Add(mainSection);

            // Bottom buttons section (only shown for manual validation steps)
            var buttonSection = new VisualElement();
            buttonSection.name = "button-section";
            buttonSection.style.paddingTop = UIStyles.SpaceLG;
            buttonSection.style.paddingBottom = UIStyles.SpaceLG;
            buttonSection.style.paddingLeft = UIStyles.SpaceXL;
            buttonSection.style.paddingRight = UIStyles.SpaceXL;
            buttonSection.style.borderTopWidth = 1;
            buttonSection.style.borderTopColor = UIStyles.BorderSubtle;
            buttonSection.style.display = DisplayStyle.None;

            // Manual validation button (icon only, compact)
            validateButton = UIStyles.CreatePrimaryButton("");
            UIStyles.SetButtonIcon(validateButton, WiseTwinIcons.Check(20, UIStyles.TextOnAccent));
            validateButton.style.alignSelf = Align.Center;
            validateButton.style.width = 80;
            validateButton.style.height = 44;
            validateButton.clicked += OnValidateButtonClicked;
            buttonSection.Add(validateButton);

            instructionPanel.Add(buttonSection);
            modalContainer.Add(instructionPanel);

            rootElement.Add(modalContainer);

            // Wire drag events on the grip strip + header. Either area starts a drag,
            // and pointer capture routes subsequent move/up events back to the same element.
            RegisterDragHandlers(dragHandleArea);
            RegisterDragHandlers(headerSection);
        }

        void RegisterDragHandlers(VisualElement handle)
        {
            handle.RegisterCallback<PointerDownEvent>(OnPanelDragStart);
            handle.RegisterCallback<PointerMoveEvent>(OnPanelDragMove);
            handle.RegisterCallback<PointerUpEvent>(OnPanelDragEnd);
            handle.RegisterCallback<PointerCaptureOutEvent>(OnPanelDragCancel);
        }

        void OnPanelDragStart(PointerDownEvent evt)
        {
            if (evt.button != 0 || instructionPanel == null) return;

            // Switch from parent-flex positioning to absolute, freezing the current on-screen position.
            if (instructionPanel.resolvedStyle.position != Position.Absolute)
            {
                var panelRect = instructionPanel.worldBound;
                var parentRect = instructionPanel.parent.worldBound;
                instructionPanel.style.position = Position.Absolute;
                instructionPanel.style.left = panelRect.x - parentRect.x;
                instructionPanel.style.top = panelRect.y - parentRect.y;
                instructionPanel.style.marginRight = 0;
            }

            isDraggingPanel = true;
            dragPointerId = evt.pointerId;
            var rect = instructionPanel.worldBound;
            dragPointerOffset = new Vector2(evt.position.x - rect.x, evt.position.y - rect.y);

            var target = evt.currentTarget as VisualElement;
            target?.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        void OnPanelDragMove(PointerMoveEvent evt)
        {
            if (!isDraggingPanel || evt.pointerId != dragPointerId || instructionPanel == null) return;

            var parentRect = instructionPanel.parent.worldBound;
            float newLeft = evt.position.x - dragPointerOffset.x - parentRect.x;
            float newTop = evt.position.y - dragPointerOffset.y - parentRect.y;

            float panelWidth = instructionPanel.resolvedStyle.width;
            float panelHeight = instructionPanel.resolvedStyle.height;
            newLeft = Mathf.Clamp(newLeft, 0f, Mathf.Max(0f, parentRect.width - panelWidth));
            newTop = Mathf.Clamp(newTop, 0f, Mathf.Max(0f, parentRect.height - panelHeight));

            instructionPanel.style.left = newLeft;
            instructionPanel.style.top = newTop;

            savedPanelPosition = new Vector2(newLeft, newTop);
            hasSavedPanelPosition = true;
        }

        void OnPanelDragEnd(PointerUpEvent evt)
        {
            if (!isDraggingPanel || evt.pointerId != dragPointerId) return;
            EndDrag(evt.currentTarget as VisualElement, evt.pointerId);
            evt.StopPropagation();
        }

        void OnPanelDragCancel(PointerCaptureOutEvent evt)
        {
            if (!isDraggingPanel || evt.pointerId != dragPointerId) return;
            EndDrag(evt.currentTarget as VisualElement, evt.pointerId);
        }

        void EndDrag(VisualElement target, int pointerId)
        {
            isDraggingPanel = false;
            dragPointerId = -1;
            if (target != null && target.HasPointerCapture(pointerId))
            {
                target.ReleasePointer(pointerId);
            }
        }

        void StartCurrentStep()
        {
            if (currentStepIndex >= steps.Count)
            {
                CompleteProcedure();
                return;
            }

            var currentStep = steps[currentStepIndex];
            stepStartTime = Time.time;
            wrongClicksCount = 0;

            // Cacher le feedback d'erreur de l'étape précédente
            if (errorFeedbackLabel != null)
            {
                errorFeedbackLabel.style.display = DisplayStyle.None;
            }

            // Numeric progress only (language-neutral)
            progressLabel.text = $"{currentStepIndex + 1} / {steps.Count}";

            // Afficher le titre de l'étape si présent, suivi de l'instruction
            if (!string.IsNullOrEmpty(currentStep.title))
            {
                stepLabel.text = $"<b><size=22>{currentStep.title}</size></b>\n\n{currentStep.instruction}";
            }
            else
            {
                stepLabel.text = currentStep.instruction;
            }

            // Ajouter le hint si présent
            if (!string.IsNullOrEmpty(currentStep.hint))
            {
                stepLabel.text += $"\n\n{currentStep.hint}";
            }

            // NEW: Show/hide image if available
            if (!string.IsNullOrEmpty(currentStep.imagePath))
            {
                ShowStepImage(currentStep.imagePath);
            }
            else
            {
                HideStepImage();
            }

            // Show/hide manual validation button (only for manual validation type)
            // Footer is only shown for manual validation steps
            var buttonSection = modalContainer?.Q<VisualElement>("button-section");
            if (buttonSection != null)
            {
                buttonSection.style.display = currentStep.validationType == "manual"
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            }
            if (validateButton != null)
            {
                validateButton.style.display = currentStep.validationType == "manual"
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
                UIStyles.SetButtonIcon(validateButton, WiseTwinIcons.Check(20, UIStyles.TextOnAccent));
            }

            // Mettre à jour la barre de progression
            float progress = (float)currentStepIndex / steps.Count * 100f;
            progressFill.style.width = Length.Percent(progress);

            // Mettre à jour le texte d'instruction selon le contexte
            UpdateInstructionLabel(currentStep);

            // IMPORTANT : Nettoyer TOUS les objets de la séquence avant de démarrer la nouvelle étape
            // Cela évite qu'un objet garde un handler ou une surbrillance de l'étape précédente
            foreach (var obj in allSequenceObjects)
            {
                if (obj != null)
                {
                    // Retirer le handler de clic
                    var oldHandler = obj.GetComponent<ProcedureStepClickHandler>();
                    if (oldHandler != null)
                    {
                        Destroy(oldHandler);
                    }

                    // Retirer la surbrillance
                    if (shouldHighlight)
                    {
                        RemoveHighlight(obj);
                    }
                }
            }

            // Nettoyer les zone triggers des étapes précédentes
            CleanupZoneTriggers();

            currentHighlightedObjects.Clear();
            currentCorrectObject = null;
            currentGroupRemaining.Clear();

            // Configurer l'étape selon le type de validation
            switch (currentStep.validationType)
            {
                case "click":
                    // Surligner TOUS les objets de l'étape actuelle (target + fakes)
                    if (currentStep.targetObject != null)
                    {
                        currentCorrectObject = currentStep.targetObject;

                        // Créer une liste de tous les objets à surligner
                        var objectsToHighlight = new List<GameObject> { currentStep.targetObject };

                        // Ajouter les fake objects spécifiques à cette étape
                        if (currentStep.fakeObjects != null && currentStep.fakeObjects.Count > 0)
                        {
                            foreach (var fake in currentStep.fakeObjects)
                            {
                                if (fake.gameObject != null)
                                {
                                    objectsToHighlight.Add(fake.gameObject);
                                }
                            }
                        }

                        foreach (var obj in objectsToHighlight)
                        {
                            if (obj == null) continue;

                            // Surligner l'objet UNIQUEMENT si shouldHighlight ET useBlinking sont activés
                            if (shouldHighlight && currentStep.useBlinking)
                            {
                                HighlightObject(obj, currentStep.useBlinking);
                            }

                            currentHighlightedObjects.Add(obj);

                            // Ajouter un composant pour gérer le clic
                            var clickHandler = obj.AddComponent<ProcedureStepClickHandler>();
                            clickHandler.Initialize(this, currentStepIndex, obj);
                        }
                    }
                    break;

                case "zone":
                    // Zone trigger mode - add ProcedureZoneTrigger to the zone object
                    if (currentStep.zoneObject != null)
                    {
                        // Réactiver la zone si elle a été désactivée par un effet de collecte précédent
                        if (!currentStep.zoneObject.activeSelf)
                        {
                            currentStep.zoneObject.SetActive(true);
                            currentStep.zoneObject.transform.localScale = Vector3.one;
                        }

                        var zoneTrigger = currentStep.zoneObject.AddComponent<ProcedureZoneTrigger>();
                        zoneTrigger.Initialize(this, currentStepIndex);

                        // Optionally highlight the target object if present (visual cue only, no click handler)
                        if (currentStep.targetObject != null && shouldHighlight && currentStep.useBlinking)
                        {
                            HighlightObject(currentStep.targetObject, currentStep.useBlinking);
                            currentHighlightedObjects.Add(currentStep.targetObject);
                        }

                        Debug.Log($"[ProcedureDisplayer] Zone trigger mode - waiting for player to enter zone: {currentStep.zoneObjectName}");
                    }
                    else
                    {
                        Debug.LogWarning($"[ProcedureDisplayer] Zone object not found: {currentStep.zoneObjectName}");
                    }
                    break;

                case "manual":
                    // Manual validation mode - no object highlighting or click handlers needed.
                    // The footer "Validate Step" button is shown so the player can advance.
                    Debug.Log("[ProcedureDisplayer] Manual validation mode - no object interaction required");
                    break;

                case "external":
                    // External validation mode - the step is advanced only by an external call
                    // to WiseTwinAPI.ValidateCurrentStep(). No highlight, no zone, no validate
                    // button: the package does not give the player any way to skip the step.
                    Debug.Log("[ProcedureDisplayer] External validation mode - waiting for WiseTwinAPI.ValidateCurrentStep()");
                    break;

                case "group":
                    // Group mode: highlight every target object and add a click handler on each.
                    // Step advances when all objects in currentGroupRemaining have been clicked.
                    if (currentStep.targetObjects != null && currentStep.targetObjects.Count > 0)
                    {
                        foreach (var obj in currentStep.targetObjects)
                        {
                            if (obj == null) continue;
                            currentGroupRemaining.Add(obj);

                            if (shouldHighlight && currentStep.useBlinking)
                            {
                                HighlightObject(obj, currentStep.useBlinking);
                            }
                            currentHighlightedObjects.Add(obj);

                            var clickHandler = obj.AddComponent<ProcedureStepClickHandler>();
                            clickHandler.Initialize(this, currentStepIndex, obj);
                        }

                        // Fake objects for groups behave the same as click steps
                        if (currentStep.fakeObjects != null)
                        {
                            foreach (var fake in currentStep.fakeObjects)
                            {
                                if (fake.gameObject == null) continue;
                                if (shouldHighlight && currentStep.useBlinking)
                                {
                                    HighlightObject(fake.gameObject, currentStep.useBlinking);
                                }
                                currentHighlightedObjects.Add(fake.gameObject);
                                var fakeHandler = fake.gameObject.AddComponent<ProcedureStepClickHandler>();
                                fakeHandler.Initialize(this, currentStepIndex, fake.gameObject);
                            }
                        }

                        Debug.Log($"[ProcedureDisplayer] Group step: waiting for {currentGroupRemaining.Count} objects to be clicked");
                    }
                    else
                    {
                        Debug.LogWarning("[ProcedureDisplayer] Group step has no target objects — auto-validating");
                        ValidateCurrentStep(success: true);
                    }
                    break;
            }
        }

        // Instruction label was removed from the procedure UI (footer hidden for click/zone).
        // Kept as a no-op stub to avoid touching every caller.
        void UpdateInstructionLabel(ProcedureStep step) { }

        /// <summary>
        /// Stocke les matériaux originaux de tous les Renderers d'un objet et ses enfants
        /// </summary>
        void StoreOriginalMaterials(GameObject obj)
        {
            var renderers = obj.GetComponentsInChildren<Renderer>();
            foreach (var renderer in renderers)
            {
                if (!originalMaterials.ContainsKey(renderer))
                {
                    originalMaterials[renderer] = renderer.material;
                }
            }
        }

        void HighlightObject(GameObject obj, bool useBlinking)
        {
            if (obj == null) return;

            var renderers = obj.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;

            foreach (var renderer in renderers)
            {
                // Créer un nouveau matériau avec émission (garde la couleur d'origine)
                Material highlightMaterial = new Material(renderer.material);

                // Activer l'émission
                highlightMaterial.EnableKeyword("_EMISSION");
                highlightMaterial.SetColor("_EmissionColor", highlightColor * highlightIntensity);

                renderer.material = highlightMaterial;
            }

            // Ajouter un composant pour l'animation de pulsation UNIQUEMENT si useBlinking est activé
            if (useBlinking && pulseHighlight)
            {
                // Détruire l'ancien PulseEffect s'il existe (peut rester d'une étape précédente)
                var oldPulse = obj.GetComponent<PulseEffect>();
                if (oldPulse != null)
                {
                    DestroyImmediate(oldPulse);
                }

                // Ajouter un nouveau PulseEffect (gère tous les enfants via GetComponentsInChildren)
                var pulse = obj.AddComponent<PulseEffect>();
                pulse.Initialize(highlightColor, highlightIntensity, pulseSpeed);
                Debug.Log($"[ProcedureDisplayer] Blinking enabled for object: {obj.name} ({renderers.Length} renderers)");
            }
            else
            {
                Debug.Log($"[ProcedureDisplayer] Blinking disabled for object: {obj.name}");
            }
        }

        void RemoveHighlight(GameObject obj)
        {
            if (obj == null) return;

            var renderers = obj.GetComponentsInChildren<Renderer>();

            // Restaurer les matériaux originaux de tous les renderers
            foreach (var renderer in renderers)
            {
                if (originalMaterials.ContainsKey(renderer))
                {
                    renderer.material = originalMaterials[renderer];
                }
            }

            // Retirer l'effet de pulsation
            var pulse = obj.GetComponent<PulseEffect>();
            if (pulse != null)
            {
                Destroy(pulse);
            }
        }

        /// <summary>
        /// Enable/disable object interaction (no longer uses InteractableObject component)
        /// Objects are now interacted with directly via raycasts in the procedure system
        /// </summary>
        void EnableObjectInteraction(GameObject obj, bool enabled)
        {
            // In the new system, we don't need to enable/disable anything
            // Objects are clicked directly, and the procedure system handles validation
            // This method is kept for compatibility but does nothing
        }

        void ShowErrorFeedback(string customMessage = null)
        {
            if (errorFeedbackLabel == null || currentStepIndex >= steps.Count) return;

            string message;

            // Use custom message if provided, otherwise minimal counter-only feedback.
            // We use plain ASCII "x" instead of \u2717 (\u2717) \u2014 Unicode dingbat glyphs are
            // missing from the WebGL bundled font and produce empty squares in production.
            if (!string.IsNullOrEmpty(customMessage))
            {
                message = $"{customMessage}  x{wrongClicksCount}";
            }
            else
            {
                message = $"x{wrongClicksCount}";
            }

            errorFeedbackLabel.text = message;
            errorFeedbackLabel.style.display = DisplayStyle.Flex;

            // Cacher le message après 3 secondes
            StartCoroutine(HideErrorFeedbackAfterDelay(3f));
        }

        System.Collections.IEnumerator HideErrorFeedbackAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);
            if (errorFeedbackLabel != null)
            {
                errorFeedbackLabel.style.display = DisplayStyle.None;
            }
        }

        /// <summary>
        /// Called by ProcedureStepClickHandler when the user clicks an object in the scene.
        /// Checks whether the clicked object matches the current step's expected object,
        /// and either advances the procedure or records a wrong click + error feedback.
        /// </summary>
        public void OnObjectClicked(GameObject clickedObject)
        {
            if (currentStepIndex >= steps.Count) return;

            var currentStep = steps[currentStepIndex];

            // Guard: zone steps are validated via ProcedureZoneTrigger, not clicks
            if (currentStep.validationType == "zone") return;

            // Group step: each click in the set "consumes" one target. The step advances
            // when the set becomes empty.
            if (currentStep.validationType == "group")
            {
                if (currentGroupRemaining.Contains(clickedObject))
                {
                    currentGroupRemaining.Remove(clickedObject);

                    // Stop blinking + remove the click handler on this specific object
                    if (shouldHighlight)
                    {
                        RemoveHighlight(clickedObject);
                    }
                    var handler = clickedObject.GetComponent<ProcedureStepClickHandler>();
                    if (handler != null) Destroy(handler);

                    Debug.Log($"[ProcedureDisplayer] Group: '{clickedObject.name}' clicked, {currentGroupRemaining.Count} remaining");

                    if (currentGroupRemaining.Count == 0)
                    {
                        ValidateCurrentStep(success: true);
                    }
                    return;
                }

                // Otherwise it's a fake (or stale) click — fall through to the wrong-click path
                wrongClicksCount++;
                totalWrongClicksInProcedure++;

                string customErrorMessage = null;
                if (currentStep.fakeObjects != null)
                {
                    foreach (var fake in currentStep.fakeObjects)
                    {
                        if (fake.gameObject == clickedObject)
                        {
                            customErrorMessage = fake.errorMessage;
                            break;
                        }
                    }
                }
                ShowErrorFeedback(customErrorMessage);
                return;
            }

            // Wrong object → record error and stay on current step
            if (clickedObject != currentCorrectObject)
            {
                wrongClicksCount++;
                totalWrongClicksInProcedure++;

                Debug.Log($"[ProcedureDisplayer] Wrong object clicked! Expected: {currentCorrectObject?.name}, Got: {clickedObject?.name}. Wrong clicks: {wrongClicksCount}");

                string customErrorMessage = null;
                if (currentStep.fakeObjects != null)
                {
                    foreach (var fake in currentStep.fakeObjects)
                    {
                        if (fake.gameObject == clickedObject)
                        {
                            customErrorMessage = fake.errorMessage;
                            break;
                        }
                    }
                }
                if (customErrorMessage == null && fakeObjects != null)
                {
                    foreach (var fake in fakeObjects)
                    {
                        if (fake.gameObject == clickedObject)
                        {
                            customErrorMessage = fake.errorMessage;
                            break;
                        }
                    }
                }

                ShowErrorFeedback(customErrorMessage);
                return;
            }

            // Right object: only auto-validate when not in manual mode (manual = wait for button)
            if (currentStep.requireManualValidation)
            {
                Debug.Log("[ProcedureDisplayer] Step with manual validation - waiting for validate button");
                return;
            }

            ValidateCurrentStep(success: true);
        }

        /// <summary>
        /// Single funnel that finalises the current step (records analytics, advances to the next step).
        /// Called internally by OnObjectClicked, OnZoneEntered, and OnValidateButtonClicked.
        /// Also exposed via WiseTwinAPI.ValidateCurrentStep() so external scripts can validate a step
        /// from custom 3D logic (e.g. an object reaching a target position).
        /// </summary>
        /// <param name="success">true if the step is completed successfully, false to record a failed step</param>
        /// <returns>true if a step was advanced; false if there is no current step</returns>
        public bool ValidateCurrentStep(bool success = true)
        {
            if (steps == null || currentStepIndex >= steps.Count) return false;

            var currentStep = steps[currentStepIndex];
            currentStep.completed = success;

            float stepDuration = Time.time - stepStartTime;
            string targetId = currentStep.validationType == "zone"
                ? currentStep.zoneObjectName
                : currentStep.targetObjectName;

            var stepData = new ProcedureStepData
            {
                stepNumber = currentStepIndex + 1,
                stepKey = $"step_{currentStepIndex + 1}",
                targetObjectId = targetId,
                completed = success,
                duration = stepDuration,
                wrongClicksOnThisStep = wrongClicksCount
            };

            completedSteps.Add(stepData);

            if (TrainingAnalytics.Instance != null)
            {
                TrainingAnalytics.Instance.AddProcedureStepData(stepData);
            }

            if (errorFeedbackLabel != null)
            {
                errorFeedbackLabel.style.display = DisplayStyle.None;
            }

            Debug.Log($"[ProcedureDisplayer] Step {stepData.stepNumber} validated (success: {success}, duration: {stepDuration:F2}s, wrong clicks: {wrongClicksCount})");

            WiseTwinAPI.RaiseStepValidated(currentStepIndex, success);

            // Click steps get a small visual delay; zone/manual advance immediately
            if (currentStep.validationType == "click")
            {
                StartCoroutine(NextStepAfterDelay(0.5f));
            }
            else
            {
                currentStepIndex++;
                StartCurrentStep();
            }

            return true;
        }

        System.Collections.IEnumerator NextStepAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);

            currentStepIndex++;
            StartCurrentStep();
        }

        /// <summary>
        /// Réinitialise la procédure
        /// Note: Cette méthode n'est plus appelée automatiquement pour les clics hors séquence
        /// </summary>
        public void ResetProcedure()
        {
            Debug.Log("[ProcedureDisplayer] Resetting procedure manually");

            // Retirer les surbrillances actuelles si elles sont actives
            if (shouldHighlight)
            {
                foreach (var obj in currentHighlightedObjects)
                {
                    if (obj != null)
                    {
                        RemoveHighlight(obj);

                        // Retirer le composant de clic temporaire
                        var clickHandler = obj.GetComponent<ProcedureStepClickHandler>();
                        if (clickHandler != null)
                        {
                            Destroy(clickHandler);
                        }
                    }
                }
            }
            currentHighlightedObjects.Clear();

            // Réinitialiser l'index
            currentStepIndex = 0;

            // Réactiver toutes les interactions
            foreach (var obj in allSequenceObjects)
            {
                EnableObjectInteraction(obj, true);
            }

            // Redémarrer la première étape
            StartCurrentStep();
        }

        void OnValidateButtonClicked()
        {
            ValidateCurrentStep(success: true);
        }

        /// <summary>
        /// Called by ProcedureZoneTrigger when the player enters the zone.
        /// </summary>
        public void OnZoneEntered()
        {
            if (currentStepIndex >= steps.Count) return;
            if (steps[currentStepIndex].validationType != "zone") return;

            ValidateCurrentStep(success: true);
        }

        /// <summary>
        /// Clean up ProcedureZoneTrigger components from all steps
        /// </summary>
        void CleanupZoneTriggers()
        {
            foreach (var step in steps)
            {
                if (step.zoneObject != null)
                {
                    var zoneTrigger = step.zoneObject.GetComponent<ProcedureZoneTrigger>();
                    if (zoneTrigger != null)
                    {
                        Destroy(zoneTrigger);
                    }

                    // Détruire l'effet de collecte s'il est en cours
                    var collectEffect = step.zoneObject.GetComponent<ZoneCollectEffect>();
                    if (collectEffect != null)
                    {
                        Destroy(collectEffect);
                    }
                }
            }
        }

        // Show step image
        void ShowStepImage(string imagePath)
        {
            if (imageContainer == null || imageElement == null) return;

            Debug.Log($"[ProcedureDisplayer] ShowStepImage called with path: {imagePath}");

            // Try to load the image from StreamingAssets or Resources
            // For now, we'll use the path as-is (it should be a path to a Sprite asset)
            var texture = LoadImageTexture(imagePath);

            if (texture != null)
            {
                Debug.Log($"[ProcedureDisplayer] Image loaded successfully, setting as background");
                imageElement.style.backgroundImage = new StyleBackground(texture);
                imageContainer.style.display = DisplayStyle.Flex;

                // Add hover effect to show it's clickable
                imageElement.RegisterCallback<MouseEnterEvent>(evt =>
                {
                    if (!imageZoomed)
                    {
                        imageElement.style.opacity = 0.85f;
                        UIStyles.SetBorderWidth(imageElement, 2);
                        var accentHalf = new Color(UIStyles.Accent.r, UIStyles.Accent.g, UIStyles.Accent.b, 0.5f);
                        UIStyles.SetBorderColor(imageElement, accentHalf);
                    }
                });

                imageElement.RegisterCallback<MouseLeaveEvent>(evt =>
                {
                    if (!imageZoomed)
                    {
                        imageElement.style.opacity = 1f;
                        UIStyles.SetBorderWidth(imageElement, 0);
                    }
                });

                imageZoomed = false;
            }
            else
            {
                Debug.LogWarning($"[ProcedureDisplayer] Failed to load image from path: {imagePath}");
                HideStepImage();
            }
        }

        // NEW: Hide step image
        void HideStepImage()
        {
            if (imageContainer != null)
            {
                imageContainer.style.display = DisplayStyle.None;
            }
            imageZoomed = false;
        }

        // NEW: Handle image click for zoom
        void OnImageClicked(ClickEvent evt)
        {
            if (imageElement == null) return;

            Debug.Log($"[ProcedureDisplayer] Image clicked, zoomed state: {imageZoomed}");

            if (!imageZoomed)
            {
                // Zoom in - create a full-screen overlay at the root of the UI
                Debug.Log("[ProcedureDisplayer] Zooming in image");

                // Get the root visual element (full screen)
                var uiDocument = rootElement.panel.visualTree;

                // Create full-screen overlay
                var overlay = new VisualElement();
                overlay.name = "image-zoom-overlay";
                overlay.style.position = Position.Absolute;
                overlay.style.left = 0;
                overlay.style.top = 0;
                overlay.style.width = Length.Percent(100);
                overlay.style.height = Length.Percent(100);
                overlay.style.backgroundColor = UIStyles.BackdropHeavy;
                overlay.style.justifyContent = Justify.Center;
                overlay.style.alignItems = Align.Center;
                overlay.pickingMode = PickingMode.Position;

                // Create a copy of the image for the zoom view
                var zoomedImage = new VisualElement();
                zoomedImage.style.width = Length.Percent(90);
                zoomedImage.style.height = Length.Percent(90);
                zoomedImage.style.backgroundImage = imageElement.style.backgroundImage;
                zoomedImage.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
                zoomedImage.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Center);
                zoomedImage.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Center);

                // Close hint (drawn icon \u2014 Unicode \u2715 is missing from WebGL fonts)
                var closeIcon = WiseTwinIcons.CloseX(20, UIStyles.TextPrimary);
                closeIcon.style.position = Position.Absolute;
                closeIcon.style.top = 20;
                closeIcon.style.right = 20;

                overlay.Add(zoomedImage);
                overlay.Add(closeIcon);

                // Add to root of UI (full screen)
                uiDocument.Add(overlay);
                overlay.BringToFront(); // Ensure it's on top of everything

                // Click overlay to close
                overlay.RegisterCallback<ClickEvent>(closeEvt =>
                {
                    Debug.Log("[ProcedureDisplayer] Closing zoomed image");
                    uiDocument.Remove(overlay);
                    imageZoomed = false;
                    evt.StopPropagation(); // Prevent the click from triggering again
                });

                imageZoomed = true;
                evt.StopPropagation(); // Prevent the click from bubbling up
            }
        }

        // NEW: Load image texture from path
        Texture2D LoadImageTexture(string imagePath)
        {
            if (string.IsNullOrEmpty(imagePath)) return null;

            Debug.Log($"[ProcedureDisplayer] Attempting to load image from: {imagePath}");

            // Clean up the path for Resources.Load
            string resourcePath = imagePath;

            // Remove "Assets/Resources/" prefix if present
            if (resourcePath.StartsWith("Assets/Resources/"))
            {
                resourcePath = resourcePath.Substring("Assets/Resources/".Length);
            }
            else if (resourcePath.StartsWith("Resources/"))
            {
                resourcePath = resourcePath.Substring("Resources/".Length);
            }

            // Remove file extension if present
            if (resourcePath.Contains("."))
            {
                resourcePath = resourcePath.Substring(0, resourcePath.LastIndexOf('.'));
            }

            Debug.Log($"[ProcedureDisplayer] Cleaned resource path: {resourcePath}");

            // Try to load as Texture2D first (for PNG/JPG)
            var texture = Resources.Load<Texture2D>(resourcePath);
            if (texture != null)
            {
                Debug.Log($"[ProcedureDisplayer] Successfully loaded texture: {resourcePath}");
                return texture;
            }

            // Try to load as Sprite
            var sprite = Resources.Load<Sprite>(resourcePath);
            if (sprite != null)
            {
                Debug.Log($"[ProcedureDisplayer] Successfully loaded sprite: {resourcePath}");
                return sprite.texture;
            }

            // If not in Resources, try loading from StreamingAssets
            string streamingPath = System.IO.Path.Combine(Application.streamingAssetsPath, imagePath);
            if (System.IO.File.Exists(streamingPath))
            {
                Debug.Log($"[ProcedureDisplayer] Loading from StreamingAssets: {streamingPath}");
                byte[] imageData = System.IO.File.ReadAllBytes(streamingPath);
                Texture2D tex = new Texture2D(2, 2);
                if (tex.LoadImage(imageData))
                {
                    return tex;
                }
            }

            Debug.LogWarning($"[ProcedureDisplayer] Could not load image from path: {imagePath} (cleaned: {resourcePath})");
            return null;
        }

        void Update()
        {
            // Plus besoin de détecter les clics en dehors de la séquence
            // On ne compte les erreurs que quand l'utilisateur clique sur un objet surligné mais mauvais
            // Ce qui est géré directement dans ValidateCurrentStep()
        }

        void CompleteProcedure()
        {
            // Retirer toutes les surbrillances si elles étaient actives
            if (shouldHighlight)
            {
                foreach (var obj in allSequenceObjects)
                {
                    if (obj != null)
                    {
                        RemoveHighlight(obj);
                        EnableObjectInteraction(obj, true);
                    }
                }
            }
            else
            {
                // Juste réactiver les interactions si pas de highlight
                foreach (var obj in allSequenceObjects)
                {
                    if (obj != null)
                    {
                        EnableObjectInteraction(obj, true);
                    }
                }
            }

            // Calculer la durée totale de la procédure
            float totalDuration = Time.time - procedureStartTime;
            bool perfectCompletion = totalWrongClicksInProcedure == 0;

            // Terminer le tracking de la procédure globale
            if (TrainingAnalytics.Instance != null)
            {
                TrainingAnalytics.Instance.CompleteProcedureInteraction(perfectCompletion, totalWrongClicksInProcedure, totalDuration);
            }

            Debug.Log($"[ProcedureDisplayer] Procedure completed - Duration: {totalDuration}s, Total wrong clicks: {totalWrongClicksInProcedure}, Perfect: {perfectCompletion}");

            // Envoyer l'événement de complétion AVANT de fermer pour que ContentDisplayManager puisse le gérer
            OnCompleted?.Invoke(currentObjectId, true);

            // Fermer après avoir envoyé l'événement
            Close();
        }


        public void Close()
        {

            // Nettoyer tous les GameObjects actuellement surlignés si le highlight était actif
            if (shouldHighlight)
            {
                foreach (var obj in currentHighlightedObjects)
                {
                    if (obj != null)
                    {
                        RemoveHighlight(obj);

                        // Retirer le composant de clic temporaire
                        var clickHandler = obj.GetComponent<ProcedureStepClickHandler>();
                        if (clickHandler != null)
                        {
                            Destroy(clickHandler);
                        }
                    }
                }
            }
            currentHighlightedObjects.Clear();
            currentCorrectObject = null;

            // Nettoyer toutes les surbrillances et réactiver les interactions
            if (allSequenceObjects != null)
            {
                foreach (var obj in allSequenceObjects)
                {
                    if (obj != null)
                    {
                        if (shouldHighlight)
                        {
                            RemoveHighlight(obj);
                        }
                        EnableObjectInteraction(obj, true);

                        // S'assurer qu'aucun handler ne reste
                        var handler = obj.GetComponent<ProcedureStepClickHandler>();
                        if (handler != null)
                        {
                            Destroy(handler);
                        }
                    }
                }
            }

            // Nettoyer les zone triggers
            if (steps != null)
            {
                CleanupZoneTriggers();
            }

            // Réinitialiser l'état
            currentStepIndex = 0;
            steps = null;
            allSequenceObjects = null;
            originalMaterials?.Clear();

            rootElement?.Clear();
            OnClosed?.Invoke(currentObjectId);
        }

        List<ProcedureStep> ExtractProcedureSteps(Dictionary<string, object> data, string language)
        {
            var procedureSteps = new List<ProcedureStep>();

            // NEW FORMAT: Check for "steps" array (scenario-based metadata)
            if (data.ContainsKey("steps"))
            {
                var stepsData = data["steps"];
                if (TryConvertToList(stepsData, out List<Dictionary<string, object>> stepsList))
                {
                    foreach (var stepData in stepsList)
                    {
                        // Determine validationType: use "validationType" field if present, fallback to "requireManualValidation" for backward compat
                        string valType = ExtractString(stepData, "validationType");
                        if (string.IsNullOrEmpty(valType))
                        {
                            valType = ExtractBool(stepData, "requireManualValidation", false) ? "manual" : "click";
                        }

                        var step = new ProcedureStep
                        {
                            targetObjectName = ExtractString(stepData, "targetObjectName"),
                            instruction = ExtractLocalizedText(stepData, "text", language),
                            hint = ExtractLocalizedText(stepData, "hint", language),
                            highlightColor = ParseColor(ExtractString(stepData, "highlightColor"), Color.yellow),
                            useBlinking = ExtractBool(stepData, "useBlinking", true),
                            validationType = valType,
                            zoneObjectName = ExtractString(stepData, "zoneObjectName"),
                            imagePath = ExtractLocalizedText(stepData, "imagePath", language)
                        };

                        // NEW: Extract fake objects for this step
                        if (stepData.ContainsKey("fakeObjects") && TryConvertToList(stepData["fakeObjects"], out List<Dictionary<string, object>> stepFakeList))
                        {
                            foreach (var fakeData in stepFakeList)
                            {
                                step.fakeObjects.Add(new FakeObjectData
                                {
                                    objectName = ExtractString(fakeData, "objectName"),
                                    errorMessage = ExtractLocalizedText(fakeData, "errorMessage", language)
                                });
                            }
                        }

                        // Group target list (only used when validationType == "group")
                        if (stepData.ContainsKey("targetObjectNames") && stepData["targetObjectNames"] is Newtonsoft.Json.Linq.JArray namesArr)
                        {
                            foreach (var n in namesArr)
                            {
                                string objName = n?.ToString();
                                if (!string.IsNullOrEmpty(objName)) step.targetObjectNames.Add(objName);
                            }
                        }

                        procedureSteps.Add(step);
                    }

                    // Extract global fake objects (for backward compatibility)
                    if (data.ContainsKey("fakeObjects") && TryConvertToList(data["fakeObjects"], out List<Dictionary<string, object>> fakeList))
                    {
                        fakeObjects = new List<FakeObjectData>();
                        foreach (var fakeData in fakeList)
                        {
                            fakeObjects.Add(new FakeObjectData
                            {
                                objectName = ExtractString(fakeData, "objectName"),
                                errorMessage = ExtractLocalizedText(fakeData, "errorMessage", language)
                            });
                        }
                    }

                    return procedureSteps;
                }
            }

            // LEGACY FORMAT: Check for step_1, step_2, etc. (old system - kept for backward compatibility)
            var stepKeys = data.Keys
                .Where(k => k.StartsWith("step_"))
                .OrderBy(k =>
                {
                    if (int.TryParse(k.Replace("step_", ""), out int stepNumber))
                        return stepNumber;
                    return 999;
                })
                .ToList();

            foreach (var stepKey in stepKeys)
            {
                if (data[stepKey] is Dictionary<string, object> stepData ||
                    (data[stepKey] != null && TryConvertToDict(data[stepKey], out stepData)))
                {
                    var step = new ProcedureStep
                    {
                        targetObjectName = ExtractString(stepData, "correctObjectId"), // Legacy: use correctObjectId as targetObjectName
                        title = ExtractLocalizedText(stepData, "title", language),
                        instruction = ExtractLocalizedText(stepData, "instruction", language),
                        validation = ExtractLocalizedText(stepData, "validation", language),
                        hint = ExtractLocalizedText(stepData, "hint", language)
                    };

                    procedureSteps.Add(step);
                }
            }

            return procedureSteps;
        }

        bool TryConvertToList(object obj, out List<Dictionary<string, object>> list)
        {
            list = null;
            if (obj == null) return false;

            try
            {
                string json = Newtonsoft.Json.JsonConvert.SerializeObject(obj);
                list = Newtonsoft.Json.JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(json);
                return list != null;
            }
            catch
            {
                return false;
            }
        }

        Color ParseColor(string colorHex, Color defaultColor)
        {
            if (string.IsNullOrEmpty(colorHex)) return defaultColor;

            if (ColorUtility.TryParseHtmlString(colorHex, out Color color))
            {
                return color;
            }

            return defaultColor;
        }

        bool ExtractBool(Dictionary<string, object> data, string key, bool defaultValue)
        {
            if (!data.ContainsKey(key)) return defaultValue;

            var value = data[key];
            if (value is bool boolValue) return boolValue;
            if (value is string strValue && bool.TryParse(strValue, out bool parsed)) return parsed;

            return defaultValue;
        }

        bool TryConvertToDict(object obj, out Dictionary<string, object> dict)
        {
            dict = null;
            if (obj == null) return false;

            try
            {
                string json = Newtonsoft.Json.JsonConvert.SerializeObject(obj);
                dict = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                return dict != null;
            }
            catch
            {
                return false;
            }
        }

        // Flat string extraction (mono-language, with legacy {en, fr} backward compat)
        string ExtractLocalizedText(Dictionary<string, object> data, string key, string language)
        {
            return LocalizedValueReader.ReadString(data, key);
        }

        string ExtractString(Dictionary<string, object> data, string key)
        {
            return data.ContainsKey(key) ? data[key]?.ToString() ?? "" : "";
        }
    }

    /// <summary>
    /// Effet de pulsation pour les objets mis en surbrillance
    /// </summary>
    public class PulseEffect : MonoBehaviour
    {
        private Renderer[] objectRenderers;
        private Color baseColor;
        private float intensity;
        private float speed;
        private float time;

        public void Initialize(Color color, float emissionIntensity, float pulseSpeed)
        {
            objectRenderers = GetComponentsInChildren<Renderer>();
            baseColor = color;
            intensity = emissionIntensity;
            speed = pulseSpeed;
        }

        void Update()
        {
            if (objectRenderers == null || objectRenderers.Length == 0) return;

            time += Time.deltaTime * speed;
            float pulse = (Mathf.Sin(time) + 1f) / 2f; // Valeur entre 0 et 1
            // Pulse entre 0 (couleur originale) et intensité max (jaune brillant)
            float currentIntensity = Mathf.Lerp(0f, intensity, pulse);

            foreach (var renderer in objectRenderers)
            {
                if (renderer != null && renderer.material.HasProperty("_EmissionColor"))
                {
                    renderer.material.SetColor("_EmissionColor", baseColor * currentIntensity);
                }
            }
        }

        void OnDestroy()
        {
            // Nettoyer si nécessaire
        }
    }
}