using System.Collections.Immutable;
using AudioNest.Domain;

namespace AudioNest.Application;

/// <summary>Pure edit operations; invalid edits cannot partially mutate a project.</summary>
public static class ProjectEdits
{
    public static AudioProject AddTrack(AudioProject project) => AddTrack(project, TrackChannelLayout.Stereo);
    public static AudioProject AddTrack(AudioProject project, TrackChannelLayout layout) =>
        project with { Tracks = project.Tracks.Add(new AudioTrack { Name = $"Track {project.Tracks.Length + 1}", Layout = layout }) };

    public static AudioProject DeleteTrack(AudioProject project, Guid id)
    {
        if (!project.Tracks.Any(t => t.Id == id)) throw new ArgumentException("Track not found.", nameof(id));
        return project with { Tracks = project.Tracks.RemoveAll(t => t.Id == id) };
    }

    public static AudioProject UpdateTrack(AudioProject project, Guid id, Func<AudioTrack, AudioTrack> edit)
    {
        var index = project.Tracks.FindIndex(t => t.Id == id);
        if (index < 0) throw new ArgumentException("Track not found.", nameof(id));
        var next = edit(project.Tracks[index]);
        return next == project.Tracks[index] ? project : project with { Tracks = project.Tracks.SetItem(index, next) };
    }

    public static AudioProject UpdateClip(AudioProject project, Guid id, Func<AudioClip, AudioClip> edit)
    {
        var track = FindTrack(project, id);
        var index = track.Clips.FindIndex(c => c.Id == id);
        var next = edit(track.Clips[index]);
        return next == track.Clips[index] ? project :
            UpdateTrack(project, track.Id, t => t with { Clips = t.Clips.SetItem(index, next) });
    }

    public static AudioTrack FindTrack(AudioProject project, Guid clipId) =>
        project.Tracks.FirstOrDefault(t => t.Clips.Any(c => c.Id == clipId))
        ?? throw new ArgumentException("Clip not found.", nameof(clipId));

    public static AudioProject Import(AudioProject project, AudioAsset asset, string name, Guid trackId, long at)
    {
        var next = project with { Assets = project.Assets.Add(asset) };
        return UpdateTrack(next, trackId, t => t with
        {
            Clips = t.Clips.Add(new AudioClip
            {
                AssetId = asset.Id,
                Name = name,
                Start = at,
                Length = asset.FrameCount
            })
        });
    }

    public static AudioProject Split(AudioProject project, Guid clipId, long at)
    {
        var track = FindTrack(project, clipId);
        var clip = track.Clips.First(c => c.Id == clipId);
        if (at <= clip.Start || at >= clip.End) return project;
        var leftLength = at - clip.Start;
        var left = clip with { Length = leftLength };
        var right = clip with
        {
            Id = Guid.NewGuid(),
            Start = at,
            SourceOffset = clip.SourceOffset + leftLength,
            Length = clip.Length - leftLength
        };
        return UpdateTrack(project, track.Id, t => t with
        {
            Clips = t.Clips.SelectMany(c => c.Id == clipId ? new[] { left, right } : [c]).ToImmutableArray()
        });
    }

    public static AudioProject Delete(AudioProject project, Guid clipId) =>
        UpdateTrack(project, FindTrack(project, clipId).Id,
            t => t with { Clips = t.Clips.RemoveAll(c => c.Id == clipId) });

    public static AudioProject TrimStart(AudioProject project, Guid clipId, long at) =>
        UpdateClip(project, clipId, c => ClipTrimming.Start(c, at));

    public static AudioProject TrimEnd(AudioProject project, Guid clipId, long at) =>
        UpdateClip(project, clipId, c => ClipTrimming.End(c, project.Assets.First(a => a.Id == c.AssetId), at));

    public static AudioProject InsertClip(AudioProject project, AudioAsset asset, AudioClip clip, Guid track)
    {
        var existing = project.Assets.FirstOrDefault(a => a.Id == asset.Id);
        if (existing is not null && existing != asset) throw new ArgumentException("Asset identity collision.", nameof(asset));
        var next = existing is null ? project with { Assets = project.Assets.Add(asset) } : project;
        return UpdateTrack(next, track, t => t with { Clips = t.Clips.Add(clip) });
    }

    public static AudioProject Move(AudioProject project, Guid clipId, Guid destinationTrack, long start)
    {
        if (start < 0) throw new ArgumentOutOfRangeException(nameof(start));
        var clip = FindTrack(project, clipId).Clips.First(c => c.Id == clipId);
        return UpdateTrack(Delete(project, clipId), destinationTrack,
            t => t with { Clips = t.Clips.Add(clip with { Start = start }) });
    }

    private static int FindIndex<T>(this ImmutableArray<T> items, Func<T, bool> predicate)
    {
        for (var i = 0; i < items.Length; i++)
            if (predicate(items[i])) return i;
        return -1;
    }
}
