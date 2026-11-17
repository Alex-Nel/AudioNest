using System.ComponentModel;
using AudioNest.Domain;

namespace AudioNest.Desktop;

public sealed record ClipEffectRow(string Name, string Value, string State);

/// <summary>Presentation-only inspector sections; effect rows can gain dedicated editors later.</summary>
public sealed class ClipInspectorViewModel : INotifyPropertyChanged
{
    private readonly MainViewModel _owner;
    public ClipInspectorViewModel(MainViewModel owner)
    {
        _owner = owner;
        Quieter = new(() => owner.ApplyGain(-3, false), () => owner.HasSelectedClip);
        Louder = new(() => owner.ApplyGain(3, false), () => owner.HasSelectedClip);
        ToggleLimiter = new(() => owner.ToggleLimiter(false), () => owner.HasSelectedClip);
        Split = new(owner.Split, () => owner.HasSelectedClip);
        Delete = new(owner.Delete, () => owner.HasSelectedClip);
        NudgeLeft = new(() => owner.Nudge(-0.1), () => owner.HasSelectedClip);
        NudgeRight = new(() => owner.Nudge(0.1), () => owner.HasSelectedClip);
        TrackLimiter = new(() => owner.ToggleLimiter(true), () => owner.HasSelectedTrack);
    }
    public string ClipName => _owner.SelectedClip?.Name ?? "No clip selected";
    public string TrackName => _owner.SelectedTrack?.Name ?? "No track selected";
    public string SelectionText => _owner.SelectedClip is { } clip
        ? $"Start: {(double)clip.Start / _owner.Project.SampleRate:0.000} s\nLength: {(double)clip.Length / _owner.Project.SampleRate:0.000} s\nSource in: {(double)clip.SourceOffset / _owner.Project.SampleRate:0.000} s"
        : "Select a clip to inspect its timing and effects.";
    public bool HasClip => _owner.HasSelectedClip;
    public IReadOnlyList<ClipEffectRow> Effects { get; private set; } = [];
    public bool HasEffects => Effects.Count > 0;
    public UiCommand Quieter { get; }
    public UiCommand Louder { get; }
    public UiCommand ToggleLimiter { get; }
    public UiCommand Split { get; }
    public UiCommand Delete { get; }
    public UiCommand NudgeLeft { get; }
    public UiCommand NudgeRight { get; }
    public UiCommand TrackLimiter { get; }
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Refresh()
    {
        Effects = _owner.SelectedClip?.Effects.Select(e => new ClipEffectRow(
            e.Kind == EffectKind.Gain ? "Gain" : "Hard limiter",
            e.Kind == EffectKind.Gain ? $"{e.Value:0.00}×" : $"{20 * Math.Log10(e.Value):0.0} dBFS",
            e.Bypassed ? "Bypassed" : "Active")).ToArray() ?? [];
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        foreach (var command in new[] { Quieter, Louder, ToggleLimiter, Split, Delete, NudgeLeft, NudgeRight, TrackLimiter })
            command.Refresh();
    }
}
