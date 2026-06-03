using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;
using System;
using System.Linq;
using WiseTwin.Analytics;
using Newtonsoft.Json.Linq;
using WiseTwin.UI;

namespace WiseTwin.UI
{
    /// <summary>
    /// Afficheur spécialisé pour les questions (QCM, Vrai/Faux, etc.)
    /// Supporte les questions multiples séquentielles
    /// </summary>
    public class QuestionDisplayer : MonoBehaviour, IContentDisplayer
    {
        [Header("🔧 Debug Settings")]
        [SerializeField, Tooltip("Enable debug logs for this component")]
        private bool enableDebugLogs = false;

        public event Action<string> OnClosed;
        public event Action<string, bool> OnCompleted;

        private string currentObjectId;
        private VisualElement rootElement;
        private VisualElement modalContainer;
        private int selectedAnswerIndex = -1;
        private List<int> selectedAnswerIndexes = new List<int>(); // Pour les réponses multiples
        private int correctAnswerIndex; // Pour réponse unique
        private List<int> correctAnswerIndexes = new List<int>(); // Pour réponses multiples
        private bool hasAnswered = false;
        private bool isMultipleChoice = false; // Détermine si c'est un QCM multiple ou unique

        // Pour gérer les questions séquentielles
        private List<string> questionKeys;
        private int currentQuestionIndex = 0;
        private Dictionary<string, object> allObjectData;

        // Références UI pour mise à jour
        private Label questionLabel;
        private VisualElement questionImageElement;
        private Label progressLabel;
        private VisualElement optionsContainer;
        private Button validateButton;
        private VisualElement feedbackContainer;
        private Label feedbackLabel;
        private string currentFeedback;
        private string currentIncorrectFeedback;

        // Analytics tracking
        private QuestionInteractionData currentQuestionData;
        private string currentQuestionKey; // Clé de la question pour tracking

        // Store content data for language change updates
        private Dictionary<string, object> storedContentData;
        private Dictionary<string, object> currentQuestionData_Raw; // Current question raw data

        // Interface implementation
        public void Display(string objectId, Dictionary<string, object> contentData, VisualElement root)
        {
            currentObjectId = objectId;
            rootElement = root;

            // Block player controls during question display
            PlayerControls.SetEnabled(false);

            storedContentData = contentData;

            // Si contentData contient déjà les données de question directement (nouveau format)
            if (contentData.ContainsKey("questionText") || contentData.ContainsKey("options"))
            {
                // Question unique passée directement
                questionKeys = null;
                allObjectData = null;
                DisplaySingleQuestion(contentData);
            }
            // NEW: Support for "questions" array format
            else if (contentData.ContainsKey("questions"))
            {
                var questionsValue = contentData["questions"];
                if (questionsValue is Newtonsoft.Json.Linq.JArray questionsArray && questionsArray.Count > 0)
                {
                    // Convert JArray to list of dictionaries
                    allObjectData = new Dictionary<string, object>();
                    questionKeys = new List<string>();

                    for (int i = 0; i < questionsArray.Count; i++)
                    {
                        string questionKey = $"question_{i + 1}";
                        var questionDict = questionsArray[i].ToObject<Dictionary<string, object>>();
                        allObjectData[questionKey] = questionDict;
                        questionKeys.Add(questionKey);
                    }

                    if (questionKeys.Count > 0)
                    {
                        currentQuestionIndex = 0;
                        CreateQuestionUI(); // Create UI elements before displaying
                        DisplayCurrentQuestion();
                    }
                    else
                    {
                        Debug.LogError("[QuestionDisplayer] No questions found in array");
                    }
                }
                else if (questionsValue is List<object> questionsList && questionsList.Count > 0)
                {
                    // Handle as list of objects
                    allObjectData = new Dictionary<string, object>();
                    questionKeys = new List<string>();

                    for (int i = 0; i < questionsList.Count; i++)
                    {
                        string questionKey = $"question_{i + 1}";
                        allObjectData[questionKey] = questionsList[i];
                        questionKeys.Add(questionKey);
                    }

                    if (questionKeys.Count > 0)
                    {
                        currentQuestionIndex = 0;
                        CreateQuestionUI(); // Create UI elements before displaying
                        DisplayCurrentQuestion();
                    }
                }
                else
                {
                    Debug.LogError("[QuestionDisplayer] Invalid 'questions' format");
                }
            }
            else
            {
                Debug.LogError($"[QuestionDisplayer] No valid question format found for {objectId}. Expected 'questionTextEN/FR' or 'questions' array.");
            }
        }

