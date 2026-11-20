using AudioNest.Domain;
using AudioNest.Hardware;
using AudioNest.Audio.Effects;

namespace AudioNest.Audio;

/// <summary>
/// Prepared audio-thread-owned graph. No domain editing, I/O, locks, or allocations
/// in Render. Sample buffers may be shared; processor state and scratch space may not.
/// </summary>
public sealed class RenderPlan
{
    private const int QuantumFrames = 1024;
    private readonly PreparedTrack[] _tracks;
    private readonly float[] _clipScratch;
    private readonly float[] _trackScratch;
    private sealed record PreparedClip(AudioClip Clip, AudioBuffer Buffer, IAudioProcessor[] Effects, EffectSpec[] Descriptions, float Left, float Right);
    private sealed record PreparedTrack(Guid Id, PreparedClip[] Clips, IAudioProcessor[] Effects, EffectSpec[] Descriptions, float Left, float Right, bool Mono, float Volume);
    private long? _nextFrame;
    private readonly bool _builtInEffects;
    private readonly Dictionary<(Guid Id, bool Track, int Slot), Analysis> _analyses = [];
    private sealed record Analysis(PreparedClip[] Clips, bool Mono, float Volume, EffectSpec[] Prefix, double Peak, long Duration);
    public int NormalizationAnalysisCount { get; private set; }

    public RenderPlan(AudioProject project, Func<Guid, AudioBuffer> getBuffer, IAudioEffectFactory? effects = null, RenderPlan? previous = null)
    {
        project.Validate();
        Format = new PcmFormat(project.SampleRate, 2);
        Duration = project.Duration;
        effects ??= new BuiltInEffectFactory();
        _builtInEffects = effects is BuiltInEffectFactory;
        _clipScratch = new float[QuantumFrames * Format.Channels];
        _trackScratch = new float[QuantumFrames * Format.Channels];
        double PeakFor(Guid id, bool isTrack, PreparedClip[] clips, bool mono, float volume, EffectSpec[] prefix, Func<double> scan)
        {
            static bool SameInput(Analysis old, PreparedClip[] clips, bool mono, float volume, EffectSpec[] prefix, bool track)
            {
                if (old.Mono != mono || old.Volume != volume || old.Clips.Length != clips.Length || !old.Prefix.AsSpan().SequenceEqual(prefix)) return false;
                for (var i = 0; i < clips.Length; i++)
                {
                    var a = old.Clips[i];
                    var b = clips[i];
                    if (!ReferenceEquals(a.Buffer, b.Buffer) || a.Clip.SourceOffset != b.Clip.SourceOffset ||
                        a.Clip.Length != b.Clip.Length || a.Clip.Volume != b.Clip.Volume) return false;
                    if (track && (a.Clip.Start != b.Clip.Start || a.Clip.Pan != b.Clip.Pan ||
                        !a.Descriptions.AsSpan().SequenceEqual(b.Descriptions))) return false;
                }
                return true;
            }
            var key = (id, isTrack, prefix.Length);
            double peak;
            if (_builtInEffects && previous is { _builtInEffects: true } && previous.Format == Format &&
                previous._analyses.TryGetValue(key, out var old) && (!isTrack || old.Duration == Duration) &&
                SameInput(old, clips, mono, volume, prefix, isTrack))
                peak = old.Peak;
            else { NormalizationAnalysisCount++; peak = scan(); }
            _analyses[key] = new(clips, mono, volume, prefix, peak, Duration);
            return peak;
        }
        (IAudioProcessor[] Processors, EffectSpec[] Descriptions) Prepare(IEnumerable<EffectSpec> chain, Func<IAudioProcessor[], EffectSpec[], double> analyze)
        {
            var processors = new List<IAudioProcessor>();
            var descriptions = chain.Where(e => !e.Bypassed).ToArray();
            foreach (var description in descriptions)
            {
                var processor = effects.Create(description, Format);
                if (processor is IPeakNormalizer normalizer)
                    normalizer.SetInputPeak(analyze(processors.ToArray(), descriptions.AsSpan(0, processors.Count).ToArray()));
                processors.Add(processor);
            }
            return (processors.ToArray(), descriptions);
        }
        var anySolo = project.Tracks.Any(t => t.Solo);
        _tracks = project.Tracks.Where(t => !t.Muted && (!anySolo || t.Solo)).Select(t =>
        {
            var mono = t.Layout == TrackChannelLayout.Mono;
            var clips = t.Clips.Where(c => !c.Muted).Select(c =>
            {
                var buffer = getBuffer(c.AssetId);
                if (buffer.Format != Format || c.SourceOffset + c.Length > buffer.FrameCount)
                    throw new InvalidDataException("Decoded audio does not match the project.");
                var chain = Prepare(c.Effects, (prefix, descriptions) => PeakFor(c.Id, false,
                    [new PreparedClip(c, buffer, [], [], 1, 1)], mono, 1, descriptions, () => AnalyzeClip(c, buffer, mono, prefix)));
                return new PreparedClip(c, buffer, chain.Processors, chain.Descriptions, mono ? 1 : 1 - Math.Max(0, c.Pan), mono ? 1 : 1 + Math.Min(0, c.Pan));
            }).ToArray();
            var trackEffects = Prepare(t.Effects, (prefix, descriptions) => PeakFor(t.Id, true, clips, mono, t.Volume,
                descriptions, () => AnalyzeTrack(clips, mono, t.Volume, prefix)));
            return new PreparedTrack(t.Id, clips, trackEffects.Processors, trackEffects.Descriptions, mono ? 1 : 1 - Math.Max(0, t.Pan),
                mono ? 1 : 1 + Math.Min(0, t.Pan), mono, t.Volume);
        }).ToArray();
        Reset();
    }

