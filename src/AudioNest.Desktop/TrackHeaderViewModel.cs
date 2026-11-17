using System.ComponentModel;
using AudioNest.Domain;

namespace AudioNest.Desktop;

/// <summary>Stable per-track presentation state: edits never recreate focused controls.</summary>
public sealed class TrackHeaderViewModel : INotifyPropertyChanged
{
    private readonly MainViewModel _owner;
    public TrackHeaderViewModel(MainViewModel owner, Guid id)
    {
        _owner = owner;
        Id = id;
        Mute = Action(() => owner.ToggleTrackMute(Id));
        Solo = Action(() => owner.ToggleTrackSolo(Id));
        Delete = Action(() => owner.DeleteTrack(Id));
        Quieter = Action(() => owner.ChangeTrackGain(Id, -3));
        Louder = Action(() => owner.ChangeTrackGain(Id, 3));
        PanLeft = Action(() => owner.SetTrackPan(Id, Track.Pan - 0.25f));
        PanCenter = Action(() => owner.SetTrackPan(Id, 0));
        PanRight = Action(() => owner.SetTrackPan(Id, Track.Pan + 0.25f));
    }
    public Guid Id { get; }
    private AudioTrack Track => _owner.Project.Tracks.First(t => t.Id == Id);
    public string Name => Track.Name;
    public bool IsSelected => _owner.SelectedTrackId == Id;
    public bool IsMuted => Track.Muted;
    public bool IsSolo => Track.Solo;
    public bool CanEdit => _owner.CanEdit;
    public string GainText => $"{20 * Math.Log10(Math.Max(0.0001, Track.Effects.FirstOrDefault(e => e.Kind == EffectKind.Gain)?.Value ?? 1)):0.0} dB";
    public string PanText => Track.Pan == 0 ? "Center" : $"{(Track.Pan < 0 ? "L" : "R")} {Math.Abs(Track.Pan):P0}";
    public UiCommand Mute { get; }
    public UiCommand Solo { get; }
    public UiCommand Delete { get; }
    public UiCommand Quieter { get; }
    public UiCommand Louder { get; }
    public UiCommand PanLeft { get; }
    public UiCommand PanCenter { get; }
    public UiCommand PanRight { get; }
    public event PropertyChangedEventHandler? PropertyChanged;
    private UiCommand Action(Action action) => new(() =>
    {
        _owner.Select(Id, null);
        action();
    }, () => _owner.CanEdit);
    public void Select() => _owner.Select(Id, null);
    public void Refresh()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        foreach (var command in new[] { Mute, Solo, Delete, Quieter, Louder, PanLeft, PanCenter, PanRight }) command.Refresh();
    }
}
