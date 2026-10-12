using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;
using System;

namespace WiseTwin.UI
{
    /// <summary>
    /// Displayer for language selection screen.
    /// Allows user to select their preferred language before continuing.
    /// Fires OnLanguageSelected event when a language is chosen.
    /// </summary>
    public class LanguageSelectorDisplayer : MonoBehaviour, IContentDisplayer
    {
        public event Action<string> OnClosed;
        public event Action<string, bool> OnCompleted;

        /// <summary>
        /// Fired when user selects a language. Subscribe to this to handle the language change.
        /// </summary>
        public static event Action<string> OnLanguageSelected;

        private string currentObjectId;
        private VisualElement rootElement;
        private VisualElement modalContainer;
        private VisualElement buttonsContainer;
        private Button validateButton;
        private Label titleLabel;
        private Label instructionLabel;
        private bool autoAdvance = true;
        private bool showFullNames = true;

        // Keys for localization
        private string titleKey;
        private string instructionKey;

        // Default available languages
        private static string[] defaultLanguages = { "fr", "en", "ja" };
        private static string currentLanguage = "fr";
        private string selectedLanguage;

        /// <summary>
        /// Set the available languages and current language from external source (e.g., LocalizationManager)
        /// </summary>
        public static void Configure(string[] availableLanguages, string current)
        {
            if (availableLanguages != null && availableLanguages.Length > 0)
            {
                defaultLanguages = availableLanguages;
            }
            if (!string.IsNullOrEmpty(current))
            {
                currentLanguage = current;
            }
        }

        public void Display(string objectId, Dictionary<string, object> contentData, VisualElement root)
        {
            currentObjectId = objectId;
            rootElement = root;
            selectedLanguage = currentLanguage;

            // Store the keys for live preview
            titleKey = ExtractString(contentData, "title", "@training.bootstrap.scenario3.title");
            instructionKey = ExtractString(contentData, "instruction", "@training.bootstrap.scenario3.instruction");
            showFullNames = ExtractBool(contentData, "showFullLanguageName", true);
            autoAdvance = ExtractBool(contentData, "autoAdvance", true);

            // Resolve localization keys
            string title = LocalizationHelper.Resolve(titleKey);
            string instruction = LocalizationHelper.Resolve(instructionKey);

            CreateLanguageSelectorUI(title, instruction);
        }

