# AudioNest implementation audit

## 2026-11-16 — MVP foundation

- Read the supplied `AudioNest Technical Doc.txt`; repository was empty and clean.
- Preserve the four architectural layers: hardware abstraction, audio engine,
  project/domain, and Avalonia presentation. Split adapters/services into separate
  assemblies so external frameworks cannot leak into domain types.
- Target C# / .NET 10 (installed LTS SDK). Use Avalonia 11.3.23 and SoundFlow 1.4.2.
- SoundFlow uses MiniAudio for cross-platform hardware and decoding. Its upstream
  maintainer reports a hiatus through February 2027; isolate it behind interfaces.
- MVP scope: multitrack waveforms, non-destructive split/delete/move, undo/redo,
  playback/seek, microphone capture, portable project persistence, basic gain and
  hard limiting, and WAV mixdown.
- Advanced DSP, independent pitch/tempo, VST hosting, and recording-time effects
  are extension work, not represented as working features.
- Use source-generated JSON and explicit DI registration to avoid introducing
  reflection requirements in AudioNest code. Native AOT remains a verification
  target, not a guarantee about third-party libraries.

Further implementation and verification entries are appended below.

### Core modules

- Added solution/build configuration, central package versions, SDK selection,
  editor settings, and build-output ignores.
- Added immutable project/asset/track/clip/effect records with project-frame timing,
  bounds/reference/version validation, pure editing operations, and bounded
  metadata-only undo/redo (200 edits).
- Added backend-neutral device/PCM/callback contracts and a lazy SoundFlow adapter
  for decoding, playback, device enumeration, and WAV recording.
- Added shared decoded buffers, cached min/max waveform peaks, prepared multitrack
  render plans, ordered clip/track gain and hard-limiter processing, mute/solo,
  a frame-driven transport, and streaming atomic PCM16 WAV export.
- Added source-generated, versioned JSON persistence. Saves copy content-addressed
  original media into a sibling `.media` directory before replacing metadata;
  loads reject unsupported versions, invalid references, escaping paths, and
  missing media. Move the project file and its media directory together.
- MVP tradeoffs: decoded audio lives in RAM, individual imports are limited to ten
  minutes, effects are stateless, and the render loop is deliberately simple.
  SoundFlow's stock recorder performs encoding in its callback; a bounded
  capture-to-worker queue is required before claiming hard real-time recording.

### Desktop integration

- Added an explicitly registered DI composition root, dark Avalonia shell,
  transport/import/project/export controls, keyboard editing shortcuts, clip/track
  gain and limiter controls, track mute/solo, and undo/redo.
- Added waveform timeline selection, playhead seeking, zoom, drag-to-move across
  tracks, and keyboard-accessible clip nudging. A separate playhead overlay polls
  the engine clock; clock ticks do not rebuild waveform geometry.
- Added mono default-microphone recording (no live monitoring), stop/finalize/import,
  UI busy/recording guards, user-visible operation errors, staged project loading,
  and unsaved-change confirmation before new/open/close.
- Imports are copied to application-local working media; project saves additionally
  copy them into portable project media. Working media is intentionally retained
  after closing/crashes, pending a future recovery/cleanup workflow.
- Initial Release build succeeded. Suppressed only Avalonia AVLN3001 because
  MainWindow is deliberately constructor-injected, not runtime-XAML-activated.
  Follow-up checks and tests are recorded below.

### Extension seams and automated coverage

- Replaced sample-by-sample effect dispatch with prepared block processors and
  `IAudioProcessor` / `IAudioEffectFactory` contracts. Scratch buffers are allocated
  during graph preparation, not during rendering. Seek/stop resets DSP state.
  Advanced effects still need tail/latency handling before being exposed.
- Added domain/history, mixing/effects, waveform, transport, export/cancellation,
  portable persistence/path validation, native SoundFlow decode/resampling, and
  Avalonia headless workflow tests. The initial suite passed all 22 cases.
- Tests use fake devices; no microphone was captured and no sound was played
  through the user's hardware. Native decode was exercised with generated WAV.
