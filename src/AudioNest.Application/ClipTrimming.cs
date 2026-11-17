using AudioNest.Domain;

namespace AudioNest.Application;

/// <summary>Non-destructive edge edits; source frames are revealed, never stretched or repeated.</summary>
public static class ClipTrimming
{
    public static AudioClip Start(AudioClip clip, long at)
    {
        var start = Math.Clamp(at, Math.Max(0, clip.Start - clip.SourceOffset), clip.End - 1);
        return start == clip.Start ? clip : clip with
        {
            Start = start,
            SourceOffset = clip.SourceOffset + (start - clip.Start),
            Length = clip.End - start
        };
    }

    public static AudioClip End(AudioClip clip, AudioAsset asset, long at)
    {
        var maximum = clip.Start + Math.Min(asset.FrameCount - clip.SourceOffset, long.MaxValue - clip.Start);
        var end = Math.Clamp(at, clip.Start + 1, maximum);
        return end == clip.End ? clip : clip with { Length = end - clip.Start };
    }
}