        private void CreateLanguageSelectorUI(string title, string instruction)
        {
            rootElement.Clear();

            // Backdrop
            modalContainer = new VisualElement();
            UIStyles.ApplyBackdropHeavyStyle(modalContainer);

            // Content card
            var contentBox = new VisualElement();
            contentBox.style.position = Position.Relative;
            contentBox.style.width = 500;
            contentBox.style.maxWidth = Length.Percent(90);
            contentBox.style.minHeight = 300;
            UIStyles.ApplyCardStyle(contentBox, UIStyles.RadiusXL);
            contentBox.style.flexDirection = FlexDirection.Column;
            contentBox.style.alignItems = Align.Center;

            // Header
            var headerSection = new VisualElement();
            headerSection.style.width = Length.Percent(100);
            headerSection.style.paddingTop = UIStyles.Space3XL;
            headerSection.style.paddingBottom = UIStyles.SpaceXL;
            headerSection.style.paddingLeft = UIStyles.Space4XL;
            headerSection.style.paddingRight = UIStyles.Space4XL;
            headerSection.style.alignItems = Align.Center;

            // Title
            titleLabel = UIStyles.CreateTitle(title ?? "", UIStyles.Font2XL + 4);
            titleLabel.style.color = UIStyles.TextPrimary;
            titleLabel.style.marginBottom = UIStyles.SpaceSM;
            titleLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            headerSection.Add(titleLabel);

            // Instruction
            instructionLabel = UIStyles.CreateSubtitle(instruction ?? "", UIStyles.FontBase);
            instructionLabel.style.color = UIStyles.TextMuted;
            instructionLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            headerSection.Add(instructionLabel);

            headerSection.Add(UIStyles.CreateSeparator(UIStyles.SpaceLG));
            contentBox.Add(headerSection);

            // Language buttons container
            buttonsContainer = new VisualElement();
            buttonsContainer.style.flexGrow = 1;
            buttonsContainer.style.width = Length.Percent(100);
            buttonsContainer.style.paddingLeft = UIStyles.Space4XL;
            buttonsContainer.style.paddingRight = UIStyles.Space4XL;
            buttonsContainer.style.paddingTop = UIStyles.SpaceXL;
            buttonsContainer.style.paddingBottom = UIStyles.SpaceMD;
            buttonsContainer.style.alignItems = Align.Center;

            // Create buttons for each available language
            RefreshLanguageButtons();

            contentBox.Add(buttonsContainer);

            // Validate button section
            var validateSection = new VisualElement();
            validateSection.style.width = Length.Percent(100);
            validateSection.style.paddingLeft = UIStyles.Space4XL;
            validateSection.style.paddingRight = UIStyles.Space4XL;
            validateSection.style.paddingBottom = UIStyles.Space3XL;
            validateSection.style.alignItems = Align.Center;

            validateButton = new Button(HandleValidate);
            validateButton.text = LocalizationHelper.Resolve("@common.confirm") ?? "Valider";
            if (validateButton.text.StartsWith("@")) validateButton.text = "Valider";
            validateButton.style.width = 280;
            validateButton.style.height = 50;
            validateButton.style.fontSize = UIStyles.FontLG;
            validateButton.style.unityFontStyleAndWeight = FontStyle.Bold;
            validateButton.style.backgroundColor = UIStyles.Success;
            validateButton.style.color = UIStyles.TextOnAccent;
            UIStyles.SetBorderRadius(validateButton, UIStyles.RadiusLG);
            validateButton.style.borderLeftWidth = 0;
            validateButton.style.borderRightWidth = 0;
            validateButton.style.borderTopWidth = 0;
            validateButton.style.borderBottomWidth = 0;

            // Hover effect for validate button
            validateButton.RegisterCallback<MouseEnterEvent>(_ =>
            {
                validateButton.style.backgroundColor = new Color(0.15f, 0.55f, 0.35f); // Darker green
            });
            validateButton.RegisterCallback<MouseLeaveEvent>(_ =>
            {
                validateButton.style.backgroundColor = UIStyles.Success;
            });

            validateSection.Add(validateButton);
            contentBox.Add(validateSection);

            modalContainer.Add(contentBox);
            rootElement.Add(modalContainer);
        }

        private void RefreshLanguageButtons()
        {
            buttonsContainer.Clear();

            foreach (string langCode in defaultLanguages)
            {
                var langButton = CreateLanguageButton(langCode, showFullNames, langCode == selectedLanguage);
                buttonsContainer.Add(langButton);
            }
        }

