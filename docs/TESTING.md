# Testing

## Automated checks

```sh
dotnet test AudioNest.sln -c Release
```

The suite uses a fake hardware backend for transport and recording tests and
Avalonia Headless for desktop workflows. `NativeCodec` tests exercise actual
SoundFlow WAV decoding/resampling and all five export/import round trips without
starting a playback or capture device.
Test working media is isolated in per-test temporary directories.

```sh
dotnet test AudioNest.sln -c Release --filter "Category!=NativeCodec"
```

Use the filter only for environments that cannot load native dependencies; it
does **not** verify SoundFlow deployment.

## Manual hardware acceptance

Use disposable test material first:

- Confirm maximized startup with normal OS decorations, and restore the window.
  Start the app with no default audio device: editing should still work; Play or
  Record should show an actionable error without crashing.
- Import WAV/MP3/AAC/M4A/OGG/FLAC at 44.1/48/96 kHz. Confirm timing, channels, waveform, and
  correct speed/pitch after normalization.
- Play multiple overlapping clips. Pause, resume, seek, restart at end, and stop.
  Check playback/export gain parity with a known reference signal.
- Split a clip twice, remove its middle segment, drag across tracks, undo/redo.
  Confirm the original source is unchanged.
- Click/drag only the ruler to seek, including dragging outside it while captured.
  Clip selection/movement and track-header buttons must not move the playhead.
- Toggle Snap, align both clip edges to the cursor/other clips at different zoom
  levels, and hold Alt to bypass. Undo a drag in one operation.
- Use toolbar magnifiers at different scroll offsets: preserve the visible
  playhead's screen x, or bring an offscreen playhead into view. Left-Alt wheel
  zoom must instead preserve the time under the mouse. Ordinary wheel scrolling
  navigates without changing zoom.
- Hold left Shift while seeking with the ruler: test nearby boundaries on every
  track. Right Alt/Shift should not activate these side-specific gestures.
- Drag both clip edges inward/outward, including adjacent/tiny clips. Expansion
  must reveal the original source, stop at source limits/zero time, and keep at
  least one frame. Check preview waveform, undo/redo, and save/open.
- Switch selection/razor tools with toolbar buttons or V/C. Razor clicks and
  right-click splits must use the clicked frame, not the playhead; endpoint clicks
  must not create zero-length clips.
- Exercise Ctrl/Cmd+C/X/V and contextual cut/copy/paste. Confirm copied offsets/
  effects are independent of later edits, each paste has a fresh ID, and paste at
  the playhead still works after cutting the final clip. Try same-rate project
  changes, no-track paste, undo/redo, and saving the pasted result.
- Left-Shift click clips across several tracks, toggle membership, then drag,
  cut/copy/paste, delete, and undo each operation. Relative timing/track spacing
  must survive, including movement against zero time and the first/last track.
- Rename through an existing header's context menu, undo/redo, save/reopen.
  Delete a different, right-clicked track through the same menu and undo it.
  Drag its bottom grip to the 164/600 px limits; verify all later headers, clips,
  hit targets, and live recording drafts remain aligned when scrolled.
- Right-click blank track space with/without clipboard content and verify only
  relevant paste/mute actions. Right-click blank header space to add stereo/mono
  tracks; it must not reuse that creation menu on existing track headers.
- Compare stereo and mono with asymmetric and anti-phase test material: mono
  should average channels, stay centered despite legacy saved pan, disable the
  pan slider, show the downmix envelope, and match exports.
  Load a v1 project and confirm stereo defaults without changing the source file;
  save/reopen v3 mono projects, including upgrading older v2 mono projects.
- Resize to 760–1500 px widths and vary height; confirm pinned track headers,
  ruler/clip horizontal alignment, fixed horizontal scrollbar, and inspector hiding.
- Toggle light/dark mode and operate File/Edit/Effects/Tools/Help. On macOS,
  verify native menu export, enabled/check states, and Cmd shortcuts physically.
