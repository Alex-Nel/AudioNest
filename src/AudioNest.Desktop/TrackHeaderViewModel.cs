using System.ComponentModel;
using AudioNest.Domain;

namespace AudioNest.Desktop;

/// <summary>Stable per-track presentation state: edits never recreate focused controls.</summary>
public sealed class TrackHeaderViewModel : INotifyPropertyChanged
{
    private readonly MainViewModel _owner;
    private double? _previewVolume;
    private double? _previewPan;
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
    public string ChannelText => Track.Layout == TrackChannelLayout.Mono ? "MONO" : "STEREO";
    public bool IsSelected => _owner.FocusedTarget is not null && _owner.SelectedTrackId == Id;
    public bool IsMuted => Track.Muted;
    public bool IsSolo => Track.Solo;
    public bool CanEdit => _owner.CanEdit;
    public bool CanPan => CanEdit && Track.Layout == TrackChannelLayout.Stereo;
    public double Height => _owner.TrackHeight(Id);
    public void Resize(double height) => _owner.SetTrackHeight(Id, height);
    public string GainText => $"{20 * Math.Log10(Math.Max(0.0001, Track.Volume)):0.0} dB";
    public string PanText => Track.Pan == 0 ? "Center" : $"{(Track.Pan < 0 ? "L" : "R")} {Math.Abs(Track.Pan):P0}";
    public double Volume
    {
        get => _previewVolume ?? Math.Clamp(Track.Volume, 0, 2);
        set
        {
            if (!_owner.CanEdit || !double.IsFinite(value)) return;
            value = Math.Round(Math.Clamp(value, 0, 2) / 0.05, MidpointRounding.AwayFromZero) * 0.05;
            if (Math.Abs(value - Volume) < 0.00001) return;
            if (_previewVolume.HasValue)
            {
                _previewVolume = value;
                _owner.PreviewTrackMix(Id, _previewVolume, _previewPan);
                PreviewChanged();
            }
            else _owner.SetTrackVolume(Id, (float)value);
        }
    }
    public double Pan
    {
        get => Track.Layout == TrackChannelLayout.Mono ? 0 : _previewPan ?? Track.Pan;
        set
        {
            if (!CanPan || !double.IsFinite(value)) return;
            value = Math.Round(Math.Clamp(value, -1, 1) / 0.05, MidpointRounding.AwayFromZero) * 0.05;
            if (Math.Abs(value - Pan) < 0.00001) return;
            if (_previewPan.HasValue)
            {
                _previewPan = value;
                _owner.PreviewTrackMix(Id, _previewVolume, _previewPan);
                PreviewChanged();
            }
            else _owner.SetTrackPan(Id, (float)value);
        }
    }
    public string VolumeText => $"{_previewVolume ?? Track.Volume:0.00}×";
    public string VolumeHint => _previewVolume is null && Track.Volume > 2
        ? "Saved gain exceeds 2×. Moving this slider sets a new volume within 0–2×."
        : "Volume: 0 is silence, 1 is original, 2 is twice the amplitude";
    public string BalanceText => Track.Layout == TrackChannelLayout.Mono ? "Mono" :
        Math.Abs(Pan) < 0.001 ? "C" : $"{(Pan < 0 ? "L" : "R")} {Math.Abs(Pan):P0}";
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
    public void BeginVolumeAdjustment() { if (_owner.CanEdit) { Select(); _previewVolume = Volume; } }
    public void BeginPanAdjustment() { if (_owner.CanEdit) { Select(); _previewPan = Pan; } }
    public void EndAdjustment()
    {
        var volume = _previewVolume;
        var pan = _previewPan;
        _previewVolume = _previewPan = null;
        if (_owner.Project.Tracks.Any(t => t.Id == Id) && _owner.CanEdit)
        {
            if (volume is { } v) _owner.SetTrackVolume(Id, (float)v);
            if (pan is { } p) _owner.SetTrackPan(Id, (float)p);
        }
        PreviewChanged();
    }
    private void PreviewChanged() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    public void Refresh()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        foreach (var command in new[] { Mute, Solo, Delete, Quieter, Louder, PanLeft, PanCenter, PanRight }) command.Refresh();
    }
}
