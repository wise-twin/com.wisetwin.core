using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;
using System;
using System.Linq;
using WiseTwin.Analytics;

namespace WiseTwin.UI
{
    /// <summary>
    /// Afficheur spécialisé pour le contenu texte avec support de formatage markdown-like
    /// Design moderne avec header fixe et contenu scrollable minimaliste
    /// </summary>
    public class TextDisplayer : MonoBehaviour, IContentDisplayer
    {
        public event Action<string> OnClosed;
        public event Action<string, bool> OnCompleted;

        private string currentObjectId;
        private VisualElement rootElement;
        private VisualElement modalContainer;

        // Analytics tracking
        private TextInteractionData currentTextData;
        private float displayStartTime;
        private ScrollView contentScrollView;
        private float maxScrollPercentage = 0f;

        public void Display(string objectId, Dictionary<string, object> contentData, VisualElement root)
        {
            currentObjectId = objectId;
            rootElement = root;

            string lang = "";

            string title = ExtractLocalizedText(contentData, "title", lang);
            string subtitle = ExtractLocalizedText(contentData, "subtitle", lang);
            string content = ExtractLocalizedText(contentData, "content", lang);
            string imagePath = ExtractLocalizedText(contentData, "imagePath", lang);
            bool showContinueButton = ExtractBool(contentData, "showContinueButton", true);

            CreateModernTextUI(title, subtitle, content, showContinueButton, imagePath);

            // Analytics
            if (TrainingAnalytics.Instance != null)
            {
                displayStartTime = Time.time;

                string contentKey = contentData.Keys.FirstOrDefault(k => k.StartsWith("text_"));
                if (string.IsNullOrEmpty(contentKey))
                {
                    contentKey = "text_content";
                }

                currentTextData = new TextInteractionData
                {
                    contentKey = contentKey,
                    objectId = objectId,
                    timeDisplayed = 0f,
                    readComplete = false,
                    scrollPercentage = 0f
                };

                var textId = $"{objectId}_{contentKey}";
                var interaction = TrainingAnalytics.Instance.StartInteraction(objectId, "text", "informative");
                if (interaction != null)
                {
                    interaction.attempts = 1;
                    var dataDict = currentTextData.ToDictionary();
                    foreach (var kvp in dataDict)
                    {
                        interaction.AddData(kvp.Key, kvp.Value);
                    }
                }
            }
        }

