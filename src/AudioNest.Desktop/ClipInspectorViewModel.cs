using System.Collections.ObjectModel;
using System.ComponentModel;
using AudioNest.Domain;

namespace AudioNest.Desktop;

/// <summary>Owns a permanent properties section and ordered, typed effect editors.
/// Editors remain stable across clock/selection notifications and parameter changes.</summary>
public sealed class ClipInspectorViewModel : INotifyPropertyChanged
{
    private readonly MainViewModel _owner;
    private Guid? _clipId;
    public ClipInspectorViewModel(MainViewModel owner)
    {
        _owner = owner;
        Properties = new ClipPropertiesViewModel(owner);
    }
    public ClipPropertiesViewModel Properties { get; }
    public string ClipName => _owner.SelectedClip?.Name ?? "No clip selected";
    public string SelectionText => _owner.SelectedClip is { } clip
        ? $"Length: {(double)clip.Length / _owner.Project.SampleRate:0.000} s\nSource in: {(double)clip.SourceOffset / _owner.Project.SampleRate:0.000} s"
        : "Select a clip to inspect its timing and effects.";
    public bool HasClip => _owner.HasSelectedClip;
    public ObservableCollection<ClipEffectEditorViewModel> Effects { get; } = [];
    public bool HasEffects => Effects.Count > 0;
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Refresh()
    {
        var clip = _owner.SelectedClip;
        var effects = clip?.Effects ?? [];
        if (_clipId != clip?.Id || Effects.Count != effects.Length ||
            Effects.Where((editor, i) => editor.Kind != effects[i].Kind).Any())
        {
            Effects.Clear();
            if (clip is not null)
                for (var i = 0; i < effects.Length; i++)
                    Effects.Add(ClipEffectEditorFactory.Create(_owner, clip.Id, i, effects[i]));
            _clipId = clip?.Id;
        }
        for (var i = 0; i < Effects.Count; i++) Effects[i].Refresh(effects[i]);
        Properties.Refresh(clip);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }
}
