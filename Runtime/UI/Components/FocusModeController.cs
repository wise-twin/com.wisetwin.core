using UnityEngine;
using UnityEngine.UIElements;
using System.Reflection;
using System;

namespace WiseTwin.UI
{
    /// <summary>
    /// Controls the focus mode: blurs the background and blocks interactions
    /// outside the WiseTwin UI during specific scenarios.
    ///
    /// Uses a combination of:
    /// - UI overlay for darkening/blocking clicks
    /// - Post-processing blur effect (if URP with depth of field available)
    /// - Camera layer masking for interaction blocking
    /// </summary>
    public class FocusModeController : MonoBehaviour
    {
        [Header("Configuration")]
        [Tooltip("Opacity of the dark overlay (0-1)")]
        [SerializeField] private float overlayOpacity = 0.85f;

        [Tooltip("Blur amount for post-processing (if available)")]
        [SerializeField] private float blurAmount = 8f;

        [Tooltip("Transition duration in seconds")]
        [SerializeField] private float transitionDuration = 0.3f;

        [Header("Debug")]
        [SerializeField] private bool debugMode = false;

        // UI elements
        private UIDocument uiDocument;
        private VisualElement root;
        private VisualElement overlayElement;

        // Post-processing (URP) - using Component to avoid compile-time dependency
        private Component focusVolume;
        private GameObject volumeObject;
        private object volumeProfile;

        // State
        private bool isActive = false;
        private Coroutine transitionCoroutine;

        // Singleton
        public static FocusModeController Instance { get; private set; }

        /// <summary>
        /// Whether focus mode is currently active
        /// </summary>
        public bool IsActive => isActive;

