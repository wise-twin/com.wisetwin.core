# WiseTwin Public API

This document describes the **public, stable API** that external scripts can use to integrate with the WiseTwin training system. All calls go through the `WiseTwinAPI` static class — internal managers (`WiseTwinManager`, `ProgressionManager`, `TrainingAnalytics`, `ProcedureDisplayer`) may change between versions, but `WiseTwinAPI` is preserved.

```csharp
using WiseTwin;

// Anywhere in your project's scripts
WiseTwinAPI.ValidateCurrentStep();
WiseTwinAPI.LogCustomEvent("forbidden_zone_entered", success: false, weight: 3.0f);
WiseTwinAPI.OnStepValidated += (stepIndex, success) => { /* ... */ };
```

---

## Quick reference

The sample script `CustomTrainingExample.cs` (importable from the Package Manager) wires
five common operations to keyboard shortcuts. Use this table as a cheat-sheet — every entry
links to the full reference below.

| Sample key | Method | What it does |
|------------|--------|--------------|
| `V` | [`ValidateCurrentStep(success: true)`](#validatecurrentstepbool-success--true--bool) | Force-completes the current procedure step |
| `S` | [`SkipCurrentScenario()`](#skipcurrentscenario--void) | Closes the current scenario, advances to the next |
| `M` | [`LogCustomEvent(id, success: false, weight: 2)`](#logcustomeventstring-eventid-bool-success-float-weight--10f-string-description--null--void) | Records a mistake worth 2 wrong interactions |
| `B` | [`LogCustomEvent(id, success: true, weight: 1)`](#logcustomeventstring-eventid-bool-success-float-weight--10f-string-description--null--void) | Records a successful action |
| `C` | [`CompleteTraining(name)`](#completetrainingstring-trainingname--null--void) | Shows the completion screen + notifies the SaaS |

Plus three read-only helpers used in the sample's context menu:
[`GetCurrentScenarioInfo()`](#getcurrentscenarioinfo--scenarioinfo),
[`GetCurrentScore()`](#getcurrentscore--float),
[`IsTrainingActive()`](#istrainingactive--bool).

---

## Table of contents

- [Methods](#methods)
  - [Flow control](#flow-control)
  - [State queries](#state-queries)
  - [Custom events](#custom-events)
- [Events](#events)
- [Recipes](#recipes)
  - [Validate a step from custom 3D logic](#recipe-1--validate-a-step-from-custom-3d-logic)
  - [Penalise the score on a forbidden action](#recipe-2--penalise-the-score-on-a-forbidden-action)
  - [Drive a custom HUD with score & step events](#recipe-3--drive-a-custom-hud-with-score--step-events)
  - [Skip the package UI for a specific scenario](#recipe-4--skip-the-package-ui-for-a-specific-scenario)

---

## Methods

### Flow control

#### `ValidateCurrentStep(bool success = true) → bool`

Validates the current procedure step from external code. Bypasses the package's UI — no need to click the highlighted object or press the validate button. Returns `true` if a step was validated, `false` if no procedure is active.

```csharp
WiseTwinAPI.ValidateCurrentStep();              // Mark current step as completed
WiseTwinAPI.ValidateCurrentStep(success: false); // Mark current step as failed (still advances)
```

#### `SkipCurrentScenario() → void`

Closes the current scenario's content displayer and advances to the next scenario. The skipped scenario is **not** marked as successfully completed.

```csharp
WiseTwinAPI.SkipCurrentScenario();
```

#### `CompleteTraining(string trainingName = null) → void`

Completes the entire training, fires `OnTrainingCompleted`, and notifies the parent web application via the WebGL bridge in production builds.

```csharp
WiseTwinAPI.CompleteTraining("Safety Training Module 1");
```

#### `RestartTraining() → void`

Fully resets the training and reloads the current scene from scratch — the **same behavior as the red restart button in the HUD, but WITHOUT the confirmation dialog**. All in-memory state (analytics session, scenario progression, UI, player position, control mode) is discarded and re-instantiated fresh by the scene reload. Fires `OnTrainingRestarted` just before the reload.

```csharp
WiseTwinAPI.RestartTraining();
```

> **Note:** because the scene reloads, do not assume the WiseTwin singletons still exist in code running after this call within the same frame.

---

### State queries

#### `GetCurrentScenarioInfo() → ScenarioInfo?`

Returns a snapshot of the currently active scenario, or `null` if no scenario is active.

```csharp
var info = WiseTwinAPI.GetCurrentScenarioInfo();
if (info.HasValue)
{
    Debug.Log($"Scenario {info.Value.Index + 1}/{info.Value.Total}: {info.Value.Id} ({info.Value.Type})");
}
```

`ScenarioInfo` fields:
- `Index` — zero-based index of the scenario
- `Id` — scenario id from metadata (e.g. `"scenario_1"`)
- `Type` — `"question"`, `"procedure"`, `"text"`, or `"dialogue"`
- `Total` — total number of scenarios in the training

#### `GetCurrentScore() → float`

Returns the current cumulative score (0–100) computed from all interactions so far.

```csharp
float score = WiseTwinAPI.GetCurrentScore();
```

#### `IsTrainingActive() → bool`

`true` if a training session is currently running (after the tutorial, before completion).

```csharp
if (WiseTwinAPI.IsTrainingActive()) { /* ... */ }
```

---

### Custom events

#### `LogCustomEvent(string eventId, bool success, float weight = 1.0f, string description = null) → void`

Logs a custom event from external 3D logic. The event is recorded in analytics and participates in the score calculation according to its `weight`.

| Parameter     | Description                                                                                                                |
|---------------|----------------------------------------------------------------------------------------------------------------------------|
| `eventId`     | Unique identifier (e.g. `"forbidden_zone_entered"`, `"helmet_picked_up"`)                                                  |
| `success`     | `true` for a positive action, `false` for a mistake or penalty                                                             |
| `weight`      | Score impact: `1.0` (default) counts as one normal interaction, `3.0` as three. Use `0` to track the event without affecting the score. |
| `description` | Optional human-readable description shown in the SaaS export                                                               |

```csharp
WiseTwinAPI.LogCustomEvent(
    eventId: "forbidden_zone_entered",
    success: false,
    weight: 3.0f,
    description: "Player entered the high-voltage area"
);
```

---

## Events

All events are static and can be subscribed to from anywhere. Remember to unsubscribe in `OnDisable` / `OnDestroy` to avoid leaks across scene reloads.

| Event                                  | Args                          | Fires when                                          |
|----------------------------------------|-------------------------------|-----------------------------------------------------|
| `OnStepValidated`                      | `(int stepIndex, bool success)` | A procedure step is validated (success or fail)    |
| `OnScoreChanged`                       | `(float newScore)`            | The cumulative score changes (0-100)                |
| `OnScenarioStarted`                    | `(int index, ScenarioData scenario)` | A scenario begins                            |
| `OnTrainingCompleted`                  | `()`                          | The training ends (`CompleteTraining` was called)   |
| `OnTrainingRestarted`                  | `()`                          | The training is reset (`RestartTraining` was called), just before the scene reloads |
| `OnCustomEventLogged`                  | `(string eventId, bool success, float weight, string description)` | `LogCustomEvent` was called |

```csharp
void OnEnable()
{
    WiseTwinAPI.OnStepValidated += HandleStepValidated;
    WiseTwinAPI.OnScoreChanged += HandleScoreChanged;
}

void OnDisable()
{
    WiseTwinAPI.OnStepValidated -= HandleStepValidated;
    WiseTwinAPI.OnScoreChanged -= HandleScoreChanged;
}
```

---

## Recipes

### Recipe 1 — Validate a step from custom 3D logic

**Use case:** the procedure step says "place the wrench on the workbench". You want to validate the step automatically when the wrench's transform reaches the workbench, instead of asking the user to click a UI button.

```csharp
using UnityEngine;
using WiseTwin;

public class WrenchPlacementValidator : MonoBehaviour
{
    [SerializeField] Transform workbench;
    [SerializeField] float snapDistance = 0.1f;

    void Update()
    {
        if (Vector3.Distance(transform.position, workbench.position) < snapDistance)
        {
            WiseTwinAPI.ValidateCurrentStep(success: true);
            enabled = false; // only fire once
        }
    }
}
```

In the WiseTwin Editor, set the procedure step's `validationType` to `"manual"` so the package UI doesn't try to validate it itself — your script will do it.

---

### Recipe 2 — Penalise the score on a forbidden action

**Use case:** the player walked into a high-voltage area they shouldn't have. You want to track the mistake and reduce the final score, without interrupting the training.

```csharp
using UnityEngine;
using WiseTwin;

public class ForbiddenZoneTrigger : MonoBehaviour
{
    [SerializeField] string zoneName = "High-voltage area";
    [SerializeField] float scoreWeight = 3.0f; // counts as 3 mistakes

    bool alreadyTriggered = false;

    void OnTriggerEnter(Collider other)
    {
        if (alreadyTriggered) return;
        if (!other.CompareTag("Player")) return;

        WiseTwinAPI.LogCustomEvent(
            eventId: $"forbidden_zone_{name}",
            success: false,
            weight: scoreWeight,
            description: $"Player entered {zoneName}"
        );
        alreadyTriggered = true;
    }
}
```

The event appears in the exported analytics JSON under `interactions[]` with `type: "custom"`, so the SaaS can display a timeline of the player's mistakes.

---

### Recipe 3 — Drive a custom HUD with score & step events

**Use case:** the project has its own HUD (e.g. for branding) and wants to show the score and current scenario in real time.

```csharp
using UnityEngine;
using TMPro;
using WiseTwin;

public class CustomHUD : MonoBehaviour
{
    [SerializeField] TextMeshProUGUI scoreLabel;
    [SerializeField] TextMeshProUGUI stepLabel;

    void OnEnable()
    {
        WiseTwinAPI.OnScoreChanged += UpdateScore;
        WiseTwinAPI.OnStepValidated += UpdateStep;
        WiseTwinAPI.OnScenarioStarted += (idx, scenario) => UpdateStep(0, true);
    }

    void OnDisable()
    {
        WiseTwinAPI.OnScoreChanged -= UpdateScore;
        WiseTwinAPI.OnStepValidated -= UpdateStep;
    }

    void UpdateScore(float newScore) => scoreLabel.text = $"Score: {newScore:F0}%";

    void UpdateStep(int stepIndex, bool success)
    {
        var info = WiseTwinAPI.GetCurrentScenarioInfo();
        if (info.HasValue) stepLabel.text = $"Scenario {info.Value.Index + 1}/{info.Value.Total}";
    }
}
```

---

### Recipe 4 — Skip the package UI for a specific scenario

**Use case:** one of the scenarios is a free-form 3D simulation that doesn't fit the package's question/procedure/dialogue model. You want the package to track time and score, but to handle the interaction yourself.

In the metadata, define the scenario as `text` with an empty body (or a one-line briefing). In your scene, add a script that:

1. Listens for `OnScenarioStarted`.
2. When the scenario of interest starts, immediately calls `SkipCurrentScenario()` to dismiss the package's text panel (or shows your own UI on top).
3. When your custom logic finishes, calls `LogCustomEvent` to record the outcome and `SkipCurrentScenario()` to advance.

```csharp
using UnityEngine;
using WiseTwin;

public class CustomScenarioHandler : MonoBehaviour
{
    [SerializeField] string customScenarioId = "scenario_freeplay";

    void OnEnable() => WiseTwinAPI.OnScenarioStarted += HandleScenarioStarted;
    void OnDisable() => WiseTwinAPI.OnScenarioStarted -= HandleScenarioStarted;

    void HandleScenarioStarted(int index, ScenarioData scenario)
    {
        if (scenario.id != customScenarioId) return;

        WiseTwinAPI.SkipCurrentScenario();   // dismiss the package UI
        StartCoroutine(RunCustomScenario());  // your own logic
    }

    System.Collections.IEnumerator RunCustomScenario()
    {
        // ... your gameplay ...
        yield return new WaitForSeconds(30);
        WiseTwinAPI.LogCustomEvent("freeplay_completed", success: true, weight: 5);
    }
}
```

---

## Debugging — Score Monitor

When developing a training, it's useful to see the score change in real time and understand which actions affected it. Drop a `ScoreDebugMonitor` component on any GameObject (typically a child of `WiseTwinSystem`) to get:

- A small overlay in a screen corner showing the live score (color-coded: green / yellow / red)
- A rolling list of the last operations (interactions completed, procedure steps, custom events, scenario transitions)
- Mirror logs to the Unity console with a `[ScoreDebug]` prefix

Toggle the component checkbox in the Inspector to enable/disable it live during Play Mode.

**Quick add:** menu shortcut `WiseTwin > Debug > Add Score Monitor to Scene` creates the GameObject automatically (parented to `WiseTwinSystem` if found).

**Inspector options:**

| Field | Description |
|-------|-------------|
| `Show Overlay` | Toggle the on-screen UI without disabling the component |
| `Overlay Corner` | Top-left, top-right, bottom-left, bottom-right |
| `Max Log Entries` | How many recent operations to display (3–20) |
| `Log To Console` | Mirror each operation to the Unity console |
| `Log Prefix` | Prefix for console logs (default `[ScoreDebug]`) |

The monitor only listens to `WiseTwinAPI` events — it never modifies the score. It's safe to leave enabled in development builds and disabled in production.

---

## See also

- A working sample script is available under **Samples~/CustomScripting/CustomTrainingExample.cs** — import it from the Package Manager.
- Internal architecture is documented in `CLAUDE.md` at the package root.
