# Architecture and extension guide

## Dependency direction

```text
Desktop (Avalonia + explicit DI composition root)
  ├─ Application ── Domain
  ├─ Persistence ── Application / Domain
  ├─ Audio ──────── Domain + Hardware contracts
  └─ Hardware.SoundFlow ── Hardware contracts + SoundFlow/MiniAudio/FFmpeg
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
source region → clip volume → clip effect chain → clip stereo balance
              → track sum → track volume → track effect chain
              → stereo balance → master sum → safety clamp → device / audio exporter
```

Mute and solo are resolved during plan preparation. Clip/track chains preserve
order. Structural timeline edits and undo/redo pause playback and rebuild the
graph. Track mixing controls and inspector effect edits instead build a fresh graph
on the control thread and publish it with `AudioTransport.UpdatePlan`. The callback
reads one graph reference per invocation; an in-flight callback finishes the old
graph before the next one sees the new graph. Position, output device, and playing
state are preserved. Graphs are never concurrently mutated. Output-format changes
are rejected on the live path. Export creates a separate graph, so it never shares
processor state with playback.

At the callback boundary, `AdoptStateFrom` transfers compressor/limiter envelopes
and noise-reduction filter/gain and echo/reverb delay state
for unchanged immutable effect records on the same track/clip. Only the audio
thread reads/copies that mutable state, after the old callback has completed.
New, edited, bypassed/unbypassed effects start fresh. Pause/seek/stop quiesce the
device before reset; discontinuous direct renders also reset. There are no
parameter crossfades/pre-roll or native processors to dispose. General tail/state
migration and deferred native retirement remain prerequisites for future plugins.

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
Move and trim previews/commits share frame-based resolution. Trim snapping considers
the playhead and every other clip's edges across tracks, including other selected
clips, but rejects targets outside source/one-frame bounds and ignores its own edges.
Left Shift XORs the clip Snap toggle; the legacy Alt bypass still forces free
editing. Physical-left Shift changes invalidate the preview even without pointer
movement. A Shift-click on a selected, draggable clip defers membership removal
until release without a drag, so Shift-drag preserves the anchor/group.
Ruler/clip horizontal scroll offsets are synchronized; vertical track scrolling
moves headers and clips together. Timeline extents grow from the viewport rather
than a fixed minimum width. Clip drawing is limited to its effective viewport.

Toolbar/menu zoom uses the playhead anchor; left-Alt wheel zoom uses the pointer's
timeline position. Both apply the new layout extent before assigning the new
scroll offset. Only physical-left
Alt/Shift state enables the new gestures; window deactivation clears that state.
Left-Shift ruler snapping reuses frame-based clip boundaries with a screen-pixel
tolerance, independent of the clip move/resize Snap toggle: ruler seeking is free
unless Left Shift is held.

Track slider drags keep a transient presentation preview and commit one metadata
edit on release or capture loss; keyboard steps commit individually. Thus dragging
does not fill the 200-edit history or commit project metadata on every mouse move.
The volume slider is 0–2×, while pre-existing higher saved gains are left intact.
Both sliders quantize to 0.05. Inspector width, track heights (164–600 px), zoom,
theme, and snapping remain view state, not project fields. Shared dynamic track
geometry drives headers, waveforms, hit testing, draft clips, and drag previews.
The editor consumes both Space key-down and key-up before focused controls see
them; key repeat is suppressed. Text-box descendants are exempt for text entry.
Modal dialogs retain their normal keyboard behavior.

`ClipInspectorViewModel` owns a permanent `ClipPropertiesViewModel` and an ordered
observable collection of `ClipEffectEditorViewModel` sections. Global properties
include immediate name/start text, clip mute/lock, and 5%-stepped clip volume/pan.
The same view models/templates serve focused tracks (name/mute/solo/volume/pan,
without timeline-start/lock fields). `EffectTarget` captures an ID plus clip/track
scope, independently of the remembered import destination. Blank canvas/header
space clears focus and leaves an empty Clip Inspector. Header clicks focus tracks.
Invalid text remains local without changing persisted metadata. A text-edit's own
notifications preserve the typed form/caret; unrelated property edits do not erase
invalid input. Source-region metadata stays read-only. Effect sections are stable
across ordinary notifications/parameter edits, preserving focused controls. Changes
in selection or chain shape rebuild the relevant editor set. Commands capture the
target ID/scope, chain index, and expected effect record; detached/stale editors cannot
silently edit another instance after removal. The editor structure itself requires
no new persisted effect IDs.

