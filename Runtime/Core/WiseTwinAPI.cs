using System;
using UnityEngine;
using WiseTwin.Analytics;
using WiseTwin.UI;

namespace WiseTwin
{
    /// <summary>
    /// Public, stable entry point for external scripts that want to interact with the WiseTwin
    /// training system. Use this façade instead of accessing managers (WiseTwinManager,
    /// ProgressionManager, TrainingAnalytics, ProcedureDisplayer) directly — internal
    /// refactors will preserve this surface.
    ///
    /// Common scenarios:
    ///  • Validate the current procedure step from custom 3D logic (e.g. an object reaching a target)
    ///  • Penalise the score when the player does something wrong outside the package's UI
    ///  • Listen to step / score / scenario events to drive a custom HUD
    ///  • Skip a scenario or complete the training programmatically
    ///
    /// All methods are safe to call before the training is initialised — they will log a warning
    /// and return a sensible default rather than throwing.
    /// </summary>
    public static class WiseTwinAPI
    {
        // ─────────────────────────────────────────────────────────────
        //  Events
        // ─────────────────────────────────────────────────────────────

        /// <summary>Fires after a procedure step is validated (success or fail). Args: stepIndex, success.</summary>
        public static event Action<int, bool> OnStepValidated;

        /// <summary>Fires whenever the current cumulative score changes. Arg: newScore (0-100).</summary>
        public static event Action<float> OnScoreChanged;

        /// <summary>Fires when a scenario begins. Args: scenarioIndex, scenario.</summary>
        public static event Action<int, ScenarioData> OnScenarioStarted;

        /// <summary>Fires when the entire training is completed (CompleteTraining was called).</summary>
        public static event Action OnTrainingCompleted;

        /// <summary>Fires when the training is fully reset (RestartTraining was called), just before the scene reloads.</summary>
        public static event Action OnTrainingRestarted;

        /// <summary>Fires when LogCustomEvent is called. Args: eventId, success, weight, description.</summary>
        public static event Action<string, bool, float, string> OnCustomEventLogged;

        // Internal raisers — called by managers/displayers. Kept internal so external code
        // cannot fire them directly.
        internal static void RaiseStepValidated(int stepIndex, bool success) => OnStepValidated?.Invoke(stepIndex, success);
        internal static void RaiseScoreChanged(float newScore) => OnScoreChanged?.Invoke(newScore);
        internal static void RaiseScenarioStarted(int index, ScenarioData scenario) => OnScenarioStarted?.Invoke(index, scenario);
        internal static void RaiseTrainingCompleted() => OnTrainingCompleted?.Invoke();
        internal static void RaiseTrainingRestarted() => OnTrainingRestarted?.Invoke();
        internal static void RaiseCustomEventLogged(string eventId, bool success, float weight, string description) => OnCustomEventLogged?.Invoke(eventId, success, weight, description);

        // ─────────────────────────────────────────────────────────────
        //  Flow control
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Validates the current procedure step from external code. Bypasses the package's UI
        /// (no need to click the highlighted object or press the validate button).
        /// </summary>
        /// <param name="success">true to mark the step as completed, false to record a failed step.</param>
        /// <returns>true if a step was validated, false if no procedure is active.</returns>
        /// <example>
        /// <code>
        /// // In a custom MonoBehaviour: validate the step when a 3D object reaches a target
        /// void Update() {
        ///     if (Vector3.Distance(valve.position, target.position) &lt; 0.1f) {
        ///         WiseTwinAPI.ValidateCurrentStep(success: true);
        ///         enabled = false; // only fire once
        ///     }
        /// }
        /// </code>
        /// </example>
        public static bool ValidateCurrentStep(bool success = true)
        {
            var procedure = ContentDisplayManager.Instance?.CurrentDisplayer as ProcedureDisplayer;
            if (procedure == null)
            {
                Debug.LogWarning("[WiseTwinAPI] ValidateCurrentStep called but no procedure is active");
                return false;
            }
            return procedure.ValidateCurrentStep(success);
        }

        /// <summary>
        /// Skips the current scenario and advances to the next one. The current scenario is
        /// closed (its content displayer is dismissed) without being marked as successfully completed.
        /// </summary>
        public static void SkipCurrentScenario()
        {
            var progression = ProgressionManager.Instance;
            if (progression == null)
            {
                Debug.LogWarning("[WiseTwinAPI] SkipCurrentScenario called but ProgressionManager is not initialised");
                return;
            }

            ContentDisplayManager.Instance?.CloseCurrentContent();
            progression.MoveToNextScenario();
        }

