using System;
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR

public class WiseTwinEditor : EditorWindow
{
    // Centralized data container
    private WiseTwin.Editor.WiseTwinEditorData data;
    private string settingsFilePath;

    // UI State
    private int selectedTab = 0;
    private string[] tabNames = { "General Settings", "Metadata Config", "Scenario Configuration", "Dialogue", "Video" };
    
    
    [MenuItem("WiseTwin/WiseTwin Editor")]
    public static void ShowWindow()
    {
        WiseTwinEditor window = GetWindow<WiseTwinEditor>("WiseTwin Editor");
        window.minSize = new Vector2(700, 600);
        window.Show();
    }
    
    // Static callback for graph editor to trigger dialogue persistence
    public static System.Action OnRequestDialogueSave;

    void OnEnable()
    {
        // Initialize data container
        data = new WiseTwin.Editor.WiseTwinEditorData();

        settingsFilePath = Path.Combine(Application.persistentDataPath, "WiseTwinSettings.json");
        LoadSettings();
        LoadDialogueData();
        InitializeSceneId();

        // Register callback for graph editor saves
        OnRequestDialogueSave = SaveDialogueData;

        // Synchroniser automatiquement avec WiseTwinManager au chargement
        EditorApplication.delayCall += () =>
        {
            SyncWithSceneManager();
        };
        LoadExistingJSONContent();
        InitializeUnityContent();
    }