Global clip/track volume uses dedicated amplitude fields before the respective chains;
existing ordered gain effects remain independent and unchanged. Clip balance is
applied after clip effects and before track summing. Muted clips are omitted from
the prepared graph without shortening the timeline. Mono tracks ignore clip and
track pan and recenter after track processing. `ClipMixSlider` previews a prepared
graph without project mutation and commits one edit on release/capture loss.
Selection changes discard transient mix/effect previews and restore the persisted graph.

Locking prevents individual/group movement, timeline-start edits, and edge trims
in the pure edit operations as well as the pointer UI. Any locked group member
anchors the entire selection. Explicit split/delete/paste remain available, and
record copies preserve all clip properties. Inspector sections use edge-to-edge,
theme-aware high-contrast 5 px dividers with no enclosing boxes. Content padding
belongs to the section bodies, not the scroll container, so separators stay full
width as the inspector resizes/scrolls. Global fields are 26 px tall; checkboxes
size automatically to the theme's indicator row (normally 32 px) to avoid clipping.
Remove buttons sit immediately beside effect names. The add-only Effects menu has
one item per available effect and dispatches to the current focused target;
no-focus commands are disabled and guarded in the view model.

`ClipEffectEditorFactory` explicitly registers typed gain/dynamics/normalizer/noise/amplifier editors; typed
XAML templates provide their individual controls. The common base owns only section
lifecycle (enabled/bypass/removal), target capture, and gesture-level effect snapshots.
`EffectParameterViewModel` supplies descriptor-based controls for compressor's five
parameters and limiter release, sharing one undoable commit per gesture.
`SingleParameterEffectEditorViewModel` is an optional gesture/preview helper, not
a restriction on future multi-parameter editors. To add an effect editor, register
its model in the factory, add a typed template (or dedicated view), then implement
its validated domain description/DSP/persistence and tests. No reflection scanning
or runtime code generation is used.

Track/effect slider previews publish prepared transient graphs without changing
the project/history; release commits one metadata edit. Canceling a detached effect
preview restores the current project graph. Live publication never performs UI,
allocation, locks, or device restart within the audio callback.

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
Left-Shift toggles group membership; an ordinary drag on a selected clip preserves
the group. `MoveClips` clamps a common time/track delta, preserving group offsets.
Clipboard metadata (`ClipClipboardGroup` / `ClipClipboardItem`) is immutable; the
desktop retains associated `AudioBuffer` objects separately. Group move/cut/paste
each commit one edit. Paste resolves safe asset/clip identities, shares PCM, preserves
relative time/track offsets, and creates missing destination tracks within that edit.
The session clipboard is not an OS audio clipboard and requires a matching project
clock. Graph rebuilds preserve the edit cursor, including after deleting the last clip.

Track layouts are stereo by default. Mono tracks average normalized source L/R
before clip effects, duplicate the mono signal into the stereo render bus, then
apply track effects and recenter the result. Saved pan is ignored for mono, and
its pan control is disabled. Assets are never converted destructively.
`AudioBuffer` caches both source-envelope and mono-downmix peaks, so mono
anti-phase cancellation is reflected in the waveform as well as playback/export.

`AudioBuffer` owns interleaved normalized float PCM and a 256-frame min/max cache.
The caller transfers array ownership and must not subsequently mutate it.
`AudioLibrary` keys buffers by asset ID **and PCM format**, so project history
can reference different rates without re-decoding inside undo/redo or callbacks.
New/Open clears those variants; the immutable session clipboard retains its own
buffer references. Trying many rates increases retained RAM; streaming/cache
eviction and conversion progress/cancellation remain future work.

`ProjectSampleRate` performs pure clock conversion using Int128 rational arithmetic,
rounding frame boundaries to the nearest target sample. Asset lengths come from
fresh decoding of original media, with a small resampler-rounding tolerance;
source offsets/lengths are bounded to the decoded asset, with at least one frame.
Global clock conversion also applies to locked clips, preserving their times
rather than their old frame numbers. Effects, identities, mix and track properties
are unchanged. Extremely distant clips that would overflow the new clock fail
before publication; cursor conversion instead saturates at the timeline limit.

