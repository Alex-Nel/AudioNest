# Testing

## Automated checks

```sh
dotnet test AudioNest.sln -c Release
```

The suite uses a fake hardware backend for transport and recording tests and
Avalonia Headless for desktop workflows. The `NativeCodec` test exercises actual
SoundFlow WAV decoding/resampling but does not start a playback or capture device.
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
- Import WAV/MP3/FLAC at 44.1/48/96 kHz. Confirm timing, channels, waveform, and
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
- Right-click blank track space with/without clipboard content and verify only
  relevant paste/mute actions. Right-click blank header space to add stereo/mono
  tracks; it must not reuse that creation menu on existing track headers.
- Compare stereo and mono with asymmetric and anti-phase test material: mono
  should average channels, show the downmix envelope, and match WAV export.
  Load a v1 project and confirm stereo defaults without changing the source file;
  save/reopen v2 mono projects.
- Resize to 760–1500 px widths and vary height; confirm pinned track headers,
  ruler/clip horizontal alignment, fixed horizontal scrollbar, and inspector hiding.
- Toggle light/dark mode and operate File/Edit/Effects/Tools/Help. On macOS,
  verify native menu export, enabled/check states, and Cmd shortcuts physically.
- Test each track's mute, volume (0 / 1 / 2×), pan slider, and delete button. A
  pointer slider drag should undo in one step, including interrupted capture.
  Keyboard slider steps should work. Undo deletion,
  save/open balance, and compare stereo-channel results in playback/export.
- Drag the inspector divider to both limits, shrink/restore the window, and
  verify remembered width and timeline/ruler alignment. Change clip selection and
  confirm status text and inspector metadata/effect rows follow the selection.
- Resize the meter section independently, inspect consistent toolbar button sizes,
  adjacent skip buttons, grouped menu separators, and the softer light-mode canvas.
- Confirm separate Play and Pause buttons, skip-to-start/end, and live status-bar
  microsecond time/exact frame count while playing and seeking.
- Apply gain/limiter to one segment and its parent track. Check mute and solo.
- Grant microphone permission. Record a short mono take, stop, listen, save,
  close, reopen, and export. Confirm recording has no monitoring feedback.
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
- Run a native AOT publish on each intended target and repeat import/play/record/
  save/open/export. UI startup alone does not validate native audio deployment.

Local verification covers Windows x64 build, tests, native AOT compilation, and
native startup/clean shutdown. Physical audio I/O, macOS/Linux execution, and
long-duration/stress/performance acceptance remain manual work.