        private Button CreateLanguageButton(string langCode, bool showFullName, bool isSelected)
        {
            string displayName = showFullName ? GetLanguageName(langCode) : langCode.ToUpper();

            var button = new Button(() => HandleLanguageClicked(langCode));
            button.text = displayName;
            button.style.width = 280;
            button.style.height = 56;
            button.style.marginBottom = UIStyles.SpaceMD;
            button.style.fontSize = UIStyles.FontLG;
            button.style.unityFontStyleAndWeight = isSelected ? FontStyle.Bold : FontStyle.Normal;

            // Style based on selection
            if (isSelected)
            {
                button.style.backgroundColor = UIStyles.Accent;
                button.style.color = UIStyles.TextOnAccent;
                button.style.borderLeftWidth = 4;
                button.style.borderRightWidth = 4;
                button.style.borderTopWidth = 4;
                button.style.borderBottomWidth = 4;
                button.style.borderLeftColor = UIStyles.AccentHover;
                button.style.borderRightColor = UIStyles.AccentHover;
                button.style.borderTopColor = UIStyles.AccentHover;
                button.style.borderBottomColor = UIStyles.AccentHover;
            }
            else
            {
                button.style.backgroundColor = UIStyles.BgElevated;
                button.style.color = UIStyles.TextPrimary;
                button.style.borderLeftWidth = 2;
                button.style.borderRightWidth = 2;
                button.style.borderTopWidth = 2;
                button.style.borderBottomWidth = 2;
                button.style.borderLeftColor = UIStyles.BorderSubtle;
                button.style.borderRightColor = UIStyles.BorderSubtle;
                button.style.borderTopColor = UIStyles.BorderSubtle;
                button.style.borderBottomColor = UIStyles.BorderSubtle;
            }

            UIStyles.SetBorderRadius(button, UIStyles.RadiusLG);

            // Hover effects
            button.RegisterCallback<MouseEnterEvent>(_ =>
            {
                if (!isSelected)
                {
                    button.style.backgroundColor = UIStyles.BgInputHover;
                    button.style.borderLeftColor = UIStyles.Accent;
                    button.style.borderRightColor = UIStyles.Accent;
                    button.style.borderTopColor = UIStyles.Accent;
                    button.style.borderBottomColor = UIStyles.Accent;
                }
            });

            button.RegisterCallback<MouseLeaveEvent>(_ =>
            {
                if (!isSelected)
                {
                    button.style.backgroundColor = UIStyles.BgElevated;
                    button.style.borderLeftColor = UIStyles.BorderSubtle;
                    button.style.borderRightColor = UIStyles.BorderSubtle;
                    button.style.borderTopColor = UIStyles.BorderSubtle;
                    button.style.borderBottomColor = UIStyles.BorderSubtle;
                }
            });

            return button;
        }

        private void HandleLanguageClicked(string langCode)
        {
            selectedLanguage = langCode;

            // Preview the language immediately by loading it
            OnLanguageSelected?.Invoke(selectedLanguage);

            // Update UI texts with the new language
            RefreshUITexts();
            RefreshLanguageButtons();
        }

        private void RefreshUITexts()
        {
            // Update title
            if (titleLabel != null)
            {
                string resolvedTitle = LocalizationHelper.Resolve(titleKey);
                titleLabel.text = resolvedTitle;
            }

            // Update instruction
            if (instructionLabel != null)
            {
                string resolvedInstruction = LocalizationHelper.Resolve(instructionKey);
                instructionLabel.text = resolvedInstruction;
            }

            // Update validate button
            if (validateButton != null)
            {
                string confirmText = LocalizationHelper.Resolve("@common.confirm");
                validateButton.text = string.IsNullOrEmpty(confirmText) || confirmText.StartsWith("@")
                    ? "Confirmer"
                    : confirmText;
            }
        }

        private void HandleValidate()
        {
            // Update current language (already applied via preview)
            currentLanguage = selectedLanguage;

            // Language was already applied in HandleLanguageClicked for preview
            // Just complete and close
            OnCompleted?.Invoke(currentObjectId, true);
            Close();
        }

        private string GetLanguageName(string code)
        {
            return code switch
            {
                "fr" => "Français",
                "en" => "English",
                "ja" => "日本語",
                "de" => "Deutsch",
                "es" => "Español",
                "it" => "Italiano",
                "pt" => "Português",
                "zh" => "中文",
                "ko" => "한국어",
                "ar" => "العربية",
                _ => code.ToUpper()
            };
        }

        public void Close()
        {
            rootElement?.Clear();
            OnClosed?.Invoke(currentObjectId);
        }

        private string ExtractString(Dictionary<string, object> data, string key, string defaultValue = "")
        {
            if (data == null || !data.ContainsKey(key)) return defaultValue;
            var value = data[key];
            return value?.ToString() ?? defaultValue;
        }

        private bool ExtractBool(Dictionary<string, object> data, string key, bool defaultValue = false)
        {
            if (data == null || !data.ContainsKey(key)) return defaultValue;

            var value = data[key];
            if (value is bool boolValue) return boolValue;
            if (value is string stringValue) return bool.TryParse(stringValue, out bool result) && result;
            if (value is int intValue) return intValue != 0;

            return defaultValue;
        }
    }
}