The Edit-menu dialog uses an editable AutoCompleteBox plus an explicit dropdown
button for common rates. Custom whole-Hz values in the existing 8–192 kHz domain
range are accepted, with optional Hz/kHz suffixes. Apply stages decode, metadata
validation, and render/normalization preparation on background tasks before
publishing one history entry and cache variants. Playback pauses; recording and
other edits are disabled while busy. Failures leave old metadata/PCM/history
intact. A sample-rate change (including undo/redo) sets a new transport graph and
rescales cursor time; live same-format handoff is deliberately not used.
The existing project format already stores its rate, so no schema migration is
needed. Save retains original media and records new decoded frame counts; Open
decodes those originals at the saved project rate.

## Adding effects

`IAudioProcessor` is an audio-thread-owned block processor, created outside the
callback through `IAudioEffectFactory`. Implement `Process` without allocation,
blocking, I/O, or UI; implement `Reset` for seek/stop. Inject the factory when
preparing a render plan. Built-in gain, legacy hard clipping, stereo-linked peak
compression, zero-lookahead peak limiting, multiband suppression, a dB amplifier,
echo, algorithmic reverb, and soft saturation
are the reference examples. Dynamics
process by sample frame, preserve stereo balance, and do not depend on block size.

For additional built-ins, extend the effect description, validation, source-generated
serialization if needed, factory, tests, and inspector. Persistent descriptions must
remain separate from runtime DSP. V6 `EffectSpec` stores the primary value plus
ratio/attack/release/makeup/reduction depth/mix/feedback/damping/wet output trim
with explicit optional constructor defaults. Validation
rejects non-finite/out-of-range parameters, including bypassed descriptions.

`IPeakNormalizer` is configured during `RenderPlan` construction. Clip analysis
reads its actual source region/volume and preceding processors; track analysis mixes
non-muted clips, their effects/balance, global track volume, and the track prefix.
It measures pre-safety-clamp sample peaks and sets a fixed double-precision gain.
No full-session PCM copy or callback scan is used; silent sources remain silent.
Merged occupied intervals avoid traversing distant empty timeline regions. Track
intervals extend by the sum of prefix `TailFrames`, capped at the project duration,
so delayed peaks in gaps are measured before silence is skipped.
`AdvanceSilence` analytically decays compressor/limiter release and noise-reduction
filters/per-band gain across these gaps.
Delay effects render their bounded zero-input drain before sparse skipping and
reset once empty. Their `AdvanceSilence` discards unobserved output using bounded
stack scratch or resets directly for gaps longer than the remaining tail.

Analysis results can be reused from the previous prepared plan, with exact input
comparison (PCM reference, source bounds, relevant volume/pan/timing/layout, and
preceding active descriptions, plus project duration for track analysis).
New PCM invalidates the cache, even with the same
asset ID. Target-only/metadata changes reuse peaks. Processor instances/scratch are
always new, never shared with the callback. Analysis still runs on the control
thread; progress, cancellation, and background-job orchestration remain future work.
`MainViewModel.Edit` prepares/validates before committing history, so failed
normalization does not publish a project that differs from the playing graph.

`NoiseReductionProcessor` uses a complementary parallel one-pole filter bank with
cutoffs at 80/250/800/2500/7000 Hz where below 80% of Nyquist, plus the high residual.
It shares soft noise-suppression gain between stereo channels per band, with a
bandwidth-weighted manual broadband noise-floor reference and bounded reduction
depth. Opening/closing gains use attack/release smoothing. A 12-double stack scratch
and constructor-allocated state arrays keep callback work bounded/allocation-free.
Zero reduction passes samples through exactly. Zero-input frames output exact silence
while internal filters/gains decay, preserving this processor's no-output-tail
behavior. Sparse silence advance and unchanged-record graph handoff preserve
state. This is frequency-selective downward expansion, not profile learning, FFT
overlap-add, AI denoising, or a substitute for a later restoration-quality processor.
It can color/attenuate wanted low-level content; listening acceptance remains important.

