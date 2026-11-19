using System.Collections.ObjectModel;
using System.ComponentModel;
using AudioNest.Domain;

namespace AudioNest.Desktop;

/// <summary>Owns a permanent properties section and ordered, typed effect editors.
/// Editors remain stable across clock/selection notifications and parameter changes.</summary>
public sealed class ClipInspectorViewModel : INotifyPropertyChanged
{
    private readonly MainViewModel _owner;
    private EffectTarget? _target;
    public ClipInspectorViewModel(MainViewModel owner)
    {
        _owner = owner;
        Properties = new ClipPropertiesViewModel(owner);
    }
    public ClipPropertiesViewModel Properties { get; }
    public string Title => _owner.IsTrackFocused ? "Track Inspector" : "Clip Inspector";
    public string ClipName => _owner.SelectedClip?.Name ?? "No clip selected";
    public string SelectionText => _owner.IsTrackFocused && _owner.SelectedTrack is { } track
        ? $"{track.Clips.Length} {(track.Clips.Length == 1 ? "clip" : "clips")} · {track.Layout}"
        : _owner.SelectedClip is { } clip
        ? $"Length: {(double)clip.Length / _owner.Project.SampleRate:0.000} s\nSource in: {(double)clip.SourceOffset / _owner.Project.SampleRate:0.000} s"
        : "Select a clip or track to inspect its properties and effects.";
    public bool HasClip => _owner.HasSelectedClip;
    public ObservableCollection<ClipEffectEditorViewModel> Effects { get; } = [];
    public bool HasEffects => Effects.Count > 0;
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Refresh()
    {
        var clip = _owner.SelectedClip;
        var track = _owner.IsTrackFocused ? _owner.SelectedTrack : null;
        var target = _owner.FocusedTarget;
        var effects = track?.Effects ?? clip?.Effects ?? [];
        if (_target != target || Effects.Count != effects.Length ||
            Effects.Where((editor, i) => editor.Kind != effects[i].Kind).Any())
        {
            foreach (var editor in Effects) editor.CancelAdjustment();
            Effects.Clear();
            if (target is { } selected)
                for (var i = 0; i < effects.Length; i++)
                    Effects.Add(ClipEffectEditorFactory.Create(_owner, selected.Id, i, effects[i], selected.IsTrack));
            _target = target;
        }
        for (var i = 0; i < Effects.Count; i++) Effects[i].Refresh(effects[i]);
        Properties.Refresh(clip, track);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }
}
