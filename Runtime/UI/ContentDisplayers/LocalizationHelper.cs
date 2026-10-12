using System;

namespace WiseTwin.UI
{
    /// <summary>
    /// Static helper for resolving localization keys in content displayers.
    /// The LocalizeText delegate should be set by the localization system (e.g., LocalizationManager).
    /// </summary>
    public static class LocalizationHelper
    {
        /// <summary>
        /// Delegate to resolve localization keys. Set by the localization bridge.
        /// Takes a key (without @ prefix) and returns the translated text.
        /// </summary>
        public static Func<string, string> LocalizeText;

        /// <summary>
        /// Resolve text that may be a localization key (starting with @).
        /// Returns the original text if not a key or if LocalizeText is not set.
        /// </summary>
        public static string Resolve(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            if (!text.StartsWith("@")) return text;

            if (LocalizeText == null) return text;

            string key = text.Substring(1);
            string resolved = LocalizeText(key);

            // Return original if not found (resolved equals key)
            if (string.IsNullOrEmpty(resolved) || resolved == key)
                return text;

            return resolved;
        }
    }
}