        private void DisplaySingleQuestion(Dictionary<string, object> contentData)
        {
            // Store raw question data for language updates
            currentQuestionData_Raw = contentData;

            hasAnswered = false;
            isValidating = false; // Réinitialiser le flag de validation
            selectedAnswerIndex = -1;
            selectedAnswerIndexes.Clear();

            // Réinitialiser le bouton (désactivé au départ)
            if (validateButton != null)
            {
                validateButton.SetEnabled(false);
                validateButton.style.opacity = 0.5f;
            }

            // Masquer le feedback container
            if (feedbackContainer != null)
            {
                feedbackContainer.style.display = DisplayStyle.None;
            }

            if (ContentDisplayManager.Instance?.DebugMode ?? false)
            {
                LogDebug($"Displaying single question for {currentObjectId}");
            }

            // Obtenir la langue actuelle
            string lang = "";

            // Extraire les données de la question (nouveau format uniquement)
            string questionText = ExtractLocalizedText(contentData, "questionText", lang);
            var options = ExtractLocalizedList(contentData, "options", lang);
            currentQuestionKey = "question"; // Clé unique pour question simple

            // Vérifier le mode de sélection (nouveau format uniquement)
            isMultipleChoice = contentData.ContainsKey("isMultipleChoice") && contentData["isMultipleChoice"] is bool b && b;

            // Gérer les réponses correctes selon le mode
            if (isMultipleChoice)
            {
                // Pour les réponses multiples, on peut avoir un tableau ou une string avec des virgules
                correctAnswerIndexes.Clear();
                if (contentData.ContainsKey("correctAnswers"))
                {
                    var correctAnswers = contentData["correctAnswers"];
                    LogDebug($"[MULTIPLE CHOICE] correctAnswers type: {correctAnswers?.GetType()?.FullName ?? "null"}");

                    if (correctAnswers is Newtonsoft.Json.Linq.JArray jarray)
                    {
                        correctAnswerIndexes = jarray.Select(x => (int)(long)x).ToList();
                        LogDebug($"Parsed as JArray: {string.Join(", ", correctAnswerIndexes)}");
                    }
                    else if (correctAnswers is List<object> list)
                    {
                        correctAnswerIndexes = list.Select(x => Convert.ToInt32(x)).ToList();
                        LogDebug($"Parsed as List<object>: {string.Join(", ", correctAnswerIndexes)}");
                    }
                    else if (correctAnswers is object[] objArray)
                    {
                        correctAnswerIndexes = objArray.Select(x => Convert.ToInt32(x)).ToList();
                        LogDebug($"Parsed as object[]: {string.Join(", ", correctAnswerIndexes)}");
                    }
                    else if (correctAnswers is string str)
                    {
                        correctAnswerIndexes = str.Split(',').Select(x => int.Parse(x.Trim())).ToList();
                        LogDebug($"Parsed as string: {string.Join(", ", correctAnswerIndexes)}");
                    }
                    else if (correctAnswers is int[] intArray)
                    {
                        correctAnswerIndexes = intArray.ToList();
                        LogDebug($"Parsed as int[]: {string.Join(", ", correctAnswerIndexes)}");
                    }
                    else if (correctAnswers is long[] longArray)
                    {
                        correctAnswerIndexes = longArray.Select(x => (int)x).ToList();
                        LogDebug($"Parsed as long[]: {string.Join(", ", correctAnswerIndexes)}");
                    }
                    else
                    {
                        // Fallback : essayer de sérialiser/désérialiser
                        LogWarning($"Unhandled correctAnswers type: {correctAnswers?.GetType()?.FullName}, attempting JSON conversion");
                        try
                        {
                            string json = Newtonsoft.Json.JsonConvert.SerializeObject(correctAnswers);
                            correctAnswerIndexes = Newtonsoft.Json.JsonConvert.DeserializeObject<List<int>>(json);
                            LogDebug($"Parsed via JSON fallback: {string.Join(", ", correctAnswerIndexes)}");
                        }
                        catch (System.Exception e)
                        {
                            LogError($"Failed to parse correctAnswers: {e.Message}");
                        }
                    }

                    LogDebug($"Final correctAnswers for multiple choice: {string.Join(", ", correctAnswerIndexes)}");
                }
                else
                {
                    LogWarning("No correctAnswers field found for multiple choice question!");
                }
            }
            else
            {
                // Pour réponse unique - lire depuis correctAnswers[0]
                if (contentData.ContainsKey("correctAnswers"))
                {
                    var correctAnswers = contentData["correctAnswers"];
                    if (correctAnswers is Newtonsoft.Json.Linq.JArray jarray && jarray.Count > 0)
                    {
                        correctAnswerIndex = (int)(long)jarray[0];
                    }
                    else if (correctAnswers is List<object> list && list.Count > 0)
                    {
                        correctAnswerIndex = Convert.ToInt32(list[0]);
                    }
                    else if (correctAnswers is int[] intArray && intArray.Length > 0)
                    {
                        correctAnswerIndex = intArray[0];
                    }
                }
            }

            string feedback = ExtractLocalizedText(contentData, "feedback", lang);
            string incorrectFeedback = ExtractLocalizedText(contentData, "incorrectFeedback", lang);
            string questionType = ExtractString(contentData, "type");

            if (ContentDisplayManager.Instance?.DebugMode ?? false)
            {
                LogDebug($"Question: {questionText}");
                LogDebug($"Options count: {options?.Count ?? 0}");
            }

            // Créer l'UI pour question unique
            CreateSingleQuestionUI(questionText, options, questionType, feedback, incorrectFeedback);

            // Initialiser le tracking analytics
            InitializeQuestionTracking();
        }

        // Show/hide the per-question illustration image in the sequential (multi-question) flow.
        private void UpdateQuestionImage(string imagePath)
        {
            if (questionImageElement == null) return;
            questionImageElement.Clear();

            var tex = WiseTwinImage.Load(imagePath);
            if (tex != null)
            {
                var thumb = WiseTwinImage.CreateThumbnail(tex, 180f);
                thumb.style.marginBottom = UIStyles.SpaceLG;
                questionImageElement.Add(thumb);
                questionImageElement.style.display = DisplayStyle.Flex;
            }
            else
            {
                questionImageElement.style.display = DisplayStyle.None;
            }
        }

