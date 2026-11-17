using AudioNest.Domain;

namespace AudioNest.Application;

/// <summary>Immutable session clipboard metadata, independent of selection and subsequent edits.</summary>
public sealed record ClipClipboardItem(AudioClip Clip, AudioAsset Asset, int SampleRate)
{
    public static ClipClipboardItem Capture(AudioProject project, Guid clipId)
    {
        var clip = ProjectEdits.FindTrack(project, clipId).Clips.First(c => c.Id == clipId);
        return new(clip, project.Assets.First(a => a.Id == clip.AssetId), project.SampleRate);
    }
}
