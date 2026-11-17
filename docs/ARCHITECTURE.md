# Architecture and extension guide

## Dependency direction

```text
Desktop (Avalonia + explicit DI composition root)
  ├─ Application ── Domain
  ├─ Persistence ── Application / Domain
  ├─ Audio ──────── Domain + Hardware contracts
  └─ Hardware.SoundFlow ── Hardware contracts + SoundFlow/MiniAudio
```

`Domain` and `Hardware` have no NuGet dependencies. Neither the domain nor the
engine references Avalonia, serialization, DI, or SoundFlow. Persistence and
hardware adapters are replaceable through `IProjectStore` and `IAudioBackend`.
Several assemblies belong to the same conceptual layer; this is not a new
seven-layer architecture.

## Project state and edits

- A project is an immutable record graph of assets, tracks, clips, and ordered
  effect descriptions. Every timeline time is an integer **project sample frame**.
- Asset frame counts and clip source offsets refer to PCM normalized to the
  project clock, not compressed byte offsets. A clip references a source region
  and a timeline position. Split clips share media and decoded PCM.
- `ProjectEdits` implements pure transformations. `ProjectSession.Apply` validates
  before committing and stores the previous graph in bounded undo history.
  Snapshots share unchanged records and never duplicate PCM.
- Domain invariants are checked after edits, before saving, and on loading. Invalid
  edits leave the current graph unchanged.
- `ProjectSession` and `AudioLibrary` are control/UI-thread-owned. Background tasks
  decode data into staging objects; the UI publishes them after successful decode.
  Failed project loading preserves the active project.

Add an edit by implementing a pure transform and invoking `Apply` once per user
gesture. Never insert sample buffers, view objects, or device handles into project
records. Add a project format migration before changing persisted semantics;
unknown versions currently fail closed.

## Rendering and thread boundaries

`RenderPlan` compiles a validated project into a prepared graph with references to
shared PCM, private processor instances, and preallocated scratch buffers. It mixes
bounded 1024-frame blocks:

```text
source region → clip effect chain → track sum → track effect chain
              → stereo balance → master sum → safety clamp → device / WAV exporter
```

Mute and solo are resolved during plan preparation. Clip/track chains preserve
order. For the MVP, editing pauses playback and rebuilds the graph; there is no
concurrent graph mutation. Export creates a separate graph, so it never shares
processor state with playback.

The render callback performs no file access, UI calls, locks, LINQ, or managed
allocation. It is a managed soft-real-time implementation, **not** a hard-real-time
latency guarantee. Sample lookup, linear graph traversal, RAM decoding, and lack
of a streaming scheduler are deliberate MVP tradeoffs.

`AudioTransport` owns the output session and frame position. Only device callbacks
advance playback position; UI timers merely sample `ITransportClock`. Stop/seek
quiesces the device before changing position or resetting processors. The Avalonia
playhead is a separate overlay, so clock ticks do not invalidate waveform content.
The reported position is rendered frames, not hardware-latency-compensated audible
position.

The UI separates a clip canvas from a pinned ruler and actual per-track controls.
Only ruler pointer gestures seek; clip drags preserve the cursor. `ClipSnapping`
resolves frame positions independently of UI, with an eight-pixel tolerance
converted to frames by the presentation layer. One drag is one undoable edit.
Ruler/clip horizontal scroll offsets are synchronized; vertical track scrolling
moves headers and clips together. Timeline extents grow from the viewport rather
than a fixed minimum width. Clip drawing is limited to its effective viewport.

Toolbar/menu zoom uses the playhead anchor; left-Alt wheel zoom uses the pointer's
timeline position. Both apply the new layout extent before assigning the new
scroll offset. Only physical-left
Alt/Shift state enables the new gestures; window deactivation clears that state.
Left-Shift ruler snapping reuses frame-based clip boundaries with a screen-pixel
tolerance, independent of the optional clip-move snapping toggle.

Track slider drags keep a transient presentation preview and commit one metadata
edit on release or capture loss; keyboard steps commit individually. Thus dragging
does not fill the 200-edit history or mutate the audio graph on every mouse move.
The volume slider is 0–2×, while pre-existing higher saved gains are left intact.
Inspector width, zoom, theme, and snapping remain view state, not project fields.
`ClipInspectorView` / `ClipInspectorViewModel` isolate metadata, ordered effect rows,
and actions so future parameter editors need not be embedded in the window layout.

Track `Pan` is an additive version-1 field (missing values default to zero). It is
stereo balance in [-1, 1], not a channel-summing mono panner; center preserves unity.
Track deletion retains media for undo and does not garbage-collect source files.

`ClipTrimming` computes source-bounded start/end metadata. The start edge changes
source offset and timeline start while preserving the opposite end; the end edge
changes only length. Canvas previews use these same calculations and source peaks,
and commit once per pointer gesture. Interior hit testing takes precedence over
neighboring resize halos; short clips retain a movable center.

