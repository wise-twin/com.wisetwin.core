# Changelog

All notable changes to the WiseTwin Core Package will be documented in this file.

## [1.8.1] - 2026-06-26

### Added
- **Web Build Optimizer** (`Editor/WebBuildOptimizer.cs`, menu `WiseTwin → Optimisation → Optimiseur Web Build`) — Editor window to shrink the WebGL `.data` download (the main blocker on weak connections). Sets a WebGL platform override on textures (max size + crunch — never upscales, source untouched) and applies Mesh Compression to models. Dry-run analysis before applying; only touches `Assets/` (never package assets); always skips lightmaps, sprites, cookies and cursors.

## [1.8.0] - 2026-06-03

### Added
- **Illustration images for scenarios (text, QCM, procedure)** — Optional image per text scenario (top of the bubble), per question (under the prompt, single and multi-question flows) and per procedure step. Click to zoom full-screen. Images are configured by dragging any imported `Texture2D` in the WiseTwin Editor (Scenario Configuration tab).
- **`WiseTwinImage` runtime helper** (`Runtime/UI/WiseTwinImage.cs`) — shared `Load(path)` + `CreateThumbnail(texture)` (clickable, zoom overlay) used by the Text, Question and Procedure displayers.

### Changed
- **Images are now embedded in the build via Resources** — On *Generate Metadata*, each referenced image is copied into `Assets/WiseTwin/Resources/ScenarioImages/` and the JSON stores a Resources-relative path (`"imagePath": "ScenarioImages/foo"`). Loaded at runtime with `Resources.Load`, so it works offline and on WebGL.

### Fixed
- **Scenario images never loaded at runtime** — The editor previously stored an `Assets/...` project path (via `AssetDatabase.GetAssetPath`) that the runtime loader could not resolve, so procedure-step images silently failed (the "images don't save" report). The new copy-to-Resources pipeline fixes this end to end.
- **Image field appeared empty after reopening the editor** — The `Texture2D` reference is now re-hydrated from the stored Resources path on load.
- **Procedure step image was locked to Click/Group steps** — Now available for every validation type.

## [1.7.0] - 2026-06-03

### Added
- **Selectable player control modes (build option)** — Two toggles in the WiseTwin Editor *General Settings* tab, `Allow Keyboard (WASD)` and `Allow Mouse (click-to-move)`, baked into the `WiseTwinManager` scene component. The onboarding tutorial (welcome panel + instructions) **always shows**; only the control-mode *choice* adapts:
  - **Both enabled** → the player chooses keyboard or mouse on the controls step (unchanged default behavior).
  - **Exactly one enabled** → no choice; the controls step shows that single mode pre-selected and applies it automatically.
  - **Neither enabled** → the controls step is hidden and WiseTwin applies no controller; the host Unity project owns the player controller.
  - Exposed at runtime via `WiseTwinManager.AllowKeyboardControl` / `AllowMouseControl`; `TutorialUI.Configure(keyboard, mouse)` drives the adaptive panel. Stored internally as inverted "disable" flags (default false) so existing scenes/prefabs keep both modes enabled with no migration.

## [1.6.0] - 2026-06-03

