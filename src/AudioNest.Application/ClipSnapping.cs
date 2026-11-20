using AudioNest.Domain;

namespace AudioNest.Application;

public readonly record struct ClipSnap(long Start, long? Target);
public readonly record struct ClipTrimSnap(long Frame, long? Target);

/// <summary>Frame-based magnetic snapping, independent of UI coordinates and zoom.</summary>
public static class ClipSnapping
{
    public static long SnapPlayhead(AudioProject project, long proposed, long tolerance)
    {
        var position = Math.Max(0, proposed);
        var best = position;
        var distance = long.MaxValue;
        foreach (var clip in project.Tracks.SelectMany(t => t.Clips))
        {
            foreach (var edge in new[] { clip.Start, clip.End })
            {
                var delta = Math.Abs(edge - position);
                if (delta <= tolerance && delta < distance) { best = edge; distance = delta; }
            }
        }
        return best;
    }

    /// <summary>Snap only the resized edge, ignoring itself but not other selected
    /// clips. Unreachable source/one-frame bounds are never magnetic targets.</summary>
    public static ClipTrimSnap ResolveTrim(AudioProject project, AudioClip clip, long proposed, bool start,
        long playhead, long tolerance)
    {
        if (clip.Locked) return new(start ? clip.Start : clip.End, null);
        var asset = project.Assets.First(a => a.Id == clip.AssetId);
        var minimum = start ? Math.Max(0, clip.Start - clip.SourceOffset) : clip.Start + 1;
        var maximum = start ? clip.End - 1 :
            clip.Start + Math.Min(asset.FrameCount - clip.SourceOffset, long.MaxValue - clip.Start);
        var frame = Math.Clamp(proposed, minimum, maximum);
        var best = frame;
        long? guide = null;
        var distance = long.MaxValue;
        void Consider(long target)
        {
            if (target < minimum || target > maximum) return;
            var delta = Math.Abs(target - frame);
            if (delta > tolerance || delta >= distance) return;
            best = target;
            guide = target;
            distance = delta;
        }
        Consider(playhead);
        foreach (var other in project.Tracks.SelectMany(t => t.Clips))
        {
            if (other.Id == clip.Id) continue;
            Consider(other.Start);
            Consider(other.End);
        }
        return new(best, guide);
    }

    public static ClipSnap Resolve(AudioProject project, AudioClip moving, long proposedStart, long playhead, long tolerance,
        IReadOnlySet<Guid>? ignore = null)
    {
        var start = Math.Clamp(proposedStart, 0, long.MaxValue - moving.Length);
        var bestStart = start;
        long? guide = null;
        var bestDistance = long.MaxValue;

        void Consider(long target)
        {
            foreach (var candidate in new[] { target, target - moving.Length })
            {
                if (candidate < 0 || candidate > long.MaxValue - moving.Length) continue;
                var distance = Math.Abs(candidate - start);
                if (distance > tolerance || distance >= bestDistance) continue;
                bestDistance = distance;
                bestStart = candidate;
                guide = target;
            }
        }

        Consider(playhead);
        foreach (var clip in project.Tracks.SelectMany(t => t.Clips))
        {
            if (clip.Id == moving.Id || ignore?.Contains(clip.Id) == true) continue;
            Consider(clip.Start);
            Consider(clip.End);
        }
        return new ClipSnap(bestStart, guide);
    }
}
