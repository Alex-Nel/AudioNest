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