### Added
- **`WiseTwinAPI.RestartTraining()`** — Public API method that fully resets the training and reloads the current scene, identical to the red restart button in the HUD but **without the confirmation dialog**. Discards all in-memory state (analytics, progression, UI, player position, control mode). New `OnTrainingRestarted` event fires just before the scene reloads.
- **`WiseTwinManager.RestartTraining()`** — Underlying reset logic, now the single source of truth. `TrainingHUD.ConfirmRestart()` (the red button's confirm action) delegates to it instead of duplicating the destroy-singletons-and-reload sequence.

## [1.5.0] - 2026-05-06

### Added
- **Public API (`WiseTwinAPI`)** — Static facade exposing 7 methods (`ValidateCurrentStep`, `SkipCurrentScenario`, `CompleteTraining`, `GetCurrentScenarioInfo`, `GetCurrentScore`, `IsTrainingActive`, `LogCustomEvent`) and 5 events (`OnStepValidated`, `OnScoreChanged`, `OnScenarioStarted`, `OnTrainingCompleted`, `OnCustomEventLogged`) for hybrid trainings where external scripts need to drive flow alongside the package's UI. Internal managers may change between versions; this façade is preserved.
- **Sample script** — `Samples~/CustomScripting/CustomTrainingExample.cs`, importable via Package Manager. Demonstrates every API method with keyboard shortcuts (V/S/M/B/C) and event subscriptions.
- **Score Debug Monitor** — `ScoreDebugMonitor` MonoBehaviour with live on-screen overlay (color-coded score, rolling event log) and console mirroring. Toggle via inspector checkbox or menu `WiseTwin > Debug > Add Score Monitor to Scene`.
- **Group validation type for procedures** — `validationType: "group"` lets a step require multiple objects to be clicked in any order. Step advances when all are touched. Editor UI exposes a list of target GameObjects.
- **`WiseTwinIcons` factory** — VisualElement and Texture2D-based icon shapes (`PlayTriangle`, `ArrowRight`/`ArrowLeft`, `Check`, `Cross`, `CloseX`, `Reset`, `Warning`, `Chevron`, `Bullet`) plus `UIStyles.SetButtonIcon` helper.
- **Custom event support in analytics** — `TrainingAnalytics.LogCustomEvent(eventId, success, weight, description)` records score-affecting events from external 3D logic. Counts in `CalculateScore()` proportionally to weight.
- **`API.md` and `README.md`** — Public API reference (with quick reference + 4 recipes) and package overview.

### Changed
- **`CompleteTraining` shows the completion UI** — Calling `WiseTwinManager.CompleteTraining` (and therefore `WiseTwinAPI.CompleteTraining`) now displays the score screen and exports analytics, just like the end-of-progression flow. Falls back to a direct WebGL notification if no panel settings are available in the scene.
- **Procedure right-side panel auto-sizes to content** — Short instructions like "Turn the valve" produce a compact card instead of a floor-to-ceiling sidebar. Long instructions still scroll inside a max-85%-screen-height panel.
- **`ProcedureDisplayer` validation paths unified** — All three paths (click / zone / manual) now funnel through a single `ValidateCurrentStep(success)` method, ensuring consistent analytics handling and enabling external API control. Renamed `ValidateCurrentStep(GameObject)` → `OnObjectClicked(GameObject)` and `ValidateZoneStep()` → `OnZoneEntered()` (internal-only callers updated).
- **Reset confirmation dialog** — Now shows "Restart Training?" + "Your progress will be lost." text alongside the warning icon. Intentional exception to the mono-language icon-based design for destructive actions where icons alone were ambiguous.
- **`TrainingHUD` only auto-shows when scenarios exist** — API-only trainings (no scenarios in metadata) no longer display the top progress bar.
- **`TrainingAnalytics` auto-creation moved to `WiseTwinManager`** — Single point of responsibility instead of being created lazily by `ContentDisplayManager`.
- **Score-impact events now fire `OnCustomEventLogged` and `OnScoreChanged`** — Used by `ScoreDebugMonitor` and any subscribed external HUD.

### Fixed
- **WebGL: missing icons across the entire UI** — Replaced every Unicode glyph used as a visible label (▶ ⚠ ↻ ✕ ✓ ✗ → ← › • plus the 🎉 emoji) with drawn `WiseTwinIcons` shapes. The bundled font in WebGL builds was missing these glyphs, producing empty boxes in production while rendering fine in the Editor (which falls back to system fonts).
- **Dialogue editor: duplicates spawning on every reload** — `LoadDialogueDataFromJSON` was generating fresh `dialogue_N` IDs every time the metadata was parsed and adding them to the library, causing linear growth per session. Now embeds `dialogueId` in the metadata JSON for stable cross-reload identity, with a slug fallback derived from the title for older metadata files. Added a one-shot deduplication of existing duplicates by `graphDataJSON` content on load.
- **Dialogue editor: edits not visible at runtime** — Library entries (the Dialogue tab) are now the source of truth at metadata generation. The per-scenario inline copy is only used as a fallback when no library match exists.
- **Dialogue editor: editor positions lost on reload** — Reload no longer overwrites the editor-format `graphDataJSON` with the runtime format from `metadata.json`.
- **Tutorial: WASD letters off-center in their boxes** — Labels now use absolute fill positioning with explicit `unityTextAlign = MiddleCenter` and zero padding/margin.
- **Tutorial: mouse silhouette proportions** — Divider and scroll wheel positions now derive from `bodyW`/`bodyH` constants for guaranteed symmetry.
- **Sample crash on Input System-only projects** — `CustomTrainingExample` uses `UnityEngine.InputSystem.Keyboard` instead of legacy `UnityEngine.Input` (which throws at runtime when Active Input Handling is set to Input System Package only).
- **`.gitignore`: `*~` pattern was matching `Samples~/`** — Replaced with a more specific pattern so the sample folder is properly tracked.

## [1.4.0] - 2026-03-05

### Added
- **Mouse-only control mode** - Users can choose between keyboard+mouse or mouse-only (click-to-move) navigation during the tutorial
  - `ControlModeSettings.cs` - Static manager for control mode preference, persisted via PlayerPrefs, applies NavMeshAgent/CapsuleCollider/Rigidbody setup at runtime
  - `ClickToMoveCharacter.cs` - NavMeshAgent-based click-to-move controller with ground detection, click indicator, body rotation, and animator integration
  - `ControlMode` enum (`KeyboardMouse`, `MouseOnly`) for mode selection
- **PlayerControls utility** - Centralized static class to enable/disable all player controls (both FPC and ClickToMoveCharacter) from any UI component
- **Camera-only mode for FPC** - `FirstPersonCharacter.cameraOnly` flag keeps camera (orbit, zoom, collision, head tracking) active while disabling movement and body rotation, ensuring identical camera behavior in both control modes

### Changed
- **TrainingHUD** - Removed "?" help button; uses `PlayerControls.SetEnabled()` for centralized control blocking; `ConfirmRestart()` resets `ControlModeSettings` and destroys `TutorialUI` to prevent DontDestroyOnLoad persistence bugs
- **TutorialUI** - Simplified tutorial text; control mode selection cards streamlined (icon + title only); fixed cards not responding after restart; uses `PlayerControls.SetEnabled()`
- **LanguageSelectionUI** - Uses `PlayerControls.SetEnabled()` and calls `ControlModeSettings.ApplyToPlayer()` on start
- **ProcedureZoneTrigger** - Now detects both `CharacterController` and `NavMeshAgent` for player identification; added `OnTriggerStay` alongside `OnTriggerEnter` for reliable NavMeshAgent detection
- **FirstPersonCharacter** - Scroll/zoom blocked when controls are disabled (fixes zoom through UI); camera pivot smoothed in cameraOnly mode to reduce jitter; animator guarded against missing RuntimeAnimatorController
- **All UI displayers** (`QuestionDisplayer`, `DialogueDisplayer`, `VideoDisplayer`, `TrainingCompletionUI`, `ScenarioTransitionPanel`) - Replaced direct `FirstPersonCharacter.SetControlsEnabled()` calls with `PlayerControls.SetEnabled()`

### Fixed
- Zoom/scroll passing through UI panels when controls should be blocked
- Tutorial control mode cards not responding after training restart (DontDestroyOnLoad persistence)
- Zone validation not working in mouse-only mode (missing Rigidbody for OnTriggerEnter/Stay)
- Camera jitter in mouse-only mode (smoothed pivot follow)
- Animator errors at startup when no RuntimeAnimatorController is assigned

## [1.3.0] - 2026-02-19

### Added
- **Zone Collect Effect** - Visual feedback when the player enters a validation zone during a procedure
  - `ZoneCollectEffect.cs` plays a 3-phase animation: white flash (0.15s) → cubic ease-in implosion to zero scale (0.35s) → deactivation
  - Affects all child Renderers (emission + base color) and ParticleSystems (startColor)
  - Static `ZoneCollectEffect.Play(GameObject)` convenience method
  - Zone auto-reactivates when the procedure step is replayed (reset support)
- **Scenario Transition Panel** - Prominent centered UI panel shown between scenarios
  - `ScenarioTransitionPanel.cs` singleton with full-screen dark backdrop and centered panel (500px, accent border)
  - Shows completed scenario count ("Scenario X/Y Complete") and "Continue" button
  - Fade in/out animations (0.3s)
  - Blocks player controls while visible via `FirstPersonCharacter.SetControlsEnabled()`
  - Full EN/FR localization
- **TrainingHUD.SetNextButtonVisible()** - New method to show/hide the next scenario button programmatically

### Changed
- `ProcedureZoneTrigger.cs` - Triggers `ZoneCollectEffect.Play()` after zone step validation
- `ProcedureDisplayer.cs` - Cleans up `ZoneCollectEffect` components in `CleanupZoneTriggers()`; re-enables and resets zone scale in `StartCurrentStep()` if deactivated by a previous collect effect
- `ProgressionManager.cs` - Creates `ScenarioTransitionPanel` between scenarios instead of relying on the small HUD ">" button; hides HUD next button during transitions; existing start flow (language → tutorial) unchanged
- `TrainingHUD.cs` - Added `SetNextButtonVisible(bool)` that hides/shows button and stops pulse animation

## [1.2.0] - 2026-02-19

### Added
- **Dialogue System** - New `dialogue` scenario type for interactive branching conversations with NPCs
  - Visual node graph editor (`WiseTwin > Dialogue Graph Editor`) using Unity's GraphView API
  - 4 node types: Start, Dialogue, Choice, End - connected visually with drag-and-drop edges
  - Inline editing of EN/FR text fields directly on nodes
  - Dynamic choice management: add/remove options, toggle correct/incorrect per choice
  - Toolbar with colored node creation buttons and save functionality
  - Canvas with zoom, pan (middle mouse + right mouse drag), grid background, and minimap
  - Auto-import of runtime JSON format back into editor graph with auto-layout
- **DialogueDisplayer** - Runtime UI for dialogues with chat-bubble style
  - Modal overlay with speaker name, dialogue text, Continue button, and choice buttons
  - **Context display**: previous NPC dialogue shown above choices in a quote-style box
  - **Evaluated choices**: green/red visual feedback (800ms) when choices have `isCorrect` flags
  - **Neutral choices**: blue highlight (300ms) when no choice is marked correct - no judgment
  - Supports loops (NPC can redirect back to previous choice nodes)
  - Language change support during active dialogue
  - Player controls blocked during dialogue (same pattern as QuestionDisplayer)
- **Dialogue Analytics** - `DialogueInteractionData` and `DialogueChoiceRecord` classes
  - Tracks every choice made with `choiceNodeId`, `selectedChoiceId`, `wasCorrect`, `timestamp`
  - Computes `correctChoices`, `incorrectChoices`, `finalScore`, `completedDialogue`
- **Dialogue Editor Tab** - New "Dialogue" tab in WiseTwin Editor window
  - Create, edit, delete dialogue configurations
  - "Open Graph Editor" button per dialogue
  - Dialogue linking in Scenario Configuration tab via dropdown
- **Custom Vector2 JSON Converter** - Prevents Newtonsoft.Json self-referencing loop on Unity's Vector2

### Changed
- `ScenarioConfigurationData.cs` - Added `Dialogue` to `ScenarioType` enum, added `DialogueScenarioData` class
- `ContentTypes.cs` - Added `Dialogue` to `ContentType` enum
- `ScenarioData.cs` - Added `dialogue` JObject field and case in `GetContentData()`
- `ContentDisplayManager.cs` - Registers `DialogueDisplayer`, handles `"dialogue"` scenario type
- `InteractionData.cs` - Added `DialogueInteractionData` and `DialogueChoiceRecord`
- `WiseTwinEditor.cs` - Added Dialogue tab (now 5 tabs), JSON round-trip for dialogue data
- `WiseTwinEditorData.cs` - Added `dialogues` list and `selectedDialogueIndex`
- `WiseTwinEditorScenariosTab.cs` - Added dialogue scenario editor with dropdown and graph editor button
- `TrainingCompletionUI.cs` - Added try-catch around PanelSettings assignment to prevent AssertionException

## [1.1.0] - 2026-02-18

### Added
- **Zone Trigger Validation** - New procedure step validation type where the player walks into a trigger zone to validate the step (in addition to existing Click and Manual types)
  - `ProcedureZoneTrigger.cs` component detects player entry via `CharacterController` + `OnTriggerEnter`
  - `ValidationType` enum (`Click`, `Manual`, `Zone`) replaces the old `requireManualValidation` boolean
  - `zoneObjectName` field on procedure steps to reference zone GameObjects in the scene
- **Validation Zone Prefab Creator** - Editor menu `WiseTwin > Create Validation Zone Prefab` generates a ready-to-use zone prefab with:
  - SphereCollider (isTrigger)
  - Transparent green ground disc
  - Glowing green ring (LineRenderer) on the perimeter
  - Upward particle effect from the circle edge
- **Package CLAUDE.md** - Comprehensive documentation for AI-assisted development, covering architecture, all components, JSON format, public API, and conventions

### Changed
- `ProcedureDisplayer.cs` - Refactored step validation logic from if/else to switch-based dispatch supporting click/manual/zone types
- `ScenarioConfigurationData.cs` - Replaced `requireManualValidation` bool with `ValidationType` enum + zone fields
- `WiseTwinEditorScenariosTab.cs` - Replaced manual validation toggle with `ValidationType` dropdown, shows zone object field when Zone is selected
- `WiseTwinEditor.cs` - JSON export uses `validationType` string instead of `requireManualValidation` boolean

### Backward Compatibility
- Old JSON metadata with `"requireManualValidation": true` is automatically converted to `"validationType": "manual"` on import
- Missing `validationType` field defaults to `"click"`
- Runtime `ProcedureStep.requireManualValidation` kept as computed read-only property for code compatibility

## [1.0.0] - Initial Release

### Features
- Scenario-based training system (Question, Procedure, Text)
- Multi-language support (EN/FR) with LocalizationManager
- Click and Manual validation for procedure steps
- Fake objects system for procedure steps
- Step images with zoom overlay
- Video trigger system (click 3D object to play video)
- Training analytics tracking and JSON export
- Azure API / local StreamingAssets metadata loading
- WebGL integration for web-based training
- WiseTwin Editor window for visual configuration
- Training HUD with timer and progress bar