- Added an injectable working-media directory so tests do not leave fake recordings
  in the application's normal media cache.
- Started a Windows Native AOT publish check; its result is recorded below.

### Native smoke-test correction

- Windows x64 Native AOT compilation succeeded without build warnings.
- Launching the native executable exposed a window-close reentrancy bug: a clean
  project's completed confirmation task tried to close from inside its own Closing
  callback. Deferred confirmation/shutdown onto the UI dispatcher and added a
  headless assertion that the clean window actually closes.
- Added a managed-allocation regression test for the prepared block render path.
  Native publishing and smoke testing are repeated after these corrections.

### Documentation and CI

- Added README with run/build/test/publish commands, keyboard workflows, portable
  project instructions, source-media safety, and explicit MVP limitations.
- Added architecture/dependency/threading/extension guidance, a requirement-by-
  requirement roadmap, and manual hardware/platform acceptance instructions.
- Added Windows/Linux/macOS build/test CI and a Windows Native AOT publish job.
  These definitions have not yet run on GitHub; non-Windows support is a target,
  not a claim of local execution.
- Documented import/recording length limits, retained working media, stateless
  built-in effects, default-device-only UI, and deferred VST/advanced DSP features.
- Final review added null-entry validation for malformed project data and applied
  the whitespace adjustments reported by `dotnet format --verify-no-changes`.
- Enabled real Skia rendering in the headless desktop test and added a captured-
  frame assertion, rather than relying only on view-model and window construction.

### Final verification

- `dotnet test AudioNest.sln --configuration Release`: **23 passed, 0 failed,
  0 skipped**, including the native codec test and Skia-rendered desktop workflow.
  Release compilation completed without warnings or errors.
- `dotnet format AudioNest.sln --verify-no-changes --no-restore`: passed.
- `git diff --check`: passed. All project source files are new/untracked in the
  initially empty repository; no commits were created and no user work was removed.
- Repeated `dotnet publish src/AudioNest.Desktop/AudioNest.Desktop.csproj
  --configuration Release --runtime win-x64 -p:PublishAot=true
  --output artifacts/aot/win-x64` against the final code: succeeded without
  warnings. Generated build/publish artifacts are ignored by Git.
- Started the final native executable, waited for UI input-idle, requested normal
  window close, and observed **exit code 0**. The smoke-test process was closed.
- Not verified locally: physical microphone/speaker I/O, unplug/dropout handling,
  macOS/Linux execution, non-Windows AOT, long-session stress, or advanced features
  explicitly deferred in `docs/ROADMAP.md`. No physical capture/playback was started.

## 2026-11-16 — UI TODO implementation

- Read all nine items in root `TODO.txt`. Preserve the pre-existing user change to
  `.gitignore`; do not modify the TODO source document.
- Add neutral light/dark presentation, sharper corners, responsive/pinned timeline
  layout, shared in-window/native menus, explicit transport controls, per-track
  controls, magnetic snapping, ruler-only cursor gestures, and a compact clock bar.
- Added undoable track deletion and persisted stereo balance (`Pan`, default zero,
  additive/backward-compatible with version-1 projects). Rendering/export applies
  balance after track effects, preserving unity gain at the center.
- Added frame-based clip-start/clip-end snapping to the playhead and other clips.
  Snapping remains a presentation preference, not persisted project metadata.
- Added explicit play/pause/start/end transport actions, microsecond display plus
  exact frame count, viewport-based sizing, and stable per-track presentation state.
- Cursor seeking now permits silent timeline space for placing future imports/
  recordings; seeking past the end during playback pauses there instead of restarting.
- Replaced the green/dark shell with neutral light resources and optional dark
  resources; common button corners are now 2 px.
- Split clip canvas, pinned time ruler, real track-header controls, and playhead
  overlay. Shared row metrics and viewport sizing replace fixed minimum widths.
  A fixed horizontal scrollbar keeps long sessions navigable at any vertical offset.
- Added clip-edge snap previews/guides with an 8-pixel zoom-independent tolerance
  and Alt bypass. Only ruler pointer gestures seek; clip/header clicks do not.
