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
2. Add stereo/mono tracks from **Edit**, or right-click empty space below the track
   headers. In the selection tool (**V**), drag clip centers to move them and edges
   to trim. **Click/drag the time ruler** to position the playhead;
   clip and track-header clicks do not seek. Use the magnifier buttons or **Fit**.
3. Press **S** to split at the cursor, **Delete** to remove the selected clip,
   and use **Undo / Redo** as needed. To remove an interior segment, split at
   both ends, select the middle clip, and delete it. Deletion does not ripple.
   The razor tool (**C**) splits directly where a clip is clicked, without seeking.
4. Use track-header **Mute / Solo**, the volume and stereo-balance sliders,
   and **×** beside the name (undoable track deletion). Clip effects are in the inspector; all
   clip/track effects are also in the **Effects** menu. Split first to process a segment.
5. **Save** a `.audionest` project; **Export WAV** renders a stereo PCM16 mix.

**Space** toggles playback, **Home / End** skip to the start/end, **Ctrl/Cmd+S** saves,
**Ctrl/Cmd+Shift+S** saves as, **Ctrl/Cmd+Z** undoes, and **Ctrl/Cmd+Shift+Z**
or **Ctrl/Cmd+Y** redoes. The inspector also provides 0.1-second clip nudges.
**Ctrl/Cmd+C / X / V** copy, cut, and paste the selected clip; keyboard/menu paste
uses the selected track and playhead. Right-click paste/split uses the clicked time.

### Editor UI

- **File / Edit / Effects / Tools / Help** group the available operations.
  macOS exports these to its native menu bar; other platforms use an in-window
  menu, with the same commands and enabled states.
- The application starts **maximized**, with its normal OS title bar. Restore it
  with the usual window controls.
- One grouped toolbar provides icon-only play (green), pause, stop, skip-to-start,
  skip-to-end, and record (red), followed by import, selection/razor, snap, zoom,
  and fit controls. Skip buttons are adjacent and toolbar buttons share a 42×42 size.
  Tooltips and accessibility labels identify the icons.
- Magnifier buttons zoom around the playhead, preserving its screen position when
  visible, or bringing it into view when not. **Left Alt + mouse wheel** instead
  keeps the timeline time under the **mouse pointer** anchored.
  Unmodified wheel scrolling navigates the timeline. **Left Shift + ruler drag**
  magnetically snaps the cursor to clip starts/ends on any track within eight pixels.
- **Snap** magnetically aligns either moving clip edge with the playhead or other
  clip boundaries (across tracks) within eight screen pixels. Hold **Alt** to
  bypass it for one drag; turn Snap off for free positioning.
- Light mode is the neutral default. Choose **Tools → Dark mode** to toggle.
  The light-mode clip canvas uses a soft gray background; track headers are unchanged.
  Theme, mouse tool, and snapping preferences are session-local, not project edits.
- The timeline fills the available viewport; the track headers stay pinned during
  horizontal scrolling and the ruler stays pinned during vertical scrolling.
  Below 1100 px window width, the inspector hides to give the timeline room.
  Its operations remain available through the menus. At wider sizes, drag its left
  divider to resize it (200–520 px); that width is retained during the session.
  The inspector has separate clip metadata, effects-list, and action sections.
- The compact status bar shows playhead time to six decimal places plus the exact
  project-frame index. Its left side identifies the selected track and clip;
  operation errors and recording/busy state remain visible.
  Playback position is device-frame-driven, not timer-driven.
- Track volume is a linear amplitude multiplier: **0** is silence, **1** is
  unchanged, **2** is twice the amplitude (+6 dB). A slider drag previews its value
  and commits on release/capture loss as one undo step. Pan ranges from −1 to +1.
  Existing saved gains above 2× are preserved and labeled; moving the volume
  slider chooses a new value within 0–2×.
- Track pan is **stereo balance**: center leaves both channels unchanged; moving
  toward one ear attenuates the opposite channel. It is saved with the project
  and used identically in playback and WAV export.
- Two skinny toolbar meters show actual timeline output (speaker) and microphone
  input (microphone), with dBFS readouts and peak decay. Input is metered **only
  while recording**: startup does not open or monitor the microphone.
  These are peak meters, not RMS/perceived-loudness or LUFS analysis.
  Drag the divider beside this section to resize it (140–360 px, subject to window size).

### Editing behavior

- Edge trimming is non-destructive and source-bounded. Expanding reveals the
  original audio and waveform; it never repeats, stretches, or invents samples.
  At least one source frame remains. One completed trim drag is one undo step.
- Right-click a clip for cut/copy/paste, split-here, and track mute. Empty space
  within a track offers paste (when available) and mute; empty header-column space
  offers stereo/mono track creation. Menus separate related action groups.
- The internal clipboard holds **one clip**, including its source region and
  effects, and survives project changes within the same app session. PCM is shared,
  not duplicated. Paste creates a new clip identity and is independently undoable.
  This is not an OS audio clipboard; cross-project paste requires matching project
  sample rates (new projects all use 48 kHz).
- Stereo tracks retain both channels. Mono tracks non-destructively average L/R
  before clip effects and feed that mono signal to the stereo output, with the
  track's normal gain/pan. Waveforms reflect the same downmix, including anti-phase
  cancellation. Moving a clip back to stereo restores its original channel data.

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
- New saves use project format **v2**, which records track channel layout. Existing
  v1 projects open with stereo defaults and upgrade in memory; their files are not
  rewritten until Save. Older AudioNest versions reject v2 rather than misreading mono.

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
