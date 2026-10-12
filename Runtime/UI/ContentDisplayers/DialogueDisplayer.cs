using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;
using System;
using WiseTwin.Analytics;

namespace WiseTwin.UI
{
    /// <summary>
    /// Afficheur spécialisé pour les dialogues interactifs (arbre de conversation avec PNJ).
    /// Style conversation avec avatar, bulles de dialogue, et choix de réponse.
    /// </summary>
    public class DialogueDisplayer : MonoBehaviour, IContentDisplayer
    {
        public event Action<string> OnClosed;
        public event Action<string, bool> OnCompleted;

        private string currentObjectId;
        private VisualElement rootElement;
        private VisualElement modalContainer;
        private VisualElement dialogueBox;
        private ScrollView conversationScroll;
        private VisualElement conversationContainer;
        private VisualElement choicesSection;
        private VisualElement choicesContainer;
        private Button continueButton;
        private Label headerTitleLabel;

        // Dialogue state
        private DialogueTreeData dialogueTree;
        private DialogueNodeRuntime currentNode;
        private DialogueNodeRuntime lastDialogueNode;
        private bool isProcessing = false;
        private string currentSpeakerName = "";

        // Analytics
        private DialogueInteractionData dialogueInteractionData;
        private float dialogueStartTime;

        public void Display(string objectId, Dictionary<string, object> contentData, VisualElement root)
        {
            currentObjectId = objectId;
            rootElement = root;

            PlayerControls.SetEnabled(false);

            dialogueTree = DialogueTreeData.FromDictionary(contentData);
            if (dialogueTree == null || string.IsNullOrEmpty(dialogueTree.startNodeId))
            {
                Debug.LogError("[DialogueDisplayer] Invalid dialogue data - no start node");
                Close();
                return;
            }

            InitializeAnalytics();
            CreateDialogueUI();

            var startNode = dialogueTree.GetStartNode();
            if (startNode != null && startNode.type == "start" && !string.IsNullOrEmpty(startNode.nextNodeId))
            {
                NavigateToNode(startNode.nextNodeId);
            }
            else
            {
                Debug.LogError("[DialogueDisplayer] Could not find start node or start node has no next");
                Close();
            }
        }

        private void InitializeAnalytics()
        {
            dialogueStartTime = Time.time;

            dialogueInteractionData = new DialogueInteractionData
            {
                dialogueKey = currentObjectId,
                objectId = currentObjectId,
                totalChoiceNodes = dialogueTree.CountChoiceNodes()
            };

            if (TrainingAnalytics.Instance != null)
            {
                TrainingAnalytics.Instance.StartInteraction(currentObjectId, "dialogue", "branching");
            }
        }

