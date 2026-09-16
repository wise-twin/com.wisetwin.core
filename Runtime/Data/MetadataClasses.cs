using System.Collections.Generic;
using Newtonsoft.Json;

// Classes partagées entre Editor et Runtime.
// Mono-language: all text fields are flat strings.
[System.Serializable]
public class FormationMetadataComplete
{
    public string id;
    public string title;
    public string description;
    public string version;

    // Version du package WiseTwin qui a généré ce metadata (1.10.0+). Le SaaS
    // s'en sert pour savoir si le build accepte une URL de metadata fournie
    // par l'hôte (export SCORM complet). Conservé par les éditions côté SaaS.
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string packageVersion;

    // Language code of the training content (mono-language per build).
    // ISO 639-1 ("fr", "en", "es", ...). Optional — omitted from JSON when empty.
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string language;

    public string duration;
    public string difficulty;
    public List<string> tags;
    public string imageUrl;
    public List<object> modules;
    public string createdAt;
    public string updatedAt;
    public Dictionary<string, Dictionary<string, object>> unity;

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public List<object> scenarios;

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public Dictionary<string, object> settings;

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string disclaimer;

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public List<object> videoTriggers;
}

[System.Serializable]
public class ApiResponse
{
    public bool success;
    public FormationMetadataComplete data;
    public string error;
}