        void CreateModernTextUI(string title, string subtitle, string content, bool showContinueButton, string imagePath = "")
        {
            rootElement.Clear();

            // Backdrop
            modalContainer = new VisualElement();
            UIStyles.ApplyBackdropHeavyStyle(modalContainer);

            // Click backdrop to close
            modalContainer.RegisterCallback<PointerDownEvent>((evt) =>
            {
                if (evt.target == modalContainer)
                {
                    Close();
                }
            });

            // Content card
            var contentBox = new VisualElement();
            contentBox.style.position = Position.Relative;
            contentBox.style.width = 820;
            contentBox.style.maxWidth = Length.Percent(90);
            // Auto-size to content (compact for short text) with the footer button pinned
            // to the bottom; caps at maxHeight and scrolls when the content is long.
            contentBox.style.minHeight = 260;
            contentBox.style.maxHeight = Length.Percent(85);
            UIStyles.ApplyCardStyle(contentBox, UIStyles.RadiusXL);
            contentBox.style.flexDirection = FlexDirection.Column;

            // ========== HEADER ==========
            var headerWrapper = new VisualElement();
            headerWrapper.style.position = Position.Relative;
            headerWrapper.style.flexShrink = 0;
            headerWrapper.style.backgroundColor = UIStyles.BgElevated;
            headerWrapper.style.borderTopLeftRadius = UIStyles.RadiusXL;
            headerWrapper.style.borderTopRightRadius = UIStyles.RadiusXL;

            var headerSection = new VisualElement();
            headerSection.style.paddingTop = UIStyles.Space3XL;
            headerSection.style.paddingBottom = UIStyles.SpaceXL;
            headerSection.style.paddingLeft = UIStyles.Space4XL;
            headerSection.style.paddingRight = UIStyles.Space4XL;

            if (!string.IsNullOrEmpty(title))
            {
                var titleLabel = UIStyles.CreateTitle(title, UIStyles.Font2XL + 4);
                titleLabel.style.color = UIStyles.TextPrimary;
                titleLabel.style.marginBottom = UIStyles.SpaceSM;
                headerSection.Add(titleLabel);
            }

            if (!string.IsNullOrEmpty(subtitle))
            {
                var subtitleLabel = UIStyles.CreateSubtitle(subtitle, UIStyles.FontBase);
                subtitleLabel.style.color = UIStyles.TextMuted;
                headerSection.Add(subtitleLabel);
            }

            headerSection.Add(UIStyles.CreateSeparator(UIStyles.SpaceLG));

            headerWrapper.Add(headerSection);
            contentBox.Add(headerWrapper);

            // ========== SCROLLABLE CONTENT ==========
            var contentWrapper = new VisualElement();
            contentWrapper.style.flexGrow = 1;
            contentWrapper.style.overflow = Overflow.Hidden;
            contentWrapper.style.position = Position.Relative;

            var scrollView = new ScrollView();
            scrollView.mode = ScrollViewMode.Vertical;
            scrollView.style.flexGrow = 1;
            contentScrollView = scrollView;

            // Track scroll for analytics
            scrollView.RegisterCallback<WheelEvent>((evt) => TrackScrollProgress());
            scrollView.RegisterCallback<GeometryChangedEvent>((evt) => TrackScrollProgress());
            scrollView.contentContainer.RegisterCallback<GeometryChangedEvent>((evt) => TrackScrollProgress());

            var contentContainer = new VisualElement();
            contentContainer.style.paddingTop = UIStyles.Space2XL;
            contentContainer.style.paddingBottom = UIStyles.Space2XL;
            contentContainer.style.paddingLeft = UIStyles.Space4XL;
            contentContainer.style.paddingRight = UIStyles.Space3XL;

            // Optional illustration image at the top of the bubble (clickable to zoom)
            var bubbleImage = WiseTwinImage.Load(imagePath);
            if (bubbleImage != null)
            {
                contentContainer.Add(WiseTwinImage.CreateThumbnail(bubbleImage, 240f));
            }

            if (!string.IsNullOrEmpty(content))
            {
                ParseAndCreateContent(content, contentContainer);
            }

            scrollView.Add(contentContainer);
            contentWrapper.Add(scrollView);

            // Minimal scrollbar
            contentBox.RegisterCallback<AttachToPanelEvent>((evt) => UIStyles.ApplyMinimalScrollbar(scrollView));
            scrollView.RegisterCallback<GeometryChangedEvent>((evt) => UIStyles.ApplyMinimalScrollbar(scrollView));

            contentBox.Add(contentWrapper);

            // ========== FOOTER (fixed at the bottom, separated from the scroll area) ==========
            if (showContinueButton)
            {
                var footer = new VisualElement();
                footer.style.flexShrink = 0;
                footer.style.flexDirection = FlexDirection.Row;
                footer.style.justifyContent = Justify.FlexEnd;
                footer.style.alignItems = Align.Center;
                footer.style.paddingTop = UIStyles.SpaceLG;
                footer.style.paddingBottom = UIStyles.SpaceLG;
                footer.style.paddingLeft = UIStyles.Space3XL;
                footer.style.paddingRight = UIStyles.Space3XL;
                footer.style.borderTopWidth = 1;
                footer.style.borderTopColor = UIStyles.BorderSubtle;
                footer.style.borderBottomLeftRadius = UIStyles.RadiusXL;
                footer.style.borderBottomRightRadius = UIStyles.RadiusXL;

                var continueButton = UIStyles.CreatePrimaryButton(
                    "",
                    () =>
                    {
                        if (currentTextData != null)
                        {
                            currentTextData.readComplete = true;
                            currentTextData.timeDisplayed = Time.time - displayStartTime;
                            currentTextData.scrollPercentage = maxScrollPercentage;
                        }
                        OnCompleted?.Invoke(currentObjectId, true);
                        Close();
                    }
                );
                UIStyles.SetButtonIcon(continueButton, WiseTwinIcons.ArrowRight(28, UIStyles.TextOnAccent));
                continueButton.style.width = 140;
                continueButton.style.flexShrink = 0;
                footer.Add(continueButton);

                contentBox.Add(footer);
            }

            modalContainer.Add(contentBox);
            rootElement.Add(modalContainer);
        }