        private void CreateDialogueUI()
        {
            rootElement.Clear();

            // Modal overlay
            modalContainer = new VisualElement();
            UIStyles.ApplyBackdropHeavyStyle(modalContainer);

            // Dialogue box
            dialogueBox = new VisualElement();
            dialogueBox.style.width = 700;
            dialogueBox.style.maxWidth = Length.Percent(90);
            dialogueBox.style.maxHeight = Length.Percent(85);
            UIStyles.ApplyCardStyle(dialogueBox, UIStyles.RadiusXL);
            dialogueBox.style.overflow = Overflow.Hidden;
            dialogueBox.style.flexDirection = FlexDirection.Column;

            // ========== HEADER ==========
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.paddingTop = UIStyles.SpaceLG;
            header.style.paddingBottom = UIStyles.SpaceLG;
            header.style.paddingLeft = UIStyles.Space2XL;
            header.style.paddingRight = UIStyles.Space2XL;
            header.style.backgroundColor = UIStyles.BgElevated;
            header.style.borderBottomWidth = 1;
            header.style.borderBottomColor = UIStyles.BorderSubtle;
            header.style.flexShrink = 0;

            string dialogueTitle = dialogueTree.title ?? "";
            headerTitleLabel = new Label(dialogueTitle);
            headerTitleLabel.style.fontSize = UIStyles.FontLG;
            headerTitleLabel.style.color = UIStyles.TextPrimary;
            headerTitleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            headerTitleLabel.style.flexGrow = 1;
            header.Add(headerTitleLabel);

            dialogueBox.Add(header);

            // ========== CONVERSATION AREA (scrollable) ==========
            conversationScroll = new ScrollView(ScrollViewMode.Vertical);
            conversationScroll.style.flexGrow = 1;
            conversationScroll.verticalScrollerVisibility = ScrollerVisibility.Auto;
            conversationScroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;

            conversationScroll.RegisterCallback<AttachToPanelEvent>(evt => UIStyles.ApplyMinimalScrollbar(conversationScroll));
            conversationScroll.RegisterCallback<GeometryChangedEvent>(evt => UIStyles.ApplyMinimalScrollbar(conversationScroll));

            conversationContainer = new VisualElement();
            conversationContainer.style.paddingTop = UIStyles.SpaceXL;
            conversationContainer.style.paddingBottom = UIStyles.SpaceLG;
            conversationContainer.style.paddingLeft = UIStyles.Space2XL;
            conversationContainer.style.paddingRight = UIStyles.Space2XL;

            conversationScroll.Add(conversationContainer);
            dialogueBox.Add(conversationScroll);

            // ========== CHOICES SECTION (fixed at bottom) ==========
            choicesSection = new VisualElement();
            choicesSection.style.flexShrink = 0;
            choicesSection.style.borderTopWidth = 1;
            choicesSection.style.borderTopColor = UIStyles.BorderSubtle;
            choicesSection.style.paddingTop = UIStyles.SpaceLG;
            choicesSection.style.paddingBottom = UIStyles.SpaceLG;
            choicesSection.style.paddingLeft = UIStyles.Space2XL;
            choicesSection.style.paddingRight = UIStyles.Space2XL;
            choicesSection.style.display = DisplayStyle.None;

            // "Your response" label
            var responseLabel = new Label();
            responseLabel.name = "response-label";
            responseLabel.style.fontSize = UIStyles.FontSM;
            responseLabel.style.color = UIStyles.TextMuted;
            responseLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            responseLabel.style.letterSpacing = 1;
            responseLabel.style.marginBottom = UIStyles.SpaceMD;
            choicesSection.Add(responseLabel);

            choicesContainer = new VisualElement();
            choicesSection.Add(choicesContainer);

            dialogueBox.Add(choicesSection);

            // ========== CONTINUE BUTTON (fixed at bottom) ==========
            var buttonSection = new VisualElement();
            buttonSection.style.flexShrink = 0;
            buttonSection.style.paddingTop = UIStyles.SpaceMD;
            buttonSection.style.paddingBottom = UIStyles.SpaceLG;
            buttonSection.style.paddingLeft = UIStyles.Space2XL;
            buttonSection.style.paddingRight = UIStyles.Space2XL;

            continueButton = UIStyles.CreatePrimaryButton("");
            continueButton.style.alignSelf = Align.Stretch;
            continueButton.style.display = DisplayStyle.None;
            buttonSection.Add(continueButton);

            dialogueBox.Add(buttonSection);

            modalContainer.Add(dialogueBox);
            rootElement.Add(modalContainer);
        }

        // ========== NAVIGATION ==========

        private void NavigateToNode(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId))
            {
                EndDialogue(true);
                return;
            }

            currentNode = dialogueTree.GetNode(nodeId);
            if (currentNode == null)
            {
                Debug.LogError($"[DialogueDisplayer] Node not found: {nodeId}");
                EndDialogue(true);
                return;
            }