        private void CreateQuestionUI()
        {
            rootElement.Clear();

            // Modal backdrop
            modalContainer = new VisualElement();
            UIStyles.ApplyBackdropHeavyStyle(modalContainer);

            // Question card
            var questionBox = new VisualElement();
            questionBox.style.width = 680;
            questionBox.style.maxWidth = Length.Percent(90);
            questionBox.style.maxHeight = Length.Percent(80);
            UIStyles.ApplyCardStyle(questionBox, UIStyles.RadiusXL);
            questionBox.style.overflow = Overflow.Hidden;
            UIStyles.SetPadding(questionBox, UIStyles.Space3XL);

            // Progress indicator
            progressLabel = new Label();
            progressLabel.style.fontSize = UIStyles.FontBase;
            progressLabel.style.color = UIStyles.TextMuted;
            progressLabel.style.marginBottom = UIStyles.SpaceSM;
            progressLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            questionBox.Add(progressLabel);

            // Question text
            questionLabel = new Label();
            questionLabel.style.fontSize = UIStyles.FontXL;
            questionLabel.style.color = UIStyles.TextPrimary;
            questionLabel.style.marginBottom = UIStyles.Space2XL;
            questionLabel.style.whiteSpace = WhiteSpace.Normal;
            questionLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            questionBox.Add(questionLabel);

            // Optional per-question illustration image (updated on each question)
            questionImageElement = new VisualElement();
            questionImageElement.style.display = DisplayStyle.None;
            questionBox.Add(questionImageElement);

            // Options container with scroll
            optionsContainer = new ScrollView();
            optionsContainer.style.marginBottom = UIStyles.SpaceSM;
            optionsContainer.style.maxHeight = 400;
            optionsContainer.style.flexGrow = 1;
            questionBox.Add(optionsContainer);

            // Instruction label (single/multiple choice)
            var instructionLabel = new Label();
            instructionLabel.name = "instruction-label";
            instructionLabel.style.fontSize = UIStyles.FontSM;
            instructionLabel.style.color = UIStyles.TextMuted;
            instructionLabel.style.marginTop = UIStyles.SpaceXS;
            instructionLabel.style.marginBottom = UIStyles.SpaceLG;
            instructionLabel.style.paddingTop = UIStyles.SpaceSM;
            instructionLabel.style.paddingBottom = UIStyles.SpaceSM;
            instructionLabel.style.paddingLeft = UIStyles.SpaceMD;
            instructionLabel.style.paddingRight = UIStyles.SpaceMD;
            instructionLabel.style.backgroundColor = UIStyles.BgElevated;
            UIStyles.SetBorderRadius(instructionLabel, UIStyles.RadiusSM);
            instructionLabel.style.whiteSpace = WhiteSpace.Normal;
            instructionLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            instructionLabel.style.unityFontStyleAndWeight = FontStyle.Italic;
            questionBox.Add(instructionLabel);

            // Feedback zone (hidden initially)
            feedbackContainer = new VisualElement();
            feedbackContainer.name = "feedback-container";
            feedbackContainer.style.display = DisplayStyle.None;
            feedbackContainer.style.marginTop = UIStyles.SpaceLG;
            UIStyles.SetPadding(feedbackContainer, UIStyles.SpaceLG);
            UIStyles.SetBorderRadius(feedbackContainer, UIStyles.RadiusMD);

            feedbackLabel = new Label();
            feedbackLabel.name = "feedback-text";
            feedbackLabel.style.fontSize = UIStyles.FontMD;
            feedbackLabel.style.color = UIStyles.TextPrimary;
            feedbackLabel.style.whiteSpace = WhiteSpace.Normal;
            feedbackLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            feedbackContainer.Add(feedbackLabel);
            questionBox.Add(feedbackContainer);

            // Compact validate button (icon only)
            validateButton = UIStyles.CreatePrimaryButton("", ValidateAnswer);
            UIStyles.SetButtonIcon(validateButton, WiseTwinIcons.Check(20, UIStyles.TextOnAccent));
            validateButton.name = "validate-button";
            validateButton.style.marginTop = UIStyles.SpaceLG;
            validateButton.style.alignSelf = Align.Center;
            validateButton.style.width = 64;
            validateButton.style.height = 44;
            validateButton.SetEnabled(false);
            validateButton.style.opacity = 0.5f;
            questionBox.Add(validateButton);

            modalContainer.Add(questionBox);
            rootElement.Add(modalContainer);
        }

