# AudioNest

A C# / Avalonia audio editor foundation with non-destructive multitrack editing.
SoundFlow + MiniAudio handles native audio I/O and decoding behind a replaceable adapter.

## Run

Install the **.NET 10 SDK**, then:

```sh
dotnet restore AudioNest.sln
dotnet run --project src/AudioNest.Desktop
```

Windows, macOS, and Linux are the intended desktop targets. Windows x64 has been
built and tested locally; the CI matrix is provided for the other platforms.
Linux needs Avalonia's normal desktop dependencies and a usable audio backend.
Microphone capture needs OS permission and an available default input device.

## MVP workflow

1. **Import audio** (WAV, MP3, FLAC), or **Record**, then **Stop** to add a take.
2. Add tracks, click a clip to select it and place the cursor, and drag clips to
   move them horizontally or between tracks. Zoom with **+ / −**.
3. Press **S** to split at the cursor, **Delete** to remove the selected clip,
   and use **Undo / Redo** as needed. To remove an interior segment, split at
   both ends, select the middle clip, and delete it. Deletion does not ripple.
4. Adjust clip/track gain and hard limiting in the inspector. Split first to
   process only a segment. Track headers and clip labels show gain/limiter state.
5. **Save** a `.audionest` project; **Export WAV** renders a stereo PCM16 mix.

**Space** toggles playback, **Home** seeks to the start, **Ctrl/Cmd+S** saves,
**Ctrl/Cmd+Shift+S** saves as, **Ctrl/Cmd+Z** undoes, and **Ctrl/Cmd+Shift+Z**
or **Ctrl/Cmd+Y** redoes. The inspector also provides 0.1-second clip nudges.

### Files and safety

- Edits change clip metadata; source audio is never rewritten.
- Imports and takes are retained in the app-local `AudioNest/Media` working folder
  (`%LOCALAPPDATA%\AudioNest\Media` on Windows).
- A save creates **both** `Session.audionest` and `Session.audionest.media/`.
  **Keep and move these together.** Media paths in the JSON are relative, and
  files are deduplicated by SHA-256 within that project's media directory.
- Save As creates its own media directory. Repeated saves do not delete unused
  media. Save metadata and exports are published via same-directory replacement.
- Missing/changed-length media or unsupported project versions fail with an error,
  without replacing the current session. Autosave/recovery UI and garbage
  collection are not implemented. Back up valuable recordings.

## Architecture

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for dependency rules and extension
guidance, [docs/ROADMAP.md](docs/ROADMAP.md) for feature status, and
[AUDIT.md](AUDIT.md) for the implementation/change/verification record.

| Layer | Assemblies |
|---|---|
| Hardware abstraction | `AudioNest.Hardware`, `AudioNest.Hardware.SoundFlow` |
| Real-time audio engine | `AudioNest.Audio` |
| Project / domain | `AudioNest.Domain`, `AudioNest.Application`, `AudioNest.Persistence` |
| Presentation | `AudioNest.Desktop` |

## Verify / publish

```sh
dotnet build AudioNest.sln -c Release
dotnet test AudioNest.sln -c Release
dotnet publish src/AudioNest.Desktop -c Release -r win-x64 --self-contained true
```

Native AOT is opt-in and requires the native compiler prerequisites for the target:

```sh
dotnet publish src/AudioNest.Desktop -c Release -r win-x64 -p:PublishAot=true -o artifacts/aot/win-x64
```

Windows x64 AOT compilation has been verified locally. AOT on another OS must be
published/tested on that OS; future plugin/native integrations need separate AOT
review. Publish directories contain native dependency files: distribute the
**whole directory**, not only the executable.

Tests cover edits/history, source bounds, effects/mixing, cached peaks,
allocation-free rendering, device-frame transport, export/cancellation, portable
JSON persistence, SoundFlow WAV decoding/resampling, and headless desktop flows.
Automated tests do **not** capture a real microphone or play through speakers.
See [docs/TESTING.md](docs/TESTING.md) for hardware acceptance checks.

## Current limits

- New projects are 48 kHz stereo; imported rates/channels are normalized by
  MiniAudio. Export is WAV PCM16 only. Individual decoded assets are limited to
  **10 minutes** and kept in RAM; this is not a large-session streaming engine.
- Gain and a simple **hard limiter** are the only included effects. The final mix
  is hard-clamped to prevent out-of-range output, not loudness-normalized.
- Capture uses the default microphone in mono, with no monitoring, overdub
  playback, device-settings UI, live waveform, or recording-time effects.
- Pitch/tempo/speed processing, advanced built-in effects, and VST hosting are
  **not implemented**. Extension contracts are not claims of those capabilities.
- Undo/redo retains 200 metadata edits and is not persisted across sessions.
- Working media is intentionally retained; clean it manually only after saving
  and verifying all projects that need it.

Dependencies retain their respective licenses. SoundFlow is MIT-licensed and
bundles native dependencies; review its notices when packaging. Its upstream
maintainer has announced limited support through February 2027.