            switch (currentNode.type)
            {
                case "dialogue":
                    DisplayDialogueNode(currentNode);
                    break;
                case "choice":
                    DisplayChoiceNode(currentNode);
                    break;
                case "end":
                    EndDialogue(true);
                    break;
                case "start":
                    NavigateToNode(currentNode.nextNodeId);
                    break;
                default:
                    Debug.LogWarning($"[DialogueDisplayer] Unknown node type: {currentNode.type}");
                    EndDialogue(true);
                    break;
            }
        }

        // ========== DIALOGUE NODE (NPC speaking) ==========

        private void DisplayDialogueNode(DialogueNodeRuntime node)
        {
            lastDialogueNode = node;

            // Resolve localization keys if text starts with @
            string speaker = LocalizationHelper.Resolve(node.speaker ?? "");
            string text = LocalizationHelper.Resolve(node.text ?? "");
            currentSpeakerName = speaker;

            // Clear previous content
            conversationContainer.Clear();

            // Add NPC bubble
            AddNpcBubble(speaker, text);

            // Peek at next node: if it's a choice, merge into one view
            DialogueNodeRuntime nextNode = null;
            if (!string.IsNullOrEmpty(node.nextNodeId))
            {
                nextNode = dialogueTree.GetNode(node.nextNodeId);
            }

            if (nextNode != null && nextNode.type == "choice")
            {
                // Merge: show choices directly below the NPC bubble
                currentNode = nextNode;
                ShowChoicesForNode(nextNode);
            }
            else
            {
                // Show continue button (dialogue → dialogue or dialogue → end)
                choicesSection.style.display = DisplayStyle.None;
                choicesContainer.Clear();

                UIStyles.SetButtonIcon(continueButton, WiseTwinIcons.ArrowRight(20, UIStyles.TextOnAccent));
                continueButton.style.display = DisplayStyle.Flex;
                continueButton.clickable = new Clickable(() =>
                {
                    if (isProcessing) return;
                    NavigateToNode(node.nextNodeId);
                });
            }

            ScrollToBottom();
        }

        // ========== CHOICE NODE (standalone, for direct navigation to choice) ==========

        private void DisplayChoiceNode(DialogueNodeRuntime node)
        {
            // Clear conversation area
            conversationContainer.Clear();

            // Show the NPC's previous dialogue as context bubble
            if (lastDialogueNode != null)
            {
                string ctxSpeaker = lastDialogueNode.speaker ?? "";
                string ctxText = lastDialogueNode.text ?? "";

                if (!string.IsNullOrEmpty(ctxText))
                {
                    AddNpcBubble(ctxSpeaker, ctxText);
                }
            }

            ShowChoicesForNode(node);
            ScrollToBottom();
        }

        /// <summary>
        /// Shows choices for a choice node (used by both merged dialogue+choice and standalone choice)
        /// </summary>
        private void ShowChoicesForNode(DialogueNodeRuntime node)
        {
            // Show prompt text if present (stored in node.text for choice nodes)
            string prompt = node.text ?? "";
            if (!string.IsNullOrEmpty(prompt))
            {
                var promptLabel = new Label(prompt);
                promptLabel.style.fontSize = UIStyles.FontBase;
                promptLabel.style.color = UIStyles.TextSecondary;
                promptLabel.style.unityFontStyleAndWeight = FontStyle.Italic;
                promptLabel.style.whiteSpace = WhiteSpace.Normal;
                promptLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                promptLabel.style.marginTop = UIStyles.SpaceLG;
                conversationContainer.Add(promptLabel);
            }

            // Hide continue button
            continueButton.style.display = DisplayStyle.None;

            // Show choices section
            var responseLabel = choicesSection.Q<Label>("response-label");
            if (responseLabel != null)
            {
                responseLabel.text = "";
            }

            choicesContainer.Clear();
            choicesSection.style.display = DisplayStyle.Flex;

            if (node.choices == null) return;

            bool isEvaluated = IsEvaluatedChoiceNode(node);

            foreach (var choice in node.choices)
            {
                var choiceOption = CreateChoiceOption(choice, node, isEvaluated);
                choicesContainer.Add(choiceOption);
            }
        }

        // ========== UI BUILDERS ==========

        /// <summary>
        /// Creates a chat bubble for the NPC with avatar and speech bubble
        /// </summary>
        private void AddNpcBubble(string speaker, string text)
        {
            var bubbleRow = new VisualElement();
            bubbleRow.style.flexDirection = FlexDirection.Row;
            bubbleRow.style.alignItems = Align.FlexStart;
            bubbleRow.style.marginBottom = UIStyles.SpaceMD;

            // Avatar circle
            var avatar = CreateAvatar(speaker);
            bubbleRow.Add(avatar);

            // Speech bubble
            var bubble = new VisualElement();
            bubble.style.flexGrow = 1;
            bubble.style.flexShrink = 1;
            bubble.style.backgroundColor = UIStyles.BgElevated;
            UIStyles.SetBorderRadius(bubble, UIStyles.RadiusMD);
            bubble.style.paddingTop = UIStyles.SpaceLG;
            bubble.style.paddingBottom = UIStyles.SpaceLG;
            bubble.style.paddingLeft = UIStyles.SpaceLG;
            bubble.style.paddingRight = UIStyles.SpaceLG;
            bubble.style.borderLeftWidth = 3;
            bubble.style.borderLeftColor = UIStyles.Accent;

            // Speaker name inside bubble
            if (!string.IsNullOrEmpty(speaker))
            {
                var nameLabel = new Label(speaker);
                nameLabel.style.fontSize = UIStyles.FontBase;
                nameLabel.style.color = UIStyles.Accent;
                nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                nameLabel.style.marginBottom = UIStyles.SpaceSM;
                bubble.Add(nameLabel);
            }

            // Message text
            var textLabel = new Label(text);
            textLabel.style.fontSize = UIStyles.FontMD;
            textLabel.style.color = UIStyles.TextPrimary;
            textLabel.style.whiteSpace = WhiteSpace.Normal;
            bubble.Add(textLabel);

            bubbleRow.Add(bubble);
            conversationContainer.Add(bubbleRow);
        }

        /// <summary>
        /// Creates a circular avatar with the speaker's initials
        /// </summary>
        private VisualElement CreateAvatar(string speaker)
        {
            var avatar = new VisualElement();
            avatar.style.width = 48;
            avatar.style.height = 48;
            avatar.style.minWidth = 48;
            avatar.style.minHeight = 48;
            UIStyles.SetBorderRadius(avatar, UIStyles.RadiusPill);
            avatar.style.backgroundColor = UIStyles.Accent;
            avatar.style.alignItems = Align.Center;
            avatar.style.justifyContent = Justify.Center;
            avatar.style.marginRight = UIStyles.SpaceMD;
            avatar.style.flexShrink = 0;

            // Initials
            string initials = GetInitials(speaker);
            var initialsLabel = new Label(initials);
            initialsLabel.style.fontSize = UIStyles.FontMD;
            initialsLabel.style.color = UIStyles.BgDeep;
            initialsLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            initialsLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            initialsLabel.pickingMode = PickingMode.Ignore;
            avatar.Add(initialsLabel);

            return avatar;
        }

        /// <summary>
        /// Extracts up to 2 initials from a speaker name
        /// </summary>
        private string GetInitials(string name)
        {
            if (string.IsNullOrEmpty(name)) return "?";

            var parts = name.Trim().Split(' ');
            if (parts.Length >= 2)
            {
                return $"{char.ToUpper(parts[0][0])}{char.ToUpper(parts[1][0])}";
            }
            else if (parts.Length == 1 && parts[0].Length >= 2)
            {
                return $"{char.ToUpper(parts[0][0])}{char.ToUpper(parts[0][1])}";
            }
            return $"{char.ToUpper(parts[0][0])}";
        }

        private bool IsEvaluatedChoiceNode(DialogueNodeRuntime node)
        {
            if (node.choices == null) return false;
            foreach (var choice in node.choices)
            {
                if (choice.isCorrect) return true;
            }
            return false;
        }

        private VisualElement CreateChoiceOption(DialogueChoiceRuntime choice, DialogueNodeRuntime parentNode, bool isEvaluated)
        {
            string choiceText = LocalizationHelper.Resolve(choice.text ?? "");

            var container = UIStyles.CreateSelectableOption(UIStyles.RadiusMD);

            // Chevron indicator (drawn \u2014 Unicode \u203A is missing from WebGL fonts)
            var arrow = WiseTwinIcons.Chevron(14, UIStyles.TextMuted);
            arrow.style.marginRight = UIStyles.SpaceSM;
            arrow.style.flexShrink = 0;
            arrow.pickingMode = PickingMode.Ignore;
            container.Add(arrow);

            var label = new Label(choiceText);
            label.style.fontSize = UIStyles.FontMD;
            label.style.color = UIStyles.TextPrimary;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.flexGrow = 1;
            label.pickingMode = PickingMode.Ignore;
            container.Add(label);

            // Hover
            UIStyles.ApplyOptionHover(container, () => !isProcessing);

            // Click
            container.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (isProcessing) return;
                isProcessing = true;

                // Record analytics
                float timestamp = Time.time - dialogueStartTime;
                dialogueInteractionData.RecordChoice(parentNode.id, choice.id, choice.isCorrect, timestamp);

                if (TrainingAnalytics.Instance != null)
                {
                    TrainingAnalytics.Instance.IncrementCurrentInteractionAttempts();
                }

                if (isEvaluated)
                {
                    if (choice.isCorrect)
                    {
                        UIStyles.ApplyCorrectStyle(container);
                        arrow.style.color = UIStyles.Success;
                    }
                    else
                    {
                        UIStyles.ApplyIncorrectStyle(container);
                        arrow.style.color = UIStyles.Danger;
                    }

                    rootElement.schedule.Execute(() =>
                    {
                        isProcessing = false;
                        NavigateToNode(choice.nextNodeId);
                    }).ExecuteLater(800);
                }
                else
                {
                    UIStyles.ApplySelectedStyle(container);
                    arrow.style.color = UIStyles.Info;

                    rootElement.schedule.Execute(() =>
                    {
                        isProcessing = false;
                        NavigateToNode(choice.nextNodeId);
                    }).ExecuteLater(300);
                }
            });

            return container;
        }

        // ========== END / CLOSE ==========

        private void EndDialogue(bool success)
        {
            dialogueInteractionData.Complete();

            if (TrainingAnalytics.Instance != null)
            {
                var data = dialogueInteractionData.ToDictionary();
                foreach (var kvp in data)
                {
                    TrainingAnalytics.Instance.AddDataToCurrentInteraction(kvp.Key, kvp.Value);
                }
                TrainingAnalytics.Instance.AddDataToCurrentInteraction("finalScore", dialogueInteractionData.finalScore);
                TrainingAnalytics.Instance.EndCurrentInteraction(success);
            }

            OnCompleted?.Invoke(currentObjectId, success);

            // No end card — exit the dialogue immediately and let the progression take over.
            Close();
        }

        public void Close()
        {
            isProcessing = false;

            PlayerControls.SetEnabled(true);

            rootElement?.Clear();
            OnClosed?.Invoke(currentObjectId);
        }

        // ========== HELPERS ==========

        private void ScrollToBottom()
        {
            if (conversationScroll != null)
            {
                conversationScroll.schedule.Execute(() =>
                {
                    conversationScroll.scrollOffset = new Vector2(0, conversationScroll.contentContainer.layout.height);
                }).ExecuteLater(50);
            }
        }

        void OnDestroy() { }
    }
}
