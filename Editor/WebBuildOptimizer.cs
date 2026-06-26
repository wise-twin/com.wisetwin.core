using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace WiseTwin.Editor
{
    /// <summary>
    /// Outil d'optimisation des assets pour les builds WebGL WiseTrainer.
    ///
    /// Le poids du fichier .data téléchargé par l'apprenant est le principal
    /// point de blocage sur connexion faible. Sur une formation type, ce poids
    /// est dominé par deux postes : les textures (~65 %) et les maillages
    /// (~30 %). Cet outil agit sur ces deux postes :
    ///
    /// - Textures : pose un override plateforme WebGL (taille max + crunch
    ///   compression). La texture source et les autres plateformes ne sont pas
    ///   touchées, et la taille n'est jamais augmentée — uniquement réduite.
    /// - Maillages : applique la Mesh Compression (désactivée par défaut dans
    ///   Unity), sans perte visible sur des props statiques.
    ///
    /// Non destructif par défaut : le bouton « Analyser » fait un dry-run qui
    /// liste ce qui serait modifié, sans rien changer. Les modifications ne
    /// portent que sur le dossier Assets/ (jamais les assets des packages) et
    /// ignorent toujours lightmaps, sprites/UI, cookies et curseurs.
    /// </summary>
    public class WebBuildOptimizer : EditorWindow
    {
        private enum Scope { Selection, WholeProject }

        private const string WebGLPlatform = "WebGL";
        private static readonly int[] MaxSizes = { 256, 512, 1024, 2048 };
        private static readonly string[] MaxSizeLabels = { "256", "512", "1024", "2048" };

        // Portée
        private Scope _scope = Scope.Selection;

        // Textures
        private bool _optimizeTextures = true;
        private int _maxSizeIndex = 2; // 1024 par défaut — net pour de la signalétique, 4x plus léger que 2048
        private bool _crunch = true;
        private int _crunchQuality = 50;
        private bool _includeNormalMaps = true;

        // Maillages
        private bool _optimizeMeshes = true;
        private ModelImporterMeshCompression _meshCompression = ModelImporterMeshCompression.High;

        [MenuItem("WiseTwin/Optimisation/Optimiseur Web Build")]
        public static void Open()
        {
            var window = GetWindow<WebBuildOptimizer>("WiseTwin — Optimiseur Web");
            window.minSize = new Vector2(400, 440);
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "Réduit le poids du build WebGL (textures + maillages) pour accélérer le " +
                "chargement des formations sur connexion faible.\n\n" +
                "N'agit que sur le dossier Assets/. Lance d'abord « Analyser » pour voir " +
                "ce qui changerait, puis « Appliquer ».",
                MessageType.Info);

            EditorGUILayout.Space();
            _scope = (Scope)EditorGUILayout.EnumPopup(
                new GUIContent("Portée",
                    "Sélection = assets/dossiers sélectionnés dans la fenêtre Project. " +
                    "Tout le projet = tout le dossier Assets/."),
                _scope);

            EditorGUILayout.Space();
            _optimizeTextures = EditorGUILayout.BeginToggleGroup("Optimiser les textures", _optimizeTextures);
            EditorGUI.indentLevel++;
            _maxSizeIndex = EditorGUILayout.Popup(
                new GUIContent("Taille max (WebGL)", "Override plateforme WebGL uniquement. La taille n'est jamais augmentée."),
                _maxSizeIndex, MaxSizeLabels);
            _crunch = EditorGUILayout.Toggle(new GUIContent("Crunch compression"), _crunch);
            using (new EditorGUI.DisabledScope(!_crunch))
                _crunchQuality = EditorGUILayout.IntSlider(new GUIContent("Qualité crunch"), _crunchQuality, 0, 100);
            _includeNormalMaps = EditorGUILayout.Toggle(new GUIContent("Inclure les normal maps"), _includeNormalMaps);
            EditorGUILayout.LabelField("Toujours ignorés : lightmaps, sprites/UI, cookies, curseurs.", EditorStyles.miniLabel);
            EditorGUI.indentLevel--;
            EditorGUILayout.EndToggleGroup();

            EditorGUILayout.Space();
            _optimizeMeshes = EditorGUILayout.BeginToggleGroup("Optimiser les maillages", _optimizeMeshes);
            EditorGUI.indentLevel++;
            _meshCompression = (ModelImporterMeshCompression)EditorGUILayout.EnumPopup(
                new GUIContent("Mesh Compression", "Compression de maillage à l'import (Off par défaut dans Unity)."),
                _meshCompression);
            EditorGUI.indentLevel--;
            EditorGUILayout.EndToggleGroup();

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(!_optimizeTextures && !_optimizeMeshes))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Analyser (dry-run)", GUILayout.Height(30)))
                    Run(dryRun: true);
                if (GUILayout.Button("Appliquer", GUILayout.Height(30)))
                    Run(dryRun: false);
            }
        }

        private void Run(bool dryRun)
        {
            int target = MaxSizes[_maxSizeIndex];

            var textureImporters = _optimizeTextures ? CollectImporters<TextureImporter>("t:Texture2D") : new List<TextureImporter>();
            var modelImporters = _optimizeMeshes ? CollectImporters<ModelImporter>("t:Model") : new List<ModelImporter>();

            var texturesToApply = new List<TextureImporter>();
            int texturesSkipped = 0;

            foreach (var ti in textureImporters)
            {
                if (!ShouldProcessTexture(ti)) { texturesSkipped++; continue; }

                var settings = ti.GetPlatformTextureSettings(WebGLPlatform);
                int currentMax = settings.overridden ? settings.maxTextureSize : ti.maxTextureSize;
                int newMax = Mathf.Min(currentMax, target); // ne jamais agrandir

                bool needsChange = !settings.overridden
                    || settings.maxTextureSize != newMax
                    || settings.crunchedCompression != _crunch
                    || (_crunch && settings.compressionQuality != _crunchQuality);

                if (!needsChange) { texturesSkipped++; continue; }

                if (dryRun)
                {
                    Debug.Log($"[WebOptim] (dry) Texture {ti.assetPath} : max {currentMax}→{newMax}, crunch={_crunch}");
                }
                else
                {
                    settings.overridden = true;
                    settings.maxTextureSize = newMax;
                    settings.format = TextureImporterFormat.Automatic;
                    settings.textureCompression = TextureImporterCompression.Compressed;
                    settings.crunchedCompression = _crunch;
                    settings.compressionQuality = _crunchQuality;
                    ti.SetPlatformTextureSettings(settings);
                    texturesToApply.Add(ti);
                }
            }

            var meshesToApply = new List<ModelImporter>();
            foreach (var mi in modelImporters)
            {
                if (mi.meshCompression == _meshCompression) continue;

                if (dryRun)
                    Debug.Log($"[WebOptim] (dry) Mesh {mi.assetPath} : compression {mi.meshCompression}→{_meshCompression}");
                else
                {
                    mi.meshCompression = _meshCompression;
                    meshesToApply.Add(mi);
                }
            }

            if (dryRun)
            {
                int texToChange = textureImporters.Count - texturesSkipped;
                int meshToChange = modelImporters.Count(mi => mi.meshCompression != _meshCompression);
                Debug.Log($"[WebOptim] DRY-RUN — textures à optimiser : {texToChange} (ignorées : {texturesSkipped}) ; maillages à compresser : {meshToChange}.");
                EditorUtility.DisplayDialog("WiseTwin — Optimiseur Web",
                    $"Analyse terminée (aucune modification).\n\n" +
                    $"Textures à optimiser : {texToChange}\n" +
                    $"Maillages à compresser : {meshToChange}\n\n" +
                    $"Détail par asset dans la Console.",
                    "OK");
                return;
            }

            if (texturesToApply.Count == 0 && meshesToApply.Count == 0)
            {
                EditorUtility.DisplayDialog("WiseTwin — Optimiseur Web",
                    "Rien à modifier — tout est déjà optimisé selon ces réglages.", "OK");
                return;
            }

            bool confirmed = EditorUtility.DisplayDialog("WiseTwin — Optimiseur Web",
                $"Appliquer sur :\n" +
                $"• {texturesToApply.Count} textures (max {target}, crunch {_crunch})\n" +
                $"• {meshesToApply.Count} maillages (compression {_meshCompression})\n\n" +
                $"Les assets seront réimportés. Vérifie le rendu après coup, et commit avant si besoin.",
                "Appliquer", "Annuler");
            if (!confirmed) return;

            try
            {
                int total = texturesToApply.Count + meshesToApply.Count;
                int i = 0;
                foreach (var ti in texturesToApply)
                {
                    if (EditorUtility.DisplayCancelableProgressBar("Optimisation WebGL", ti.assetPath, (float)i / total)) break;
                    ti.SaveAndReimport();
                    i++;
                }
                foreach (var mi in meshesToApply)
                {
                    if (EditorUtility.DisplayCancelableProgressBar("Optimisation WebGL", mi.assetPath, (float)i / total)) break;
                    mi.SaveAndReimport();
                    i++;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.Refresh();
            }

            Debug.Log($"[WebOptim] ✅ Appliqué — {texturesToApply.Count} textures, {meshesToApply.Count} maillages. Refais un build WebGL pour mesurer le gain.");
            EditorUtility.DisplayDialog("WiseTwin — Optimiseur Web",
                $"Terminé ✅\n\nTextures : {texturesToApply.Count}\nMaillages : {meshesToApply.Count}\n\n" +
                $"Refais un build WebGL pour mesurer le gain sur le .data.",
                "OK");
        }

        /// <summary>
        /// Récupère les importers du type demandé, selon la portée choisie,
        /// en se limitant au dossier Assets/.
        /// </summary>
        private List<T> CollectImporters<T>(string filter) where T : AssetImporter
        {
            var paths = new HashSet<string>();

            if (_scope == Scope.Selection)
            {
                var selected = Selection.assetGUIDs
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .Where(p => !string.IsNullOrEmpty(p))
                    .ToArray();

                if (selected.Length == 0)
                {
                    Debug.LogWarning("[WebOptim] Aucune sélection dans la fenêtre Project. " +
                        "Sélectionne des dossiers/assets, ou bascule la portée sur « Tout le projet ».");
                    return new List<T>();
                }

                var folders = selected.Where(AssetDatabase.IsValidFolder).ToArray();
                foreach (var asset in selected.Where(p => !AssetDatabase.IsValidFolder(p)))
                    paths.Add(asset);
                if (folders.Length > 0)
                    foreach (var guid in AssetDatabase.FindAssets(filter, folders))
                        paths.Add(AssetDatabase.GUIDToAssetPath(guid));
            }
            else
            {
                foreach (var guid in AssetDatabase.FindAssets(filter))
                    paths.Add(AssetDatabase.GUIDToAssetPath(guid));
            }

            return paths
                .Where(p => p.StartsWith("Assets/"))
                .Select(p => AssetImporter.GetAtPath(p) as T)
                .Where(imp => imp != null)
                .ToList();
        }

        private bool ShouldProcessTexture(TextureImporter importer)
        {
            switch (importer.textureType)
            {
                case TextureImporterType.Lightmap:
                case TextureImporterType.Cookie:
                case TextureImporterType.Cursor:
                case TextureImporterType.Sprite:
                    return false;
                case TextureImporterType.NormalMap:
                    return _includeNormalMaps;
                default:
                    return true;
            }
        }
    }
}