- Test each track's mute, volume (0 / 1 / 2×), pan slider, and delete button. A
  pointer slider drag should undo in one step, including interrupted capture.
  Keyboard slider steps should work in 5% increments; thumbs should not clip.
  Undo deletion,
  save/open balance, and compare stereo-channel results in playback/export.
- Repeat track mute/solo/volume/pan/rename/delete while playing: position must
  continue and the audio device must not stop/reopen. Slider previews should be
  audible before release; each drag commits one undoable edit. Removing the final
  audio naturally ends playback without rewinding the cursor.
- After clicking track, toolbar, and inspector buttons/checkboxes, press and release
  Space repeatedly. Only playback should toggle, not the focused control. Hold
  Space to check key-repeat suppression; spaces in editable names still work.
- In the inspector, change name/start text and confirm immediate application with
  no Apply button or numeric spinners. Reject empty names/negative/invalid starts
  without mutating the project; verify undo/redo/save/reopen and caret stability.
  Toggle clip-only mute/lock, adjust 5% clip volume/pan, and compare against track
  controls. Lock must block dragging, timeline-start edits, and edge trims; a mixed
  locked/unlocked selected group must not partially move. Undo unlocking/locking.
- Verify clip mute/mix in playback/export, centered mono despite saved clip pan,
  property preservation through clipboard/split, and neutral v1/v2 migration.
  Check global slider live previews, one undo per drag, and switching selection
  during a preview without leaking that mix onto the new selection.
- Use the Effects menu for gain/limiter sections, adjust each independently,
  bypass/remove one, and undo. Check processing order, parameter persistence,
  focus stability, live drag preview, and scrolling at the narrowest inspector
  width. The global section remains permanent. Inspect edge-to-edge 5 px separators
  with distinct contrast in both themes, the "Clip Inspector" title, 26 px text
  fields, fully visible checked/unchecked global checkboxes (automatic theme-safe
  height), and remove buttons immediately beside effect names.
  There should be no processing-order caption, section boxes, or
  split/delete/nudge/add-effect/track buttons in the inspector.
- Click a clip, header, blank track area, and blank area below tracks. Check
  Clip/Track/empty inspector focus without moving the playhead. Track Inspector
  must hide timeline start, replace lock with solo, synchronize the left header's
  name/mute/solo/volume/pan, and leave clip properties/effects untouched.
- Effects must offer only Add Gain/Compressor/Limiter/Normalizer/Noise Reduction/
  Amplifier/Reverb/Echo/Distortion, without clip/
  track duplicates or toggles. No-focus items must be disabled; each invocation
  appends an independent instance to the focused target. Check typed parameters,
  bypass, removal, history/save/open, mono tracks, and stale-editor/preview safety
  when switching between clip and track.
- Listen to compressor threshold/ratio/attack/release/makeup behavior with transients,
  steady tones, and asymmetric stereo. Check limiter ceiling/release without
  shifting stereo balance. Compare normalizer output to a known sample peak, with
  source trims, preceding effects, overlapping clips, silence, and quiet material.
  These are peak processors, not LUFS or inter-sample true-peak implementations.
- Compare dynamics playback/export at different device buffer sizes; seek/reset
  should restart envelopes, while unchanged effects retain state across live mix
  edits. Normalizer preparation must not run in the callback; test longer/dense
  material for acceptable control-thread preparation time and cached target edits.
- Open v1/v2/v3/v4/v5 projects and verify unchanged sound, neutral defaults, preserved
  legacy gain/clipping, v6 saving, and parameter round trips.
- Verify dark startup and checked Tools → Dark mode, then toggle to light and back
  without dirtying the project. Check dark styling in owned dialogs as well.
