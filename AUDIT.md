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

## 2026-11-17 — Group editing, live recording, and export TODO

- Read the latest TODO points; preserve the clean starting repository and retain
  the four-layer boundaries.
- Add left-Shift group selection/move/clipboard edits, clearer toolbar grouping/
  labels/icons, 5% slider steps, track rename/height controls, export options and
  additional codecs, live recording waveforms, and strict centered mono behavior.
- Added SoundFlow.Codecs.FFMpeg 1.4.2 behind the hardware adapter for MP3/AAC/OGG/
  FLAC encoding and AAC/OGG import. WAV retains the existing PCM16 writer; exports
  use temporary-file publication and the same renderer as playback.
- Added bounded, preallocated recording peak storage and UI snapshots using actual
  captured frame counts. Audio callbacks retain no raw PCM for the live preview.
- Mono tracks now ignore saved pan, and are recentered after track processing so
  a mono track cannot produce unequal output channels.
- Implemented left-Shift toggle selection, group outlines/drag previews, common
  bounded time/track movement, and group delete/cut/copy/paste. Clipboard snapshots
  retain independent metadata and shared PCM, preserve relative time/track offsets,
  survive same-rate project changes, and create missing tracks in one paste edit.
  Ordinary dragging/context menus preserve an existing selected group.
- Replaced the razor geometry with a notched double-edge blade. Added toolbar
  category spacing without lines, and centered Snap/Fit with smaller padding.
- Volume/pan sliders snap to 0.05, retaining one undo step per pointer adjustment.
  Reduced thumbs to 12 px with sufficient track-header space; mono pan is disabled.
- Added undoable, persisted track renaming through the header context menu.
  Track-height grips adjust session-only heights between 164 and 600 px; all
  subsequent row drawing/hit testing uses the same dynamic geometry.
- Replaced File → Export WAV with Export… and a destination/Browse/format dialog,
  validation, and overwrite confirmation. Added WAV/MP3/AAC/M4A/OGG/OGA/FLAC import
  filters. Export choices are WAV PCM16, MP3, AAC/ADTS, OGG/Vorbis, and FLAC.
- The stock FFmpeg factory passes `aac` where FFmpeg requires `adts`. Added a small
  source-generated native AAC encoder adapter, using the package's bundled native
  ABI with no external executable. AAC/OGG decoding explicitly normalizes source
  rate/channels. All five formats passed native non-silent round trips.
- Updated README, architecture, feature status, hardware acceptance checks, and
  dependency/license notes. Build/publish now includes the codec package's
  unmodified license/notices under `licenses/`; release-specific LGPL source/
  relinking compliance remains a documented packaging obligation.
- Added `GroupAndRecordingTests` for actual Shift gestures and group clipboard
  shortcuts/history, pure group clamping, rename dialog/save/open, multi-event
  height dragging/alignment/minimum, stepped sliders/thumb bounds, legacy mono pan,
  live recording draft/finalization, allocation-free envelope append, and export UI.
  Regression testing exposed an untemplated resize thumb (not hit-testable); added
  its visible template and made resizing robust to events before/after layout.
- Project format remains v2; no new persisted fields or source-audio rewrites.
  Startup remains maximized with normal window decorations, not fullscreen.

### Verification and remaining acceptance

- `dotnet test AudioNest.sln --configuration Release`: **67 passed, 0 failed,
  0 skipped**; no build warnings/errors. Native codec cases open no audio devices.