        private void DisplayCurrentQuestion()
        {
            // Réinitialiser le flag de validation pour chaque nouvelle question
            isValidating = false;

            if (currentQuestionIndex >= questionKeys.Count)
            {
                // Toutes les questions ont été répondues
                OnCompleted?.Invoke(currentObjectId, true);
                Close();
                return;
            }

            hasAnswered = false;
            selectedAnswerIndex = -1;
            selectedAnswerIndexes.Clear();

            // Réinitialiser le bouton (désactivé pour la nouvelle question)
            if (validateButton != null)
            {
                validateButton.SetEnabled(false);
                validateButton.style.opacity = 0.5f;
            }

            // Masquer le feedback container
            if (feedbackContainer != null)
            {
                feedbackContainer.style.display = DisplayStyle.None;
            }

            string currentKey = questionKeys[currentQuestionIndex];
            if (ContentDisplayManager.Instance?.DebugMode ?? false)
            {
                LogDebug($"Displaying question {currentQuestionIndex + 1}/{questionKeys.Count}: {currentKey}");
            }

            // Mise à jour de l'indicateur de progression
            if (progressLabel != null)
            {
                if (questionKeys.Count > 1)
                {
                    progressLabel.text = $"Question {currentQuestionIndex + 1} / {questionKeys.Count}";
                    progressLabel.style.display = DisplayStyle.Flex;
                }
                else
                {
                    progressLabel.style.display = DisplayStyle.None;
                }
            }

            if (allObjectData.ContainsKey(currentKey))
            {
                var questionData = allObjectData[currentKey];

                // Convertir en Dictionary si nécessaire
                Dictionary<string, object> questionDict = null;
                if (questionData is Dictionary<string, object> dict)
                {
                    questionDict = dict;
                    // Store raw question data for language updates
                    currentQuestionData_Raw = dict;
                }
                else if (questionData != null)
                {
                    try
                    {
                        string json = Newtonsoft.Json.JsonConvert.SerializeObject(questionData);
                        questionDict = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                        // Store raw question data for language updates
                        currentQuestionData_Raw = questionDict;
                    }
                    catch (System.Exception e)
                    {
                        if (ContentDisplayManager.Instance?.DebugMode ?? false)
                        {
                            LogError($"Failed to convert question data: {e.Message}");
                        }
                    }
                }

                if (questionDict != null)
                {
                    // Obtenir la langue actuelle
                    string lang = "";

                    // Extraire les données de la question (nouveau format uniquement)
                    string questionText = ExtractLocalizedText(questionDict, "questionText", lang);
                    var options = ExtractLocalizedList(questionDict, "options", lang);

                    // Vérifier le mode de sélection (nouveau format uniquement)
                    isMultipleChoice = questionDict.ContainsKey("isMultipleChoice") && questionDict["isMultipleChoice"] is bool b && b;

                    // Gérer les réponses correctes selon le mode
                    if (isMultipleChoice)
                    {
                        correctAnswerIndexes.Clear();
                        if (questionDict.ContainsKey("correctAnswers"))
                        {
                            var correctAnswers = questionDict["correctAnswers"];
                            LogDebug($"[SEQ MULTIPLE CHOICE] correctAnswers type: {correctAnswers?.GetType()?.FullName ?? "null"}");

                            if (correctAnswers is Newtonsoft.Json.Linq.JArray jarray)
                            {
                                correctAnswerIndexes = jarray.Select(x => (int)(long)x).ToList();
                                LogDebug($"Parsed as JArray: {string.Join(", ", correctAnswerIndexes)}");
                            }
                            else if (correctAnswers is List<object> list)
                            {
                                correctAnswerIndexes = list.Select(x => Convert.ToInt32(x)).ToList();
                                LogDebug($"Parsed as List<object>: {string.Join(", ", correctAnswerIndexes)}");
                            }
                            else if (correctAnswers is object[] objArray)
                            {
                                correctAnswerIndexes = objArray.Select(x => Convert.ToInt32(x)).ToList();
                                LogDebug($"Parsed as object[]: {string.Join(", ", correctAnswerIndexes)}");
                            }
                            else if (correctAnswers is string str)
                            {
                                correctAnswerIndexes = str.Split(',').Select(x => int.Parse(x.Trim())).ToList();
                                LogDebug($"Parsed as string: {string.Join(", ", correctAnswerIndexes)}");
                            }
                            else if (correctAnswers is int[] intArray)
                            {
                                correctAnswerIndexes = intArray.ToList();
                                LogDebug($"Parsed as int[]: {string.Join(", ", correctAnswerIndexes)}");
                            }
                            else if (correctAnswers is long[] longArray)
                            {
                                correctAnswerIndexes = longArray.Select(x => (int)x).ToList();
                                LogDebug($"Parsed as long[]: {string.Join(", ", correctAnswerIndexes)}");
                            }
                            else
                            {
                                // Fallback : essayer de sérialiser/désérialiser
                                LogWarning($"Unhandled correctAnswers type: {correctAnswers?.GetType()?.FullName}, attempting JSON conversion");
                                try
                                {
                                    string json = Newtonsoft.Json.JsonConvert.SerializeObject(correctAnswers);
                                    correctAnswerIndexes = Newtonsoft.Json.JsonConvert.DeserializeObject<List<int>>(json);
                                    LogDebug($"Parsed via JSON fallback: {string.Join(", ", correctAnswerIndexes)}");
                                }
                                catch (System.Exception e)
                                {
                                    LogError($"Failed to parse correctAnswers: {e.Message}");
                                }
                            }

                            LogDebug($"Final correctAnswers for seq multiple choice: {string.Join(", ", correctAnswerIndexes)}");
                        }
                    }
                    else
                    {
                        // Pour réponse unique - lire depuis correctAnswers[0]
                        if (questionDict.ContainsKey("correctAnswers"))
                        {
                            var correctAnswers = questionDict["correctAnswers"];
                            if (correctAnswers is Newtonsoft.Json.Linq.JArray jarray && jarray.Count > 0)
                            {
                                correctAnswerIndex = (int)(long)jarray[0];
                            }
                            else if (correctAnswers is List<object> list && list.Count > 0)
                            {
                                correctAnswerIndex = Convert.ToInt32(list[0]);
                            }
                            else if (correctAnswers is int[] intArray && intArray.Length > 0)
                            {
                                correctAnswerIndex = intArray[0];
                            }
                        }
                    }

                    currentFeedback = ExtractLocalizedText(questionDict, "feedback", lang);
                    currentIncorrectFeedback = ExtractLocalizedText(questionDict, "incorrectFeedback", lang);

                    // Mettre à jour l'UI
                    questionLabel.text = questionText;
                    UpdateQuestionImage(ExtractLocalizedText(questionDict, "imagePath", lang));
                    currentQuestionKey = currentKey; // Stocker la clé pour le tracking

                    // Mettre à jour le label d'instruction
                    var instructionLabel = modalContainer?.Q<Label>("instruction-label");
                    if (instructionLabel != null)
                    {
                        if (isMultipleChoice)
                        {
                            instructionLabel.text = lang == "fr"
                                ? "Vous pouvez sélectionner plusieurs réponses"
                                : "You can select multiple answers";
                        }
                        else
                        {
                            instructionLabel.text = lang == "fr"
                                ? "Sélectionnez une seule réponse"
                                : "Select only one answer";
                        }
                    }

                    // Clear options container et recréer les options
                    optionsContainer.Clear();
                    for (int i = 0; i < options.Count; i++)
                    {
                        int index = i;
                        var optionButton = CreateOptionButton(options[i], index);
                        optionsContainer.Add(optionButton);
                    }

                    // Réinitialiser les sélections
                    selectedAnswerIndex = -1;
                    selectedAnswerIndexes.Clear();

                    // Réinitialiser le feedback
                    feedbackContainer.style.display = DisplayStyle.None;

                    // Réinitialiser le bouton valider
                    UIStyles.SetButtonIcon(validateButton, WiseTwinIcons.Check(20, UIStyles.TextOnAccent));
                    validateButton.style.backgroundColor = UIStyles.Accent;
                    validateButton.clicked -= NextQuestion;
                    validateButton.clicked -= ValidateAnswer;
                    validateButton.clicked += ValidateAnswer;

                    // Initialiser le tracking pour cette question
                    InitializeQuestionTracking();
                }
            }
        }

