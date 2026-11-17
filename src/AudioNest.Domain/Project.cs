using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace AudioNest.Domain;

// Times are integer sample frames in the project clock, never UI pixels or wall time.
public sealed record AudioProject
{
    public const int CurrentVersion = 2;
    public int Version { get; init; } = CurrentVersion;
    public string Name { get; init; } = "Untitled";
    public int SampleRate { get; init; } = 48000;
    public ImmutableArray<AudioAsset> Assets { get; init; } = [];
    public ImmutableArray<AudioTrack> Tracks { get; init; } = [];

    [JsonIgnore]
    public long Duration => Tracks.SelectMany(t => t.Clips).Select(c => c.End).DefaultIfEmpty().Max();

    public void Validate()
    {
        if (Version != CurrentVersion)
            throw new InvalidDataException($"Unsupported project version {Version}.");
        if (string.IsNullOrWhiteSpace(Name) || SampleRate is < 8000 or > 192000 ||
            Assets.IsDefault || Tracks.IsDefault)
            throw new InvalidDataException("Invalid project header.");
        var ids = new HashSet<Guid>();
        foreach (var asset in Assets)
        {
            if (asset is null || asset.Id == Guid.Empty || !ids.Add(asset.Id) || string.IsNullOrWhiteSpace(asset.Path) ||
                asset.FrameCount <= 0)
                throw new InvalidDataException("Invalid or duplicate audio asset.");
        }
        foreach (var track in Tracks)
        {
            if (track is null || track.Id == Guid.Empty || !ids.Add(track.Id) || string.IsNullOrWhiteSpace(track.Name) ||
                track.Clips.IsDefault || !float.IsFinite(track.Pan) || track.Pan is < -1 or > 1 ||
                !Enum.IsDefined(track.Layout))
                throw new InvalidDataException("Invalid or duplicate track.");
            ValidateEffects(track.Effects);
            foreach (var clip in track.Clips)
            {
                if (clip is null) throw new InvalidDataException("Null clip.");
                var asset = Assets.FirstOrDefault(a => a.Id == clip.AssetId);
                if (clip.Id == Guid.Empty || !ids.Add(clip.Id) || asset is null ||
                    string.IsNullOrWhiteSpace(clip.Name) || clip.Start < 0 || clip.SourceOffset < 0 ||
                    clip.Length <= 0 || clip.Length > long.MaxValue - clip.Start ||
                    clip.SourceOffset > asset.FrameCount || clip.Length > asset.FrameCount - clip.SourceOffset)
                    throw new InvalidDataException("Invalid clip bounds or missing asset reference.");
                ValidateEffects(clip.Effects);
            }
        }
    }

    private static void ValidateEffects(ImmutableArray<EffectSpec> effects)
    {
        if (effects.IsDefault) throw new InvalidDataException("Missing effect chain.");
        foreach (var effect in effects)
        {
            if (effect is null || !float.IsFinite(effect.Value) || effect.Kind switch
            {
                EffectKind.Gain => effect.Value is < 0 or > 16,
                EffectKind.HardLimiter => effect.Value is <= 0 or > 1,
                _ => true
            })
                throw new InvalidDataException("Unsupported effect or invalid effect parameter.");
        }
    }
}

// Paths are absolute in a live session; persistence rebases them to portable relative paths.
public sealed record AudioAsset(Guid Id, string Path, long FrameCount);

public sealed record AudioTrack
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "Audio track";
    public bool Muted { get; init; }
    public bool Solo { get; init; }
    public TrackChannelLayout Layout { get; init; } = TrackChannelLayout.Stereo;
    // Stereo balance: -1 is left, 0 preserves both channels, +1 is right.
    public float Pan { get; init; }
    public ImmutableArray<EffectSpec> Effects { get; init; } = [];
    public ImmutableArray<AudioClip> Clips { get; init; } = [];
}

public enum TrackChannelLayout { Stereo, Mono }

public sealed record AudioClip
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid AssetId { get; init; }
    public string Name { get; init; } = "Audio clip";
    public long Start { get; init; }
    public long SourceOffset { get; init; }
    public long Length { get; init; }
    public ImmutableArray<EffectSpec> Effects { get; init; } = [];

    [JsonIgnore]
    public long End => checked(Start + Length);
}

public enum EffectKind { Gain, HardLimiter }

// Ordered, serializable descriptions, not live DSP instances or third-party plugin objects.
public sealed record EffectSpec(EffectKind Kind, float Value, bool Bypassed = false);