    public PcmFormat Format { get; }
    public long Duration { get; }

    public void Render(long start, Span<float> output)
    {
        output.Clear();
        if (_nextFrame is { } expected && expected != start) Reset();
        var totalFrames = output.Length / Format.Channels;
        for (var offset = 0; offset < totalFrames; offset += QuantumFrames)
        {
            var frames = Math.Min(QuantumFrames, totalFrames - offset);
            var block = output.Slice(offset * Format.Channels, frames * Format.Channels);
            var blockStart = start + offset;
            foreach (var track in _tracks)
            {
                var trackSamples = _trackScratch.AsSpan(0, block.Length);
                trackSamples.Clear();
                MixClips(track.Clips, track.Mono, blockStart, trackSamples);
                Scale(trackSamples, track.Volume);
                Process(trackSamples, track.Effects);
                if (track.Mono)
                    for (var i = 0; i < trackSamples.Length; i += 2)
                        trackSamples[i] = trackSamples[i + 1] = trackSamples[i] * 0.5f + trackSamples[i + 1] * 0.5f;
                for (var i = 0; i < block.Length; i++)
                    block[i] += trackSamples[i] * (i % 2 == 0 ? track.Left : track.Right);
            }
            // A final safety ceiling is shared by live output and export.
            for (var i = 0; i < block.Length; i++)
                block[i] = blockStart + i / Format.Channels >= Duration ? 0 : Math.Clamp(block[i], -1f, 1f);
        }
        _nextFrame = start <= long.MaxValue - totalFrames ? start + totalFrames : null;
    }