        void ParseAndCreateContent(string content, VisualElement container)
        {
            string[] lines = content.Split('\n');

            foreach (string line in lines)
            {
                string trimmedLine = line.Trim();

                if (string.IsNullOrEmpty(trimmedLine))
                {
                    var spacer = new VisualElement();
                    spacer.style.height = UIStyles.SpaceLG;
                    container.Add(spacer);
                    continue;
                }

                if (trimmedLine.StartsWith("###"))
                {
                    CreateHeader(trimmedLine.Substring(3).Trim(), container, 3);
                }
                else if (trimmedLine.StartsWith("##"))
                {
                    CreateHeader(trimmedLine.Substring(2).Trim(), container, 2);
                }
                else if (trimmedLine.StartsWith("#"))
                {
                    CreateHeader(trimmedLine.Substring(1).Trim(), container, 1);
                }
                else if (trimmedLine.StartsWith("-") || trimmedLine.StartsWith("\u2022"))
                {
                    CreateBulletPoint(trimmedLine.Substring(1).Trim(), container);
                }
                else if (trimmedLine.StartsWith(">"))
                {
                    CreateQuote(trimmedLine.Substring(1).Trim(), container);
                }
                else if (trimmedLine.StartsWith("!"))
                {
                    CreateWarning(trimmedLine.Substring(1).Trim(), container);
                }
                else
                {
                    CreateParagraph(trimmedLine, container);
                }
            }
        }

        void CreateHeader(string text, VisualElement container, int level)
        {
            var header = new Label(text);
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.whiteSpace = WhiteSpace.Normal;

            switch (level)
            {
                case 1:
                    header.style.fontSize = UIStyles.FontXL;
                    header.style.color = UIStyles.Accent;
                    header.style.marginTop = UIStyles.SpaceXL;
                    header.style.marginBottom = UIStyles.SpaceLG;
                    break;
                case 2:
                    header.style.fontSize = UIStyles.FontLG;
                    header.style.color = UIStyles.TextPrimary;
                    header.style.marginTop = UIStyles.SpaceLG;
                    header.style.marginBottom = UIStyles.SpaceMD;
                    break;
                case 3:
                    header.style.fontSize = UIStyles.FontMD;
                    header.style.color = UIStyles.TextSecondary;
                    header.style.marginTop = UIStyles.SpaceMD;
                    header.style.marginBottom = UIStyles.SpaceSM;
                    break;
            }

            container.Add(header);
        }

        void CreateParagraph(string text, VisualElement container)
        {
            var paragraph = UIStyles.CreateBodyText(text, UIStyles.FontBase);
            paragraph.style.color = UIStyles.TextSecondary;
            paragraph.style.marginBottom = UIStyles.SpaceMD;
            container.Add(paragraph);
        }

        void CreateBulletPoint(string text, VisualElement container)
        {
            var bulletContainer = new VisualElement();
            bulletContainer.style.flexDirection = FlexDirection.Row;
            bulletContainer.style.alignItems = Align.Center;
            bulletContainer.style.marginBottom = UIStyles.SpaceSM;
            bulletContainer.style.marginLeft = UIStyles.SpaceLG;

            // Drawn bullet \u2014 Unicode \u2022 is missing from WebGL bundled fonts
            var bullet = WiseTwinIcons.Bullet(6, UIStyles.Accent);
            bullet.style.marginRight = UIStyles.SpaceSM;
            bullet.style.flexShrink = 0;

            var contentLabel = UIStyles.CreateBodyText(text, UIStyles.FontBase);
            contentLabel.style.color = UIStyles.TextSecondary;
            contentLabel.style.flexGrow = 1;

            bulletContainer.Add(bullet);
            bulletContainer.Add(contentLabel);
            container.Add(bulletContainer);
        }

