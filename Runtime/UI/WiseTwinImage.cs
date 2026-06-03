using UnityEngine;
using UnityEngine.UIElements;

namespace WiseTwin.UI
{
    /// <summary>
    /// Shared illustration-image utilities for content displayers (Text, Question, Procedure).
    /// Images are embedded in the build under a Resources folder; the generated metadata stores
    /// a Resources-relative path (e.g. "ScenarioImages/foo"). This helper loads that texture and
    /// builds a clickable thumbnail that zooms to a full-screen overlay on click.
    /// </summary>
    public static class WiseTwinImage
    {
        /// <summary>
        /// Load an illustration texture from a Resources-relative path (e.g. "ScenarioImages/foo").
        /// Tolerates "Assets/Resources/" / "Resources/" prefixes and a trailing file extension.
        /// Falls back to StreamingAssets bytes (non-WebGL) for legacy absolute paths. Null if missing.
        /// </summary>
        public static Texture2D Load(string imagePath)
        {
            if (string.IsNullOrEmpty(imagePath)) return null;

            string resourcePath = imagePath.Replace('\\', '/');

            if (resourcePath.StartsWith("Assets/Resources/"))
                resourcePath = resourcePath.Substring("Assets/Resources/".Length);
            else if (resourcePath.StartsWith("Resources/"))
                resourcePath = resourcePath.Substring("Resources/".Length);

            int dot = resourcePath.LastIndexOf('.');
            if (dot >= 0) resourcePath = resourcePath.Substring(0, dot);

            var texture = Resources.Load<Texture2D>(resourcePath);
            if (texture != null) return texture;

            var sprite = Resources.Load<Sprite>(resourcePath);
            if (sprite != null) return sprite.texture;

            // Legacy fallback: a real file in StreamingAssets (does not work on WebGL).
            string streamingPath = System.IO.Path.Combine(Application.streamingAssetsPath, imagePath);
            if (System.IO.File.Exists(streamingPath))
            {
                byte[] bytes = System.IO.File.ReadAllBytes(streamingPath);
                var tex = new Texture2D(2, 2);
                if (tex.LoadImage(bytes)) return tex;
            }

            Debug.LogWarning($"[WiseTwinImage] Could not load image from path: {imagePath} (resolved: {resourcePath})");
            return null;
        }

        /// <summary>
        /// Build a clickable thumbnail for the given texture. Clicking opens a full-screen zoom
        /// overlay anchored at the panel root. Returns null if the texture is null.
        /// </summary>
        public static VisualElement CreateThumbnail(Texture2D texture, float height = 200f)
        {
            if (texture == null) return null;

            var image = new VisualElement();
            image.name = "scenario-image";
            image.style.height = height;
            image.style.marginTop = UIStyles.SpaceMD;
            image.style.marginBottom = UIStyles.SpaceMD;
            image.style.backgroundImage = new StyleBackground(texture);
            image.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
            image.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Center);
            image.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Center);
            UIStyles.SetBorderRadius(image, UIStyles.RadiusMD);
            image.pickingMode = PickingMode.Position;

            bool zoomed = false;

            image.RegisterCallback<MouseEnterEvent>(_ => { if (!zoomed) image.style.opacity = 0.85f; });
            image.RegisterCallback<MouseLeaveEvent>(_ => { if (!zoomed) image.style.opacity = 1f; });

            image.RegisterCallback<ClickEvent>(evt =>
            {
                if (zoomed) return;
                var root = image.panel?.visualTree;
                if (root == null) return;

                var overlay = new VisualElement();
                overlay.name = "image-zoom-overlay";
                overlay.style.position = Position.Absolute;
                overlay.style.left = 0;
                overlay.style.top = 0;
                overlay.style.width = Length.Percent(100);
                overlay.style.height = Length.Percent(100);
                overlay.style.backgroundColor = UIStyles.BackdropHeavy;
                overlay.style.justifyContent = Justify.Center;
                overlay.style.alignItems = Align.Center;
                overlay.pickingMode = PickingMode.Position;

                var zoomedImage = new VisualElement();
                zoomedImage.style.width = Length.Percent(90);
                zoomedImage.style.height = Length.Percent(90);
                zoomedImage.style.backgroundImage = new StyleBackground(texture);
                zoomedImage.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
                zoomedImage.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Center);
                zoomedImage.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Center);

                // Drawn icon — Unicode ✕ is missing from WebGL bundled fonts
                var closeIcon = WiseTwinIcons.CloseX(20, UIStyles.TextPrimary);
                closeIcon.style.position = Position.Absolute;
                closeIcon.style.top = 20;
                closeIcon.style.right = 20;

                overlay.Add(zoomedImage);
                overlay.Add(closeIcon);
                root.Add(overlay);
                overlay.BringToFront();

                overlay.RegisterCallback<ClickEvent>(closeEvt =>
                {
                    root.Remove(overlay);
                    zoomed = false;
                    closeEvt.StopPropagation();
                });

                zoomed = true;
                evt.StopPropagation();
            });

            return image;
        }
    }
}
