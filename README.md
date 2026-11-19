# AudioNest

A C# / Avalonia audio editor foundation with non-destructive multitrack editing.
SoundFlow + MiniAudio handles native audio I/O; its FFmpeg adapter adds codecs.
Both stay behind replaceable hardware contracts.

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

1. **Import audio** (WAV, MP3, AAC/M4A, OGG, FLAC), or **Record** to see a growing
   waveform draft, then **Stop** to finalize the take.
2. Add stereo/mono tracks from **Edit**, or right-click empty space below the track
   headers. In the selection tool (**V**), drag clip centers to move them and edges
    to trim. **Left Shift + click** adds/removes clips from the selection; drag a
    selected clip's center to move the group. **Click/drag the time ruler** to position the playhead;
   clip and track-header clicks do not seek. Use the magnifier buttons or **Fit**.
3. Press **S** to split the primary clip at the cursor, **Delete** to remove selected clips,
   and use **Undo / Redo** as needed. To remove an interior segment, split at
   both ends, select the middle clip, and delete it. Deletion does not ripple.
   The razor tool (**C**) splits directly where a clip is clicked, without seeking.
4. Use track-header **Mute / Solo**, the volume and stereo-balance sliders,
   and **×** beside the name (undoable track deletion). Clip effects are in the inspector; all
    click a clip or track header, then use **Effects → Add…** to append an effect
    to that target. Split first to process a segment.
   Track controls update the playing mix without stopping or reopening the output.
5. **Save** a `.audionest` project; **File → Export…** opens destination/Browse and
   format options for WAV (PCM16), MP3, AAC (ADTS), OGG (Vorbis), or FLAC.
   Export renders the same stereo mix as playback.

**Space** toggles playback, **Home / End** skip to the start/end, **Ctrl/Cmd+S** saves,
**Ctrl/Cmd+Shift+S** saves as, **Ctrl/Cmd+Z** undoes, and **Ctrl/Cmd+Shift+Z**
or **Ctrl/Cmd+Y** redoes.
In the editor, **Space** controls transport rather than activating a focused
button or checkbox; text fields still accept spaces normally.
**Ctrl/Cmd+C / X / V** copy, cut, and paste selected clips; keyboard/menu paste
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
  Timeline editing/effects remain available through the menus. At wider sizes, drag its left
  divider to resize it (200–520 px); that width is retained during the session.
   **Clip Inspector / Track Inspector** has compact global properties and individual effect
  editors, separated by edge-to-edge, high-contrast 5 px lines rather than boxes.
  Each effect's remove button sits beside its name.
- The compact status bar shows playhead time to six decimal places plus the exact
  project-frame index. Its left side identifies the selected track and clip;
  operation errors and recording/busy state remain visible.
  Playback position is device-frame-driven, not timer-driven.
- Track volume is a linear amplitude multiplier: **0** is silence, **1** is
  unchanged, **2** is twice the amplitude (+6 dB). A slider drag previews its value
  and commits on release/capture loss as one undo step. Volume and pan use **5%**
  increments; pan ranges from −1 to +1.
  Track volume is independent of ordered Gain effects. Existing saved volume above
  2× is preserved and labeled; moving the volume
  slider chooses a new value within 0–2×.
  Slider drags preview the live mix before committing on release.
- Track pan is **stereo balance**: center leaves both channels unchanged; moving
  toward one ear attenuates the opposite channel. It is saved with the project
  and used identically in playback and every export format. Mono tracks cannot pan.
- Right-click an existing track header to **Rename track…** (undoable and saved).
  The same menu offers **Delete track**; the × button does the same operation.
  Drag the thin grip at the bottom of a header to resize that row between
  **164 and 600 px**. Track heights are session-local and do not dirty the project.
- Two toolbar meters show actual timeline output (speaker) and microphone
  input (microphone), with dBFS readouts and peak decay. Input is metered **only
  while recording**: startup does not open or monitor the microphone.
  These are peak meters, not RMS/perceived-loudness or LUFS analysis.
  Their section defaults to 300 px wide; each meter is 10 px tall with space between.
  Drag the divider beside this section to resize it (140–360 px, subject to window size).

### Editing behavior

- The inspector begins with permanent **Global properties** for the primary clip:
  name, mute, lock, timeline-start text, volume, and pan. Valid text changes apply
  immediately, without an Apply button; invalid input shows an error and leaves
  the project unchanged. Length/source offset are read-only; trim in the timeline
  to change the source region.
- Click a track header to switch to **Track Inspector**: name, mute, solo, volume,
  pan, and effect sections apply to the entire track. It has no timeline-start or
  lock control. Clicking empty timeline/header space clears focus and shows empty,
  disabled clip properties. The last track may still be remembered as an import/
  paste destination; that does not make it an effect target.
- Clip mute/volume/pan are independent of track controls. Volume is 0–2× and pan
  is stereo balance from −1 to +1, both stepped by 5%. Sliders preview live audio
  and commit one undo step per drag. Mono tracks disable clip pan and ignore saved
  clip/track pan so output remains centered.