        void CreateQuote(string text, VisualElement container)
        {
            var quote = new VisualElement();
            quote.style.backgroundColor = UIStyles.BgElevated;
            quote.style.borderLeftWidth = 3;
            quote.style.borderLeftColor = new Color(UIStyles.Accent.r, UIStyles.Accent.g, UIStyles.Accent.b, 0.7f);
            quote.style.paddingTop = UIStyles.SpaceMD;
            quote.style.paddingBottom = UIStyles.SpaceMD;
            quote.style.paddingLeft = UIStyles.SpaceLG;
            quote.style.paddingRight = UIStyles.SpaceLG;
            quote.style.marginTop = UIStyles.SpaceSM;
            quote.style.marginBottom = UIStyles.SpaceSM;
            UIStyles.SetBorderRadius(quote, UIStyles.SpaceXS);

            var quoteText = new Label(text);
            quoteText.style.fontSize = UIStyles.FontBase;
            quoteText.style.color = UIStyles.TextMuted;
            quoteText.style.unityFontStyleAndWeight = FontStyle.Italic;
            quoteText.style.whiteSpace = WhiteSpace.Normal;

            quote.Add(quoteText);
            container.Add(quote);
        }

        void CreateWarning(string text, VisualElement container)
        {
            var warning = new VisualElement();
            warning.style.backgroundColor = UIStyles.WarningBg;
            warning.style.borderLeftWidth = 3;
            warning.style.borderLeftColor = UIStyles.Warning;
            warning.style.paddingTop = UIStyles.SpaceMD;
            warning.style.paddingBottom = UIStyles.SpaceMD;
            warning.style.paddingLeft = UIStyles.SpaceLG;
            warning.style.paddingRight = UIStyles.SpaceLG;
            warning.style.marginTop = UIStyles.SpaceSM;
            warning.style.marginBottom = UIStyles.SpaceSM;
            UIStyles.SetBorderRadius(warning, UIStyles.SpaceXS);

            var warningText = new Label(text);
            warningText.style.fontSize = UIStyles.FontBase;
            warningText.style.color = UIStyles.Warning;
            warningText.style.whiteSpace = WhiteSpace.Normal;

            warning.Add(warningText);
            container.Add(warning);
        }

        public void Close()
        {
            if (TrainingAnalytics.Instance != null && currentTextData != null)
            {
                currentTextData.timeDisplayed = Time.time - displayStartTime;
                currentTextData.scrollPercentage = maxScrollPercentage;
                currentTextData.readComplete = currentTextData.timeDisplayed > 5f || maxScrollPercentage > 70f || currentTextData.timeDisplayed > 1f;

                if (TrainingAnalytics.Instance.GetCurrentInteraction() != null)
                {
                    var dataDict = currentTextData.ToDictionary();
                    foreach (var kvp in dataDict)
                    {
                        TrainingAnalytics.Instance.AddDataToCurrentInteraction(kvp.Key, kvp.Value);
                    }

                    TrainingAnalytics.Instance.AddDataToCurrentInteraction("finalScore", 100f);
                }

                TrainingAnalytics.Instance.EndCurrentInteraction(true);
            }

            rootElement?.Clear();
            OnClosed?.Invoke(currentObjectId);
        }

        void TrackScrollProgress()
        {
            if (contentScrollView == null || contentScrollView.verticalScroller == null) return;

            var scroller = contentScrollView.verticalScroller;

            if (scroller.highValue > scroller.lowValue)
            {
                float scrollPercentage = (scroller.value / (scroller.highValue - scroller.lowValue)) * 100f;

                if (scrollPercentage > maxScrollPercentage)
                {
                    maxScrollPercentage = scrollPercentage;

                    if (currentTextData != null)
                    {
                        currentTextData.scrollPercentage = maxScrollPercentage;
                    }
                }
            }
        }

        string ExtractLocalizedText(Dictionary<string, object> data, string key, string language)
        {
            return LocalizedValueReader.ReadString(data, key);
        }

        bool ExtractBool(Dictionary<string, object> data, string key, bool defaultValue = false)
        {
            if (!data.ContainsKey(key)) return defaultValue;

            var value = data[key];
            if (value is bool boolValue) return boolValue;
            if (value is string stringValue) return bool.TryParse(stringValue, out bool result) && result;
            if (value is int intValue) return intValue != 0;

            return defaultValue;
        }
    }
}
