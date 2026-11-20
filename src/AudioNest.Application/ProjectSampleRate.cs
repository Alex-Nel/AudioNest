using System.Collections.Immutable;
using AudioNest.Domain;

namespace AudioNest.Application;

/// <summary>Pure clock conversion. Audio is decoded separately from original
/// media; frame boundaries are quantized to the nearest new sample frame.</summary>
public static class ProjectSampleRate
{
    public static void Validate(int rate)
    {
        if (rate is < 8000 or > 192000)
            throw new ArgumentOutOfRangeException(nameof(rate), "Sample rate must be between 8,000 and 192,000 Hz.");
    }
    private static Int128 Scaled(long frame, int from, int to)
    {
        Validate(from);
        Validate(to);
        if (frame < 0) throw new ArgumentOutOfRangeException(nameof(frame));
        return ((Int128)frame * to + from / 2) / from;
    }
    public static long Frame(long frame, int from, int to) => checked((long)Scaled(frame, from, to));
    public static long Cursor(long frame, int from, int to) => (long)Int128.Min(long.MaxValue, Scaled(frame, from, to));
    public static AudioProject Change(AudioProject project, int rate, IReadOnlyDictionary<Guid, long> decodedFrames)
    {
        Validate(rate);
        if (rate == project.SampleRate) return project;
        long Scale(long frame) => Frame(frame, project.SampleRate, rate);
        var assets = project.Assets.Select(a =>
        {
            if (!decodedFrames.TryGetValue(a.Id, out var frames) || frames <= 0)
                throw new InvalidDataException("Missing converted audio.");
            // Different decoders can round resampled asset lengths differently.
            var tolerance = (long)Math.Ceiling(rate / (double)project.SampleRate) + 2;
            if (Math.Abs(frames - Scale(a.FrameCount)) > tolerance)
                throw new InvalidDataException("Project media length has changed.");
            return a with { FrameCount = frames };
        }).ToImmutableArray();
        var lengths = assets.ToDictionary(a => a.Id, a => a.FrameCount);
        AudioClip Convert(AudioClip clip)
        {
            var start = Scale(clip.Start);
            var end = Scale(clip.End);
            var offset = Math.Min(Scale(clip.SourceOffset), lengths[clip.AssetId] - 1);
            var length = Math.Min(Math.Max(1, end - start), lengths[clip.AssetId] - offset);
            if (length > long.MaxValue - start) throw new OverflowException("Converted clip exceeds the timeline limit.");
            return clip with { Start = start, SourceOffset = offset, Length = length };
        }
        var next = project with
        {
            SampleRate = rate,
            Assets = assets,
            Tracks = project.Tracks.Select(t => t with { Clips = t.Clips.Select(Convert).ToImmutableArray() }).ToImmutableArray()
        };
        next.Validate();
        return next;
    }
}