        private void CreateSingleQuestionUI(string questionText, List<string> options, string type, string feedback, string incorrectFeedback)
        {
            currentFeedback = feedback;
            currentIncorrectFeedback = incorrectFeedback;

            // Clear root
            rootElement.Clear();

            // Modal backdrop
            modalContainer = new VisualElement();
            UIStyles.ApplyBackdropHeavyStyle(modalContainer);

            // Question card
            var questionBox = new VisualElement();
            questionBox.style.width = 700;
            questionBox.style.maxWidth = Length.Percent(90);
            questionBox.style.maxHeight = Length.Percent(80);
            UIStyles.ApplyCardStyle(questionBox, UIStyles.RadiusXL);
            questionBox.style.overflow = Overflow.Hidden;
            UIStyles.SetPadding(questionBox, UIStyles.Space3XL);

            // Question text
            questionLabel = new Label(questionText);
            questionLabel.style.fontSize = UIStyles.FontXL;
            questionLabel.style.color = UIStyles.TextPrimary;
            questionLabel.style.marginBottom = UIStyles.Space2XL;
            questionLabel.style.whiteSpace = WhiteSpace.Normal;
            questionLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            questionBox.Add(questionLabel);

            // Optional illustration image under the question (clickable to zoom)
            var qImage = WiseTwinImage.Load(ExtractLocalizedText(currentQuestionData_Raw, "imagePath", ""));
            if (qImage != null)
            {
                var thumb = WiseTwinImage.CreateThumbnail(qImage, 200f);
                thumb.style.marginBottom = UIStyles.SpaceLG;
                questionBox.Add(thumb);
            }

            // Options container with scroll
            optionsContainer = new ScrollView();
            optionsContainer.style.marginBottom = UIStyles.SpaceSM;
            optionsContainer.style.maxHeight = 400;
            optionsContainer.style.flexGrow = 1;

            // Create option buttons
            for (int i = 0; i < options.Count; i++)
            {
                int index = i;
                var optionButton = CreateOptionButton(options[i], index);
                optionsContainer.Add(optionButton);
            }

            questionBox.Add(optionsContainer);

            // Instruction label (single/multiple choice)
            var instructionLabel = new Label();
            instructionLabel.name = "instruction-label";
            instructionLabel.style.fontSize = UIStyles.FontSM;
            instructionLabel.style.color = UIStyles.TextMuted;
            instructionLabel.style.marginTop = UIStyles.SpaceXS;
            instructionLabel.style.marginBottom = UIStyles.SpaceLG;
            instructionLabel.style.paddingTop = UIStyles.SpaceSM;
            instructionLabel.style.paddingBottom = UIStyles.SpaceSM;
            instructionLabel.style.paddingLeft = UIStyles.SpaceMD;
            instructionLabel.style.paddingRight = UIStyles.SpaceMD;
            instructionLabel.style.backgroundColor = UIStyles.BgElevated;
            UIStyles.SetBorderRadius(instructionLabel, UIStyles.RadiusSM);
            instructionLabel.style.whiteSpace = WhiteSpace.Normal;
            instructionLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            instructionLabel.style.unityFontStyleAndWeight = FontStyle.Italic;

            string lang = "";
            if (isMultipleChoice)
            {
                instructionLabel.text = lang == "fr"
                    ? "Vous pouvez sélectionner plusieurs réponses"
                    : "You can select multiple answers";
            }
            else
            {
                instructionLabel.text = lang == "fr"
                    ? "Sélectionnez une seule réponse"
                    : "Select only one answer";
            }
            questionBox.Add(instructionLabel);

            // Feedback zone (hidden initially)
            feedbackContainer = new VisualElement();
            feedbackContainer.name = "feedback-container";
            feedbackContainer.style.display = DisplayStyle.None;
            feedbackContainer.style.marginTop = UIStyles.SpaceLG;
            UIStyles.SetPadding(feedbackContainer, UIStyles.SpaceLG);
            UIStyles.SetBorderRadius(feedbackContainer, UIStyles.RadiusMD);

            feedbackLabel = new Label();
            feedbackLabel.name = "feedback-text";
            feedbackLabel.style.fontSize = UIStyles.FontMD;
            feedbackLabel.style.color = UIStyles.TextPrimary;
            feedbackLabel.style.whiteSpace = WhiteSpace.Normal;
            feedbackLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            feedbackContainer.Add(feedbackLabel);

            questionBox.Add(feedbackContainer);

            // Compact validate button (icon only)
            validateButton = UIStyles.CreatePrimaryButton("", ValidateAnswer);
            UIStyles.SetButtonIcon(validateButton, WiseTwinIcons.Check(20, UIStyles.TextOnAccent));
            validateButton.name = "validate-button";
            validateButton.style.marginTop = UIStyles.SpaceLG;
            validateButton.style.alignSelf = Align.Center;
            validateButton.style.width = 64;
            validateButton.style.height = 44;
            validateButton.SetEnabled(false);
            validateButton.style.opacity = 0.5f;

            questionBox.Add(validateButton);

            modalContainer.Add(questionBox);
            rootElement.Add(modalContainer);
        }

        VisualElement CreateOptionButton(string text, int index)
        {
            var optionContainer = UIStyles.CreateSelectableOption(UIStyles.RadiusMD);
            optionContainer.style.minHeight = 50;
            optionContainer.pickingMode = PickingMode.Position;
            optionContainer.name = $"option-{index}";

            // Indicator (circle for radio, square for checkbox)
            var indicator = new VisualElement();
            indicator.name = "indicator";
            indicator.style.width = 22;
            indicator.style.height = 22;
            indicator.style.marginRight = UIStyles.SpaceMD;
            indicator.style.flexShrink = 0;
            UIStyles.SetBorderWidth(indicator, 2);
            UIStyles.SetBorderColor(indicator, UIStyles.TextSecondary);
            indicator.style.backgroundColor = Color.clear;

            if (isMultipleChoice)
            {
                UIStyles.SetBorderRadius(indicator, UIStyles.SpaceXS);
            }
            else
            {
                UIStyles.SetBorderRadius(indicator, UIStyles.RadiusPill);
            }

            // Option text
            var label = new Label(text);
            label.style.fontSize = UIStyles.FontMD;
            label.style.color = UIStyles.TextPrimary;
            label.style.flexGrow = 1;
            label.style.flexShrink = 1;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.overflow = Overflow.Hidden;
            label.style.textOverflow = TextOverflow.Clip;
            label.pickingMode = PickingMode.Ignore;

            optionContainer.Add(indicator);
            optionContainer.Add(label);

            // Click
            optionContainer.RegisterCallback<ClickEvent>((evt) => {
                if (!hasAnswered)
                {
                    SelectOption(index);
                }
            });

            // Hover
            optionContainer.RegisterCallback<MouseEnterEvent>((evt) => {
                if (!hasAnswered)
                {
                    optionContainer.style.backgroundColor = UIStyles.BgInputHover;
                }
            });

            optionContainer.RegisterCallback<MouseLeaveEvent>((evt) => {
                if (!hasAnswered && !optionContainer.ClassListContains("selected"))
                {
                    optionContainer.style.backgroundColor = UIStyles.BgInput;
                }
            });

            return optionContainer;
        }

