using System;
using UnityEngine;

namespace WiseTwin
{
    [System.Serializable]
    public class CharacterModelEntry
    {
        public string modelName;
        public GameObject modelRoot;
        public RuntimeAnimatorController animatorController;
    }

    /// <summary>
    /// Manages character model swapping at runtime.
    /// Place on the Player GameObject alongside FirstPersonCharacter.
    /// Pre-place all character models as children (only one active at a time).
    /// </summary>
    public class CharacterSwapper : MonoBehaviour
    {
        [Header("Character Models")]
        [Tooltip("List of available character models (pre-placed as children)")]
        public CharacterModelEntry[] characterModels;

        [Header("Debug")]
        [Tooltip("Enable debug logs")]
        public bool enableDebugLogs = false;

        public static CharacterSwapper Instance { get; private set; }

        /// <summary>Fired after a successful swap, with the new model name.</summary>
        public event Action<string> OnCharacterSwapped;

        /// <summary>Name of the currently active model.</summary>
        public string CurrentModelName => currentIndex >= 0 && currentIndex < characterModels.Length
            ? characterModels[currentIndex].modelName
            : null;

        private int currentIndex = -1;
        private FirstPersonCharacter firstPersonCharacter;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
        }

        void Start()
        {
            firstPersonCharacter = GetComponent<FirstPersonCharacter>();

            if (characterModels == null || characterModels.Length ==0)
            {
                DebugLog("No character models configured.");
                return;
            }

            // Find which model is already active, default to index 0
            currentIndex = 0;
            for (int i = 0; i < characterModels.Length; i++)
            {
                if (characterModels[i].modelRoot == null)
                {
                    Debug.LogWarning($"[CharacterSwapper] Model entry '{characterModels[i].modelName}' has no modelRoot assigned.");
                    continue;
                }
                if (characterModels[i].modelRoot.activeSelf)
                    currentIndex = i;
            }

            // Deactivate all except the active one (don't toggle the active model)
            for (int i = 0; i < characterModels.Length; i++)
            {
                if (characterModels[i].modelRoot != null && i != currentIndex)
                    characterModels[i].modelRoot.SetActive(false);
            }

            // Re-wire FPC to the active model (handles case where active model isn't index 0)
            RewireFirstPersonCharacter(characterModels[currentIndex]);

            DebugLog($"Initialized with {characterModels.Length} models. Active: {CurrentModelName}");
        }

        /// <summary>Swap to a character model by name.</summary>
        public void SwapTo(string modelName)
        {
            if (characterModels == null) return;

            for (int i = 0; i < characterModels.Length; i++)
            {
                if (string.Equals(characterModels[i].modelName, modelName, StringComparison.OrdinalIgnoreCase))
                {
                    SwapTo(i);
                    return;
                }
            }

            Debug.LogWarning($"[CharacterSwapper] Model '{modelName}' not found.");
        }

        /// <summary>Swap to a character model by index.</summary>
        public void SwapTo(int index)
        {
            if (characterModels == null || index < 0 || index >= characterModels.Length)
            {
                Debug.LogWarning($"[CharacterSwapper] Invalid model index: {index}");
                return;
            }

            if (index == currentIndex) return;

            var newEntry = characterModels[index];
            if (newEntry.modelRoot == null)
            {
                Debug.LogWarning($"[CharacterSwapper] Model '{newEntry.modelName}' has no modelRoot assigned.");
                return;
            }

            // Deactivate current model
            if (currentIndex >= 0 && currentIndex < characterModels.Length && characterModels[currentIndex].modelRoot != null)
            {
                characterModels[currentIndex].modelRoot.SetActive(false);
            }

            // Activate new model
            newEntry.modelRoot.SetActive(true);
            currentIndex = index;

            // Re-wire FirstPersonCharacter
            RewireFirstPersonCharacter(newEntry);

            DebugLog($"Swapped to '{newEntry.modelName}'");
            OnCharacterSwapped?.Invoke(newEntry.modelName);
        }

        void RewireFirstPersonCharacter(CharacterModelEntry entry)
        {
            if (firstPersonCharacter == null) return;

            // Animator
            var newAnimator = entry.modelRoot.GetComponentInChildren<Animator>();
            if (newAnimator != null)
            {
                // Disable root motion so animations don't override character rotation
                newAnimator.applyRootMotion = false;

                firstPersonCharacter.animator = newAnimator;

                // Apply override controller if specified
                if (entry.animatorController != null)
                {
                    newAnimator.runtimeAnimatorController = entry.animatorController;
                }
            }

            // Head bone — find a Transform containing "Head" (case-insensitive)
            firstPersonCharacter.headBone = FindBoneRecursive(entry.modelRoot.transform, "head");

            // hideInFirstPerson — all renderers in the new model
            firstPersonCharacter.hideInFirstPerson = entry.modelRoot.GetComponentsInChildren<Renderer>(true);

            DebugLog($"Rewired FPC: animator={newAnimator != null}, headBone={firstPersonCharacter.headBone?.name ?? "null"}, renderers={firstPersonCharacter.hideInFirstPerson.Length}");
        }

        static Transform FindBoneRecursive(Transform root, string nameContains)
        {
            foreach (Transform child in root)
            {
                if (child.name.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0)
                    return child;

                var found = FindBoneRecursive(child, nameContains);
                if (found != null) return found;
            }
            return null;
        }

        void DebugLog(string message)
        {
            if (enableDebugLogs)
                Debug.Log($"[CharacterSwapper] {message}");
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