- Added File/Edit/Effects/Tools/Help from shared commands, with macOS native menu
  export and in-window fallback, plus help dialogs and live enabled/check states.
- Added pointer-driven headless tests for ruler scrubbing and clip dragging,
  actual track-button interaction, deletion/undo/save/open, theme/menu state,
  resizing/scroll alignment, snap preferences/bypass, transport/status precision,
  pure snap selection, and stereo-balance rendering.
- The first Release build of the redesigned UI completed with zero warnings/errors.
- All 34 initial regression cases passed. Inspected light/dark Skia captures,
  corrected a horizontally scrolled empty-state label, and added populated editor
  captures plus further actual snap/Alt-drag and pinned vertical-layout checks.
- Updated README, architecture, and testing guidance for menus, themes, controls,
  snapping, ruler-only seeking, stereo-balance semantics, and responsive layout.
- Final populated light/dark review corrected selected-header style precedence and
  ensured alternate track backgrounds do not hide the time grid. Screenshot
  artifacts are generated under ignored test build output, not project source.

### TODO completion and verification

1. Neutral white/light UI; optional dark mode in Tools.
2. Sharper rounded corners (2 px).
3. Viewport-responsive timeline, pinned headers/ruler, fixed horizontal navigation,
   fit-to-project, and a compact-window inspector policy.
4. Shared File/Edit/Effects/Tools/Help commands with in-window menus and macOS
   native menu export/fallback.
5. Toolbar with distinct play, pause, stop, backward/start, forward/end, and record.
6. Real per-track mute, gain, L/C/R stereo-balance, and undoable delete controls.
7. Toggleable edge-to-playhead/edge-to-edge snapping with preview and Alt bypass.
8. Pointer seeking restricted to the time ruler, including captured drag gestures.
9. Compact live status bar with six-decimal time and exact frame index.

- Final `dotnet test AudioNest.sln --configuration Release`: **35 passed,
  0 failed, 0 skipped**; compilation produced no warnings/errors.
- `dotnet format AudioNest.sln --verify-no-changes --no-restore`: passed.
- `git diff --check`: passed (Git emits existing LF/CRLF conversion notices).
- Windows x64 Native AOT publish succeeded against the final UI code, without
  warnings. One concurrent test/publish restore collided on the shared assets
  file (`NETSDK1047`); rerunning publish in isolation with a forced restore passed.
  No source/configuration workaround was needed.
- Final native executable started, accepted a normal close request, and exited
  with code 0. No physical microphone/speaker I/O was started.
- Light/dark populated UI captures were inspected. Headless pointer tests also
  verify ruler scrubbing, preserving the playhead during clip dragging, actual
  snapping/Alt bypass, real track-button activation, resizing, and scroll alignment.
- macOS native menu appearance/export and non-Windows execution remain manual
  platform checks; native-menu code is implemented but not claimed as Mac-tested.
- Kept `TODO.txt` unchanged, preserved the user's pre-existing `.gitignore` change,
  and created no commits.

## 2026-11-17 — UI refinement TODO

- Read the new eleven-point TODO list. User clarified that startup should be
  **maximized**, not literal fullscreen; retain normal window decorations.
- Plan: compact menus, icon-only grouped transport, one unified toolbar with
  playhead-centered zoom and live output/recording meters, volume/pan sliders,
  left-Alt wheel zoom, left-Shift ruler snapping, selection-aware status, and a
  resizable inspector with separate metadata/effects presentation.
- Added callback-safe peak metering with atomic UI handoff, output measurements
  after timeline rendering, and input measurements subscribed only during actual
  recording. No startup microphone monitoring or sample retention is introduced.
- Added frame-based ruler boundary snapping, selection status, meter presentation,
  and an isolated inspector view model/effect-row structure for future editors.
- Added volume (0–2×, default 1×) and pan sliders. Pointer drags preview locally and
  commit once on release/capture loss, so a slider gesture creates one undo step;
  keyboard adjustments commit normally. Track deletion now precedes the name.
- Removed the separate timeline tool strip. Added a compact 24-px menu row,
  square icon-only transport (green play/red record), faint group dividers, editing
  controls in the same toolbar, and right-aligned output/input peak meters.