Selection and razor tools are presentation state. Razor/context-menu split
coordinates are translated to project frames and invoke the same pure split edit.
Clipboard metadata (`ClipClipboardItem`) is immutable; the desktop retains the
associated `AudioBuffer` separately. Paste resolves safe asset/clip identities,
shares PCM, and creates any missing destination track within the same edit.
The session clipboard is not an OS audio clipboard and requires a matching project
clock. Graph rebuilds preserve the edit cursor, including after deleting the last clip.

Track layouts are stereo by default. Mono tracks average normalized source L/R
before clip effects, duplicate the mono signal into the stereo render bus, then
apply track effects/balance normally. Assets are never converted destructively.
`AudioBuffer` caches both source-envelope and mono-downmix peaks, so mono
anti-phase cancellation is reflected in the waveform as well as playback/export.

`AudioBuffer` owns interleaved normalized float PCM and a 256-frame min/max cache.
The caller transfers array ownership and must not subsequently mutate it.

## Adding effects

`IAudioProcessor` is an audio-thread-owned block processor, created outside the
callback through `IAudioEffectFactory`. Implement `Process` without allocation,
blocking, I/O, or UI; implement `Reset` for seek/stop. Inject the factory when
preparing a render plan. Built-in gain and hard limiting are the reference examples.

For additional built-ins, extend the effect description, validation, source-generated
serialization if needed, factory, tests, and inspector. Persistent descriptions must
remain separate from runtime DSP. The current enum/value schema intentionally
describes only two simple effects; richer parameters require deliberate versioning.

Before adding delay/reverb, buffering pitch/tempo processors, or plugins, add:

1. DSP tail length and silence/drain behavior after clip end.
2. Latency reporting/compensation and seek pre-roll.
3. Consistent automation/parameter scheduling by project frame.
4. Processor disposal/lifetime ownership for native plugin instances.
5. Bounded error handling and real-time CPU/memory regression tests.

The interface is a starting point for block DSP, not a complete VST host or a
promise that arbitrary stateful effects work without those additions.

## Hardware and recording

`IAudioBackend` abstracts float PCM decoding, device discovery, output callbacks,
and file-backed recording. Only `Hardware.SoundFlow` imports SoundFlow types.
Native initialization is lazy, allowing projects/UI to open without audio hardware.
Current device selection uses defaults; adapter IDs are enumeration-scoped, not
stable identifiers to persist.

Capture records mono WAV without monitoring and imports the finished take at the
cursor on the selected track. Its true length comes from decoding the finalized
file. Original captures remain available in the working-media folder on import
failure. Capture currently delegates encoding to SoundFlow's stock recorder, which
does work on its capture callback. Replace this with a preallocated bounded ring
buffer plus background encoder before pursuing low-latency recording guarantees.

`AudioPeakMeter` transfers only peak amplitudes with atomic compare/exchange and
read/reset operations; it performs no UI calls, allocation, locks, or sample
retention. The output callback measures the rendered timeline mix. A separate
meter subscribes to the capture device only for a recording session, and is
unsubscribed/cleared when capture stops. The UI polls these independent meters and
applies display decay / a −60..0 dBFS scale. This is peak level measurement, not
LUFS/RMS loudness analysis and not microphone monitoring at startup.

For recording-time effects, keep a dry take, feed a prepared recording graph, and
separately support monitoring and wet printing. Add input/output latency
compensation and decide the dropped-frame policy explicitly; never silently bake
an effect into the only surviving source.

## Persistence and AOT

Version 2 is source-generated JSON plus a sibling media directory. V1 is upgraded
in memory with stereo layout defaults; the original file is unchanged until Save.
The version bump prevents older readers from ignoring mono-track semantics.
Save copies
SHA-256-addressed media first and replaces a temporary metadata file last. Export
uses the same temporary-file publication pattern. These protect the old file on
ordinary operation failure; they are not a database, cross-volume transaction,
media authenticity system, or guarantee against every power-loss scenario.

The loader accepts only project-relative paths within the project directory and
reports missing media. The path check is lexical, not an OS sandbox against
symlink/reparse-point tricks; do not treat project files as trusted executables.
There is no automatic media collection, relinking UI, or crash-recovery browser.

JSON uses explicit `JsonSerializerContext` metadata, and DI uses explicit factories.
Avalonia uses compiled bindings. AudioNest introduces no dynamic assembly scanning
or runtime code generation. Windows native AOT is checked; OS-specific native
libraries and future VST bridges must still be packaged and tested per RID.

## VST strategy (future)

Keep plugin scanning, native ABI binding, GUI embedding, and crash isolation in a
dedicated adapter/helper process, not the domain or Avalonia view models. Store
plugin identifiers, parameters, and versioned opaque state in the project. Prefer
VST3 first; VST2 SDK availability/distribution rights must be reviewed separately.
Do not assume a C#-only host can implement native VST ABI and cross-platform editor
embedding without native glue. Out-of-process hosting also avoids forcing runtime
managed assembly loading into the AOT desktop process.
