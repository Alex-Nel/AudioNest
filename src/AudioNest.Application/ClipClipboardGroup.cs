using System.Collections.Immutable;
using AudioNest.Domain;

namespace AudioNest.Application;

public sealed record ClipboardClip(ClipClipboardItem Item, int TrackOffset);
public sealed record ClipClipboardGroup(ImmutableArray<ClipboardClip> Clips,
    ImmutableArray<TrackChannelLayout> Layouts, long Origin, int SampleRate)
{
    public static ClipClipboardGroup Capture(AudioProject project, IReadOnlySet<Guid> ids)
    {
        var indices = project.Tracks.Select((t, i) => (Track: t, Index: i))
            .Where(t => t.Track.Clips.Any(c => ids.Contains(c.Id))).Select(t => t.Index).ToArray();
        if (indices.Length == 0) throw new ArgumentException("No clips selected.", nameof(ids));
        var first = indices.Min();
        var last = indices.Max();
        var clips = project.Tracks.SelectMany((t, i) => t.Clips.Where(c => ids.Contains(c.Id))
            .Select(c => new ClipboardClip(ClipClipboardItem.Capture(project, c.Id), i - first))).ToImmutableArray();
        return new(clips, project.Tracks.Skip(first).Take(last - first + 1).Select(t => t.Layout).ToImmutableArray(),
            clips.Min(c => c.Item.Clip.Start), project.SampleRate);
    }
}