        void SelectOption(int index)
        {
            if (hasAnswered) return;

            // Masquer le feedback d'erreur quand l'utilisateur change sa sélection
            if (feedbackContainer != null && feedbackContainer.style.display == DisplayStyle.Flex)
            {
                feedbackContainer.style.display = DisplayStyle.None;
                // Réinitialiser le feedback visuel sur les options
                ResetOptionsVisualFeedback();
            }

            if (isMultipleChoice)
            {
                // Mode checkbox - peut sélectionner/désélectionner plusieurs options
                if (selectedAnswerIndexes.Contains(index))
                {
                    selectedAnswerIndexes.Remove(index);
                }
                else
                {
                    selectedAnswerIndexes.Add(index);
                }
            }
            else
            {
                // Mode radio - une seule sélection
                selectedAnswerIndex = index;
                selectedAnswerIndexes.Clear();
                selectedAnswerIndexes.Add(index);
            }

            // Mettre à jour l'UI
            UpdateOptionsUI();

            // Activer le bouton Continuer dès qu'une sélection valide est faite
            bool hasValidSelection = isMultipleChoice ? selectedAnswerIndexes.Count > 0 : selectedAnswerIndex >= 0;
            if (validateButton != null && hasValidSelection)
            {
                validateButton.SetEnabled(true);
                validateButton.style.opacity = 1f;
            }
        }

        void UpdateOptionsUI()
        {
            var allOptions = optionsContainer.Query<VisualElement>().Build().ToList();

            for (int i = 0; i < allOptions.Count; i++)
            {
                var option = optionsContainer.Q<VisualElement>($"option-{i}");
                if (option != null)
                {
                    var indicator = option.Q<VisualElement>("indicator");
                    bool isSelected = isMultipleChoice ? selectedAnswerIndexes.Contains(i) : selectedAnswerIndex == i;

                    if (isSelected)
                    {
                        option.AddToClassList("selected");
                        UIStyles.ApplySelectedStyle(option);

                        indicator.style.backgroundColor = UIStyles.Info;
                    }
                    else
                    {
                        option.RemoveFromClassList("selected");
                        UIStyles.ResetOptionStyle(option);

                        indicator.style.backgroundColor = Color.clear;
                    }
                }
            }
        }

        private bool isValidating = false; // Protection contre les doubles clics

        /// <summary>
        /// Réinitialise le feedback visuel sur les options (retire les checkmarks/crosses et les couleurs de feedback)
        /// </summary>
        void ResetOptionsVisualFeedback()
        {
            var allOptionElements = optionsContainer.Query<VisualElement>().Where(e => e.name != null && e.name.StartsWith("option-")).ToList();

            for (int i = 0; i < allOptionElements.Count; i++)
            {
                var option = optionsContainer.Q<VisualElement>($"option-{i}");
                if (option == null) continue;

                // Retirer les checkmarks/crossmarks si présents
                var checkmark = option.Q<Label>("checkmark");
                if (checkmark != null)
                {
                    option.Remove(checkmark);
                }

                var crossmark = option.Q<Label>("crossmark");
                if (crossmark != null)
                {
                    option.Remove(crossmark);
                }
            }

            // Mettre à jour l'affichage des options (va appliquer les couleurs par défaut)
            UpdateOptionsUI();
        }

        /// <summary>
        /// Affiche un feedback visuel sur les options après validation
        /// Colore les bonnes réponses en vert et les mauvaises sélections en rouge
        /// </summary>
        void ShowAnswerFeedback(bool userAnsweredCorrectly)
        {
            // Parcourir toutes les options pour les colorer
            int optionCount = isMultipleChoice ? correctAnswerIndexes.Count : 1;
            // Pour obtenir le nombre total d'options, on utilise le nombre d'enfants dans optionsContainer
            var allOptionElements = optionsContainer.Query<VisualElement>().Where(e => e.name != null && e.name.StartsWith("option-")).ToList();

            for (int i = 0; i < allOptionElements.Count; i++)
            {
                var option = optionsContainer.Q<VisualElement>($"option-{i}");
                if (option == null) continue;

                var indicator = option.Q<VisualElement>("indicator");
                if (indicator == null) continue;

                // Déterminer si cette option est correcte
                bool isCorrectOption = isMultipleChoice
                    ? correctAnswerIndexes.Contains(i)
                    : (i == correctAnswerIndex);

                // Déterminer si cette option a été sélectionnée par l'utilisateur
                bool isUserSelected = isMultipleChoice
                    ? selectedAnswerIndexes.Contains(i)
                    : (i == selectedAnswerIndex);

                // Appliquer le style en fonction du statut
                if (isCorrectOption)
                {
                    UIStyles.ApplyCorrectStyle(option);
                    indicator.style.backgroundColor = UIStyles.Success;
                    UIStyles.SetBorderColor(indicator, UIStyles.Success);

                    if (option.Q("checkmark-icon") == null)
                    {
                        var checkmarkIcon = WiseTwinIcons.Check(20, UIStyles.Success);
                        checkmarkIcon.name = "checkmark-icon";
                        checkmarkIcon.style.marginLeft = UIStyles.SpaceSM;
                        option.Add(checkmarkIcon);
                    }
                }
                else if (isUserSelected)
                {
                    UIStyles.ApplyIncorrectStyle(option);
                    indicator.style.backgroundColor = UIStyles.Danger;
                    UIStyles.SetBorderColor(indicator, UIStyles.Danger);

                    if (option.Q("crossmark-icon") == null)
                    {
                        var crossmarkIcon = WiseTwinIcons.Cross(20, UIStyles.Danger);
                        crossmarkIcon.name = "crossmark-icon";
                        crossmarkIcon.style.marginLeft = UIStyles.SpaceSM;
                        option.Add(crossmarkIcon);
                    }
                }
                else
                {
                    option.style.backgroundColor = UIStyles.BgInput;
                    UIStyles.SetBorderColor(option, UIStyles.BorderSubtle);
                    indicator.style.backgroundColor = Color.clear;
                }
            }
        }