- Test Noise Reduction with quiet hiss/hum, voice/music over noise, asymmetric
  stereo, and low-level wanted details. Adjust noise floor/reduction/attack/release,
  compare bypass and 0 dB reduction, and check start/stop/seek/mute/gap behavior.
  This is manual-floor multiband expansion, not a learned-profile/AI denoiser.
- Test Amplifier at negative/zero/positive dB, including +3 dB default. Compare
  playback/export, clipping risk, and adding a subsequent limiter. Verify both new
  effects on clips/tracks, parameter previews/history/removal, save/open, and no-focus
  disabled menu state. Repeat listening checks at 8/44.1/48/192 kHz as available.
- Test Reverb with short transients and sustained audio: change decay/damping/wet mix,
  compare stereo/mono, and listen for decay quality. This is algorithmic, not convolution.
- Test Echo delay/feedback/wet mix, including a single repeat at zero feedback and
  bounded decay at maximum feedback (30 s maximum; the cap can be audible).
- Test Distortion drive/wet output/mix, positive/negative peaks and loud inputs.
  Wet output must not trim dry signal; 0 dB drive still saturates. Compare low-rate/
  high-drive aliasing; this processor does not oversample or model an amp/cabinet.
- Check all three at 0% wet (transparent), bypass, clip/track insertion, independent
  instances, one-undo-per-drag, removal, save/open, and playback/export agreement.
  Echo/reverb clip tails stop at clip edges; track tails ring into gaps but stop at
  project end. No automatic duration extension or seek pre-roll. Check trim, mute/
  solo, seek/stop/restart, live unrelated edits (state retained), effect changes (fresh
  state), and a normalizer after delays with overlapping/gapped/distant clips.
- Drag the inspector divider to both limits, shrink/restore the window, and
  verify remembered width and timeline/ruler alignment. Change clip selection and
  confirm status text and inspector metadata/effect rows follow the selection.
- Resize the meter section independently, inspect consistent toolbar button sizes,
  adjacent skip buttons, grouped menu separators, and the softer light-mode canvas.
  The default meter section should be 300 px wide, with 10 px-tall meter bars
  separated by a visible gap in both light and dark modes.
- Confirm separate Play and Pause buttons, skip-to-start/end, and live status-bar
  microsecond time/exact frame count while playing and seeking.
- Apply gain/limiter to one segment and its parent track. Check mute and solo.
- Grant microphone permission. Record a short mono take, stop, listen, save,
  close, reopen, and export. Confirm recording has no monitoring feedback.
  Watch the draft waveform grow before Stop; stopping replaces the draft with
  exactly one imported clip, without retaining a duplicate.
- Open File → Export…; choose destination with Browse or type it, change formats,
  cancel, and confirm overwrites. Listen to WAV/MP3/AAC/OGG/FLAC outputs in an
  independent player; check duration (allow lossy encoder padding), pitch, and channels.
- Watch both toolbar meters with actual devices: timeline output should respond
  to playing clips (including mute/gain), and the mic meter should respond only
  during recording. Startup must not request microphone access. Check silence,
  meter decay, clipping-range peaks, stopping, and restarting.
- Deny microphone permission or unplug an input device and check error handling.
  Capture-device disconnect/dropout recovery is not yet implemented.
- Keep takes below ten minutes (the import cap). If a longer recording fails
  import, its original WAV remains in the local working-media directory.
- Save, move both project and `.media` folder, remove the original import source,
  reopen, and export. Verify Save As does not alter the old project.
- Close a clean project; close a dirty one and test both discard and keep editing.
  The compact unsaved-changes dialog should center its message and button group
  horizontally/vertically without clipping, in either theme and at OS scaling levels.
- Run a native AOT publish on each intended target and repeat import/play/record/
  save/open/export. UI startup alone does not validate native audio deployment.

Local verification covers Windows x64 build, tests, native AOT compilation, and
native startup/clean shutdown. Physical audio I/O, macOS/Linux execution, and
long-duration/stress/performance acceptance remain manual work.
