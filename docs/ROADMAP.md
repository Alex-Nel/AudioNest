# Feature status and next milestones

| Technical-document requirement | MVP status |
|---|---|
| Multitrack timeline with waveforms | Implemented; scroll, zoom, selection, clip dragging |
| Split/delete/move shortcuts | Implemented; segment deletion via two splits |
| Non-destructive editing | Implemented; shared sources and bounded undo/redo |
| Clip, segment, and track effects | Gain/hard limiter; segments are independent split clips |
| Noise reduction/compressor/normalizer/reverb/echo/distortion | Not implemented |
| Independent pitch, tempo, speed | Not implemented |
| VST 2.x / 3.x | Not implemented; ABI/licensing/isolation design still needed |
| Microphone recording | Default mono input, finalized WAV imported as a clip |
| Effects automatically applied while recording | Not implemented; raw capture preserved |
| Save/open projects | Versioned JSON and portable sibling media folder |
| Multiple formats/rates | WAV/MP3/FLAC import through MiniAudio; normalized to project rate |
| Multiple output codecs | WAV PCM16 export only |
| Avalonia / C# / four layers | Implemented |
| Decoupled playhead | Device-frame transport clock plus independent UI overlay |
| Ahead-of-time compilation possible | Windows x64 Native AOT verified; other RIDs pending |

## Suggested sequence

1. **Recording reliability and larger projects**
   - Bounded capture queue/background encoder, device settings, dropout reporting.
   - Peak-cache persistence, disk-backed source streaming, visible-range timeline
     rendering, cancellation/progress, and session CPU/memory budgets.
   - Autosave/recovery, relinking, media cache management, project rename UI.
2. **Editing ergonomics**
   - Range selections, trim handles, fades/crossfades, snapping, ripple options,
     copy/paste/duplicate, accessibility, and configurable shortcuts.
3. **DSP**
   - Block processor tails, pre-roll, latency compensation, meters, automation.
   - Compressor/normalizer, echo/reverb, noise profiling/reduction, distortion.
   - Offline analysis jobs for normalization/noise profiling rather than audio-thread scans.
   - Independent time stretch/pitch shift behind a dedicated processor adapter.
4. **Production recording**
   - Dry/wet recording policy, effects monitoring, overdub transport, latency calibration.
5. **Plugins and formats**
   - Separate VST3 scanner/host process with crash recovery; revisit VST2 only after
     reviewing SDK availability and licensing.
   - Optional FFmpeg codec adapter with packaging/license review.
6. **Distribution**
   - Per-platform hardware acceptance and AOT smoke tests, installers, signing,
     microphone permission manifests, dependency notices, and update strategy.