- Added maximized startup (normal OS title bar), playhead-anchored toolbar/menu/
  left-Alt-wheel zoom, side-specific modifier tracking/reset, and left-Shift ruler
  snapping to nearby clip boundaries on all tracks.
- Split the inspector into reusable metadata, effect-list, and action sections,
  with a constrained draggable divider and remembered session-local width.
- Added regression coverage for startup state, toolbar grouping/icons, actual
  slider pointer gestures and undo, modifier-specific zoom/snapping, selected clip
  status, callback-driven meters, and actual inspector-divider dragging.
- Initial run passed 41/42 cases; corrected Fit to include an out-of-project cursor
  in the visible extent. Visual review scoped header styling away from slider
  template borders and removed duplicated inspector metadata.
- All 42 regression cases now pass, including pointer capture-loss commit. Track
  gain menu actions now share the slider's 2× editing limit; higher legacy gains
  remain preserved and truthfully labeled, with an explanatory slider tooltip.
- Updated usage, threading/extension guidance, and manual acceptance checks for
  the unified toolbar, peak-meter privacy/semantics, slider gestures, side-specific
  modifiers, maximized startup, selection status, and resizable inspector.
- Windows x64 Native AOT publishing succeeded. A first immediate input-idle/close
  smoke attempt timed out; rerunning with native-window title/close diagnostics
  observed the AudioNest window, accepted normal close, and exited with code 0.
  The smoke-test processes were cleaned up; no physical audio I/O was started.
- Strengthened ordinary-wheel coverage to assert actual navigation, not only an
  unchanged zoom. This exposed that hidden synchronized scroll viewers consumed
  the wheel without moving; added explicit track-vertical/time-horizontal routing.
  Left-Alt zoom still consumes only its intended timeline gesture.

### Completion and final verification

1. Maximized startup, not fullscreen, as clarified by the user.
2. Thinner 24-px in-window menu bar; native macOS menu support retained.
3. Removed the separate timeline strip; toolbar magnifiers zoom around the playhead.
4. Faint toolbar separators and 42×42 transport buttons.
5. Icon-only transport with green play and red record; tooltips/accessibility names retained.
6. Track volume/pan sliders, 0–2× volume with default 1×, and delete before track name.
7. Left transport, middle editing controls, right speaker/microphone peak meters.
8. Left-Alt wheel zoom; ordinary wheel actually pans time or scrolls tracks.
9. Left-Shift ruler seeking snaps to nearby start/end boundaries on any track.
10. Selected track and clip names in the left status area, without hiding errors/recording state.
11. Draggable inspector divider with reusable metadata/effect-list/action view models.

- `dotnet test AudioNest.sln --configuration Release`: **42 passed, 0 failed,
  0 skipped**, including real pointer/keyboard gestures, ordinary horizontal and
  vertical wheel navigation, callback peak handoff, allocation checks, and input
  meter inactivity outside recording. Final build produced no warnings/errors.
- `dotnet format AudioNest.sln --verify-no-changes --no-restore`: passed.
- `git diff --check`: passed; only the repository's existing LF/CRLF notices appeared.
- Repeated Windows x64 Native AOT publish after the final navigation correction:
  succeeded without warnings. Final native startup/normal-close smoke test exited
  with **code 0** and left no test process running.
- Inspected populated light/dark Skia captures. Generated screenshots/build outputs
  remain under ignored output directories.
- Physical microphone/speaker metering and non-Windows behavior still require
  manual hardware/platform acceptance. No physical recording/playback was started.
- Kept `TODO.txt` unchanged, made no commits, and preserved unrelated files.

## 2026-11-17 — Timeline editing and behavior TODO

- Read the new thirteen-point TODO list; retain all existing uncommitted work.
- Add source-bounded non-destructive trimming, selection/razor tools, a session clip
  clipboard, contextual editing/track menus, and persisted mono/stereo track layout.