        void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                Initialize();
            }
            else
            {
                Destroy(gameObject);
            }
        }

        void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            CleanupPostProcessing();
        }

        // Default sorting order for focus mode (below scenario UI)
        private const int FOCUS_MODE_SORTING_ORDER = 10;
        // High sorting order for startup overlay (above everything)
        private const int STARTUP_OVERLAY_SORTING_ORDER = 9999;

        void Initialize()
        {
            // Setup UI Document for the overlay
            uiDocument = GetComponent<UIDocument>();
            if (uiDocument == null)
            {
                uiDocument = gameObject.AddComponent<UIDocument>();
            }

            // Default sorting order - will be changed for startup overlay
            uiDocument.sortingOrder = FOCUS_MODE_SORTING_ORDER;

            // Copy PanelSettings from ContentDisplayManager if available
            var contentManager = ContentDisplayManager.Instance;
            if (contentManager != null)
            {
                var contentDoc = contentManager.GetComponent<UIDocument>();
                if (contentDoc != null && contentDoc.panelSettings != null)
                {
                    uiDocument.panelSettings = contentDoc.panelSettings;
                }
            }

            root = uiDocument.rootVisualElement;
            if (root != null)
            {
                root.style.position = Position.Absolute;
                root.style.width = Length.Percent(100);
                root.style.height = Length.Percent(100);
                root.pickingMode = PickingMode.Ignore;
            }

            // Setup post-processing volume for blur
            SetupPostProcessing();

            if (debugMode) Debug.Log("[FocusModeController] Initialized");
        }

        void SetupPostProcessing()
        {
            // Try to setup URP post-processing via reflection (to avoid compile-time dependency)
            try
            {
                // Check if Volume type exists (requires Unity.RenderPipelines.Core.Runtime)
                var volumeType = System.Type.GetType("UnityEngine.Rendering.Volume, Unity.RenderPipelines.Core.Runtime");
                var volumeProfileType = System.Type.GetType("UnityEngine.Rendering.VolumeProfile, Unity.RenderPipelines.Core.Runtime");

                if (volumeType == null || volumeProfileType == null)
                {
                    if (debugMode) Debug.Log("[FocusModeController] Render Pipelines not available, using overlay only");
                    return;
                }

                // Create volume for blur effect
                volumeObject = new GameObject("FocusModeVolume");
                volumeObject.transform.SetParent(transform);

                // Add Volume component via reflection
                focusVolume = volumeObject.AddComponent(volumeType) as Component;
                if (focusVolume == null)
                {
                    if (debugMode) Debug.Log("[FocusModeController] Could not add Volume component");
                    return;
                }

                // Set Volume properties
                SetProperty(focusVolume, "isGlobal", true);
                SetProperty(focusVolume, "priority", 100f);
                SetProperty(focusVolume, "weight", 0f);

                // Create profile
                volumeProfile = ScriptableObject.CreateInstance(volumeProfileType);
                SetProperty(focusVolume, "profile", volumeProfile);

                // Try to add DepthOfField via reflection
                var dofType = System.Type.GetType("UnityEngine.Rendering.Universal.DepthOfField, Unity.RenderPipelines.Universal.Runtime");
                if (dofType != null && volumeProfile != null)
                {
                    var addMethod = volumeProfileType.GetMethod("Add", new[] { typeof(bool) });
                    if (addMethod != null)
                    {
                        var genericAdd = addMethod.MakeGenericMethod(dofType);
                        var dof = genericAdd.Invoke(volumeProfile, new object[] { true });

                        // Set properties via reflection
                        SetVolumeParameter(dof, "mode", 0); // Gaussian = 0
                        SetVolumeParameter(dof, "gaussianStart", 0f);
                        SetVolumeParameter(dof, "gaussianEnd", 0.1f);
                        SetVolumeParameter(dof, "gaussianMaxRadius", blurAmount);
                    }
                }

                // Try to add Vignette
                var vignetteType = System.Type.GetType("UnityEngine.Rendering.Universal.Vignette, Unity.RenderPipelines.Universal.Runtime");
                if (vignetteType != null && volumeProfile != null)
                {
                    var addMethod = volumeProfileType.GetMethod("Add", new[] { typeof(bool) });
                    if (addMethod != null)
                    {
                        var genericAdd = addMethod.MakeGenericMethod(vignetteType);
                        var vignette = genericAdd.Invoke(volumeProfile, new object[] { true });

                        SetVolumeParameter(vignette, "intensity", 0.45f);
                        SetVolumeParameter(vignette, "smoothness", 0.6f);
                    }
                }

                if (debugMode) Debug.Log("[FocusModeController] Post-processing volume created");
            }
            catch (System.Exception e)
            {
                if (debugMode) Debug.Log($"[FocusModeController] Could not setup post-processing: {e.Message}");
            }
        }

        void SetProperty(object obj, string propertyName, object value)
        {
            if (obj == null) return;
            var prop = obj.GetType().GetProperty(propertyName);
            if (prop != null && prop.CanWrite)
            {
                prop.SetValue(obj, value);
            }
        }

        void SetVolumeParameter(object component, string paramName, object value)
        {
            try
            {
                var field = component.GetType().GetField(paramName);
                if (field != null)
                {
                    var param = field.GetValue(component);
                    var overrideMethod = param.GetType().GetMethod("Override");
                    if (overrideMethod != null)
                    {
                        overrideMethod.Invoke(param, new[] { value });
                    }
                }
            }
            catch { /* Silently fail if parameter doesn't exist */ }
        }

        void CleanupPostProcessing()
        {
            if (volumeObject != null)
            {
                if (volumeProfile != null && volumeProfile is UnityEngine.Object unityObj)
                {
                    DestroyImmediate(unityObj);
                }
                Destroy(volumeObject);
                volumeObject = null;
                focusVolume = null;
                volumeProfile = null;
            }
        }

        /// <summary>
        /// Activate focus mode with blur and overlay
        /// </summary>
        public void Activate()
        {
            if (isActive) return;

            if (debugMode) Debug.Log("[FocusModeController] Activating focus mode");

            isActive = true;

            // Block 3D interactions
            InteractionBlocker.Block();

            // Create overlay
            CreateOverlay();

            // Start transition
            if (transitionCoroutine != null)
            {
                StopCoroutine(transitionCoroutine);
            }
            transitionCoroutine = StartCoroutine(TransitionIn());
        }

        /// <summary>
        /// Deactivate focus mode
        /// </summary>
        public void Deactivate()
        {
            if (!isActive) return;

            if (debugMode) Debug.Log("[FocusModeController] Deactivating focus mode");

            // Start transition
            if (transitionCoroutine != null)
            {
                StopCoroutine(transitionCoroutine);
            }
            transitionCoroutine = StartCoroutine(TransitionOut());
        }

        void CreateOverlay()
        {
            if (root == null) return;

            root.Clear();

            overlayElement = new VisualElement();
            overlayElement.name = "focus-mode-overlay";
            overlayElement.style.position = Position.Absolute;
            overlayElement.style.top = 0;
            overlayElement.style.left = 0;
            overlayElement.style.right = 0;
            overlayElement.style.bottom = 0;
            overlayElement.style.backgroundColor = new Color(0, 0, 0, 0);
            overlayElement.pickingMode = PickingMode.Position; // Block clicks

            root.Add(overlayElement);
        }

        System.Collections.IEnumerator TransitionIn()
        {
            float elapsed = 0f;

            while (elapsed < transitionDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / transitionDuration);
                float easedT = EaseOutCubic(t);

                // Fade overlay
                if (overlayElement != null)
                {
                    overlayElement.style.backgroundColor = new Color(0, 0, 0, overlayOpacity * easedT);
                }

                // Fade post-processing
                SetVolumeWeight(easedT);

                yield return null;
            }

            // Final values
            if (overlayElement != null)
            {
                overlayElement.style.backgroundColor = new Color(0, 0, 0, overlayOpacity);
            }

            SetVolumeWeight(1f);

            transitionCoroutine = null;
        }

        System.Collections.IEnumerator TransitionOut()
        {
            float elapsed = 0f;

            while (elapsed < transitionDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / transitionDuration);
                float easedT = EaseOutCubic(t);

                // Fade overlay
                if (overlayElement != null)
                {
                    overlayElement.style.backgroundColor = new Color(0, 0, 0, overlayOpacity * (1f - easedT));
                }

                // Fade post-processing
                SetVolumeWeight(1f - easedT);

                yield return null;
            }

            // Cleanup
            if (root != null)
            {
                root.Clear();
            }

            SetVolumeWeight(0f);

            // Unblock 3D interactions
            InteractionBlocker.Unblock();

            isActive = false;
            transitionCoroutine = null;

            if (debugMode) Debug.Log("[FocusModeController] Focus mode fully deactivated");
        }

        void SetVolumeWeight(float weight)
        {
            if (focusVolume != null)
            {
                SetProperty(focusVolume, "weight", weight);
            }
        }

        float EaseOutCubic(float t)
        {
            return 1f - Mathf.Pow(1f - t, 3f);
        }

        /// <summary>
        /// Set the blur amount (only works before activation or requires reactivation)
        /// </summary>
        public void SetBlurAmount(float amount)
        {
            blurAmount = amount;

            if (focusVolume != null && volumeProfile != null)
            {
                // Try to update via reflection
                try
                {
                    var dofType = System.Type.GetType("UnityEngine.Rendering.Universal.DepthOfField, Unity.RenderPipelines.Universal.Runtime");
                    var volumeProfileType = volumeProfile.GetType();
                    if (dofType != null && volumeProfileType != null)
                    {
                        var tryGetMethod = volumeProfileType.GetMethod("TryGet");
                        if (tryGetMethod != null)
                        {
                            var genericTryGet = tryGetMethod.MakeGenericMethod(dofType);
                            var parameters = new object[] { null };
                            if ((bool)genericTryGet.Invoke(volumeProfile, parameters))
                            {
                                SetVolumeParameter(parameters[0], "gaussianMaxRadius", amount);
                            }
                        }
                    }
                }
                catch { /* Silently fail */ }
            }
        }

        /// <summary>
        /// Set the overlay opacity (0-1)
        /// </summary>
        public void SetOverlayOpacity(float opacity)
        {
            overlayOpacity = Mathf.Clamp01(opacity);

            if (isActive && overlayElement != null)
            {
                overlayElement.style.backgroundColor = new Color(0, 0, 0, overlayOpacity);
            }
        }

        // ==================== STARTUP OVERLAY ====================

        private VisualElement startupOverlayElement;
        private UnityEngine.UIElements.Button startupButton;
        private Action onStartupButtonClicked;

        /// <summary>
        /// Show a startup overlay with a centered button.
        /// When the button is clicked, the overlay fades out and onClicked is invoked.
        /// </summary>
        /// <param name="buttonText">Text to display on the button</param>
        /// <param name="onClicked">Callback when button is clicked (optional)</param>
        /// <param name="title">Optional title above the button</param>
        /// <param name="subtitle">Optional subtitle below the title</param>
        public void ShowStartupOverlay(string buttonText, Action onClicked = null, string title = null, string subtitle = null)
        {
            if (root == null)
            {
                Initialize();
            }

            if (debugMode) Debug.Log("[FocusModeController] Showing startup overlay");

            // Set high sorting order to be above all other UI
            if (uiDocument != null)
            {
                uiDocument.sortingOrder = STARTUP_OVERLAY_SORTING_ORDER;
            }

            onStartupButtonClicked = onClicked;
            isActive = true;

            // Block 3D interactions
            InteractionBlocker.Block();

            // Setup post-processing
            SetVolumeWeight(1f);

            // Create startup overlay
            CreateStartupOverlay(buttonText, title, subtitle);
        }

        void CreateStartupOverlay(string buttonText, string title, string subtitle)
        {
            if (root == null) return;

            root.Clear();

            // Main overlay background
            startupOverlayElement = new VisualElement();
            startupOverlayElement.name = "startup-overlay";
            startupOverlayElement.style.position = Position.Absolute;
            startupOverlayElement.style.top = 0;
            startupOverlayElement.style.left = 0;
            startupOverlayElement.style.right = 0;
            startupOverlayElement.style.bottom = 0;
            startupOverlayElement.style.backgroundColor = new Color(0, 0, 0, overlayOpacity);
            startupOverlayElement.pickingMode = PickingMode.Position;

            // Center container
            var centerContainer = new VisualElement();
            centerContainer.name = "startup-center";
            centerContainer.style.position = Position.Absolute;
            centerContainer.style.top = Length.Percent(50);
            centerContainer.style.left = Length.Percent(50);
            centerContainer.style.translate = new Translate(Length.Percent(-50), Length.Percent(-50));
            centerContainer.style.alignItems = Align.Center;
            centerContainer.style.justifyContent = Justify.Center;
            centerContainer.pickingMode = PickingMode.Ignore;

            // Title (if provided)
            if (!string.IsNullOrEmpty(title))
            {
                var titleLabel = new Label(title);
                titleLabel.name = "startup-title";
                titleLabel.style.fontSize = 32;
                titleLabel.style.color = Color.white;
                titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                titleLabel.style.marginBottom = 12;
                titleLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                titleLabel.style.textShadow = new TextShadow
                {
                    offset = new Vector2(2, 2),
                    blurRadius = 4,
                    color = new Color(0, 0, 0, 0.5f)
                };
                centerContainer.Add(titleLabel);
            }

            // Subtitle (if provided)
            if (!string.IsNullOrEmpty(subtitle))
            {
                var subtitleLabel = new Label(subtitle);
                subtitleLabel.name = "startup-subtitle";
                subtitleLabel.style.fontSize = 18;
                subtitleLabel.style.color = new Color(0.8f, 0.8f, 0.8f);
                subtitleLabel.style.marginBottom = 30;
                subtitleLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                subtitleLabel.style.maxWidth = 500;
                subtitleLabel.style.whiteSpace = WhiteSpace.Normal;
                centerContainer.Add(subtitleLabel);
            }

            // Start button
            startupButton = new UnityEngine.UIElements.Button();
            startupButton.name = "startup-button";
            startupButton.text = buttonText;
            startupButton.style.paddingTop = 18;
            startupButton.style.paddingBottom = 18;
            startupButton.style.paddingLeft = 50;
            startupButton.style.paddingRight = 50;
            startupButton.style.fontSize = 22;
            startupButton.style.unityFontStyleAndWeight = FontStyle.Bold;
            startupButton.style.backgroundColor = new Color(1f, 0.8f, 0f);
            startupButton.style.color = new Color(0.1f, 0.1f, 0.1f);
            startupButton.style.borderTopLeftRadius = 12;
            startupButton.style.borderTopRightRadius = 12;
            startupButton.style.borderBottomLeftRadius = 12;
            startupButton.style.borderBottomRightRadius = 12;
            startupButton.style.borderTopWidth = 0;
            startupButton.style.borderRightWidth = 0;
            startupButton.style.borderBottomWidth = 0;
            startupButton.style.borderLeftWidth = 0;
            startupButton.style.unityTextAlign = TextAnchor.MiddleCenter;

            // Hover effect via pseudo-states (simplified - just change cursor)
            startupButton.RegisterCallback<MouseEnterEvent>(evt =>
            {
                startupButton.style.backgroundColor = new Color(1f, 0.85f, 0.2f);
                startupButton.style.scale = new Scale(new Vector3(1.05f, 1.05f, 1f));
            });
            startupButton.RegisterCallback<MouseLeaveEvent>(evt =>
            {
                startupButton.style.backgroundColor = new Color(1f, 0.8f, 0f);
                startupButton.style.scale = new Scale(Vector3.one);
            });

            startupButton.clicked += OnStartupButtonClicked;
            centerContainer.Add(startupButton);

            startupOverlayElement.Add(centerContainer);
            root.Add(startupOverlayElement);

            // Prevent click through to background
            startupOverlayElement.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.target == startupOverlayElement)
                {
                    evt.StopPropagation();
                }
            });
        }

        void OnStartupButtonClicked()
        {
            if (debugMode) Debug.Log("[FocusModeController] Startup button clicked");

            // Transition out
            if (transitionCoroutine != null)
            {
                StopCoroutine(transitionCoroutine);
            }
            transitionCoroutine = StartCoroutine(TransitionOutStartup());
        }

        System.Collections.IEnumerator TransitionOutStartup()
        {
            float elapsed = 0f;

            while (elapsed < transitionDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / transitionDuration);
                float easedT = EaseOutCubic(t);

                // Fade overlay
                if (startupOverlayElement != null)
                {
                    startupOverlayElement.style.opacity = 1f - easedT;
                }

                // Fade post-processing
                SetVolumeWeight(1f - easedT);

                yield return null;
            }

            // Cleanup
            if (root != null)
            {
                root.Clear();
            }

            SetVolumeWeight(0f);

            // Reset sorting order
            if (uiDocument != null)
            {
                uiDocument.sortingOrder = FOCUS_MODE_SORTING_ORDER;
            }

            // Unblock 3D interactions
            InteractionBlocker.Unblock();

            isActive = false;
            transitionCoroutine = null;

            // Invoke callback
            onStartupButtonClicked?.Invoke();
            onStartupButtonClicked = null;

            if (debugMode) Debug.Log("[FocusModeController] Startup overlay hidden");
        }

        /// <summary>
        /// Hide the startup overlay without transition (for cleanup)
        /// </summary>
        public void HideStartupOverlay()
        {
            if (startupOverlayElement != null && root != null)
            {
                root.Clear();
                startupOverlayElement = null;
                startupButton = null;
            }

            SetVolumeWeight(0f);

            // Reset sorting order
            if (uiDocument != null)
            {
                uiDocument.sortingOrder = FOCUS_MODE_SORTING_ORDER;
            }

            // Unblock 3D interactions (only if was active)
            if (isActive)
            {
                InteractionBlocker.Unblock();
            }

            isActive = false;
            onStartupButtonClicked = null;
        }
    }
}