        void ValidateAnswer()
        {
            if (hasAnswered) return;

            // Protection contre les doubles appels rapides
            if (isValidating) return;
            isValidating = true;

            // Enregistrer la tentative dans l'analytics
            if (currentQuestionData != null)
            {
                var attemptIndexes = isMultipleChoice ? selectedAnswerIndexes : new List<int> { selectedAnswerIndex };
                currentQuestionData.AddUserAttempt(attemptIndexes);
                // IncrementCurrentInteractionAttempts incrémente le compteur d'attempts
                TrainingAnalytics.Instance?.IncrementCurrentInteractionAttempts();

                // Debug pour vérifier le nombre de tentatives
                LogDebug($"Attempt #{TrainingAnalytics.Instance?.GetCurrentInteraction()?.attempts} for question");
            }

            bool isCorrect = false;

            if (isMultipleChoice)
            {
                // Pour les réponses multiples, vérifier si les sélections correspondent exactement
                if (selectedAnswerIndexes.Count == 0) return;

                selectedAnswerIndexes.Sort();
                correctAnswerIndexes.Sort();

                if (ContentDisplayManager.Instance?.DebugMode ?? false)
                {
                    LogDebug($"Selected answers: {string.Join(", ", selectedAnswerIndexes)}");
                    LogDebug($"Correct answers: {string.Join(", ", correctAnswerIndexes)}");
                }

                isCorrect = selectedAnswerIndexes.Count == correctAnswerIndexes.Count &&
                           selectedAnswerIndexes.SequenceEqual(correctAnswerIndexes);
            }
            else
            {
                // Pour réponse unique
                if (selectedAnswerIndex < 0) return;
                isCorrect = selectedAnswerIndex == correctAnswerIndex;
            }

            // Afficher le feedback visuel sur les options
            ShowAnswerFeedback(isCorrect);

            // Afficher le feedback textuel seulement s'il n'est pas vide
            string feedbackText = isCorrect ? currentFeedback : currentIncorrectFeedback;
            if (!string.IsNullOrWhiteSpace(feedbackText))
            {
                feedbackContainer.style.display = DisplayStyle.Flex;
                feedbackLabel.text = feedbackText;
            }
            else
            {
                feedbackContainer.style.display = DisplayStyle.None;
            }

            if (isCorrect)
            {
                hasAnswered = true;

                // Terminer le tracking de cette question avec succès
                if (currentQuestionData != null)
                {
                    // Score = 100 seulement si correct du premier coup, sinon 0
                    currentQuestionData.finalScore = currentQuestionData.firstAttemptCorrect ? 100f : 0f;
                    // Mettre à jour les données avant de terminer
                    if (TrainingAnalytics.Instance != null)
                    {
                        TrainingAnalytics.Instance.AddDataToCurrentInteraction("finalScore", currentQuestionData.finalScore);
                        TrainingAnalytics.Instance.AddDataToCurrentInteraction("userAnswers", currentQuestionData.userAnswers);
                        TrainingAnalytics.Instance.AddDataToCurrentInteraction("firstAttemptCorrect", currentQuestionData.firstAttemptCorrect);
                        // FIX: Success = true car la réponse finale est correcte (même si pas du premier coup)
                        // Le score reste à 0 si firstAttemptCorrect = false, mais on compte l'interaction comme réussie
                        TrainingAnalytics.Instance.EndCurrentInteraction(true);
                    }
                }
                feedbackContainer.style.backgroundColor = UIStyles.SuccessBg;
                UIStyles.SetBorderWidth(feedbackContainer, 2);
                UIStyles.SetBorderColor(feedbackContainer, UIStyles.Success);

                // Si on a plusieurs questions, passer à la suivante
                if (questionKeys != null && questionKeys.Count > 1)
                {
                    bool isLastQuestion = currentQuestionIndex >= questionKeys.Count - 1;
                    UIStyles.SetButtonIcon(validateButton,
                        isLastQuestion ? WiseTwinIcons.Check(20, UIStyles.TextOnAccent)
                                       : WiseTwinIcons.ArrowRight(20, UIStyles.TextOnAccent));
                    validateButton.clicked -= ValidateAnswer;
                    validateButton.clicked += NextQuestion;
                }
                else
                {
                    // Question unique - bouton icône continue
                    UIStyles.SetButtonIcon(validateButton, WiseTwinIcons.ArrowRight(20, UIStyles.TextOnAccent));
                    validateButton.clicked -= ValidateAnswer;
                    validateButton.clicked += () => {
                        OnCompleted?.Invoke(currentObjectId, true);
                        Close();
                    };
                }
            }
            else
            {
                // Réponse incorrecte - afficher feedback d'erreur et bloquer
                hasAnswered = true;

                // Terminer le tracking de cette question avec échec (mais ne pas compter comme erreur totale si retry)
                if (currentQuestionData != null)
                {
                    // Score = 0 car pas correct du premier coup
                    currentQuestionData.finalScore = 0f;
                    // Mettre à jour les données avant de terminer
                    if (TrainingAnalytics.Instance != null)
                    {
                        TrainingAnalytics.Instance.AddDataToCurrentInteraction("finalScore", currentQuestionData.finalScore);
                        TrainingAnalytics.Instance.AddDataToCurrentInteraction("userAnswers", currentQuestionData.userAnswers);
                        TrainingAnalytics.Instance.AddDataToCurrentInteraction("firstAttemptCorrect", false);
                        // On marque success = true quand même pour ne pas bloquer la progression
                        TrainingAnalytics.Instance.EndCurrentInteraction(true);
                    }
                }

                feedbackContainer.style.backgroundColor = UIStyles.DangerBg;
                UIStyles.SetBorderWidth(feedbackContainer, 2);
                UIStyles.SetBorderColor(feedbackContainer, UIStyles.Danger);

                // Changer le bouton en "Suivant" - pas de retry
                if (questionKeys != null && questionKeys.Count > 1)
                {
                    bool isLastQuestion = currentQuestionIndex >= questionKeys.Count - 1;
                    UIStyles.SetButtonIcon(validateButton,
                        isLastQuestion ? WiseTwinIcons.Check(20, UIStyles.TextOnAccent)
                                       : WiseTwinIcons.ArrowRight(20, UIStyles.TextOnAccent));
                    validateButton.clicked -= ValidateAnswer;
                    validateButton.clicked += NextQuestion;
                }
                else
                {
                    // Question unique - bouton icône continue
                    UIStyles.SetButtonIcon(validateButton, WiseTwinIcons.ArrowRight(20, UIStyles.TextOnAccent));
                    validateButton.clicked -= ValidateAnswer;
                    validateButton.clicked += () => {
                        OnCompleted?.Invoke(currentObjectId, true);
                        Close();
                    };
                }
            }
        }