    private void FillClip(AudioClip clip, AudioBuffer buffer, bool mono, long offset, Span<float> samples)
    {
        for (var i = 0; i < samples.Length; i++)
        {
            var frame = clip.SourceOffset + offset + i / Format.Channels;
            var sample = mono ? buffer.Sample(frame, 0) * 0.5f + buffer.Sample(frame, 1) * 0.5f : buffer.Sample(frame, i % Format.Channels);
            samples[i] = sample * clip.Volume;
        }
    }
    private void MixClips(PreparedClip[] clips, bool mono, long start, Span<float> samples)
    {
        var frames = samples.Length / Format.Channels;
        foreach (var clip in clips)
        {
            var from = Math.Max(start, clip.Clip.Start);
            var to = Math.Min(start + frames, clip.Clip.End);
            if (from >= to) continue;
            var scratch = _clipScratch.AsSpan(0, (int)(to - from) * Format.Channels);
            FillClip(clip.Clip, clip.Buffer, mono, from - clip.Clip.Start, scratch);
            Process(scratch, clip.Effects);
            var destination = (int)(from - start) * Format.Channels;
            for (var i = 0; i < scratch.Length; i++)
                samples[destination + i] += scratch[i] * (i % 2 == 0 ? clip.Left : clip.Right);
        }
    }
    private static void Scale(Span<float> samples, float gain)
    {
        for (var i = 0; i < samples.Length; i++) samples[i] *= gain;
    }
    private static double Peak(ReadOnlySpan<float> samples, double peak)
    {
        foreach (var sample in samples)
        {
            if (!float.IsFinite(sample)) throw new InvalidDataException("Effects produced non-finite audio. Reduce gain before normalizing.");
            peak = Math.Max(peak, Math.Abs((double)sample));
        }
        return peak;
    }
    private static void Reset(IAudioProcessor[] chain) { foreach (var processor in chain) processor.Reset(); }
    private double AnalyzeClip(AudioClip clip, AudioBuffer buffer, bool mono, IAudioProcessor[] prefix)
    {
        Reset(prefix);
        var peak = 0d;
        try
        {
            for (long at = 0; at < clip.Length;)
            {
                var frames = (int)Math.Min(QuantumFrames, clip.Length - at);
                var samples = _clipScratch.AsSpan(0, frames * Format.Channels);
                FillClip(clip, buffer, mono, at, samples);
                Process(samples, prefix);
                peak = Peak(samples, peak);
                at += frames;
            }
            return peak;
        }
        finally { Reset(prefix); }
    }
    private double AnalyzeTrack(PreparedClip[] clips, bool mono, float volume, IAudioProcessor[] prefix)
    {
        void ClearState()
        {
            Reset(prefix);
            foreach (var clip in clips) Reset(clip.Effects);
        }
        ClearState();
        var peak = 0d;
        long previousEnd = 0;
        void Scan(long from, long to)
        {
            foreach (var effect in prefix) effect.AdvanceSilence(from - previousEnd);
            for (var at = from; at < to;)
            {
                var frames = (int)Math.Min(QuantumFrames, to - at);
                var samples = _trackScratch.AsSpan(0, frames * Format.Channels);
                samples.Clear();
                MixClips(clips, mono, at, samples);
                Scale(samples, volume);
                Process(samples, prefix);
                peak = Peak(samples, peak);
                at += frames;
            }
            previousEnd = to;
        }
        try
        {
            // Render zero-input tails before skipping truly empty gaps. Sum
            // bounds for serial delays; never analyze beyond the project end.
            long tail = 0;
            foreach (var effect in prefix) tail += Math.Min(effect.TailFrames, Duration - tail);
            long End(PreparedClip clip) => clip.Clip.End + Math.Min(tail, Duration - clip.Clip.End);
            var sorted = clips.OrderBy(c => c.Clip.Start).ToArray();
            if (sorted.Length == 0) return 0;
            var from = sorted[0].Clip.Start;
            var to = End(sorted[0]);
            foreach (var clip in sorted.Skip(1))
            {
                if (clip.Clip.Start <= to) to = Math.Max(to, End(clip));
                else { Scan(from, to); from = clip.Clip.Start; to = End(clip); }
            }
            Scan(from, to);
            return peak;
        }
        finally { ClearState(); }
    }

    private static void Process(Span<float> samples, IAudioProcessor[] effects)
    {
        foreach (var effect in effects) effect.Process(samples);
    }

    /// <summary>Audio-thread-only handoff. Preserve envelopes of unchanged effect
    /// records; new/edited/bypassed effects intentionally start with fresh state.</summary>
    public void AdoptStateFrom(RenderPlan previous)
    {
        static void Transfer(IAudioProcessor[] next, EffectSpec[] descriptions, IAudioProcessor[] old, EffectSpec[] oldDescriptions)
        {
            for (var i = 0; i < next.Length; i++)
                for (var j = 0; j < old.Length; j++)
                    if (ReferenceEquals(descriptions[i], oldDescriptions[j])) { next[i].CopyStateFrom(old[j]); break; }
        }
        foreach (var track in _tracks)
            foreach (var oldTrack in previous._tracks)
            {
                if (track.Id != oldTrack.Id || track.Mono != oldTrack.Mono) continue;
                Transfer(track.Effects, track.Descriptions, oldTrack.Effects, oldTrack.Descriptions);
                foreach (var clip in track.Clips)
                    foreach (var oldClip in oldTrack.Clips)
                        if (clip.Clip.Id == oldClip.Clip.Id)
                            Transfer(clip.Effects, clip.Descriptions, oldClip.Effects, oldClip.Descriptions);
                break;
            }
    }

    public void Reset()
    {
        _nextFrame = null;
        foreach (var track in _tracks)
        {
            foreach (var effect in track.Effects) effect.Reset();
            foreach (var clip in track.Clips)
                foreach (var effect in clip.Effects) effect.Reset();
        }
    }
}