- `dotnet format AudioNest.sln --verify-no-changes --no-restore`: passed.
- `git diff --check`: passed (Git's line-ending conversion notices only).
- Windows x64 Native AOT publish succeeded without warnings. Native main-window
  startup and normal close exited with **code 0**; no test process was retained.
  The initial input-idle-only check was too early to observe the main window;
  the subsequent process-lifetime/window/exit check verified startup explicitly.
- Inspected light/dark editor, live recording draft, and export-options Skia
  screenshots in ignored `tests/AudioNest.Tests/bin/Release/net10.0/ui-artifacts/`.
- Physical playback/microphone capture, OS file-picker interaction, codecs inside
  the AOT executable, independent-player listening checks, and non-Windows
  deployment remain manual acceptance. Live preview tests use synthetic capture.
- `TODO.txt` was left unchanged. No commits or pushes were created.

## 2026-11-17 — Playback-safe track controls and extensible inspector

- Read the three new TODO points from a clean working tree.
- Remove "undoable" from the delete tooltip and add header-context track deletion.
- Keep playback/device position running across track control changes via a prepared
  graph handoff, and reserve Space key-down/up for transport outside text inputs.
- Replace flat inspector effect rows with ordered, removable typed effect editors
  and a permanent global-properties section (name/start; source region read-only).
- Changed the × tooltip to "Delete track"; existing-header context menus now
  include captured-target Delete track, retaining undo/media safety.
- Added `AudioTransport.UpdatePlan`: freshly prepared same-format graphs are
  atomically published; each callback holds its own graph reference. In-flight
  callbacks finish the old graph; later callbacks use the new one without pausing,
  rewinding, stopping, or reopening the output device. Format changes are rejected.
  Removing all/remaining audio ends naturally without moving the edit cursor back.
- Track mute/solo/volume/pan/gain/limiter/rename/delete use the live path. Slider
  previews are audible before release, retain 5% steps, and commit one history edit.
  Structural clip edits and undo/redo still pause deliberately. No callback locks,
  allocation, native I/O, UI calls, or graph mutation were introduced.
- Space key-down and key-up are intercepted in the editor before buttons or
  checkboxes can activate; repeated key-down does not repeatedly toggle transport.
  Text-box descendants retain text-entry spaces, and modal dialogs retain normal
  keyboard behavior. Window deactivation clears held-key state.
- Added `ClipPropertiesViewModel` for draft clip name/timeline-start metadata,
  validated and applied together as one edit. Length/source offset stay read-only
  here; existing source-bounded timeline trims remain authoritative. The permanent
  global section retains nudges/split/delete and cannot be removed.
- Added an explicit AOT-safe `ClipEffectEditorFactory` with typed gain/limiter
  models/templates, an ordered stable collection, per-instance enabled/bypass and
  removal, independent parameters, and explicit add buttons. Duplicate effect kinds
  retain separate controls and processing order. The section base does not assume
  any particular parameter count; the one-parameter preview helper is optional for
  future effects. Commands guard captured clip/index/effect identity and detached
  editors cannot edit a replacement at their former index. Detached previews
  restore the current project mix rather than leaving transient audio behind.
- Updated README, architecture/extension guidance, feature status, and acceptance
  checklist. Documented that live swaps recreate current stateless processors:
  smoothing/stateful transition and native processor retirement remain future DSP
  requirements. Project format remains v2; no persisted fields or packages changed.
- Added nine regressions covering output-device/clock continuity, callback allocation,
  in-flight cross-thread publication, format rejection, live track controls, actual
  button focus + Space down/repeat/up, text-entry spaces, context deletion/tooltip,
  independent/duplicate effect edits, stale editors, global drafts/validation,
  save/open/history, typed controls, live/canceled previews, and actual slider drags.

### Verification

- `dotnet test AudioNest.sln --configuration Release`: **76 passed, 0 failed,
  0 skipped**, without build warnings/errors.
- `dotnet format AudioNest.sln --verify-no-changes --no-restore`: passed.
- `git diff --check`: passed.
- Windows x64 Native AOT publish succeeded without warnings. Native main-window
  startup/normal-close smoke check exited with code 0.
- Inspected the populated inspector-sections capture under ignored test
  `ui-artifacts/`; global properties, individual effect controls, bypass state, and
  removal buttons render correctly.
- No physical playback/recording was activated. Audible transition acceptance,
  non-Windows execution, and platform-native keyboard/menu checks remain manual.
- `TODO.txt` was not changed. No commits or pushes were created.

## 2026-11-17 — Immediate clip properties and simplified inspector

- Read the four new TODO items from a clean working tree.
- Replace boxed inspector sections with full-width 3 px separators; remove global
  action/apply buttons, effect-add buttons, and the track section.
- Add independent persisted clip mute/lock/volume/pan with immediate validated
  name/start editing, 5% clip sliders, and live previews. Lock anchors grouped
  moves and prevents repositioning/trimming; mono tracks keep clip pan centered.
- Increase default meter-section width to 240 px and meter height to 10 px.
- Bump project format to v3 so older readers cannot silently ignore clip audio/
  lock semantics; upgrade v1/v2 in memory with neutral defaults.
- Added validated clip fields (`Muted`, `Locked`, `Volume`, `Pan`) with unmuted/
  unlocked/unity/center defaults. Clipboard and split metadata copies retain these
  fields automatically. Legacy migration tests exposed source-generated JSON
  init-only assignment replacing absent volume with zero; an explicit optional
  JSON constructor parameter now preserves unity without reflection.
- The shared render graph omits muted clips without changing timeline duration.
  Clip volume is applied before clip effects, clip stereo balance after effects,
  and track processing remains independent. Mono ignores both saved pan fields
  and stays centered. Existing ordered gain effects remain unchanged and multiply
  with global clip volume.
- Immediate name/start setters validate locally and preserve incomplete/invalid
  text without corrupting the project. No Apply button or numeric spinner remains.
  Names retain typed spaces; unrelated property changes do not erase invalid input.
  Global mute/lock/mix edits use the live graph path; time repositioning remains a
  structural edit that pauses playback. Sliders use 12 px thumbs, 0.05 steps, live
  preview, and one undo commit per drag. Switching selection restores the persisted
  mix and cannot leak a preview onto the next clip.
- Locked clips cannot be moved between tracks/in time, edited through the start
  field, or edge-trimmed. Any locked group member anchors the complete group,
  preserving relative spacing. Explicit split/delete/paste are still available.
  Timeline labels include mute/lock state, and locked clips show no trim handles.
- Removed obsolete inspector action commands. Typed effect sections and their
  enable/remove/parameter controls remain; effects are added/configured through
  the existing Effects menu. Global properties remain permanent and unboxed.
- Increased the toolbar meter default from 184 to 240 px and meter bars from 7 to
  10 px; existing resize constraints and separated 18 px rows remain intact.
- Updated README, architecture, feature status, and acceptance checks for immediate
  fields, clip-only audio/locking, simplified sections, meter sizes, and v3 migration.
- Added nine tests covering independent clip/track rendering and WAV parity,
  signal-chain order/validation, pure and pointer lock enforcement, grouped locks,
  clipboard/split property retention, v1/v2 neutral defaults and v3 save/open,
  immediate real text controls, live volume/pan drags/history, selection-change
  preview cancellation, mono pan disabling, removed inspector buttons, full-width
  separators, compact thumb bounds, checkbox gestures, and meter dimensions/gaps.
  Updated earlier inspector tests to the new immediate/no-button behavior.

### Verification

- `dotnet test AudioNest.sln --configuration Release`: **85 passed, 0 failed,
  0 skipped**, with no build warnings/errors.
- `dotnet format AudioNest.sln --verify-no-changes --no-restore`: passed.
- `git diff --check`: passed.
- Windows x64 Native AOT publish succeeded without warnings. Native startup and
  normal-close smoke check exited with code 0; no test process was retained.
- Inspected light/dark `inspector-global-*.png` captures in ignored test output.
  The unboxed global/effect sections, checkboxes/sliders, extended separators,
  and larger, non-overlapping meters render correctly.
- Physical audio-device acceptance and non-Windows/native platform checks remain
  manual; no speakers or microphone were activated.
- `TODO.txt` was left unchanged. No commits or pushes were created.

## 2026-11-17 — Inspector, toolbar, and exit-dialog visual refinements

- Read the six new TODO points from a clean working tree.
- Extend inspector dividers to its viewport edges, increase thickness/contrast,
  label the panel "Clip Inspector", and compact global text/check controls.
- Remove the processing-order caption and place effect removal beside the name.
- Increase default meter-section width and center the unsaved-changes dialog
  message/actions within a smaller window, without changing discard decisions.
- Moved inspector padding into its content bodies, leaving dividers edge-to-edge
  even when resized or vertically scrolled. Separators are now 5 px (formerly 3 px)
  with dedicated higher-contrast light/dark theme brushes.
- Renamed the heading to "Clip Inspector". Global text fields are 26 px tall with
  12 px text; global checkboxes are 24 px tall, with reduced spacing. Clip property
  bindings, live previews, slider steps, validation, and history are unchanged.
- Removed the processing-order caption. Effect headings size to their content,
  with the remove button 8 px beside the name instead of at the far panel edge.
  Ordered effect processing and per-instance removal are unchanged.
- Meter section defaults to 300 px (formerly 240 px); the existing 140–360 px
  resize constraints and separated 10 px-tall bars remain.
- The unsaved-changes dialog is now 360×160, with the entire message/action group
  centered horizontally and vertically. Message text and both buttons are centered
  independently. Keep editing/Discard changes retain their prior behavior.
- Added two headless regressions covering inspector widths of 200/280/520 px,
  forced scrolling and both themes, exact separator coverage, compact field/
  checkbox sizes, adjacent remove-button placement and real removal/undo, plus
  exit-dialog centering/containment and both close decisions. Layout assertions
  allow normal subpixel/pixel rounding.
- Updated README, architecture notes, and visual/manual acceptance checks.
  No audio/domain/persistence/dependency or project-format changes were needed.

### Verification

- `dotnet test AudioNest.sln --configuration Release`: **87 passed, 0 failed,
  0 skipped**, without build warnings/errors.
- `dotnet format AudioNest.sln --verify-no-changes --no-restore`: passed.
- `git diff --check`: passed.
- Windows x64 Native AOT publish succeeded without warnings; native startup and
  normal close exited with code 0, leaving no test process running.
- Inspected light/dark inspector and `unsaved-changes-*.png` captures in ignored
  test `ui-artifacts/`. Physical OS display-scaling/non-Windows checks remain manual.
- No physical audio playback or microphone capture was started.
- `TODO.txt` was not modified. No commits or pushes were created.

## 2026-11-17 — Global checkbox clipping correction

- Preserved the existing staged visual-refinement changes.
- Removed the global checkboxes' fixed 24 px height/minimum. Fluent positions the
  20 px box inside a theme-sized indicator row (normally 32 px), so shrinking only
  the outer control clips its lower edge. Automatic sizing now respects that row;
  compact checkbox labels/padding and 26 px text fields are unchanged.
- Added a regression for indicator containment in checked/unchecked states and
  both themes, plus a taller theme indicator row. Updated layout expectations
  and current documentation without rewriting historical audit entries.
- Verification: **88 tests passed**, with no failures/skips; formatting and
  `git diff --check` passed. Inspected checked/unchecked light/dark captures and
  confirmed the checkbox bottoms are visible while text-field sizing is unchanged.
- This correction changes no audio, persistence, or dialog behavior. Native AOT
  publishing and physical display-scaling checks were not repeated for this fix.
- Existing staged changes were preserved; no commits or pushes were created.

## 2026-11-19 — Focused track inspector and built-in dynamics

- Read the three updated TODO points from a clean working tree.
- Separate inspector focus from the remembered import/paste destination. Empty
  canvas clears focus; clips show Clip Inspector and headers show Track Inspector.
- Use one add-only Effects menu for the focused target, with disabled/no-op commands
  when nothing is focused. Extend the typed editors to captured clip/track targets.
- Add stereo-linked compressor and release limiter plus analyzed peak normalization,
  retaining the legacy hard clipper for existing projects. Prepare normalizer gain
  outside the callback from its actual preceding effect chain; analyze occupied
  track ranges and advance dynamics across silent gaps.
- Add independent track volume and versioned effect parameters in project v4;
  older projects retain unity track volume and their existing ordered gain effects.
- Preserve unchanged dynamics state at audio-thread graph handoff, and explicitly
  reset new/edited/bypassed processors and on seek/stop.
- Added explicit `EffectTarget` scope to inspector/editor command capture. Header
  clicks show track globals (name/mute/solo/volume/pan); clip clicks retain existing
  clip globals; blank canvas/header space clears focus and shows empty disabled clip
  properties. The remembered paste/import track is not an effect target. Track
  global changes synchronize with header controls without rewriting effect chains.
- Replaced duplicate clip/track gain/limiter commands with Add Gain, Add Compressor,
  Add Limiter, and Add Normalizer. Each appends an independent default instance;
  guarded commands are disabled/no-op without a focused target.
- Compressor provides threshold, ratio, attack, release, and makeup editors, using
  stereo-linked peak detection and per-frame smoothed gain reduction. Limiter adds
  instantaneous linked ceiling attenuation and exponential release, with no
  lookahead/latency. Existing Hard limiter records keep their legacy clipping DSP.
- Normalizer measures the exact pre-effect input: source region/volume and preceding
  processors for clips, overlapping non-muted clips plus their processing/balance,
  track volume, and track prefix for tracks. It uses one fixed double-precision
  gain; silence remains silent. This is sample-peak normalization, not LUFS/AGC.
  Analysis uses bounded scratch blocks, no full-session PCM copy, and merged
  occupied ranges with analytical release decay across gaps.
- Added exact-input peak reuse from the previous prepared plan. Name/target-only
  edits reuse analysis; source bounds, PCM identity, timing/layout, mix, or preceding
  effects invalidate it. All analysis remains outside callbacks. Dense/long inputs
  can still take noticeable control-thread preparation time; background progress/
  cancellation is documented future work.
- Effect editors now share captured clip/track scope, preview snapshots, parameter
  descriptors, typed templates, stable controls, bypass/removal, and one undo step
  per drag. Selection/chain changes cancel previews; stale editors cannot mutate
  another target or effect at a former index.
- Audio-thread graph adoption copies only unchanged processors' envelopes after
  the previous callback completes. It performs no allocations/locks or control-
  thread DSP-state reads. New/edited/bypassed effects restart envelopes; seeks
  reset and have no pre-roll/crossfades. All current effects have no output tail.
- Edits now validate/prepare graphs before publishing history. Normalization failure
  (for example, non-finite audio from an excessive preceding gain chain) leaves the
  current project, history, and playing graph intact.
- Project v4 adds independent track volume and ratio/attack/release/makeup fields
  with explicit JSON defaults. V1/V2/V3 load without rewriting their files and
  preserve existing Gain/Hard limiter chains and audio behavior. No dependencies
  were added; four-layer boundaries and AOT-safe explicit registration remain.
- Updated README, architecture/extension notes, roadmap, manual acceptance, and
  About text. Added 25 test cases for focused menu routing, track globals/headers,
  independent parameters and actual slider gestures, stale previews, persistence/
  migration, compressor transfer curves/stereo linking, limiter ceiling/recovery,
  block independence/reset, callback allocation/state handoff, normalization order/
  overlap/sparse gaps/silence/quiet PCM/cache invalidation, playback/WAV parity,
  malformed parameters, and atomic failed preparation.

### Verification

- `dotnet test AudioNest.sln --configuration Release`: **113 passed, 0 failed,
  0 skipped**, without build warnings/errors.
- `dotnet format AudioNest.sln --verify-no-changes --no-restore`: passed.
- `git diff --check`: passed.
- Windows x64 Native AOT publish succeeded without warnings. Native main-window
  startup/normal-close smoke check exited with code 0 and left no test process.
- Inspected populated light/dark Track Inspector/dynamics captures under ignored
  test `ui-artifacts/`. Normalizer details can be reached by vertical scrolling.
- No physical playback/capture was started. Listening tests, larger-session timing,
  hardware buffer-size acceptance, non-Windows/native menus, and OS scaling remain
  manual. Sample-peak processors do not guarantee inter-sample true-peak ceilings.
- `TODO.txt` was not changed. No commits or pushes were created.
