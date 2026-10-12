using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace WiseTwin.Editor
{
    /// <summary>
    /// Polices de secours de l'UI Toolkit (voir <see cref="WiseTwin.UI.WiseTwinTextSettings"/>).
    /// Sélectionner un .ttf/.otf (ex. NotoSansJP-Regular.ttf) puis
    /// WiseTwin → Langues → Créer une police de secours UI Toolkit : le Font Asset est créé
    /// dans Assets/Resources/WiseTwinFonts et ajouté automatiquement au démarrage.
    /// Une police par système d'écriture (japonais, chinois, coréen, arabe…), pas par langue.
    /// </summary>
    public static class WiseTwinFontTools
    {
        const string FontsFolder = "Assets/Resources/" + WiseTwin.UI.WiseTwinTextSettings.FontsResourceFolder;
        const string MenuPath = "WiseTwin/Langues/Créer une police de secours UI Toolkit";

        [MenuItem(MenuPath, true)]
        static bool ValidateCreateFromSelection()
        {
            return Selection.activeObject is Font;
        }

        [MenuItem(MenuPath, false, 200)]
        static void CreateFromSelection()
        {
            foreach (var obj in Selection.objects)
            {
                if (obj is Font font)
                {
                    var created = CreateFallbackFontAsset(font);
                    if (created != null) EditorGUIUtility.PingObject(created);
                }
            }
        }

        /// <summary>
        /// Crée (ou renvoie s'il existe) un Font Asset UI Toolkit DYNAMIQUE pour <paramref name="font"/> :
        /// les glyphes sont générés à l'exécution depuis la police, embarquée dans le build.
        /// </summary>
        public static FontAsset CreateFallbackFontAsset(Font font)
        {
            if (font == null) return null;

            string fontPath = AssetDatabase.GetAssetPath(font);
            string assetPath = $"{FontsFolder}/{font.name} UITK.asset";

            var existing = AssetDatabase.LoadAssetAtPath<FontAsset>(assetPath);
            if (existing != null)
            {
                Debug.Log($"[WiseTwin] Police de secours déjà présente : {assetPath}");
                return existing;
            }

            // Une police dynamique a besoin des données de la police dans le build
            if (AssetImporter.GetAtPath(fontPath) is TrueTypeFontImporter importer && !importer.includeFontData)
            {
                importer.includeFontData = true;
                importer.SaveAndReimport();
            }

            var fontAsset = FontAsset.CreateFontAsset(font, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024,
                AtlasPopulationMode.Dynamic, true);
            if (fontAsset == null)
            {
                Debug.LogError($"[WiseTwin] Impossible de créer un Font Asset depuis {fontPath}");
                return null;
            }

            Directory.CreateDirectory(FontsFolder);
            fontAsset.name = Path.GetFileNameWithoutExtension(assetPath);
            AssetDatabase.CreateAsset(fontAsset, assetPath);

            // Atlas et matériau doivent vivre dans l'asset, sinon ils sont perdus à la sauvegarde
            foreach (var texture in fontAsset.atlasTextures)
            {
                if (texture == null) continue;
                texture.name = fontAsset.name + " Atlas";
                AssetDatabase.AddObjectToAsset(texture, fontAsset);
            }
            if (fontAsset.material != null)
            {
                fontAsset.material.name = fontAsset.name + " Material";
                AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(assetPath);
            Debug.Log($"[WiseTwin] Police de secours UI Toolkit créée : {assetPath}");
            return AssetDatabase.LoadAssetAtPath<FontAsset>(assetPath);
        }

        /// <summary>
        /// Crée le PanelTextSettings partagé du package s'il manque et le fait référencer par
        /// <paramref name="panelSettings"/>. Outil de maintenance du package.
        /// </summary>
        public static PanelTextSettings EnsureSharedTextSettings(string textSettingsPath, PanelSettings panelSettings)
        {
            var textSettings = AssetDatabase.LoadAssetAtPath<PanelTextSettings>(textSettingsPath);
            if (textSettings == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(textSettingsPath));
                textSettings = ScriptableObject.CreateInstance<PanelTextSettings>();
                AssetDatabase.CreateAsset(textSettings, textSettingsPath);
            }

            if (panelSettings != null && panelSettings.textSettings != textSettings)
            {
                panelSettings.textSettings = textSettings;
                EditorUtility.SetDirty(panelSettings);
            }

            AssetDatabase.SaveAssets();
            return textSettings;
        }
    }
}
