using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;
using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace WiseTwin.UI
{
    /// <summary>
    /// Gestionnaire principal pour afficher différents types de contenu
    /// Détermine quel afficheur utiliser selon le type de contenu
    /// </summary>
    public class ContentDisplayManager : MonoBehaviour
    {
        [Header("Configuration")]
        [SerializeField] private bool debugMode = false;

        // UI Document
        private UIDocument uiDocument;
        private VisualElement root;

        // Afficheurs de contenu
        private Dictionary<ContentType, IContentDisplayer> contentDisplayers;

        // État actuel
        private ContentType currentContentType;
        private IContentDisplayer currentDisplayer;
        private bool isDisplaying = false;

        // Singleton
        public static ContentDisplayManager Instance { get; private set; }

        // Public properties
        public bool DebugMode => debugMode;

        // Events
        public event Action<ContentType, string> OnContentDisplayed;
        public event Action<ContentType, string> OnContentClosed;
        public event Action<string, bool> OnContentCompleted; // objectId, success

        void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                // Ne pas appliquer DontDestroyOnLoad si on est dans WiseTwinSystem
                // C'est le parent WiseTwinSystem qui gère la persistance
                if (transform.parent == null)
                {
                    DontDestroyOnLoad(gameObject);
                }
                // Pas de warning si on est enfant de WiseTwinSystem
                InitializeDisplayers();
                SetupUIDocument();

                // Subscribe to scene changes for DontDestroyOnLoad support
                UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;

                if (debugMode) Debug.Log("[ContentDisplayManager] Instance created and initialized");
            }
            else
            {
                Destroy(gameObject);
                return;
            }
        }

        /// <summary>
        /// Handle scene changes - refresh UIDocument to ensure proper connection
        /// </summary>
        void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            // Skip additive scene loads
            if (mode == UnityEngine.SceneManagement.LoadSceneMode.Additive) return;

            Debug.Log($"[ContentDisplayManager] OnSceneLoaded: {scene.name}");

            // Re-validate UIDocument root element
            if (uiDocument != null)
            {
                Debug.Log($"[ContentDisplayManager] UIDocument exists, panelSettings: {(uiDocument.panelSettings != null ? uiDocument.panelSettings.name : "NULL")}");

                root = uiDocument.rootVisualElement;
                if (root == null)
                {
                    Debug.LogError("[ContentDisplayManager] Root visual element is null after scene change!");
                    return;
                }

                // Reset root configuration
                root.Clear();
                root.style.position = Position.Absolute;
                root.style.width = Length.Percent(100);
                root.style.height = Length.Percent(100);
                root.pickingMode = PickingMode.Ignore;

                // Reset state
                isDisplaying = false;
                currentDisplayer = null;

                Debug.Log("[ContentDisplayManager] UI refreshed successfully");
            }
            else
            {
                Debug.LogError("[ContentDisplayManager] UIDocument is null after scene change!");
            }
        }

        void OnDestroy()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;

            if (Instance == this)
            {
                Instance = null;
            }
        }

        void InitializeDisplayers()
        {
            contentDisplayers = new Dictionary<ContentType, IContentDisplayer>();

            // Créer les afficheurs pour chaque type
            var questionDisplayer = new GameObject("QuestionDisplayer").AddComponent<QuestionDisplayer>();
            questionDisplayer.transform.SetParent(transform);
            contentDisplayers[ContentType.Question] = questionDisplayer;

            var procedureDisplayer = new GameObject("ProcedureDisplayer").AddComponent<ProcedureDisplayer>();
            procedureDisplayer.transform.SetParent(transform);
            contentDisplayers[ContentType.Procedure] = procedureDisplayer;

            var textDisplayer = new GameObject("TextDisplayer").AddComponent<TextDisplayer>();
            textDisplayer.transform.SetParent(transform);
            contentDisplayers[ContentType.Text] = textDisplayer;

            var dialogueDisplayer = new GameObject("DialogueDisplayer").AddComponent<DialogueDisplayer>();
            dialogueDisplayer.transform.SetParent(transform);
            contentDisplayers[ContentType.Dialogue] = dialogueDisplayer;

            if (debugMode) Debug.Log($"[ContentDisplayManager] Initialized {contentDisplayers.Count} displayers");
        }

        void SetupUIDocument()
        {
            // S'assurer d'avoir notre propre UIDocument
            uiDocument = GetComponent<UIDocument>();
            if (uiDocument == null)
            {
                uiDocument = gameObject.AddComponent<UIDocument>();
                if (debugMode) Debug.Log("[ContentDisplayManager] Created UIDocument component");
            }

            // Vérifier qu'on ne partage pas le UIDocument avec un autre composant
            var otherUIUsers = GetComponents<MonoBehaviour>()
                .Where(c => c != this && c.GetType().Name.Contains("HUD"))
                .ToArray();

            if (otherUIUsers.Length > 0)
            {
                Debug.LogError($"[ContentDisplayManager] WARNING: Sharing GameObject with {otherUIUsers[0].GetType().Name}! " +
                    "ContentDisplayManager should be on its own GameObject with its own UIDocument.");
            }

            if (uiDocument.panelSettings == null)
            {
                Debug.LogWarning("[ContentDisplayManager] PanelSettings is null! Please assign it in the inspector.");
            }
            else if (debugMode)
            {
                Debug.Log($"[ContentDisplayManager] PanelSettings assigned: {uiDocument.panelSettings.name}");
            }

            root = uiDocument.rootVisualElement;
            if (root == null)
            {
                Debug.LogError("[ContentDisplayManager] Root visual element is null!");
                return;
            }

            if (debugMode) Debug.Log($"[ContentDisplayManager] Root element setup - Width: {root.resolvedStyle.width}, Height: {root.resolvedStyle.height}");

            // Configuration de base du root
            root.style.position = Position.Absolute;
            root.style.width = Length.Percent(100);
            root.style.height = Length.Percent(100);
            root.pickingMode = PickingMode.Ignore; // Par défaut, ne pas bloquer les clics
        }

        /// <summary>
        /// Display a scenario from the new scenario-based system
        /// </summary>
        public void DisplayScenario(WiseTwin.ScenarioData scenario)
        {
            Debug.Log($"[ContentDisplayManager] DisplayScenario called - scenario: {scenario?.id}, type: {scenario?.type}");
            Debug.Log($"[ContentDisplayManager] UIDocument: {(uiDocument != null ? "exists" : "NULL")}, PanelSettings: {(uiDocument?.panelSettings != null ? uiDocument.panelSettings.name : "NULL")}, root: {(root != null ? "exists" : "NULL")}");

            if (scenario == null)
            {
                Debug.LogError("[ContentDisplayManager] Scenario is null!");
                return;
            }

            // Determine ContentType from scenario type
            ContentType contentType;
            switch (scenario.type?.ToLower())
            {
                case "question":
                    contentType = ContentType.Question;
                    break;
                case "procedure":
                    contentType = ContentType.Procedure;
                    break;
                case "text":
                    contentType = ContentType.Text;
                    break;
                case "dialogue":
                    contentType = ContentType.Dialogue;
                    break;
                default:
                    Debug.LogError($"[ContentDisplayManager] Unknown scenario type: {scenario.type}");
                    return;
            }

            // Get content data
            var contentData = scenario.GetContentData();
            if (contentData == null)
            {
                Debug.LogError($"[ContentDisplayManager] No content data for scenario {scenario.id}");
                return;
            }

            // Convert JObject to Dictionary<string, object>
            Dictionary<string, object> contentDict = contentData.ToObject<Dictionary<string, object>>();

            // Display the content
            DisplayContent(scenario.id, contentType, contentDict);
        }

        /// <summary>
        /// Affiche un contenu en fonction de son type
        /// </summary>
        public void DisplayContent(string objectId, ContentType contentType, Dictionary<string, object> contentData)
        {
            if (isDisplaying)
            {
                if (debugMode) Debug.LogWarning("[ContentDisplayManager] Already displaying content, closing current...");
                CloseCurrentContent();
            }

            if (!contentDisplayers.ContainsKey(contentType))
            {
                Debug.LogError($"[ContentDisplayManager] No displayer for content type: {contentType}");
                ShowPlaceholderUI(contentType, contentData);
                return;
            }

            currentContentType = contentType;
            currentDisplayer = contentDisplayers[contentType];
            isDisplaying = true;

            // Root stays PickingMode.Ignore so host scripts that use EventSystem.IsPointerOverGameObject()
            // aren't told the whole screen is UI. Each displayer is responsible for adding its own
            // full-screen backdrop in PickingMode.Position when it needs to block 3D clicks
            // (Question / Dialogue / Video / Tutorial / Completion). The procedure displayer
            // deliberately keeps its backdrop in Ignore so the player can click 3D targets to
            // validate steps.

            if (debugMode)
            {
                Debug.Log($"[ContentDisplayManager] Before display - Root child count: {root.childCount}");
                Debug.Log($"[ContentDisplayManager] Root size: {root.resolvedStyle.width}x{root.resolvedStyle.height}");
            }

            // Afficher le contenu
            currentDisplayer.Display(objectId, contentData, root);

            if (debugMode) Debug.Log($"[ContentDisplayManager] After display - Root child count: {root.childCount}");

            // S'abonner aux événements de l'afficheur
            currentDisplayer.OnClosed += HandleContentClosed;
            currentDisplayer.OnCompleted += HandleContentCompleted;

            OnContentDisplayed?.Invoke(contentType, objectId);

            if (debugMode) Debug.Log($"[ContentDisplayManager] Displaying {contentType} content for {objectId}");
        }

        /// <summary>
        /// Affiche une UI placeholder pour les types non implémentés
        /// </summary>
        private void ShowPlaceholderUI(ContentType contentType, Dictionary<string, object> contentData)
        {
            // Clear root
            root.Clear();
            root.pickingMode = PickingMode.Position;

            // Container principal
            var container = new VisualElement();
            UIStyles.ApplyBackdropHeavyStyle(container);

            // Boîte de contenu
            var contentBox = new VisualElement();
            contentBox.style.width = 600;
            contentBox.style.maxWidth = Length.Percent(90);
            UIStyles.ApplyCardStyle(contentBox, UIStyles.RadiusXL);
            UIStyles.SetPadding(contentBox, UIStyles.Space3XL);

            // Titre
            var title = UIStyles.CreateTitle($"Content Type: {contentType}", UIStyles.Font2XL);
            title.style.marginBottom = UIStyles.SpaceLG;
            contentBox.Add(title);

            // Message
            var message = UIStyles.CreateBodyText("This content type is not yet implemented.\nClick anywhere to close.", UIStyles.FontMD);
            message.style.color = UIStyles.TextSecondary;
            message.style.unityTextAlign = TextAnchor.MiddleCenter;
            message.style.marginBottom = UIStyles.SpaceXL;
            contentBox.Add(message);

            // Afficher les données de debug si activé
            if (debugMode && contentData != null)
            {
                var debugText = UIStyles.CreateMutedText($"Data keys: {string.Join(", ", contentData.Keys)}", UIStyles.FontSM);
                debugText.style.unityTextAlign = TextAnchor.MiddleCenter;
                contentBox.Add(debugText);
            }

            container.Add(contentBox);
            root.Add(container);

            // Fermer au clic
            container.RegisterCallback<MouseDownEvent>((evt) => {
                root.Clear();
                root.pickingMode = PickingMode.Ignore;
                isDisplaying = false;
            });
        }

        void HandleContentClosed(string objectId)
        {
            if (currentDisplayer != null)
            {
                currentDisplayer.OnClosed -= HandleContentClosed;
                currentDisplayer.OnCompleted -= HandleContentCompleted;
            }

            CloseCurrentContent();
            OnContentClosed?.Invoke(currentContentType, objectId);
        }

        void HandleContentCompleted(string objectId, bool success)
        {
            OnContentCompleted?.Invoke(objectId, success);
        }

        public void CloseCurrentContent()
        {
            if (currentDisplayer != null)
            {
                currentDisplayer.Close();
                currentDisplayer = null;
            }

            root.Clear();
            root.pickingMode = PickingMode.Ignore;
            isDisplaying = false;

            if (debugMode) Debug.Log("[ContentDisplayManager] Content closed");
        }

        public bool IsDisplaying => isDisplaying;
        public ContentType CurrentContentType => currentContentType;
        public IContentDisplayer CurrentDisplayer => currentDisplayer;
    }

    /// <summary>
    /// Interface pour tous les afficheurs de contenu
    /// </summary>
    public interface IContentDisplayer
    {
        event Action<string> OnClosed;
        event Action<string, bool> OnCompleted; // objectId, success

        void Display(string objectId, Dictionary<string, object> contentData, VisualElement root);
        void Close();
    }
}