- **Locked** prevents timeline/track movement, start-field edits, and edge trimming.
  A selected group containing a locked clip stays anchored as a whole. Explicit
  split/delete and clipboard operations remain available; split/pasted clips retain
  their mute/lock/volume/pan. Unlock before repositioning a pasted locked clip.
- Each clip/track effect has its own section, in processing order, with parameter controls,
  an Enabled/bypass checkbox, and a remove button. Use the **Effects** menu to
  **add** Gain, Compressor, Limiter, or Normalizer to the focused clip/track.
  Each click appends an independent instance with defaults; with nothing focused,
  these menu items are disabled and cannot edit anything. Global volume is applied
  before the corresponding effect chain and combines
  with any existing gain effects. Parameter drags preview audio and commit one undo
  step. Removing/bypassing/editing a section affects only that effect instance.

### Built-in effects

- **Gain:** amplitude multiplier, initially 1×.
- **Compressor:** stereo-linked peak compression, initially −18 dB threshold, 4:1
  ratio, 10 ms attack, 100 ms release, 0 dB makeup. All five parameters are editable.
- **Limiter:** stereo-linked sample-peak ceiling, initially −1 dBFS and 50 ms
  release. Attenuation is instantaneous; release recovers smoothly. It has no
  lookahead/latency and is not a true-peak/inter-sample limiter.
- **Normalizer:** non-destructive sample-peak normalization, initially −1 dBFS.
  It measures the full clip source region or overlapping track mix **at that point
  in the effect chain**, including global volume and preceding effects. One fixed
  gain is then used for playback/export, preserving relative dynamics; silence
  stays silent. This is **not** automatic gain riding or RMS/LUFS normalization.
  Later effects/pan can still change the resulting peak.
- Existing projects' **Hard limiter** sections remain their original hard clippers.

Normalization analysis happens during graph preparation, not in the audio callback.
Unchanged audio inputs reuse cached peaks (including when renaming or changing only
the target peak). Audio-affecting edits can require another scan; large/dense sessions
may take noticeable preparation time. Progress/cancellation/background analysis UI
is not yet implemented. No source audio is rewritten.

- Edge trimming is non-destructive and source-bounded. Expanding reveals the
  original audio and waveform; it never repeats, stretches, or invents samples.
  At least one source frame remains. One completed trim drag is one undo step.
- Right-click a clip for cut/copy/paste, split-here, and track mute. Empty space
  within a track offers paste (when available) and mute; empty header-column space
  offers stereo/mono track creation. Menus separate related action groups.
- The internal clipboard holds the **selected group**, including source regions,
  effects, relative timing, and relative track offsets, and survives project changes
  within the same app session. PCM is shared, not duplicated. Paste creates fresh
  clip identities and any missing destination tracks in one undoable edit.
  This is not an OS audio clipboard; cross-project paste requires matching project
  sample rates (new projects all use 48 kHz).
- Stereo tracks retain both channels. Mono tracks non-destructively average L/R
  before clip effects and feed that mono signal to the stereo output, with the
  track's normal gain. Mono is recentered after track effects and ignores saved pan.
  Waveforms reflect the same downmix, including anti-phase
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
- New saves use project format **v4**, including independent track volume and
  dynamics parameters. V1/v2/v3 projects upgrade in memory, preserving existing
  ordered Gain/Hard limiter effects and adding unity track volume; older clip/layout
  defaults remain neutral. Their files are not rewritten until Save. Older AudioNest
  versions reject v4 rather than silently ignoring these audio semantics.

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
  the hardware adapter. Export uses default codec settings (no bitrate/quality UI).
  Individual decoded assets are limited to
  **10 minutes** and kept in RAM; this is not a large-session streaming engine.
- Gain, compressor, limiter, and peak normalizer are included. The final mix is
  still safety-clamped, not loudness-normalized. Unchanged dynamics retain envelopes
  across live mix edits; newly edited/bypassed effects and seeks restart their
  envelopes without pre-roll or parameter crossfades.
- Capture uses the default microphone in mono with a live peak-envelope preview,
  but no monitoring, overdub playback, device-settings UI, or recording-time effects.
  The envelope retains at most ten minutes; longer takes still exceed the import cap.
- Pitch/tempo/speed processing, noise reduction/reverb/echo/distortion, and VST hosting are
  **not implemented**. Extension contracts are not claims of those capabilities.
- Undo/redo retains 200 metadata edits and is not persisted across sessions.
- Working media is intentionally retained; clean it manually only after saving
  and verifying all projects that need it.

Dependencies retain their respective licenses. SoundFlow is MIT-licensed; its
FFmpeg codec includes LGPL dependencies. See [docs/DEPENDENCIES.md](docs/DEPENDENCIES.md)
for packaging obligations and bundled notices. Its upstream
maintainer has announced limited support through February 2027.
