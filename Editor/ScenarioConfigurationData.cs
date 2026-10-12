using System;
using System.Collections.Generic;
using UnityEngine;

namespace WiseTwin.Editor
{
    /// <summary>
    /// Data classes for scenario configuration in WiseTwinEditor.
    /// Mono-language: all text fields are flat strings edited in the target language.
    /// </summary>

    [Serializable]
    public enum ScenarioType
    {
        Question,
        Procedure,
        Text,
        Dialogue,
        LanguageSelector
    }

    [Serializable]
    public enum ValidationType
    {
        Click,
        Manual,
        Zone,
        Group,    // Touch every object in the list (any order). Step advances when all touched.
        External  // No package-side validation: only WiseTwinAPI.ValidateCurrentStep advances the step.
    }

    [Serializable]
    public class ScenarioConfiguration
    {
        public string id = "scenario_1";
        public ScenarioType type = ScenarioType.Question;
        // Formation multi-scènes : scène qui joue ce scénario (vide pour une formation à une scène)
        public string scene = "";

        /// <summary>
        /// If true, the background will be blurred and interactions outside WiseTwin UI will be blocked.
        /// Forces the user to focus on the current scenario before continuing.
        /// </summary>
        public bool focusMode = false;

        public List<QuestionScenarioData> questions = new List<QuestionScenarioData>();
        public ProcedureScenarioData procedureData = new ProcedureScenarioData();
        public TextScenarioData textData = new TextScenarioData();
        public DialogueScenarioData dialogueData = new DialogueScenarioData();
        public LanguageSelectorScenarioData languageSelectorData = new LanguageSelectorScenarioData();

        public ScenarioConfiguration()
        {
            questions = new List<QuestionScenarioData> { new QuestionScenarioData() };
            procedureData = new ProcedureScenarioData();
            textData = new TextScenarioData();
            dialogueData = new DialogueScenarioData();
            languageSelectorData = new LanguageSelectorScenarioData();
        }
    }

    [Serializable]
    public class QuestionScenarioData
    {
        public string questionText = "";
        public List<string> options = new List<string> { "Option 1", "Option 2" };
        public List<int> correctAnswers = new List<int> { 0 };
        public bool isMultipleChoice = false;
        public string feedback = "";
        public string incorrectFeedback = "";
        public string hint = "";

        // Optional illustration image (embedded in the build via Resources).
        // 'image' is the editor reference; 'imagePath' is the Resources-relative path
        // written to / read from the metadata JSON.
        public Texture2D image = null;
        public string imagePath = "";
    }

    [Serializable]
    public class ProcedureScenarioData
    {
        public string title = "";
        public string description = "";
        public List<ProcedureStep> steps = new List<ProcedureStep>();
        public List<FakeObject> fakeObjects = new List<FakeObject>();
    }

    [Serializable]
    public class ProcedureStep
    {
        public string text = "";
        public GameObject targetObject = null;
        public string targetObjectName = "";
        public Color highlightColor = Color.yellow;
        public bool useBlinking = true;
        public string hint = "";
        public ValidationType validationType = ValidationType.Click;
        public GameObject zoneObject = null;
        public string zoneObjectName = "";
        // Optional illustration image (embedded in the build via Resources).
        public Texture2D image = null;
        public string imagePath = "";
        public List<FakeObject> fakeObjects = new List<FakeObject>();

        // Used only when validationType == Group: every object in this list must be clicked
        // (in any order) for the step to advance. Names are kept in sync so the runtime
        // can resolve them via GameObject.Find.
        public List<GameObject> targetObjects = new List<GameObject>();
        public List<string> targetObjectNames = new List<string>();

        // Used only when validationType == External: the step advances only when
        // WiseTwinAPI.ValidateExternalStep(id) is called with a matching ID.
        // If empty, any call to ValidateCurrentStep() advances the step (backward compat).
        public string externalValidationId = "";
    }

    [Serializable]
    public class FakeObject
    {
        public GameObject fakeObject = null;
        public string fakeObjectName = "";
        public string errorMessage = "Wrong object!";
    }

    [Serializable]
    public class TextScenarioData
    {
        public string title = "";
        public string content = "";

        // Optional illustration image (embedded in the build via Resources).
        public Texture2D image = null;
        public string imagePath = "";
    }

    [Serializable]
    public class DialogueScenarioData
    {
        public string dialogueId = "";
        public string title = "";
        // Serialized graph data JSON from the visual editor
        public string graphDataJSON = "";
    }

    [Serializable]
    public class LanguageSelectorScenarioData
    {
        public string title = "Sélection de la langue";
        public string instruction = "Veuillez sélectionner votre langue";
        public bool showFullLanguageName = true;
        public bool autoAdvance = true; // Advance to next scenario after selection
    }

    /// <summary>
    /// Configuration for video triggers - click on 3D object to play video.
    /// </summary>
    [Serializable]
    public class VideoTriggerConfiguration
    {
        public GameObject targetObject = null;
        public string targetObjectName = "";
        public string videoUrl = "";
    }
}