        /// <summary>
        /// Nouvelle fonction: Continue sans validation ni feedback
        /// Enregistre la réponse et passe directement à la suite
        /// </summary>
        void ContinueToNext()
        {
            if (hasAnswered) return;

            // Vérifier qu'une sélection a été faite
            if (isMultipleChoice)
            {
                if (selectedAnswerIndexes.Count == 0) return;
            }
            else
            {
                if (selectedAnswerIndex < 0) return;
            }

            hasAnswered = true;

            // Enregistrer la réponse dans les analytics
            if (currentQuestionData != null)
            {
                var attemptIndexes = isMultipleChoice ? selectedAnswerIndexes : new List<int> { selectedAnswerIndex };
                currentQuestionData.AddUserAttempt(attemptIndexes);

                // Vérifier si la réponse est correcte pour les analytics
                bool isCorrect = false;
                if (isMultipleChoice)
                {
                    selectedAnswerIndexes.Sort();
                    correctAnswerIndexes.Sort();
                    isCorrect = selectedAnswerIndexes.Count == correctAnswerIndexes.Count &&
                               selectedAnswerIndexes.SequenceEqual(correctAnswerIndexes);
                }
                else
                {
                    isCorrect = selectedAnswerIndex == correctAnswerIndex;
                }

                // Enregistrer le score dans analytics
                currentQuestionData.finalScore = isCorrect ? 100f : 0f;

                if (TrainingAnalytics.Instance != null)
                {
                    TrainingAnalytics.Instance.AddDataToCurrentInteraction("finalScore", currentQuestionData.finalScore);
                    TrainingAnalytics.Instance.AddDataToCurrentInteraction("userAnswers", currentQuestionData.userAnswers);
                    TrainingAnalytics.Instance.AddDataToCurrentInteraction("firstAttemptCorrect", isCorrect);
                    TrainingAnalytics.Instance.EndCurrentInteraction(isCorrect);
                }
            }

            // Passer à la question suivante ou terminer (sans feedback visuel)
            if (questionKeys != null && questionKeys.Count > 1)
            {
                // Plusieurs questions: passer à la suivante
                currentQuestionIndex++;
                if (currentQuestionIndex >= questionKeys.Count)
                {
                    // Toutes les questions terminées
                    OnCompleted?.Invoke(currentObjectId, true);
                    Close();
                }
                else
                {
                    // Afficher la question suivante
                    DisplayCurrentQuestion();
                }
            }
            else
            {
                // Question unique: terminer
                OnCompleted?.Invoke(currentObjectId, true);
                Close();
            }
        }

        void NextQuestion()
        {
            // Réinitialiser le flag de validation pour la prochaine question
            isValidating = false;
            currentQuestionIndex++;
            if (currentQuestionIndex >= questionKeys.Count)
            {
                // Toutes les questions terminées
                OnCompleted?.Invoke(currentObjectId, true);
                Close();
            }
            else
            {
                // Afficher la question suivante
                DisplayCurrentQuestion();
            }
        }

        public void Close()
        {
            // Réinitialiser tous les flags
            isValidating = false;
            hasAnswered = false;

            // Re-enable player controls
            PlayerControls.SetEnabled(true);

            rootElement?.Clear();
            OnClosed?.Invoke(currentObjectId);
        }

        void OnDestroy() { }

        // Flat string extraction (mono-language, with legacy {en, fr} backward compat)
        string ExtractLocalizedText(Dictionary<string, object> data, string key, string language)
        {
            return LocalizedValueReader.ReadString(data, key);
        }

        List<string> ExtractLocalizedList(Dictionary<string, object> data, string key, string language)
        {
            return LocalizedValueReader.ReadStringList(data, key);
        }

        int ExtractInt(Dictionary<string, object> data, string key)
        {
            if (data.ContainsKey(key))
            {
                if (data[key] is int intValue) return intValue;
                if (data[key] is long longValue) return (int)longValue;
                if (data[key] is float floatValue) return (int)floatValue;
                if (data[key] is double doubleValue) return (int)doubleValue;
                if (int.TryParse(data[key]?.ToString(), out int parsed)) return parsed;
            }
            return 0;
        }

        string ExtractString(Dictionary<string, object> data, string key)
        {
            return data.ContainsKey(key) ? data[key]?.ToString() ?? "" : "";
        }

        // Méthode pour initialiser le tracking analytics
        void InitializeQuestionTracking()
        {
            if (TrainingAnalytics.Instance == null)
            {
                LogWarning("TrainingAnalytics not available - creating instance");
                var analyticsGO = new GameObject("TrainingAnalytics");
                analyticsGO.AddComponent<Analytics.TrainingAnalytics>();
            }

            // Créer les données de la question avec clés uniquement (pas de texte)
            currentQuestionData = new QuestionInteractionData();
            currentQuestionData.questionKey = currentQuestionKey; // Clé pour jointure avec metadata
            currentQuestionData.objectId = currentObjectId; // ObjectId pour retrouver dans metadata
            // IMPORTANT : Créer une COPIE de la liste pour éviter que les Clear() ultérieurs ne vident les données
            currentQuestionData.correctAnswers = isMultipleChoice ? new List<int>(correctAnswerIndexes) : new List<int> { correctAnswerIndex };

            // Démarrer l'interaction
            string questionId = $"{currentObjectId}_{currentQuestionKey}";

            var subtype = isMultipleChoice ? "multiple_choice" : "single_choice";

            LogDebug($"Initializing tracking for question: {questionId} (key: {currentQuestionKey})");
            TrainingAnalytics.Instance.TrackQuestionInteraction(currentObjectId, questionId, currentQuestionData);
        }

        private void LogDebug(string message)
        {
            if (enableDebugLogs)
            {
                Debug.Log($"[QuestionDisplayer] {message}");
            }
        }

        private void LogWarning(string message)
        {
            if (enableDebugLogs)
            {
                Debug.LogWarning($"[QuestionDisplayer] {message}");
            }
        }

        private void LogError(string message)
        {
            Debug.LogError($"[QuestionDisplayer] {message}");
        }
    }
}