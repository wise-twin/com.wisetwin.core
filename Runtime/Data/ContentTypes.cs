using System;
using System.Collections.Generic;
using UnityEngine;

namespace WiseTwin
{
    /// <summary>
    /// Types de contenu supportés dans les formations WiseTwin
    /// </summary>
    public enum ContentType
    {
        Question,         // Questions QCM, Vrai/Faux, etc.
        Procedure,        // Procédures séquentielles
        Text,             // Affichage de texte informatif
        Dialogue,         // Dialogue interactif avec PNJ (arbre de conversation)
        LanguageSelector  // Sélection de la langue
    }

    /// <summary>
    /// Types de questions disponibles
    /// </summary>
    public enum QuestionType
    {
        MultipleChoice,
        TrueFalse,
        TextInput,
        DragAndDrop,
        Matching,
        Ordering,
        Hotspot
    }

    /// <summary>
    /// Types de médias supportés
    /// </summary>
    public enum MediaType
    {
        Image,
        Video,
        Audio,
        Model3D,
        PDF,
        Animation
    }

    /// <summary>
    /// Niveaux de difficulté
    /// </summary>
    public enum DifficultyLevel
    {
        Beginner,
        Easy,
        Intermediate,
        Hard,
        Expert
    }

    /// <summary>
    /// États de progression
    /// </summary>
    public enum ProgressState
    {
        NotStarted,
        InProgress,
        Completed,
        Failed,
        Skipped
    }

    /// <summary>
    /// Données de résultat pour une formation
    /// </summary>
    [Serializable]
    public class TrainingResultData
    {
        public string trainingId;
        public string userId;
        public DateTime startTime;
        public DateTime endTime;
        public float completionTime;
        public int score;
        public int maxScore;
        public float percentage;
        public ProgressState state;
        public List<QuestionResult> questionResults;
        public List<ProcedureResult> procedureResults;
        public Dictionary<string, object> customData;

        public TrainingResultData()
        {
            questionResults = new List<QuestionResult>();
            procedureResults = new List<ProcedureResult>();
            customData = new Dictionary<string, object>();
            startTime = DateTime.Now;
        }

        public void Complete()
        {
            endTime = DateTime.Now;
            completionTime = (float)(endTime - startTime).TotalSeconds;
            state = ProgressState.Completed;
            CalculateScore();
        }

        private void CalculateScore()
        {
            int totalScore = 0;
            int totalMax = 0;

            foreach (var qr in questionResults)
            {
                totalScore += qr.score;
                totalMax += qr.maxScore;
            }

            foreach (var pr in procedureResults)
            {
                if (pr.completed) totalScore += 10;
                totalMax += 10;
            }

            score = totalScore;
            maxScore = totalMax;
            percentage = maxScore > 0 ? (float)score / maxScore * 100f : 0f;
        }
    }

    /// <summary>
    /// Résultat d'une question
    /// </summary>
    [Serializable]
    public class QuestionResult
    {
        public string questionId;
        public string question;
        public string userAnswer;
        public string correctAnswer;
        public bool isCorrect;
        public float timeSpent;
        public int attempts;
        public int score;
        public int maxScore;
        public DateTime answeredAt;
    }

    /// <summary>
    /// Résultat d'une procédure
    /// </summary>
    [Serializable]
    public class ProcedureResult
    {
        public string procedureId;
        public string title;
        public bool completed;
        public int stepsCompleted;
        public int totalSteps;
        public float timeSpent;
        public List<string> errorsMade;
        public DateTime completedAt;

        public ProcedureResult()
        {
            errorsMade = new List<string>();
        }
    }

    /// <summary>
    /// Contenu de procédure avec étapes
    /// </summary>
    [Serializable]
    public class ProcedureContent
    {
        public string id;
        public string title;
        public string description;
        public List<ProcedureStep> steps;
        public float estimatedTime;
        public DifficultyLevel difficulty;
        public List<string> requiredTools;
        public List<string> safetyWarnings;

        public ProcedureContent()
        {
            steps = new List<ProcedureStep>();
            requiredTools = new List<string>();
            safetyWarnings = new List<string>();
        }
    }

    /// <summary>
    /// Étape individuelle d'une procédure
    /// </summary>
    [Serializable]
    public class ProcedureStep
    {
        public int order;
        public string instruction;
        public string validation;
        public string hint;
        public string mediaUrl;
        public MediaType mediaType;
        public float timeLimit;
        public bool critical; // Étape critique qui ne peut pas être sautée
        public List<string> checkpoints;

        public ProcedureStep()
        {
            checkpoints = new List<string>();
        }
    }

    /// <summary>
    /// Contenu de question enrichi
    /// </summary>
    [Serializable]
    public class QuestionContent
    {
        public string id;
        public string text;
        public QuestionType type;
        public List<string> options;
        public string correctAnswer;
        public int correctAnswerIndex;
        public string explanation;
        public string feedback;
        public string incorrectFeedback;
        public string hint;
        public float timeLimit;
        public int points;
        public DifficultyLevel difficulty;
        public string mediaUrl;
        public MediaType mediaType;
        public Dictionary<string, object> metadata;

        public QuestionContent()
        {
            options = new List<string>();
            metadata = new Dictionary<string, object>();
            type = QuestionType.MultipleChoice;
            points = 10;
        }
    }

    /// <summary>
    /// Contenu de dialogue pour interactions narratives
    /// </summary>
    [Serializable]
    public class DialogueContent
    {
        public string id;
        public string character;
        public string avatarUrl;
        public List<DialogueLine> lines;
        public List<DialogueChoice> choices;
        public string emotion;
        public float displayDuration;

        public DialogueContent()
        {
            lines = new List<DialogueLine>();
            choices = new List<DialogueChoice>();
        }
    }

    /// <summary>
    /// Ligne de dialogue individuelle
    /// </summary>
    [Serializable]
    public class DialogueLine
    {
        public string text;
        public string audioUrl;
        public float duration;
        public string emotion;
        public bool waitForInput;
    }

    /// <summary>
    /// Choix dans un dialogue interactif
    /// </summary>
    [Serializable]
    public class DialogueChoice
    {
        public string text;
        public string nextDialogueId;
        public int points;
        public string consequence;
        public bool enabled = true;
    }

    /// <summary>
    /// Configuration d'évaluation globale
    /// </summary>
    [Serializable]
    public class AssessmentConfig
    {
        public int passingScore = 70;
        public int maxAttempts = 3;
        public bool allowSkip = false;
        public bool randomizeQuestions = false;
        public bool showFeedback = true;
        public bool showScore = true;
        public float timeLimit = 0f; // 0 = pas de limite
        public DifficultyLevel difficulty = DifficultyLevel.Intermediate;
    }
}