        /// <summary>
        /// Completes the entire training, fires OnTrainingCompleted, and notifies the parent
        /// web application via the WebGL bridge (in production builds).
        /// </summary>
        /// <param name="trainingName">Optional display name for the training (defaults to scene name)</param>
        public static void CompleteTraining(string trainingName = null)
        {
            var manager = WiseTwinManager.Instance;
            if (manager == null)
            {
                Debug.LogWarning("[WiseTwinAPI] CompleteTraining called but WiseTwinManager is not initialised");
                return;
            }

            manager.CompleteTraining(trainingName);
            // Note: WiseTwinManager.CompleteTraining already calls RaiseTrainingCompleted
        }

        /// <summary>
        /// Fully resets the training and reloads the current scene from scratch — the same
        /// behavior as the red restart button in the HUD, but WITHOUT the confirmation dialog.
        /// All in-memory state (analytics, progression, UI, player position) is discarded.
        ///
        /// Fires OnTrainingRestarted just before the scene reloads. Because the scene reloads,
        /// any code running after this call in the same frame should not assume the WiseTwin
        /// singletons still exist.
        /// </summary>
        /// <example>
        /// <code>
        /// // Restart the whole training from a custom "give up" button, no confirmation popup
        /// void OnGiveUpClicked() {
        ///     WiseTwinAPI.RestartTraining();
        /// }
        /// </code>
        /// </example>
        public static void RestartTraining()
        {
            var manager = WiseTwinManager.Instance;
            if (manager == null)
            {
                Debug.LogWarning("[WiseTwinAPI] RestartTraining called but WiseTwinManager is not initialised");
                return;
            }

            manager.RestartTraining();
            // Note: WiseTwinManager.RestartTraining already calls RaiseTrainingRestarted
        }

        // ─────────────────────────────────────────────────────────────
        //  State queries
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns information about the currently active scenario, or null if no scenario is active.
        /// </summary>
        public static ScenarioInfo? GetCurrentScenarioInfo()
        {
            var progression = ProgressionManager.Instance;
            var scenario = progression?.CurrentScenario;
            if (scenario == null) return null;

            return new ScenarioInfo
            {
                Index = progression.CurrentScenarioIndex,
                Id = scenario.id,
                Type = scenario.type,
                Total = progression.TotalScenarios
            };
        }

        /// <summary>
        /// Returns the current cumulative score (0-100) computed from all interactions so far.
        /// </summary>
        public static float GetCurrentScore()
        {
            return TrainingAnalytics.Instance?.CalculateScore() ?? 100f;
        }

        /// <summary>
        /// True if a training session is currently running (after the tutorial, before completion).
        /// </summary>
        public static bool IsTrainingActive()
        {
            return ProgressionManager.Instance?.IsProgressionActive ?? false;
        }

        // ─────────────────────────────────────────────────────────────
        //  Custom events (score-impacting external actions)
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Logs a custom event from external 3D logic. The event is recorded in analytics and
        /// participates in the score calculation according to its weight.
        /// </summary>
        /// <param name="eventId">Unique identifier (e.g. "forbidden_zone_entered", "helmet_picked_up")</param>
        /// <param name="success">true for a positive action, false for a mistake/penalty</param>
        /// <param name="weight">
        /// Score impact: 1.0 (default) counts as one normal interaction, 3.0 as three.
        /// Use 0 to track the event in analytics without affecting the score.
        /// </param>
        /// <param name="description">Optional human-readable description shown in the SaaS export</param>
        /// <example>
        /// <code>
        /// // Penalise the player when they enter a restricted zone
        /// void OnTriggerEnter(Collider other) {
        ///     if (other.CompareTag("Player")) {
        ///         WiseTwinAPI.LogCustomEvent(
        ///             eventId: "forbidden_zone_entered",
        ///             success: false,
        ///             weight: 3.0f,
        ///             description: "Player entered the high-voltage area"
        ///         );
        ///     }
        /// }
        /// </code>
        /// </example>
        public static void LogCustomEvent(string eventId, bool success, float weight = 1.0f, string description = null)
        {
            var analytics = TrainingAnalytics.Instance;
            if (analytics == null)
            {
                Debug.LogWarning($"[WiseTwinAPI] LogCustomEvent('{eventId}') called but TrainingAnalytics is not initialised");
                return;
            }

            analytics.LogCustomEvent(eventId, success, weight, description);
        }
    }

    /// <summary>
    /// Snapshot of the currently active scenario, returned by WiseTwinAPI.GetCurrentScenarioInfo().
    /// </summary>
    public struct ScenarioInfo
    {
        /// <summary>Zero-based index of the scenario in the scenarios list.</summary>
        public int Index;

        /// <summary>Scenario id from metadata (e.g. "scenario_1").</summary>
        public string Id;

        /// <summary>Scenario type: "question", "procedure", "text", or "dialogue".</summary>
        public string Type;

        /// <summary>Total number of scenarios in the training.</summary>
        public int Total;
    }
}