Amplifier converts its primary dB value to an amplitude factor at construction.
It reuses the stateless gain processor, with no automatic limiting or saturation;
the existing master ceiling and optional subsequent limiter remain independent.
Dark startup is set in both application XAML and the view-model default to avoid
an initial light-theme transition; the Tools toggle remains session-local.

`DelayEffectProcessor` owns the finite zero-input drain contract and same-format,
same-description state copying. Echo uses a same-channel feedback ring, with
10–2000 ms delay, feedback below unity, and a −80 dB/30-second tail bound.
Reverb uses four parallel damped feedback combs (29.7/37.1/41.1/43.7 ms) followed
by two serial allpasses (5/1.7 ms) per channel. Feedback is calibrated from the
low-frequency −60 dB decay setting. Its tail is bounded at twice decay plus 0.1 s.
All rings/indices/filter state are preallocated; render and state handoff allocate
nothing. Identical per-channel networks preserve centered mono without crossfeed.
These are basic algorithmic effects, not convolution or a stereo-width model.

Clip DSP only processes the clip's source-bounded region: clip tails are deliberately
cut at its edge. Track DSP processes silence in gaps, allowing tails up to the fixed
project end. The renderer, transport, and exporter do not extend duration. Seeking
and changed descriptions reset delay state with no pre-roll/crossfade. Full
post-project/post-clip tail expansion is not implemented.

Distortion uses symmetric `tanh(input * drive)` with dB-based drive, wet-only output
trim, and dry/wet interpolation. It has no state, oversampling, or amp/cabinet model;
high drive can alias. Zero wet mix is exactly transparent for all three new effects.

Before adding buffering pitch/tempo processors or plugins, extend:

1. Tail expansion after clip/project end and processor drain bounds as needed.
2. Latency reporting/compensation and seek pre-roll.
3. Consistent automation/parameter scheduling by project frame.
4. Processor disposal/lifetime ownership for native plugin instances.
5. Bounded error handling and real-time CPU/memory regression tests.

The interface is a starting point for block DSP, not a complete VST host or a
promise that arbitrary stateful effects work without those additions.

## Hardware and recording

`IAudioBackend` abstracts float PCM decoding/encoding, device discovery, output
callbacks, and file-backed recording. Only `Hardware.SoundFlow` imports SoundFlow types.
Native initialization is lazy, allowing projects/UI to open without audio hardware.
Current device selection uses defaults; adapter IDs are enumeration-scoped, not
stable identifiers to persist.

Capture records mono WAV without monitoring and imports the finished take at the
cursor on the selected track. Its true length comes from decoding the finalized
file. Original captures remain available in the working-media folder on import
failure. Capture currently delegates encoding to SoundFlow's stock recorder, which
does work on its capture callback. Replace this with a preallocated bounded ring
buffer plus background encoder before pursuing low-latency recording guarantees.

`RecordingPreview` adds a single-producer fixed envelope (40 bins/second, up to
ten minutes) and an atomic captured-frame count. Append does not allocate or lock;
UI snapshots copy only published, immutable completed bins. A presentation-only
recording draft grows from captured frames, not wall time, and disappears on Stop;
only importing the finalized WAV adds an undoable project clip.

WAV/MP3/FLAC imports retain MiniAudio decoding. AAC/M4A/OGG use the bundled
`SoundFlow.Codecs.FFMpeg` 1.4.2 decoder with explicit rate/channel normalization.
`AudioExporter` streams the shared renderer into `IAudioFileEncoder` for MP3,
AAC/ADTS, OGG/Vorbis, and FLAC; WAV keeps the managed PCM16 writer. Temporary files
are renamed only after successful finalization. `AacFileEncoder` uses source-generated
native interop for the bundled wrapper's ADTS muxer: its stock factory incorrectly
passes the codec name `aac` as a container. No external ffmpeg executable is needed.
Native codec packaging and LGPL obligations are described in `DEPENDENCIES.md`.

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

Version 6 is source-generated JSON plus a sibling media directory. V1/V2/V3/V4/V5 upgrade
in memory with neutral defaults and preserved legacy effect
order; older clip/layout defaults remain neutral.
Optional JSON constructor parameters explicitly default missing clip/track volume to
unity (source-generated init-only assignment would otherwise use zero).
The original file is unchanged until Save. The version bump prevents older readers
from ignoring new effect identities/mix/feedback/damping/output-trim semantics.
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
