using UnityEngine;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine.SceneManagement;

namespace WiseTwin.Editor
{
    /// <summary>
    /// Processeur de build pour s'assurer que les paramètres WiseTwin sont correctement configurés avant le build
    /// </summary>
    public class WiseTwinBuildProcessor : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            Debug.Log("[WiseTwin] Vérification des paramètres avant build...");

            // Indépendant de la scène ouverte : le fichier de la formation est celui de la scène de départ
            ValidateMultiSceneMetadata();

            // Trouver le WiseTwinManager dans la scène
            WiseTwinManager manager = Object.FindFirstObjectByType<WiseTwinManager>();

            if (manager == null)
            {
                Debug.LogWarning("[WiseTwin] ⚠️ WiseTwinManager non trouvé dans la scène! Le build pourrait ne pas fonctionner correctement.");
                return;
            }

            // Afficher la configuration actuelle
            bool isProductionMode = manager.IsProductionMode();
            Debug.Log($"[WiseTwin] Build en mode: {(isProductionMode ? "🌐 PRODUCTION" : "💻 LOCAL")}");

            // Avertissement si on build en mode local
            if (!isProductionMode && report.summary.platform == BuildTarget.WebGL)
            {
                bool continueWithLocal = EditorUtility.DisplayDialog(
                    "WiseTwin - Mode Local Détecté",
                    "Vous êtes sur le point de faire un build WebGL en mode LOCAL.\n\n" +
                    "En mode local, les métadonnées seront chargées depuis StreamingAssets.\n" +
                    "Pour un déploiement production, utilisez le mode Production.\n\n" +
                    "Voulez-vous continuer avec le mode Local?",
                    "Continuer (Local)",
                    "Annuler"
                );

                if (!continueWithLocal)
                {
                    throw new BuildFailedException("Build annulé par l'utilisateur (Mode Local détecté)");
                }
            }

            // S'assurer que les métadonnées sont présentes en mode local
            if (!isProductionMode)
            {
                // Obtenir le nom de la première scène dans le build
                string sceneName = "default-scene";
                if (report.summary.guid.ToString() != System.Guid.Empty.ToString())
                {
                    var scenePaths = EditorBuildSettings.scenes;
                    if (scenePaths.Length > 0 && scenePaths[0].enabled)
                    {
                        // Nom exact : c'est celui qu'utilisent l'éditeur WiseTwin et MetadataLoader
                        sceneName = System.IO.Path.GetFileNameWithoutExtension(scenePaths[0].path);
                    }
                }

                string metadataPath = $"Assets/StreamingAssets/{sceneName}-metadata.json";

                if (!System.IO.File.Exists(metadataPath))
                {
                    Debug.LogError($"[WiseTwin] ❌ Fichier de métadonnées manquant: {metadataPath}");
                    Debug.LogError("[WiseTwin] En mode local, le fichier metadata.json doit être présent dans StreamingAssets!");

                    bool createMetadata = EditorUtility.DisplayDialog(
                        "WiseTwin - Métadonnées Manquantes",
                        $"Le fichier de métadonnées '{sceneName}-metadata.json' est manquant dans StreamingAssets.\n\n" +
                        "Voulez-vous ouvrir l'éditeur WiseTwin pour générer les métadonnées?",
                        "Ouvrir l'éditeur",
                        "Annuler le build"
                    );

                    if (createMetadata)
                    {
                        EditorApplication.ExecuteMenuItem("WiseTwin/WiseTwin Editor");
                    }

                    throw new BuildFailedException("Build annulé: Métadonnées manquantes pour le mode local");
                }
                else
                {
                    Debug.Log($"[WiseTwin] ✅ Métadonnées trouvées: {metadataPath}");
                }
            }
            else
            {
                // En mode production, vérifier la configuration API
                MetadataLoader loader = Object.FindFirstObjectByType<MetadataLoader>();
                if (loader != null)
                {
                    if (string.IsNullOrEmpty(loader.apiBaseUrl) && !loader.useAzureStorageDirect)
                    {
                        Debug.LogWarning("[WiseTwin] ⚠️ URL de l'API non configurée pour le mode Production!");
                    }
                    if (string.IsNullOrEmpty(loader.containerId))
                    {
                        Debug.LogWarning("[WiseTwin] ⚠️ Container ID non configuré pour le mode Production!");
                    }
                }
            }

            Debug.Log("[WiseTwin] ✅ Vérification pré-build terminée");
        }

        /// <summary>
        /// Formation multi-scènes : signale (sans bloquer) les incohérences du fichier unique qui
        /// sera uploadé comme metadata.json — scènes hors Build Settings, scénarios jamais joués,
        /// ids en double (le SaaS mélangerait leurs statistiques).
        /// </summary>
        static void ValidateMultiSceneMetadata()
        {
            var buildScenes = new System.Collections.Generic.List<string>();
            foreach (var buildScene in EditorBuildSettings.scenes)
            {
                if (buildScene.enabled) buildScenes.Add(System.IO.Path.GetFileNameWithoutExtension(buildScene.path));
            }
            if (buildScenes.Count == 0) return;

            string metadataPath = $"Assets/StreamingAssets/{buildScenes[0]}-metadata.json";
            if (!System.IO.File.Exists(metadataPath)) return;

            Newtonsoft.Json.Linq.JObject root;
            try
            {
                root = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(metadataPath));
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[WiseTwin] ⚠️ {metadataPath} illisible : {e.Message}");
                return;
            }

            var scenes = (root["scenes"] as Newtonsoft.Json.Linq.JArray)?.ToObject<System.Collections.Generic.List<string>>();
            if (scenes == null || scenes.Count == 0) return; // Formation classique

            foreach (var scene in scenes)
            {
                if (!buildScenes.Contains(scene))
                    Debug.LogWarning($"[WiseTwin] ⚠️ Formation multi-scènes : la scène '{scene}' n'est pas activée dans les Build Settings");
            }

            var ids = new System.Collections.Generic.HashSet<string>();
            int count = 0;
            if (root["scenarios"] is Newtonsoft.Json.Linq.JArray scenarios)
            {
                foreach (var scenario in scenarios)
                {
                    count++;
                    string id = scenario["id"]?.ToString();
                    string scene = scenario["scene"]?.ToString();
                    if (string.IsNullOrEmpty(scene) || !scenes.Contains(scene))
                        Debug.LogWarning($"[WiseTwin] ⚠️ Formation multi-scènes : le scénario '{id}' (scène '{scene}') ne sera jamais joué");
                    if (!ids.Add(id))
                        Debug.LogWarning($"[WiseTwin] ⚠️ Formation multi-scènes : id de scénario en double '{id}' (statistiques du SaaS mélangées)");
                }
            }

            Debug.Log($"[WiseTwin] 🧩 Formation multi-scènes : {count} scénario(s) sur {scenes.Count} scène(s) dans {metadataPath} (à uploader comme metadata.json)");
        }
    }
}