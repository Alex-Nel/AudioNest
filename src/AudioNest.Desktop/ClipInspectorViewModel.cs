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
    private readonly Dictionary<EffectTarget, List<ClipEffectEditorViewModel>> _editors = [];
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
    public void Reset()
    {
        foreach (var editor in Effects) editor.CancelAdjustment();
        Effects.Clear();
        _editors.Clear();
        _target = null;
    }
    public void Refresh()
    {
        var clip = _owner.SelectedClip;
        var track = _owner.IsTrackFocused ? _owner.SelectedTrack : null;
        var target = _owner.FocusedTarget;
        var effects = track?.Effects ?? clip?.Effects ?? [];
        var previous = Effects.ToArray();
        if (_target != target)
            foreach (var editor in previous) editor.CancelAdjustment();
        var next = new ClipEffectEditorViewModel?[effects.Length];
        var used = new HashSet<ClipEffectEditorViewModel>();
        if (target is { } selected)
        {
            if (!_editors.TryGetValue(selected, out var pool)) _editors[selected] = pool = [];
            // Match immutable record identities first, so reordering duplicate
            // kinds does not swap their editors or collapse states.
            for (var i = 0; i < effects.Length; i++)
            {
                next[i] = pool.FirstOrDefault(e => !used.Contains(e) && e.Matches(effects[i]));
                if (next[i] is { } match) used.Add(match);
            }
            for (var i = 0; i < effects.Length; i++)
            {
                if (next[i] is not null) continue;
                var editor = _target == target && i < previous.Length && previous[i].Kind == effects[i].Kind && !used.Contains(previous[i])
                    ? previous[i] : ClipEffectEditorFactory.Create(_owner, selected.Id, i, effects[i], selected.IsTrack);
                if (!pool.Contains(editor)) pool.Add(editor);
                next[i] = editor;
                used.Add(editor);
            }
        }
        foreach (var editor in previous)
            if (!used.Contains(editor)) editor.CancelAdjustment();
        for (var i = 0; i < next.Length; i++)
        {
            var editor = next[i]!;
            editor.Index = i;
            var oldIndex = Effects.IndexOf(editor);
            if (oldIndex < 0) Effects.Insert(i, editor);
            else if (oldIndex != i) Effects.Move(oldIndex, i);
        }
        while (Effects.Count > next.Length) Effects.RemoveAt(Effects.Count - 1);
        _target = target;
        for (var i = 0; i < Effects.Count; i++) Effects[i].Refresh(effects[i]);
        Properties.Refresh(clip, track);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }
}