    void LoadSettings()
    {
        if (File.Exists(settingsFilePath))
        {
            try
            {
                string jsonContent = File.ReadAllText(settingsFilePath);
                var settingsDict = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonContent);

                if (settingsDict.ContainsKey("useLocalMode"))
                    data.useLocalMode = (bool)settingsDict["useLocalMode"];
                if (settingsDict.ContainsKey("azureApiUrl"))
                    data.azureApiUrl = settingsDict["azureApiUrl"].ToString();
                if (settingsDict.ContainsKey("containerId"))
                    data.containerId = settingsDict["containerId"].ToString();
                if (settingsDict.ContainsKey("buildType"))
                    data.buildType = settingsDict["buildType"].ToString();
                if (settingsDict.ContainsKey("allowKeyboardControl"))
                    data.allowKeyboardControl = (bool)settingsDict["allowKeyboardControl"];
                if (settingsDict.ContainsKey("allowMouseControl"))
                    data.allowMouseControl = (bool)settingsDict["allowMouseControl"];

            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[WiseTwin] Could not load settings: {e.Message}");
            }
        }
    }
    
    void SaveSettings()
    {
        try
        {
            var settingsDict = new Dictionary<string, object>
            {
                ["useLocalMode"] = data.useLocalMode,
                ["azureApiUrl"] = data.azureApiUrl,
                ["containerId"] = data.containerId,
                ["buildType"] = data.buildType,
                ["allowKeyboardControl"] = data.allowKeyboardControl,
                ["allowMouseControl"] = data.allowMouseControl
            };

            string jsonContent = JsonConvert.SerializeObject(settingsDict, Formatting.Indented);
            File.WriteAllText(settingsFilePath, jsonContent);

        }
        catch (System.Exception e)
        {
            Debug.LogError($"[WiseTwin] Could not save settings: {e.Message}");
        }
    }

    string dialogueDataFilePath => Path.Combine(Application.persistentDataPath, "WiseTwinDialogues.json");

    void SaveDialogueData()
    {
        try
        {
            var dialoguesList = new List<Dictionary<string, string>>();
            foreach (var d in data.dialogues)
            {
                dialoguesList.Add(new Dictionary<string, string>
                {
                    ["dialogueId"] = d.dialogueId ?? "",
                    ["title"] = d.title ?? "",
                    ["graphDataJSON"] = d.graphDataJSON ?? ""
                });
            }
            string json = JsonConvert.SerializeObject(dialoguesList, Formatting.Indented);
            File.WriteAllText(dialogueDataFilePath, json);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[WiseTwin] Could not save dialogue data: {e.Message}");
        }
    }

    void LoadDialogueData()
    {
        if (!File.Exists(dialogueDataFilePath)) return;

        try
        {
            string json = File.ReadAllText(dialogueDataFilePath);
            var dialoguesList = JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(json);
            if (dialoguesList == null) return;

            // One-shot cleanup: previous versions of the editor could spawn many dialogues with
            // identical graphDataJSON content but different generated IDs (dialogue_3, dialogue_4...)
            // each time the editor reopened. Dedupe by content here so the library settles down.
            var seenContent = new HashSet<string>();
            int duplicatesDropped = 0;

            foreach (var dict in dialoguesList)
            {
                // Backward compat: old format stored "titleEN" / "titleFR"
                string title = "";
                if (dict.ContainsKey("title")) title = dict["title"];
                else if (dict.ContainsKey("titleEN") && !string.IsNullOrEmpty(dict["titleEN"])) title = dict["titleEN"];
                else if (dict.ContainsKey("titleFR")) title = dict["titleFR"];

                var dialogue = new WiseTwin.Editor.DialogueScenarioData
                {
                    dialogueId = dict.ContainsKey("dialogueId") ? dict["dialogueId"] : "",
                    title = title,
                    graphDataJSON = dict.ContainsKey("graphDataJSON") ? dict["graphDataJSON"] : ""
                };

                if (!string.IsNullOrEmpty(dialogue.graphDataJSON))
                {
                    if (seenContent.Contains(dialogue.graphDataJSON))
                    {
                        duplicatesDropped++;
                        continue;
                    }
                    seenContent.Add(dialogue.graphDataJSON);
                }
                data.dialogues.Add(dialogue);
            }

            if (duplicatesDropped > 0)
            {
                Debug.Log($"[WiseTwin] Dropped {duplicatesDropped} duplicate dialogue(s) from the library on load. Saving cleaned version.");
                SaveDialogueData();
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[WiseTwin] Could not load dialogue data: {e.Message}");
        }
    }

    void InitializeSceneId()
    {
        // Get current scene name
        data.sceneId = SceneManager.GetActiveScene().name;
        if (string.IsNullOrEmpty(data.sceneId))
        {
            data.sceneId = "default-scene";
        }
    }
    
    void LoadExistingJSONContent()
    {
        string targetFileName = $"{data.sceneId}-metadata.json";

        // Possible paths to search for JSON file
        string[] possiblePaths = {
            Path.Combine(Application.streamingAssetsPath, targetFileName),
            Path.Combine(Application.streamingAssetsPath, "metadata.json"), // Fallback
        };

        string foundPath = null;
        foreach (string path in possiblePaths)
        {
            if (File.Exists(path))
            {
                foundPath = path;
                break;
            }
        }

        if (foundPath != null)
        {
            try
            {
                string jsonContent = File.ReadAllText(foundPath);
                ParseExistingJSON(jsonContent);
                data.currentLoadedFile = Path.GetFileName(foundPath);
                data.hasLoadedExistingJSON = true;

            }
            catch (System.Exception e)
            {
                Debug.LogError($"❌ Error loading JSON: {e.Message}");
                data.hasLoadedExistingJSON = false;
            }
        }
        else
        {
            data.hasLoadedExistingJSON = false;
        }
    }
    
    void ParseExistingJSON(string jsonContent)
    {
        try
        {
            // Parse as raw JObject first so we can accept both flat strings and legacy {en, fr} objects
            var root = Newtonsoft.Json.Linq.JObject.Parse(jsonContent);

            // Top-level localized fields (flat strings, backward compat with multi-lang)
            data.projectTitle = GetFlatString(root["title"]) ?? data.projectTitle;
            data.projectDescription = GetFlatString(root["description"]) ?? data.projectDescription;

            var version = root["version"]?.ToString();
            if (!string.IsNullOrEmpty(version)) data.projectVersion = version;

            var imageUrl = root["imageUrl"]?.ToString();
            if (!string.IsNullOrEmpty(imageUrl)) data.imageUrl = imageUrl;

            var durationStr = root["duration"]?.ToString();
            if (!string.IsNullOrEmpty(durationStr)) ParseDurationFromString(durationStr);

            var difficultyStr = root["difficulty"]?.ToString();
            if (!string.IsNullOrEmpty(difficultyStr)) ParseDifficultyFromString(difficultyStr);

            var languageStr = root["language"]?.ToString();
            if (!string.IsNullOrEmpty(languageStr)) ParseLanguageFromString(languageStr);

            var tagsToken = root["tags"] as Newtonsoft.Json.Linq.JArray;
            if (tagsToken != null && tagsToken.Count > 0)
                data.tags = tagsToken.ToObject<List<string>>();

            // Unity legacy section
            var unityToken = root["unity"] as Newtonsoft.Json.Linq.JObject;
            if (unityToken != null)
            {
                data.unityContentJSON = unityToken.ToString(Formatting.Indented);
                ValidateUnityContent();
            }
            else
            {
                InitializeUnityContent();
            }

            // Scenarios (new format)
            var scenariosToken = root["scenarios"] as Newtonsoft.Json.Linq.JArray;
            if (scenariosToken != null && scenariosToken.Count > 0)
            {
                var scenariosList = scenariosToken.ToObject<List<object>>();
                LoadScenariosFromJSON(scenariosList);
                Debug.Log($"✅ Loaded {data.scenarios.Count} scenarios from metadata");
            }

            // Video triggers
            var videoTriggersToken = root["videoTriggers"] as Newtonsoft.Json.Linq.JArray;
            if (videoTriggersToken != null && videoTriggersToken.Count > 0)
            {
                var triggersList = videoTriggersToken.ToObject<List<object>>();
                LoadVideoTriggersFromJSON(triggersList);
                Debug.Log($"✅ Loaded {data.videoTriggers.Count} video triggers from metadata");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"❌ Error parsing existing JSON: {e.Message}");
            InitializeUnityContent();
        }
    }

    void LoadVideoTriggersFromJSON(List<object> videoTriggersJSON)
    {
        data.videoTriggers.Clear();

        foreach (var triggerObj in videoTriggersJSON)
        {
            try
            {
                var triggerDict = triggerObj as Dictionary<string, object>;
                if (triggerDict == null)
                {
                    var jObject = Newtonsoft.Json.Linq.JObject.FromObject(triggerObj);
                    triggerDict = jObject.ToObject<Dictionary<string, object>>();
                }

                var trigger = new WiseTwin.Editor.VideoTriggerConfiguration();

                // Load target object name
                if (triggerDict.ContainsKey("targetObjectName"))
                {
                    trigger.targetObjectName = triggerDict["targetObjectName"]?.ToString() ?? "";
                }

                // Load video URL (flat string, with backward compat for {en, fr})
                if (triggerDict.ContainsKey("videoUrl"))
                {
                    trigger.videoUrl = GetFlatString(triggerDict["videoUrl"]);
                }

                data.videoTriggers.Add(trigger);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"⚠️ Failed to load video trigger: {e.Message}");
            }
        }
    }

    void LoadScenariosFromJSON(List<object> scenariosJSON)
    {
        data.scenarios.Clear();

        foreach (var scenarioObj in scenariosJSON)
        {
            try
            {
                var scenarioDict = scenarioObj as Dictionary<string, object>;
                if (scenarioDict == null)
                {
                    var jObject = Newtonsoft.Json.Linq.JObject.FromObject(scenarioObj);
                    scenarioDict = jObject.ToObject<Dictionary<string, object>>();
                }

                var scenario = new WiseTwin.Editor.ScenarioConfiguration();

                // Load basic fields
                if (scenarioDict.ContainsKey("id"))
                    scenario.id = scenarioDict["id"]?.ToString();

                if (scenarioDict.ContainsKey("type"))
                {
                    string typeStr = scenarioDict["type"]?.ToString();
                    if (Enum.TryParse<WiseTwin.Editor.ScenarioType>(typeStr, true, out var type))
                        scenario.type = type;
                }

                // Load content based on type
                switch (scenario.type)
                {
                    case WiseTwin.Editor.ScenarioType.Question:
                        scenario.questions.Clear();
                        if (scenarioDict.ContainsKey("question"))
                        {
                            // Single question
                            var questionData = new WiseTwin.Editor.QuestionScenarioData();
                            LoadQuestionDataFromJSON(questionData, scenarioDict["question"]);
                            scenario.questions.Add(questionData);
                        }
                        else if (scenarioDict.ContainsKey("questions"))
                        {
                            // Multiple questions
                            var questionsArray = scenarioDict["questions"] as Newtonsoft.Json.Linq.JArray;
                            if (questionsArray != null)
                            {
                                foreach (var questionObj in questionsArray)
                                {
                                    var questionData = new WiseTwin.Editor.QuestionScenarioData();
                                    LoadQuestionDataFromJSON(questionData, questionObj);
                                    scenario.questions.Add(questionData);
                                }
                            }
                        }
                        break;

                    case WiseTwin.Editor.ScenarioType.Procedure:
                        if (scenarioDict.ContainsKey("procedure"))
                            LoadProcedureDataFromJSON(scenario.procedureData, scenarioDict["procedure"]);
                        break;

                    case WiseTwin.Editor.ScenarioType.Text:
                        if (scenarioDict.ContainsKey("text"))
                            LoadTextDataFromJSON(scenario.textData, scenarioDict["text"]);
                        break;

                    case WiseTwin.Editor.ScenarioType.Dialogue:
                        if (scenarioDict.ContainsKey("dialogue"))
                            LoadDialogueDataFromJSON(scenario.dialogueData, scenarioDict["dialogue"]);
                        break;
                }

                data.scenarios.Add(scenario);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"⚠️ Failed to load scenario: {e.Message}");
            }
        }
    }

    void LoadQuestionDataFromJSON(WiseTwin.Editor.QuestionScenarioData question, object questionObj)
    {
        var jObject = Newtonsoft.Json.Linq.JObject.FromObject(questionObj);
        var questionDict = jObject.ToObject<Dictionary<string, object>>();

        // Load question text
        if (questionDict.ContainsKey("questionText"))
        {
            question.questionText = GetFlatString(questionDict["questionText"]);
        }

        // Load options
        if (questionDict.ContainsKey("options"))
        {
            question.options = GetFlatStringList(questionDict["options"]);
        }

        // Load correct answers
        if (questionDict.ContainsKey("correctAnswers"))
        {
            var correctAnswersObj = questionDict["correctAnswers"];
            if (correctAnswersObj is Newtonsoft.Json.Linq.JArray jArray)
            {
                question.correctAnswers = jArray.ToObject<List<int>>();
            }
        }

        // Load flags
        if (questionDict.ContainsKey("isMultipleChoice"))
            question.isMultipleChoice = Convert.ToBoolean(questionDict["isMultipleChoice"]);

        // Load feedback
        if (questionDict.ContainsKey("feedback"))
        {
            question.feedback = GetFlatString(questionDict["feedback"]);
        }

        if (questionDict.ContainsKey("incorrectFeedback"))
        {
            question.incorrectFeedback = GetFlatString(questionDict["incorrectFeedback"]);
        }

        // Load hint (reset to empty if not present)
        if (questionDict.ContainsKey("hint"))
        {
            question.hint = GetFlatString(questionDict["hint"]);
        }
        else
        {
            question.hint = "";
        }

        // Load image path + re-hydrate the editor texture reference from Resources
        if (questionDict.ContainsKey("imagePath"))
        {
            question.imagePath = GetFlatString(questionDict["imagePath"]);
            question.image = LoadImageAssetFromResourcesPath(question.imagePath);
        }
        else
        {
            question.imagePath = "";
            question.image = null;
        }
    }

    void LoadProcedureDataFromJSON(WiseTwin.Editor.ProcedureScenarioData procedure, object procedureObj)
    {
        var jObject = Newtonsoft.Json.Linq.JObject.FromObject(procedureObj);
        var procedureDict = jObject.ToObject<Dictionary<string, object>>();

        // Load title
        if (procedureDict.ContainsKey("title"))
        {
            procedure.title = GetFlatString(procedureDict["title"]);
        }

        // Load description
        if (procedureDict.ContainsKey("description"))
        {
            procedure.description = GetFlatString(procedureDict["description"]);
        }

        // Load steps
        if (procedureDict.ContainsKey("steps"))
        {
            LoadProcedureStepsFromJSON(procedure.steps, procedureDict["steps"]);
        }

        // Load fake objects
        if (procedureDict.ContainsKey("fakeObjects"))
        {
            LoadFakeObjectsFromJSON(procedure.fakeObjects, procedureDict["fakeObjects"]);
        }
    }

    void LoadProcedureStepsFromJSON(List<WiseTwin.Editor.ProcedureStep> steps, object stepsObj)
    {
        steps.Clear();

        if (stepsObj is Newtonsoft.Json.Linq.JArray jArray)
        {
            foreach (var stepObj in jArray)
            {
                var step = new WiseTwin.Editor.ProcedureStep();
                var stepDict = stepObj.ToObject<Dictionary<string, object>>();

                // Load text
                if (stepDict.ContainsKey("text"))
                {
                    step.text = GetFlatString(stepDict["text"]);
                }

                // Load target object name
                if (stepDict.ContainsKey("targetObjectName"))
                    step.targetObjectName = stepDict["targetObjectName"]?.ToString();

                // Load highlight color
                if (stepDict.ContainsKey("highlightColor"))
                {
                    string colorHex = stepDict["highlightColor"]?.ToString();
                    if (!string.IsNullOrEmpty(colorHex) && ColorUtility.TryParseHtmlString(colorHex, out Color color))
                        step.highlightColor = color;
                }

                // Load blinking
                if (stepDict.ContainsKey("useBlinking"))
                    step.useBlinking = Convert.ToBoolean(stepDict["useBlinking"]);

                // Load validation type (with backward compat for requireManualValidation)
                if (stepDict.ContainsKey("validationType"))
                {
                    string valTypeStr = stepDict["validationType"]?.ToString();
                    if (Enum.TryParse<WiseTwin.Editor.ValidationType>(valTypeStr, true, out var valType))
                        step.validationType = valType;
                }
                else if (stepDict.ContainsKey("requireManualValidation"))
                {
                    step.validationType = Convert.ToBoolean(stepDict["requireManualValidation"])
                        ? WiseTwin.Editor.ValidationType.Manual
                        : WiseTwin.Editor.ValidationType.Click;
                }

                // Load zone object name
                if (stepDict.ContainsKey("zoneObjectName"))
                    step.zoneObjectName = stepDict["zoneObjectName"]?.ToString();

                // Load group target object names (only when validationType == Group)
                if (stepDict.ContainsKey("targetObjectNames"))
                {
                    step.targetObjectNames = new List<string>();
                    step.targetObjects = new List<GameObject>();
                    if (stepDict["targetObjectNames"] is Newtonsoft.Json.Linq.JArray arr)
                    {
                        foreach (var n in arr)
                        {
                            string objName = n?.ToString();
                            if (!string.IsNullOrEmpty(objName))
                            {
                                step.targetObjectNames.Add(objName);
                                step.targetObjects.Add(null); // resolved at runtime, kept null in editor
                            }
                        }
                    }
                }

                // Load image path + re-hydrate the editor texture reference from Resources
                if (stepDict.ContainsKey("imagePath"))
                {
                    step.imagePath = GetFlatString(stepDict["imagePath"]);
                    step.image = LoadImageAssetFromResourcesPath(step.imagePath);
                }

                // Load hint (reset to empty if not present)
                if (stepDict.ContainsKey("hint"))
                {
                    step.hint = GetFlatString(stepDict["hint"]);
                }
                else
                {
                    step.hint = "";
                }

                // NEW: Load fake objects for this step
                if (stepDict.ContainsKey("fakeObjects"))
                {
                    LoadFakeObjectsFromJSON(step.fakeObjects, stepDict["fakeObjects"]);
                }

                steps.Add(step);
            }
        }
    }

    void LoadFakeObjectsFromJSON(List<WiseTwin.Editor.FakeObject> fakeObjects, object fakeObjectsObj)
    {
        fakeObjects.Clear();

        if (fakeObjectsObj is Newtonsoft.Json.Linq.JArray jArray)
        {
            foreach (var fakeObj in jArray)
            {
                var fake = new WiseTwin.Editor.FakeObject();
                var fakeDict = fakeObj.ToObject<Dictionary<string, object>>();

                // Load object name
                if (fakeDict.ContainsKey("objectName"))
                    fake.fakeObjectName = fakeDict["objectName"]?.ToString();

                // Load error message
                if (fakeDict.ContainsKey("errorMessage"))
                {
                    fake.errorMessage = GetFlatString(fakeDict["errorMessage"]);
                }

                fakeObjects.Add(fake);
            }
        }
    }

    void LoadTextDataFromJSON(WiseTwin.Editor.TextScenarioData text, object textObj)
    {
        var jObject = Newtonsoft.Json.Linq.JObject.FromObject(textObj);
        var textDict = jObject.ToObject<Dictionary<string, object>>();

        // Load title
        if (textDict.ContainsKey("title"))
        {
            text.title = GetFlatString(textDict["title"]);
        }

        // Load content
        if (textDict.ContainsKey("content"))
        {
            text.content = GetFlatString(textDict["content"]);
        }

        // Load image path + re-hydrate the editor texture reference from Resources
        if (textDict.ContainsKey("imagePath"))
        {
            text.imagePath = GetFlatString(textDict["imagePath"]);
            text.image = LoadImageAssetFromResourcesPath(text.imagePath);
        }
        else
        {
            text.imagePath = "";
            text.image = null;
        }
    }

    void LoadDialogueDataFromJSON(WiseTwin.Editor.DialogueScenarioData dialogue, object dialogueObj)
    {
        var jObject = Newtonsoft.Json.Linq.JObject.FromObject(dialogueObj);
        var dialogueDict = jObject.ToObject<Dictionary<string, object>>();

        if (dialogueDict.ContainsKey("title"))
        {
            dialogue.title = GetFlatString(dialogueDict["title"]);
        }

        // Resolve a stable dialogueId: prefer the one written to metadata, fall back to a slug
        // derived from the title so old metadata files (without dialogueId) still match an existing
        // library entry on subsequent reloads instead of spawning new ones.
        string idFromMetadata = dialogueDict.ContainsKey("dialogueId") ? GetFlatString(dialogueDict["dialogueId"]) : "";
        dialogue.dialogueId = !string.IsNullOrEmpty(idFromMetadata)
            ? idFromMetadata
            : DeriveDialogueIdFromTitle(dialogue.title);

        // Link to existing library entry by id; if none exists, add a single new entry.
        // The persistent library file is the source of truth — we never create duplicates here.
        WiseTwin.Editor.DialogueScenarioData libraryEntry = null;
        foreach (var d in data.dialogues)
        {
            if (d.dialogueId == dialogue.dialogueId)
            {
                libraryEntry = d;
                break;
            }
        }

        if (libraryEntry != null)
        {
            // Library wins for graph data — preserves editor-format positions across reloads
            dialogue.title = libraryEntry.title;
            dialogue.graphDataJSON = libraryEntry.graphDataJSON;
        }
        else
        {
            // First time we see this dialogue — register it in the library so it's reusable.
            // Runtime-format JSON is auto-imported to editor format on first graph editor open.
            dialogue.graphDataJSON = jObject.ToString(Formatting.Indented);
            data.dialogues.Add(new WiseTwin.Editor.DialogueScenarioData
            {
                dialogueId = dialogue.dialogueId,
                title = dialogue.title,
                graphDataJSON = dialogue.graphDataJSON
            });
        }
    }

    /// <summary>
    /// Derive a stable, slug-style dialogueId from a title (e.g. "Safety Briefing" → "dialogue_safety_briefing").
    /// Used as a fallback when metadata files don't carry a dialogueId field (old format).
    /// </summary>
    string DeriveDialogueIdFromTitle(string title)
    {
        if (string.IsNullOrEmpty(title)) return "dialogue_untitled";

        var sb = new System.Text.StringBuilder("dialogue_");
        bool lastWasUnderscore = true;
        foreach (char c in title.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c)) { sb.Append(c); lastWasUnderscore = false; }
            else if (!lastWasUnderscore) { sb.Append('_'); lastWasUnderscore = true; }
        }
        string result = sb.ToString().TrimEnd('_');
        return result == "dialogue" ? "dialogue_untitled" : result;
    }

    // Helper methods for JSON parsing

    /// <summary>
    /// Read a string value from a JSON token. Accepts a flat string (current format)
    /// or a legacy multi-language object {en, fr} (uses "en" if present, else "fr").
    /// </summary>
    string GetFlatString(object obj)
    {
        if (obj == null) return "";
        if (obj is string s) return s;
        if (obj is Newtonsoft.Json.Linq.JValue jv) return jv.Value?.ToString() ?? "";
        if (obj is Newtonsoft.Json.Linq.JObject jObj)
        {
            var en = jObj["en"]?.ToString();
            if (!string.IsNullOrEmpty(en)) return en;
            var fr = jObj["fr"]?.ToString();
            return fr ?? "";
        }
        if (obj is Dictionary<string, object> dict)
        {
            if (dict.TryGetValue("en", out var ven) && ven != null) return ven.ToString();
            if (dict.TryGetValue("fr", out var vfr) && vfr != null) return vfr.ToString();
            return "";
        }
        return obj.ToString() ?? "";
    }

    /// <summary>
    /// Read a string list from a JSON token. Accepts a flat array (current format)
    /// or a legacy multi-language object {en: [...], fr: [...]} (prefers "en").
    /// </summary>
    List<string> GetFlatStringList(object obj)
    {
        if (obj == null) return new List<string>();
        if (obj is Newtonsoft.Json.Linq.JArray jArray)
            return jArray.ToObject<List<string>>();
        if (obj is List<object> objList)
            return objList.Select(o => o?.ToString() ?? "").ToList();
        if (obj is Newtonsoft.Json.Linq.JObject jObj)
        {
            var en = jObj["en"] as Newtonsoft.Json.Linq.JArray;
            if (en != null && en.Count > 0) return en.ToObject<List<string>>();
            var fr = jObj["fr"] as Newtonsoft.Json.Linq.JArray;
            if (fr != null) return fr.ToObject<List<string>>();
        }
        if (obj is Dictionary<string, object> dict)
        {
            if (dict.TryGetValue("en", out var ven) && ven is Newtonsoft.Json.Linq.JArray jen)
                return jen.ToObject<List<string>>();
            if (dict.TryGetValue("fr", out var vfr) && vfr is Newtonsoft.Json.Linq.JArray jfr)
                return jfr.ToObject<List<string>>();
        }
        return new List<string>();
    }
    
    void ParseDurationFromString(string durationStr)
    {
        try
        {
            // Extract numbers from string (e.g., "30 minutes" -> 30)
            string numbersOnly = System.Text.RegularExpressions.Regex.Match(durationStr, @"\d+").Value;
            if (!string.IsNullOrEmpty(numbersOnly))
            {
                data.durationMinutes = int.Parse(numbersOnly);
            }
        }
        catch
        {
            data.durationMinutes = 30; // default value
        }
    }

    void ParseDifficultyFromString(string difficultyStr)
    {
        for (int i = 0; i < data.difficultyOptions.Length; i++)
        {
            if (string.Equals(data.difficultyOptions[i], difficultyStr, System.StringComparison.OrdinalIgnoreCase))
            {
                data.difficultyIndex = i;
                return;
            }
        }
        data.difficultyIndex = 1; // Default "Intermediate"
    }

    void ParseLanguageFromString(string languageStr)
    {
        for (int i = 0; i < data.languageOptions.Length; i++)
        {
            if (string.Equals(data.languageOptions[i], languageStr, System.StringComparison.OrdinalIgnoreCase))
            {
                data.languageIndex = i;
                return;
            }
        }
        data.languageIndex = 0; // Default "fr"
    }
    
    void InitializeUnityContent()
    {
        if (string.IsNullOrEmpty(data.unityContentJSON))
        {
            data.unityContentJSON = JsonConvert.SerializeObject(new Dictionary<string, object>(), Formatting.Indented);
        }
        ValidateUnityContent();
    }

    void ValidateUnityContent()
    {
        try
        {
            if (!string.IsNullOrEmpty(data.unityContentJSON))
            {
                JsonConvert.DeserializeObject(data.unityContentJSON);
                data.isUnityContentValid = true;
            }
        }
        catch
        {
            data.isUnityContentValid = false;
        }
    }
    
    void OnGUI()
    {
        DrawHeader();
        DrawLoadedFileInfo();
        DrawTabs();

        data.scrollPosition = EditorGUILayout.BeginScrollView(data.scrollPosition);

        switch (selectedTab)
        {
            case 0:
                WiseTwin.Editor.WiseTwinEditorGeneralTab.Draw(data, this);
                break;
            case 1:
                WiseTwin.Editor.WiseTwinEditorMetadataTab.Draw(data);
                break;
            case 2:
                WiseTwin.Editor.WiseTwinEditorScenariosTab.Draw(data);
                break;
            case 3:
                WiseTwin.Editor.WiseTwinEditorDialogueTab.Draw(data);
                break;
            case 4:
                WiseTwin.Editor.WiseTwinEditorVideoTab.Draw(data);
                break;
        }

        EditorGUILayout.EndScrollView();

        DrawBottomButtons();
    }
    
    void DrawHeader()
    {
        EditorGUILayout.Space();
        EditorGUI.DrawRect(EditorGUILayout.GetControlRect(false, 2), new Color(0.3f, 0.6f, 1f));
        EditorGUILayout.Space();

        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("🎯", GUILayout.Width(30));
        EditorGUILayout.LabelField("WiseTwin Editor", EditorStyles.largeLabel);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.LabelField("Centralized management for WiseTwin Unity projects", EditorStyles.helpBox);
        EditorGUILayout.Space();
    }
    
    void DrawLoadedFileInfo()
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField($"🎯 Scene: {data.sceneId} (current active scene)", EditorStyles.boldLabel);

        if (data.hasLoadedExistingJSON)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"📄 Loaded file: {data.currentLoadedFile}", EditorStyles.helpBox);
            if (GUILayout.Button("🔄 Reload", GUILayout.Width(100)))
            {
                LoadExistingJSONContent();
            }
            EditorGUILayout.EndHorizontal();
        }
        else
        {
            EditorGUILayout.LabelField("ℹ️ No JSON file found. Creating new content.", EditorStyles.helpBox);
        }
        EditorGUILayout.EndVertical();
        EditorGUILayout.Space();
    }
    
    void DrawTabs()
    {
        selectedTab = GUILayout.Toolbar(selectedTab, tabNames, GUILayout.Height(25));
        EditorGUILayout.Space();
    }

    // ============================================================
    // Tab drawing methods have been extracted to separate files:
    // - WiseTwinEditorGeneralTab.cs
    // - WiseTwinEditorMetadataTab.cs
    // - WiseTwinEditorScenariosTab.cs
    // ============================================================

    void DrawBottomButtons()
    {
        EditorGUILayout.Space();
        EditorGUI.DrawRect(EditorGUILayout.GetControlRect(false, 1), Color.gray);
        EditorGUILayout.Space();
        
        EditorGUILayout.BeginHorizontal();
        
        if (GUILayout.Button("🔍 Preview JSON", GUILayout.Height(30)))
        {
            ShowJSONPreview();
        }
        
        if (GUILayout.Button("💾 Generate Metadata", GUILayout.Height(30)))
        {
            GenerateMetadata();
        }
        
        EditorGUILayout.EndHorizontal();
    }
    
    FormationMetadataComplete GenerateCompleteMetadata()
    {
        var metadata = new FormationMetadataComplete
        {
            id = data.sceneId,
            title = data.projectTitle,
            description = data.projectDescription,
            version = data.projectVersion,
            language = data.languageOptions[data.languageIndex], // ISO 639-1 ("fr", "en", ...)
            duration = $"{data.durationMinutes} minutes", // Auto formatting
            difficulty = data.difficultyOptions[data.difficultyIndex], // Get from dropdown (déjà en français)
            tags = new List<string>(data.tags),
            imageUrl = data.imageUrl,
            modules = new List<object>(),
            createdAt = data.includeTimestamp ? System.DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") : "",
            updatedAt = data.includeTimestamp ? System.DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") : ""
        };

        // 🎯 SIMPLIFIED STRUCTURE: Unity contains objects directly (legacy)
        try
        {
            if (!string.IsNullOrEmpty(data.unityContentJSON) && data.isUnityContentValid)
            {
                // Parse JSON directly to unity section
                metadata.unity = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, object>>>(data.unityContentJSON);
            }
            else
            {
                // If no Unity content, create empty section
                metadata.unity = new Dictionary<string, Dictionary<string, object>>();
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error parsing Unity content: {e.Message}");
            metadata.unity = new Dictionary<string, Dictionary<string, object>>();
        }

        // 🎯 NEW: Convert scenarios to JSON format
        if (data.scenarios != null && data.scenarios.Count > 0)
        {
            metadata.scenarios = ConvertScenariosToJSON();

            // Add default settings
            metadata.settings = new Dictionary<string, object>
            {
                ["allowPause"] = true,
                ["showTimer"] = true,
                ["showProgress"] = true
            };
        }

        // 🎬 Convert video triggers to JSON format
        if (data.videoTriggers != null && data.videoTriggers.Count > 0)
        {
            metadata.videoTriggers = ConvertVideoTriggersToJSON();
        }

        return metadata;
    }

    List<object> ConvertVideoTriggersToJSON()
    {
        var videoTriggersJSON = new List<object>();

        foreach (var trigger in data.videoTriggers)
        {
            // Skip if no object name
            if (string.IsNullOrEmpty(trigger.targetObjectName))
                continue;

            // Skip if no URL
            if (string.IsNullOrEmpty(trigger.videoUrl))
                continue;

            videoTriggersJSON.Add(new Dictionary<string, object>
            {
                ["targetObjectName"] = trigger.targetObjectName,
                ["videoUrl"] = trigger.videoUrl
            });
        }

        return videoTriggersJSON;
    }

    List<object> ConvertScenariosToJSON()
    {
        var scenariosJSON = new List<object>();

        foreach (var scenario in data.scenarios)
        {
            var scenarioDict = new Dictionary<string, object>
            {
                ["id"] = scenario.id,
                ["type"] = scenario.type.ToString().ToLower()
            };

            // Add content based on type
            switch (scenario.type)
            {
                case WiseTwin.Editor.ScenarioType.Question:
                    // Export as "question" if single, "questions" if multiple
                    if (scenario.questions.Count == 1)
                    {
                        scenarioDict["question"] = ConvertQuestionDataToJSON(scenario.questions[0]);
                    }
                    else if (scenario.questions.Count > 1)
                    {
                        var questionsArray = new List<object>();
                        foreach (var q in scenario.questions)
                        {
                            questionsArray.Add(ConvertQuestionDataToJSON(q));
                        }
                        scenarioDict["questions"] = questionsArray;
                    }
                    break;

                case WiseTwin.Editor.ScenarioType.Procedure:
                    scenarioDict["procedure"] = ConvertProcedureDataToJSON(scenario.procedureData);
                    break;

                case WiseTwin.Editor.ScenarioType.Text:
                    scenarioDict["text"] = ConvertTextDataToJSON(scenario.textData);
                    break;

                case WiseTwin.Editor.ScenarioType.Dialogue:
                    scenarioDict["dialogue"] = ConvertDialogueDataToJSON(scenario.dialogueData);
                    break;
            }

            scenariosJSON.Add(scenarioDict);
        }

        return scenariosJSON;
    }

    // Managed Resources folder where scenario illustration images are copied so they ship in the build.
    const string ScenarioImagesResourcesDir = "Assets/WiseTwin/Resources/ScenarioImages";

    /// <summary>
    /// Copy a scenario illustration image into a Resources folder (so it is embedded in the build)
    /// and return its Resources-relative path without extension (e.g. "ScenarioImages/foo").
    /// If the image is already under any Resources/ folder, its path is reused without copying.
    /// Returns "" when image is null or has no asset path.
    /// </summary>
    string CopyImageToResources(Texture2D image)
    {
        if (image == null) return "";
        string srcPath = AssetDatabase.GetAssetPath(image);
        if (string.IsNullOrEmpty(srcPath)) return "";

        string normalized = srcPath.Replace('\\', '/');
        string ext = Path.GetExtension(normalized);
        string nameNoExt = Path.GetFileNameWithoutExtension(normalized);

        // Already under a Resources folder → reuse it directly, no copy.
        int resIdx = normalized.IndexOf("/Resources/", StringComparison.OrdinalIgnoreCase);
        if (resIdx >= 0)
        {
            string rel = normalized.Substring(resIdx + "/Resources/".Length);
            if (rel.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                rel = rel.Substring(0, rel.Length - ext.Length);
            return rel;
        }

        EnsureFolderExists(ScenarioImagesResourcesDir);
        string destPath = $"{ScenarioImagesResourcesDir}/{nameNoExt}{ext}";

        if (AssetDatabase.LoadAssetAtPath<Texture2D>(destPath) != null)
            AssetDatabase.DeleteAsset(destPath);

        if (!AssetDatabase.CopyAsset(srcPath, destPath))
        {
            Debug.LogWarning($"[WiseTwinEditor] Failed to copy image into Resources: {srcPath} -> {destPath}");
            return "";
        }
        AssetDatabase.ImportAsset(destPath);
        return $"ScenarioImages/{nameNoExt}";
    }

    void EnsureFolderExists(string assetFolder)
    {
        if (AssetDatabase.IsValidFolder(assetFolder)) return;
        string[] parts = assetFolder.Split('/');
        string current = parts[0]; // "Assets"
        for (int i = 1; i < parts.Length; i++)
        {
            string next = $"{current}/{parts[i]}";
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }

    /// <summary>Re-hydrate the editor Texture2D reference from a stored Resources-relative path.</summary>
    Texture2D LoadImageAssetFromResourcesPath(string resourcesPath)
    {
        if (string.IsNullOrEmpty(resourcesPath)) return null;
        string p = resourcesPath;
        int dot = p.LastIndexOf('.');
        if (dot >= 0) p = p.Substring(0, dot);
        return Resources.Load<Texture2D>(p);
    }

    Dictionary<string, object> ConvertQuestionDataToJSON(WiseTwin.Editor.QuestionScenarioData question)
    {
        var questionDict = new Dictionary<string, object>
        {
            ["questionText"] = question.questionText ?? "",
            ["options"] = new List<string>(question.options),
            ["correctAnswers"] = new List<int>(question.correctAnswers),
            ["isMultipleChoice"] = question.isMultipleChoice
        };

        // Illustration image: embed in the build (Resources) and store the relative path
        string qImagePath = question.image != null ? CopyImageToResources(question.image) : question.imagePath;
        if (!string.IsNullOrEmpty(qImagePath))
        {
            question.imagePath = qImagePath;
            questionDict["imagePath"] = qImagePath;
        }

        // Add feedback if provided
        if (!string.IsNullOrEmpty(question.feedback))
        {
            questionDict["feedback"] = question.feedback;
        }

        if (!string.IsNullOrEmpty(question.incorrectFeedback))
        {
            questionDict["incorrectFeedback"] = question.incorrectFeedback;
        }

        // Add hint if provided
        if (!string.IsNullOrEmpty(question.hint))
        {
            questionDict["hint"] = question.hint;
        }

        return questionDict;
    }

    Dictionary<string, object> ConvertProcedureDataToJSON(WiseTwin.Editor.ProcedureScenarioData procedure)
    {
        var procedureDict = new Dictionary<string, object>
        {
            ["title"] = procedure.title ?? "",
            ["description"] = procedure.description ?? "",
            ["steps"] = ConvertProcedureStepsToJSON(procedure.steps)
        };

        // Add fake objects if any
        if (procedure.fakeObjects != null && procedure.fakeObjects.Count > 0)
        {
            procedureDict["fakeObjects"] = ConvertFakeObjectsToJSON(procedure.fakeObjects);
        }

        return procedureDict;
    }

    List<object> ConvertProcedureStepsToJSON(List<WiseTwin.Editor.ProcedureStep> steps)
    {
        var stepsJSON = new List<object>();

        foreach (var step in steps)
        {
            var stepDict = new Dictionary<string, object>
            {
                ["text"] = step.text ?? "",
                ["targetObjectName"] = step.targetObjectName,
                ["highlightColor"] = ColorToHex(step.highlightColor),
                ["useBlinking"] = step.useBlinking,
                ["validationType"] = step.validationType.ToString().ToLower()
            };

            // Add zone object name if validation type is Zone
            if (step.validationType == WiseTwin.Editor.ValidationType.Zone && !string.IsNullOrEmpty(step.zoneObjectName))
            {
                stepDict["zoneObjectName"] = step.zoneObjectName;
            }

            // Add the list of target objects when validation type is Group
            if (step.validationType == WiseTwin.Editor.ValidationType.Group && step.targetObjectNames != null)
            {
                var groupNames = new List<string>();
                foreach (var name in step.targetObjectNames)
                {
                    if (!string.IsNullOrEmpty(name)) groupNames.Add(name);
                }
                stepDict["targetObjectNames"] = groupNames;
            }

            // Illustration image: embed in the build (Resources) and store the relative path
            string stepImagePath = step.image != null ? CopyImageToResources(step.image) : step.imagePath;
            if (!string.IsNullOrEmpty(stepImagePath))
            {
                step.imagePath = stepImagePath;
                stepDict["imagePath"] = stepImagePath;
            }

            // Note: Hints removed for procedures - not exported to JSON anymore

            // NEW: Add fake objects for this step if any
            if (step.fakeObjects != null && step.fakeObjects.Count > 0)
            {
                stepDict["fakeObjects"] = ConvertFakeObjectsToJSON(step.fakeObjects);
            }

            stepsJSON.Add(stepDict);
        }

        return stepsJSON;
    }

    List<object> ConvertFakeObjectsToJSON(List<WiseTwin.Editor.FakeObject> fakeObjects)
    {
        var fakeObjectsJSON = new List<object>();

        foreach (var fake in fakeObjects)
        {
            if (string.IsNullOrEmpty(fake.fakeObjectName))
                continue;

            fakeObjectsJSON.Add(new Dictionary<string, object>
            {
                ["objectName"] = fake.fakeObjectName,
                ["errorMessage"] = fake.errorMessage ?? ""
            });
        }

        return fakeObjectsJSON;
    }

    Dictionary<string, object> ConvertTextDataToJSON(WiseTwin.Editor.TextScenarioData text)
    {
        var dict = new Dictionary<string, object>
        {
            ["title"] = text.title ?? "",
            ["content"] = text.content ?? ""
        };

        // Illustration image: embed in the build (Resources) and store the relative path
        string imgPath = text.image != null ? CopyImageToResources(text.image) : text.imagePath;
        if (!string.IsNullOrEmpty(imgPath))
        {
            text.imagePath = imgPath;
            dict["imagePath"] = imgPath;
        }

        return dict;
    }

    Dictionary<string, object> ConvertDialogueDataToJSON(WiseTwin.Editor.DialogueScenarioData dialogue)
    {
        // When a scenario is linked to a library dialogue by id, the library entry is the source
        // of truth. The scenario's inline graphDataJSON copy may be stale (e.g. user edited via
        // the Dialogue tab after the link was made), so always prefer the live library entry.
        var source = dialogue;
        if (!string.IsNullOrEmpty(dialogue.dialogueId))
        {
            var lib = data.dialogues.FirstOrDefault(d => d.dialogueId == dialogue.dialogueId);
            if (lib != null && !string.IsNullOrEmpty(lib.graphDataJSON))
            {
                source = lib;
            }
        }

        Dictionary<string, object> result = null;

        if (!string.IsNullOrEmpty(source.graphDataJSON))
        {
            try
            {
                var editorData = WiseTwin.Editor.DialogueEditor.DialogueGraphSerializer.DeserializeEditorData(source.graphDataJSON);
                if (editorData != null && editorData.nodes.Count > 0)
                {
                    result = WiseTwin.Editor.DialogueEditor.DialogueGraphSerializer.ConvertToRuntimeFormat(
                        editorData, source.title);
                }
                else
                {
                    var rawData = JsonConvert.DeserializeObject<Dictionary<string, object>>(source.graphDataJSON);
                    if (rawData != null && rawData.ContainsKey("startNodeId"))
                        result = rawData;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[WiseTwinEditor] Failed to parse dialogue graph JSON: {e.Message}");
            }
        }

        // Fallback: minimal valid dialogue structure
        if (result == null)
        {
            result = new Dictionary<string, object>
            {
                ["title"] = source.title ?? "",
                ["startNodeId"] = "node_001",
                ["nodes"] = new List<object>
                {
                    new Dictionary<string, object>
                    {
                        ["id"] = "node_001",
                        ["type"] = "start",
                        ["nextNodeId"] = "node_002"
                    },
                    new Dictionary<string, object>
                    {
                        ["id"] = "node_002",
                        ["type"] = "end"
                    }
                }
            };
        }

        // Embed the dialogueId so reloading this metadata in any project (the same one or a
        // different one) can re-link to the existing library entry instead of creating a duplicate.
        // The runtime DialogueDisplayer ignores this field, so it stays forward-compatible.
        if (!string.IsNullOrEmpty(source.dialogueId))
        {
            result["dialogueId"] = source.dialogueId;
        }

        return result;
    }

    string ColorToHex(Color color)
    {
        return $"#{ColorUtility.ToHtmlStringRGB(color)}";
    }
    
    void ShowJSONPreview()
    {
        var metadata = GenerateCompleteMetadata();
        string json = JsonConvert.SerializeObject(metadata, Formatting.Indented);
        
        MetadataPreviewWindow.ShowWindow(json);
    }
    
    void GenerateMetadata()
    {
        // Create StreamingAssets folder if it doesn't exist
        string streamingAssetsPath = Application.streamingAssetsPath;
        if (!Directory.Exists(streamingAssetsPath))
        {
            Directory.CreateDirectory(streamingAssetsPath);
        }

        var metadata = GenerateCompleteMetadata();
        string json = JsonConvert.SerializeObject(metadata, Formatting.Indented);

        string fileName = $"{data.sceneId}-metadata.json";
        string fullPath = Path.Combine(streamingAssetsPath, fileName);

        File.WriteAllText(fullPath, json);
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("Generation Successful",
            $"Metadata generated successfully!\n\n" +
            $"📁 File: {fileName}\n" +
            $"📍 Location: StreamingAssets/\n" +
            $"🎯 File will be automatically included in Unity build.",
            "Perfect!");

        // Mark as loaded for next opening
        data.currentLoadedFile = fileName;
        data.hasLoadedExistingJSON = true;
    }
    
    
    async void DownloadMetadataFromAPI()
    {
        if (string.IsNullOrEmpty(data.azureApiUrl) || string.IsNullOrEmpty(data.containerId))
        {
            EditorUtility.DisplayDialog("Error",
                "Please configure API URL and Container ID first!",
                "OK");
            return;
        }

        // Construire l'URL avec les paramètres
        string url = $"{data.azureApiUrl}?buildName={UnityEngine.Networking.UnityWebRequest.EscapeURL(data.sceneId)}" +
                     $"&buildType={UnityEngine.Networking.UnityWebRequest.EscapeURL(data.buildType)}" +
                     $"&containerId={UnityEngine.Networking.UnityWebRequest.EscapeURL(data.containerId)}";

        EditorUtility.DisplayProgressBar("Downloading Metadata", "Connecting to API...", 0.1f);

        try
        {
            using (var client = new System.Net.Http.HttpClient())
            {
                client.Timeout = System.TimeSpan.FromSeconds(30);

                Debug.Log($"📥 Downloading from: {url}");

                var response = await client.GetAsync(url);

                EditorUtility.DisplayProgressBar("Downloading Metadata", "Receiving data...", 0.5f);

                if (response.IsSuccessStatusCode)
                {
                    string jsonContent = await response.Content.ReadAsStringAsync();

                    // Parser la réponse API
                    var apiResponse = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonContent);

                    string metadataJson;

                    // Si l'API retourne {success: true, data: {...}}
                    if (apiResponse.ContainsKey("success") && apiResponse.ContainsKey("data"))
                    {
                        metadataJson = JsonConvert.SerializeObject(apiResponse["data"], Formatting.Indented);
                    }
                    else
                    {
                        // Sinon on prend la réponse directement
                        metadataJson = jsonContent;
                    }

                    // Sauvegarder dans StreamingAssets
                    string streamingAssetsPath = Path.Combine(Application.dataPath, "StreamingAssets");
                    if (!Directory.Exists(streamingAssetsPath))
                    {
                        Directory.CreateDirectory(streamingAssetsPath);
                    }

                    string fileName = $"{data.sceneId}-metadata.json";
                    string filePath = Path.Combine(streamingAssetsPath, fileName);

                    File.WriteAllText(filePath, metadataJson);

                    EditorUtility.DisplayProgressBar("Downloading Metadata", "Saved to StreamingAssets!", 1f);

                    AssetDatabase.Refresh();

                    Debug.Log($"✅ Metadata downloaded and saved to: {filePath}");

                    EditorUtility.ClearProgressBar();

                    // Afficher le succès et proposer de passer en mode Local
                    if (EditorUtility.DisplayDialog("Success",
                        $"Metadata downloaded successfully!\n\nSaved to: StreamingAssets/{fileName}\n\n" +
                        "Do you want to switch to Local Mode now?",
                        "Yes, switch to Local", "No"))
                    {
                        data.useLocalMode = true;
                        ApplySettingsToScene();
                    }
                }
                else
                {
                    EditorUtility.ClearProgressBar();
                    string error = $"API Error: {response.StatusCode} - {response.ReasonPhrase}";
                    Debug.LogError($"❌ {error}");

                    string responseContent = await response.Content.ReadAsStringAsync();
                    Debug.LogError($"Response: {responseContent}");

                    EditorUtility.DisplayDialog("Download Failed", error, "OK");
                }
            }
        }
        catch (System.Exception e)
        {
            EditorUtility.ClearProgressBar();
            Debug.LogError($"❌ Download failed: {e.Message}");
            EditorUtility.DisplayDialog("Download Failed",
                $"Failed to download metadata:\n{e.Message}",
                "OK");
        }
    }

    void SyncWithSceneManager()
    {
        // Synchroniser l'état de l'éditeur avec le WiseTwinManager de la scène
        WiseTwin.WiseTwinManager manager = FindFirstObjectByType<WiseTwin.WiseTwinManager>();
        if (manager != null)
        {
            SerializedObject managerSO = new SerializedObject(manager);
            SerializedProperty prodModeProp = managerSO.FindProperty("useProductionMode");
            if (prodModeProp != null)
            {
                // Lire l'état actuel du manager et synchroniser l'éditeur
                data.useLocalMode = !prodModeProp.boolValue;
                Debug.Log($"[WiseTwinEditor] Synchronisé avec WiseTwinManager: Mode {(data.useLocalMode ? "Local" : "Production")}");
            }

            // Synchroniser les modes de contrôle joueur (sérialisés en flags "disable" inversés)
            SerializedProperty kbProp = managerSO.FindProperty("disableKeyboardControl");
            SerializedProperty mouseProp = managerSO.FindProperty("disableMouseControl");
            if (kbProp != null) data.allowKeyboardControl = !kbProp.boolValue;
            if (mouseProp != null) data.allowMouseControl = !mouseProp.boolValue;
        }
    }

    void ApplyLocalModeToManager()
    {
        // Chercher le WiseTwinManager dans la scène
        WiseTwin.WiseTwinManager manager = FindFirstObjectByType<WiseTwin.WiseTwinManager>();
        if (manager != null)
        {
            // Appliquer le mode Production/Local
            SerializedObject managerSO = new SerializedObject(manager);
            SerializedProperty prodModeProp = managerSO.FindProperty("useProductionMode");
            if (prodModeProp != null)
            {
                prodModeProp.boolValue = !data.useLocalMode;  // Inverser car useLocalMode est l'opposé de useProductionMode
                managerSO.ApplyModifiedProperties();
                EditorUtility.SetDirty(manager);

                // Marquer la scène comme modifiée pour forcer la sauvegarde
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);

                Debug.Log($"✅ WiseTwinManager: Mode {(data.useLocalMode ? "Local" : "Production")} appliqué automatiquement et scène marquée pour sauvegarde");
            }
        }
        else
        {
            Debug.LogWarning("❌ WiseTwinManager not found in scene! Please add WiseTwinManager to apply mode settings.");
        }
    }

    void ApplySettingsToScene()
    {
        // Appliquer le mode local/production
        ApplyLocalModeToManager();

        // Chercher le MetadataLoader dans la scène
        MetadataLoader loader = FindFirstObjectByType<MetadataLoader>();
        if (loader != null)
        {
            // Appliquer les paramètres API
            loader.useAzureStorageDirect = data.useAzureStorageDirect;
            loader.azureStorageUrl = data.azureStorageUrl;
            loader.apiBaseUrl = data.azureApiUrl;
            loader.containerId = data.containerId;
            loader.buildType = data.buildType;

            Debug.Log($"✅ MetadataLoader configured:");
            Debug.Log($"   - Mode: {(data.useLocalMode ? "Local" : "Production")}");
            Debug.Log($"   - Azure Direct: {data.useAzureStorageDirect}");
            if (data.useAzureStorageDirect)
            {
                Debug.Log($"   - Storage URL: {data.azureStorageUrl}");
            }
            else
            {
                Debug.Log($"   - API URL: {data.azureApiUrl}");
            }
            Debug.Log($"   - Container ID: {data.containerId}");
            Debug.Log($"   - Build Type: {data.buildType}");

            EditorUtility.SetDirty(loader);
        }
        else
        {
            Debug.LogWarning("❌ MetadataLoader not found in scene!");
        }

        // Sauvegarder les changements
        SaveSettings();

        EditorUtility.DisplayDialog("Success",
            $"Settings applied to scene!\n\nMode: {(data.useLocalMode ? "Local" : "Production")}\n" +
            $"API: {data.azureApiUrl}\n" +
            $"Container: {data.containerId}",
            "OK");
    }

    void OnDisable()
    {
        SaveSettings();
        SaveDialogueData();
        if (OnRequestDialogueSave == SaveDialogueData)
            OnRequestDialogueSave = null;
    }
}

// Preview window
public class MetadataPreviewWindow : EditorWindow
{
    private Vector2 scrollPosition;
    private string jsonContent;
    
    public static void ShowWindow(string json)
    {
        MetadataPreviewWindow window = GetWindow<MetadataPreviewWindow>("JSON Preview");
        window.jsonContent = json;
        window.minSize = new Vector2(500, 400);
        window.Show();
    }
    
    void OnGUI()
    {
        EditorGUILayout.LabelField("📋 Metadata JSON Preview", EditorStyles.largeLabel);
        EditorGUILayout.Space();
        
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("📋 Copy JSON"))
        {
            EditorGUIUtility.systemCopyBuffer = jsonContent;
            ShowNotification(new GUIContent("JSON copied to clipboard!"));
        }
        if (GUILayout.Button("💾 Save as..."))
        {
            string path = EditorUtility.SaveFilePanel("Save JSON", "", "metadata", "json");
            if (!string.IsNullOrEmpty(path))
            {
                File.WriteAllText(path, jsonContent);
                ShowNotification(new GUIContent($"Saved: {path}"));
            }
        }
        EditorGUILayout.EndHorizontal();
        
        EditorGUILayout.Space();
        
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        EditorGUILayout.TextArea(jsonContent, GUILayout.ExpandHeight(true));
        EditorGUILayout.EndScrollView();
    }
}

#endif