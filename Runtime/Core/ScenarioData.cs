using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace WiseTwin
{
    /// <summary>
    /// Represents a single scenario in the training
    /// </summary>
    [Serializable]
    public class ScenarioData
    {
        [JsonProperty("id")]
        public string id;

        [JsonProperty("type")]
        public string type; // "question", "procedure", "text"

        [JsonProperty("scene")]
        public string scene; // Target scene name (null = all scenes)

        [JsonProperty("question")]
        public JObject question;

        [JsonProperty("questions")]
        public JArray questions; // Support for multiple questions

        [JsonProperty("procedure")]
        public JObject procedure;

        [JsonProperty("text")]
        public JObject text;

        [JsonProperty("dialogue")]
        public JObject dialogue;

        /// <summary>
        /// Get content data based on scenario type
        /// </summary>
        public JObject GetContentData()
        {
            switch (type?.ToLower())
            {
                case "question":
                    // Return single question or wrap questions array
                    if (question != null)
                        return question;
                    else if (questions != null && questions.Count > 0)
                    {
                        // Wrap questions array in a container object for the displayer
                        var wrapper = new JObject();
                        wrapper["questions"] = questions;
                        return wrapper;
                    }
                    return null;
                case "procedure":
                    return procedure;
                case "text":
                    return text;
                case "dialogue":
                    return dialogue;
                default:
                    return null;
            }
        }

        /// <summary>
        /// Get typed content data
        /// </summary>
        public T GetContentData<T>() where T : class
        {
            var contentData = GetContentData();
            if (contentData == null) return null;

            try
            {
                return contentData.ToObject<T>();
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError($"Error parsing scenario content: {e.Message}");
                return null;
            }
        }
    }

    /// <summary>
    /// Training settings from metadata
    /// </summary>
    [Serializable]
    public class TrainingSettings
    {
        [JsonProperty("allowPause")]
        public bool allowPause = true;

        [JsonProperty("showTimer")]
        public bool showTimer = true;

        [JsonProperty("showProgress")]
        public bool showProgress = true;
    }
}
