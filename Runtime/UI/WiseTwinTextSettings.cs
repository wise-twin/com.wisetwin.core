using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace WiseTwin.UI
{
    /// <summary>
    /// Polices de secours de l'UI Toolkit (HUD, scénarios, sélecteur de langue).
    ///
    /// En WebGL il n'y a pas de police système : un caractère absent de la police par défaut
    /// (japonais, chinois, coréen, arabe…) s'affiche en carré — dans l'éditeur, l'OS masque le
    /// problème. Le package fournit un PanelTextSettings partagé
    /// (<c>Runtime/Resources/WiseTwinPanelTextSettings</c>) référencé par ses PanelSettings ;
    /// le PROJET y ajoute les polices des langues qu'il livre en déposant des Font Assets
    /// UI Toolkit dans un dossier <c>Resources/WiseTwinFonts</c>
    /// (menu WiseTwin → Langues → Créer une police de secours UI Toolkit).
    /// Une police par système d'écriture, pas par langue : le latin est déjà couvert.
    /// </summary>
    public static class WiseTwinTextSettings
    {
        public const string TextSettingsResource = "WiseTwinPanelTextSettings";
        public const string FontsResourceFolder = "WiseTwinFonts";

        static PanelTextSettings shared;
        // Polices ajoutées à l'asset partagé : retirées en sortie de Play mode pour ne pas
        // modifier l'asset du package.
        static List<FontAsset> addedFallbacks;

        /// <summary>Le PanelTextSettings partagé, polices du projet comprises (null si absent).</summary>
        public static PanelTextSettings Shared
        {
            get
            {
                EnsureLoaded();
                return shared;
            }
        }

        // Avant le chargement de la première scène : aucun texte n'a encore été généré.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Initialize()
        {
            EnsureLoaded();
        }

        static void EnsureLoaded()
        {
            if (shared != null) return;

            shared = Resources.Load<PanelTextSettings>(TextSettingsResource);
            if (shared == null)
            {
                Debug.LogWarning($"[WiseTwin] PanelTextSettings '{TextSettingsResource}' introuvable : pas de polices de secours pour l'UI.");
                return;
            }

            if (shared.fallbackFontAssets == null)
                shared.fallbackFontAssets = new List<FontAsset>();

            addedFallbacks = new List<FontAsset>();
            foreach (var font in Resources.LoadAll<FontAsset>(FontsResourceFolder))
            {
                if (font == null || shared.fallbackFontAssets.Contains(font)) continue;
                shared.fallbackFontAssets.Add(font);
                addedFallbacks.Add(font);
            }

            if (addedFallbacks.Count > 0)
                Debug.Log($"[WiseTwin] Polices de secours UI : {string.Join(", ", addedFallbacks.ConvertAll(f => f.name))}");

#if UNITY_EDITOR
            UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
#endif
        }

        /// <summary>
        /// Donne le PanelTextSettings partagé à un PanelSettings qui n'en a pas — pour un
        /// PanelSettings créé par code. Les assets du package le référencent déjà.
        /// </summary>
        public static void Apply(PanelSettings panelSettings)
        {
            if (panelSettings == null || panelSettings.textSettings != null) return;
#if UNITY_EDITOR
            // Ne jamais réécrire un asset du projet depuis le Play mode
            if (UnityEditor.EditorUtility.IsPersistent(panelSettings)) return;
#endif
            var textSettings = Shared;
            if (textSettings != null) panelSettings.textSettings = textSettings;
        }

#if UNITY_EDITOR
        static void OnPlayModeStateChanged(UnityEditor.PlayModeStateChange state)
        {
            if (state != UnityEditor.PlayModeStateChange.ExitingPlayMode) return;
            UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;

            if (shared != null && addedFallbacks != null)
            {
                foreach (var font in addedFallbacks)
                    shared.fallbackFontAssets.Remove(font);
            }
            shared = null;
            addedFallbacks = null;
        }
#endif
    }
}