- Add grouped menu separators, softer gray light-mode timeline, adjacent skip
  buttons, uniform toolbar sizing, a resizable meter section, and pointer-anchored
  wheel zoom while retaining playhead-anchored toolbar zoom.
- Added pure trim/insert/clipboard metadata operations. Expanding a clip reveals its
  original source region; it does not stretch, repeat, or overwrite source samples.
- Added a stereo-default track layout field and mono downmix in the shared render
  graph, so mono is an audio behavior rather than only a label.
- Clipboard snapshots preserve offsets/effects and retain shared decoded PCM across
  project changes. Paste uses fresh clip IDs and safe asset identities, and can
  create a destination track in the same undoable operation.
- Added trim handles/resize cursors with live source-region waveform previews,
  selection/razor modes, and targeted clip/track context menus. Right-click split/
  paste target the clicked frame without moving the playhead.
- Added grouped separators to in-window and native menus, Edit-menu clipboard
  actions/shortcuts, stereo/mono creation, and blank-header-space creation menus.
- Removed toolbar track creation, made skip controls adjacent, normalized toolbar
  controls to 42×42, and added a constrained meter-section splitter.
- Wheel zoom now anchors the mouse's timeline position; toolbar/menu zoom retains
  the playhead anchor. Mono waveforms use a cached downmix envelope matching audio.
- Preserved the edit cursor when rebuilding the audio graph after cut/trim/delete,
  preventing the next paste from unexpectedly moving to project start.
- Added domain/render/persistence and pointer/keyboard tests for source restoration,
  razor edits, clipboard snapshots and undo, contextual targeting, mono downmix/
  waveform/serialization, toolbar grouping, and meter resizing. Initial suite:
  **52 passed**. Edge hit testing prioritizes clip interiors at adjoining boundaries
  and leaves a movable center region even for very short clips.
- New saves use project format v2 for explicit track-channel semantics; loading v1
  upgrades metadata in memory with stereo defaults and does not rewrite the source
  project until Save. Older applications therefore reject v2 instead of silently
  playing a mono track as stereo. Newly created tracks become the selected target.
- Follow-up suite passes **53 cases**, including clipboard independence, no-track
  paste as a single undo step, context-menu target isolation, v1 migration without
  rewriting source files, and suppression of stale context menus.
- Updated usage, architecture, roadmap, and manual acceptance documentation. The
  clipboard is explicitly single-clip/session-local and requires a matching project
  clock; v2 migration and non-destructive mono semantics are documented.

### Completion and final verification

1. Softer gray light-mode clip canvas; left headers and dark palette preserved.
2. Category separators in every in-window/native submenu.
3. Source-bounded edge trimming with faithful waveform preview and undo.
4. Default selection tool and click-to-split razor tool, with V/C shortcuts.
5. Adjacent backward/forward skip buttons.
6. Resizable live-meter section, constrained to 140–360 px as window space permits.
7. Pointer-anchored Alt-wheel zoom; playhead-anchored zoom buttons remain unchanged.
8. Clip cut/copy/paste in Edit, with Ctrl/Cmd+X/C/V and independent clipboard snapshots.
9. Clip/empty-track context menus with contextual paste/split/mute targeting.
10. Stereo-default and mono tracks, with Edit-menu creation and actual downmix behavior.
11. Blank header-space track-creation menu; toolbar add-track button removed.
12. File-menu import retained alongside the toolbar import icon.
13. Uniform 42×42 toolbar controls, including zoom and fit.

- `dotnet test AudioNest.sln --configuration Release`: **53 passed, 0 failed,
  0 skipped**, with no build warnings/errors.
- `dotnet format AudioNest.sln --verify-no-changes --no-restore`: passed.
- `git diff --check`: passed (existing Git LF/CRLF notices only).
- Windows x64 Native AOT publish succeeded without warnings; native startup/
  normal-close smoke test exited with **code 0** and left no test process running.
- Populated light/dark Skia captures were inspected; generated artifacts remain
  under ignored build output.
- No physical recording/playback was started. Physical audio-device acceptance
  and non-Windows/native macOS menu checks remain manual.
- `TODO.txt` was not modified. Existing work was preserved; no commits were created.
