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
    private sealed record PreparedClip(AudioClip Clip, AudioBuffer Buffer, IAudioProcessor[] Effects);
    private sealed record PreparedTrack(PreparedClip[] Clips, IAudioProcessor[] Effects, float Left, float Right, bool Mono);

    public RenderPlan(AudioProject project, Func<Guid, AudioBuffer> getBuffer, IAudioEffectFactory? effects = null)
    {
        project.Validate();
        Format = new PcmFormat(project.SampleRate, 2);
        Duration = project.Duration;
        effects ??= new BuiltInEffectFactory();
        _clipScratch = new float[QuantumFrames * Format.Channels];
        _trackScratch = new float[QuantumFrames * Format.Channels];
        IAudioProcessor[] Prepare(IEnumerable<EffectSpec> chain) =>
            chain.Where(e => !e.Bypassed).Select(e => effects.Create(e, Format)).ToArray();
        var anySolo = project.Tracks.Any(t => t.Solo);
        _tracks = project.Tracks.Where(t => !t.Muted && (!anySolo || t.Solo)).Select(t =>
            new PreparedTrack(t.Clips.Select(c =>
            {
                var buffer = getBuffer(c.AssetId);
                if (buffer.Format != Format || c.SourceOffset + c.Length > buffer.FrameCount)
                    throw new InvalidDataException("Decoded audio does not match the project.");
                return new PreparedClip(c, buffer, Prepare(c.Effects));
            }).ToArray(), Prepare(t.Effects),
                t.Layout == TrackChannelLayout.Mono ? 1 : 1 - Math.Max(0, t.Pan),
                t.Layout == TrackChannelLayout.Mono ? 1 : 1 + Math.Min(0, t.Pan),
                t.Layout == TrackChannelLayout.Mono)).ToArray();
    }

    public PcmFormat Format { get; }
    public long Duration { get; }

    public void Render(long start, Span<float> output)
    {
        output.Clear();
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
                foreach (var clip in track.Clips)
                {
                    var from = Math.Max(blockStart, clip.Clip.Start);
                    var to = Math.Min(blockStart + frames, clip.Clip.End);
                    if (from >= to) continue;
                    var clipSamples = _clipScratch.AsSpan(0, (int)(to - from) * Format.Channels);
                    for (var i = 0; i < clipSamples.Length; i++)
                    {
                        var sourceFrame = clip.Clip.SourceOffset + from - clip.Clip.Start + i / Format.Channels;
                        clipSamples[i] = track.Mono
                            ? clip.Buffer.Sample(sourceFrame, 0) * 0.5f + clip.Buffer.Sample(sourceFrame, 1) * 0.5f
                            : clip.Buffer.Sample(sourceFrame, i % Format.Channels);
                    }
                    Process(clipSamples, clip.Effects);
                    var destination = (int)(from - blockStart) * Format.Channels;
                    for (var i = 0; i < clipSamples.Length; i++) trackSamples[destination + i] += clipSamples[i];
                }
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
    }

    private static void Process(Span<float> samples, IAudioProcessor[] effects)
    {
        foreach (var effect in effects) effect.Process(samples);
    }

    public void Reset()
    {
        foreach (var track in _tracks)
        {
            foreach (var effect in track.Effects) effect.Reset();
            foreach (var clip in track.Clips)
                foreach (var effect in clip.Effects) effect.Reset();
        }
    }
}
