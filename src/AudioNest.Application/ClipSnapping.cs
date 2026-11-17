using AudioNest.Domain;

namespace AudioNest.Application;

public readonly record struct ClipSnap(long Start, long? Target);

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

    public static ClipSnap Resolve(AudioProject project, AudioClip moving, long proposedStart, long playhead, long tolerance)
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
            if (clip.Id == moving.Id) continue;
            Consider(clip.Start);
            Consider(clip.End);
        }
        return new ClipSnap(bestStart, guide);
    }
}
