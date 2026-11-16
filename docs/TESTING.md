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

- Start the app with no default audio device: editing should still work; Play or
  Record should show an actionable error without crashing.
- Import WAV/MP3/FLAC at 44.1/48/96 kHz. Confirm timing, channels, waveform, and
  correct speed/pitch after normalization.
- Play multiple overlapping clips. Pause, resume, seek, restart at end, and stop.
  Check playback/export gain parity with a known reference signal.
- Split a clip twice, remove its middle segment, drag across tracks, undo/redo.
  Confirm the original source is unchanged.
- Apply gain/limiter to one segment and its parent track. Check mute and solo.
- Grant microphone permission. Record a short mono take, stop, listen, save,
  close, reopen, and export. Confirm recording has no monitoring feedback.
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
