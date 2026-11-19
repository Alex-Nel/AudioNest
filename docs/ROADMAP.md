# Feature status and next milestones

| Technical-document requirement | MVP status |
|---|---|
| Multitrack timeline with waveforms | Implemented; grouped selection/dragging, rename, resizable tracks |
| Split/delete/move shortcuts | Implemented; razor, edge trims, cut/copy/paste, and context menus |
| Non-destructive editing | Implemented; shared sources and bounded undo/redo |
| Clip, segment, and track effects | Focused Clip/Track Inspector; add-only scoped Effects menu; independent global volume; split clips define segments |
| Compressor/limiter/normalizer | Implemented: stereo-linked dynamics and analyzed sample-peak normalization; legacy hard clipper retained |
| Noise reduction/reverb/echo/distortion | Not implemented |
| Independent pitch, tempo, speed | Not implemented |
| VST 2.x / 3.x | Not implemented; ABI/licensing/isolation design still needed |
| Microphone recording | Default mono input, live waveform draft, finalized WAV imported as a clip |
| Effects automatically applied while recording | Not implemented; raw capture preserved |
| Save/open projects | Versioned JSON and portable sibling media folder |
| Multiple formats/rates | WAV/MP3/FLAC through MiniAudio; AAC/M4A/OGG through FFmpeg; normalized to project rate |
| Multiple output codecs | Export options for WAV PCM16, MP3, AAC/ADTS, OGG/Vorbis, FLAC |
| Avalonia / C# / four layers | Implemented |
| Decoupled playhead | Device-frame clock; live graph handoff keeps track controls from stopping playback |
| Ahead-of-time compilation possible | Windows x64 Native AOT verified; other RIDs pending |

## Suggested sequence

1. **Recording reliability and larger projects**
   - Bounded capture queue/background encoder, device settings, dropout reporting.
   - Peak-cache persistence, disk-backed source streaming, visible-range timeline
     rendering, cancellation/progress, and session CPU/memory budgets.
   - Autosave/recovery, relinking, media cache management, project rename UI.
2. **Editing ergonomics**
   - Range selections, fades/crossfades, ripple options, duplicate, accessibility,
     and configurable shortcuts; group clipboard/dragging, trims, and snapping exist.
3. **DSP**
   - Block processor tails, pre-roll, latency compensation, meters, automation.
   - Echo/reverb, noise profiling/reduction, distortion, true-peak/LUFS analysis.
   - Background/progress/cancel orchestration for normalization/noise profiling.
   - Independent time stretch/pitch shift behind a dedicated processor adapter.
4. **Production recording**
   - Dry/wet recording policy, effects monitoring, overdub transport, latency calibration.
5. **Plugins and formats**
   - Separate VST3 scanner/host process with crash recovery; revisit VST2 only after
     reviewing SDK availability and licensing.
   - Codec quality/bitrate controls and per-platform codec acceptance; complete
     release-specific FFmpeg source/relink packaging and license review.
6. **Distribution**
   - Per-platform hardware acceptance and AOT smoke tests, installers, signing,
     microphone permission manifests, dependency notices, and update strategy.
