using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace WiseTwin.Editor
{
    /// <summary>
    /// Bloc <c>localization</c> du metadata.json (1.12.0).
    ///
    /// Un texte de scénario qui commence par "@" est une clé, traduite dans Unity par les fichiers
    /// <c>Assets/StreamingAssets/Localization/{langue}.json</c> (embarqués dans le build). Le SaaS
    /// ne voit pas ces fichiers : sans ce bloc, ses statistiques afficheraient la clé au lieu de
    /// la question. On y recopie, pour chaque langue, la traduction des seules clés utilisées
    /// par le metadata.
    /// </summary>
    public static class MetadataLocalizationBuilder
    {
        public const string LocalizationFolder = "Assets/StreamingAssets/Localization";

        /// <returns>null si le metadata n'utilise aucune clé (formation mono-langue classique).</returns>
        public static Dictionary<string, object> Build(object metadata, string defaultLanguage)
        {
            var keys = new SortedSet<string>();
            CollectKeys(JToken.FromObject(metadata), keys);
            if (keys.Count == 0) return null;

            var tables = LoadTables();
            if (tables.Count == 0)
            {
                Debug.LogWarning($"[WiseTwin] Le metadata utilise {keys.Count} clé(s) \"@…\" mais aucun fichier de langue n'a été trouvé dans {LocalizationFolder}.");
                return null;
            }

            var strings = new Dictionary<string, object>();
            foreach (var table in tables)
            {
                var used = new SortedDictionary<string, string>();
                var missing = new List<string>();
                foreach (var key in keys)
                {
                    if (table.Value.TryGetValue(key, out var value)) used[key] = value;
                    else missing.Add(key);
                }
                strings[table.Key] = used;

                if (missing.Count > 0)
                {
                    Debug.LogWarning($"[WiseTwin] {missing.Count} clé(s) sans traduction en '{table.Key}' : {string.Join(", ", missing.Take(10))}{(missing.Count > 10 ? "…" : "")}");
                }
            }

            return new Dictionary<string, object>
            {
                ["defaultLanguage"] = string.IsNullOrEmpty(defaultLanguage) ? "fr" : defaultLanguage,
                ["languages"] = tables.Keys.ToList(),
                ["strings"] = strings,
            };
        }

        static void CollectKeys(JToken token, ISet<string> keys)
        {
            switch (token)
            {
                case JValue value when value.Type == JTokenType.String:
                    var text = (string)value;
                    if (!string.IsNullOrEmpty(text) && text.Length > 1 && text[0] == '@')
                        keys.Add(text.Substring(1));
                    break;
                case JContainer container:
                    foreach (var child in container.Children()) CollectKeys(child, keys);
                    break;
            }
        }

        /// <summary>
        /// Langue → (clé à plat "a.b.c" → texte). <c>fr.json</c> donne "fr" ; les fichiers
        /// additionnels <c>fr_xxx.json</c> (LocalizationManager.additionalFiles) complètent "fr".
        /// </summary>
        static SortedDictionary<string, Dictionary<string, string>> LoadTables()
        {
            var tables = new SortedDictionary<string, Dictionary<string, string>>();
            if (!Directory.Exists(LocalizationFolder)) return tables;

            // Fichiers principaux d'abord, puis les additionnels qui les complètent
            var files = Directory.GetFiles(LocalizationFolder, "*.json")
                .OrderBy(f => Path.GetFileNameWithoutExtension(f).Contains("_") ? 1 : 0)
                .ThenBy(f => f);

            foreach (var file in files)
            {
                string name = Path.GetFileNameWithoutExtension(file);
                string language = name.Split('_')[0];
                try
                {
                    var root = JToken.Parse(File.ReadAllText(file));
                    if (!tables.TryGetValue(language, out var table))
                    {
                        table = new Dictionary<string, string>();
                        tables[language] = table;
                    }
                    Flatten(root, "", table);
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[WiseTwin] Fichier de langue illisible {file} : {e.Message}");
                }
            }
            return tables;
        }

        static void Flatten(JToken token, string prefix, IDictionary<string, string> into)
        {
            if (token is JObject obj)
            {
                foreach (var prop in obj.Properties())
                    Flatten(prop.Value, string.IsNullOrEmpty(prefix) ? prop.Name : $"{prefix}.{prop.Name}", into);
            }
            else if (token is JValue value && value.Type != JTokenType.Null && !string.IsNullOrEmpty(prefix))
            {
                into[prefix] = value.ToString();
            }
        }
    }
}
