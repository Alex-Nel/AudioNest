using System.ComponentModel;
using System.Globalization;
using AudioNest.Domain;

namespace AudioNest.Desktop;

/// <summary>Immediate clip-only properties; invalid text stays local, while slider
/// gestures preview the live graph and commit one undoable edit on release.</summary>
public sealed class ClipPropertiesViewModel(MainViewModel owner) : INotifyPropertyChanged
{
    private AudioClip? _clip;
    private string _name = "";
    private string _startText = "";
    private bool _writingStart;
    private double? _previewVolume;
    private double? _previewPan;
    private Guid? _adjustingClip;
    public string Name
    {
        get => _name;
        set
        {
            if (_name == value) return;
            _name = value ?? "";
            if (CanEdit && ValidName) owner.SetClipName(_clip!.Id, _name);
            Changed();
        }
    }
    public string StartText
    {
        get => _startText;
        set
        {
            if (_startText == value) return;
            _startText = value ?? "";
            if (CanMove && TryStart(out var frame) && frame != _clip!.Start)
            {
                _writingStart = true;
                try { owner.SetClipStart(_clip.Id, frame); }
                finally { _writingStart = false; }
            }
            Changed();
        }
    }
    public bool Muted
    {
        get => _clip?.Muted ?? false;
        set { if (CanEdit && value != Muted) owner.SetClipMuted(_clip!.Id, value); }
    }
    public bool Locked
    {
        get => _clip?.Locked ?? false;
        set { if (CanEdit && value != Locked) owner.SetClipLocked(_clip!.Id, value); }
    }
    public bool CanEdit => owner.HasSelectedClip && owner.SelectedClipId == _clip?.Id;
    public bool CanMove => CanEdit && !Locked;
    public bool CanPan => CanEdit && owner.SelectedTrack?.Layout == TrackChannelLayout.Stereo;
    public double Volume
    {
        get => _previewVolume ?? _clip?.Volume ?? 1;
        set
        {
            if (!CanEdit || !double.IsFinite(value)) return;
            value = Step(value, 0, 2);
            if (Math.Abs(value - Volume) < 0.00001) return;
            if (_previewVolume.HasValue)
            {
                _previewVolume = value;
                owner.PreviewClipMix(_clip!.Id, _previewVolume, _previewPan);
                Changed();
            }
            else owner.SetClipMix(_clip!.Id, value, null);
        }
    }
    public double Pan
    {
        get => CanPan ? _previewPan ?? _clip?.Pan ?? 0 : 0;
        set
        {
            if (!CanPan || !double.IsFinite(value)) return;
            value = Step(value, -1, 1);
            if (Math.Abs(value - Pan) < 0.00001) return;
            if (_previewPan.HasValue)
            {
                _previewPan = value;
                owner.PreviewClipMix(_clip!.Id, _previewVolume, _previewPan);
                Changed();
            }
            else owner.SetClipMix(_clip!.Id, null, value);
        }
    }
    public string VolumeText => $"{Volume:0.00}×";
    public string BalanceText => !CanEdit ? "C" : owner.SelectedTrack?.Layout == TrackChannelLayout.Mono ? "Mono" :
        Math.Abs(Pan) < 0.001 ? "C" : $"{(Pan < 0 ? "L" : "R")} {Math.Abs(Pan):P0}";
    private static double Step(double value, double minimum, double maximum) =>
        Math.Round(Math.Clamp(value, minimum, maximum) / 0.05, MidpointRounding.AwayFromZero) * 0.05;
    private bool ValidName => !string.IsNullOrWhiteSpace(Name) && Name.Length <= 120;
    private bool TryStart(out long frame)
    {
        frame = 0;
        const NumberStyles style = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint |
            NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite;
        if (_clip is null || !(decimal.TryParse(StartText, style, CultureInfo.CurrentCulture, out var seconds) ||
            decimal.TryParse(StartText, style, CultureInfo.InvariantCulture, out seconds)) ||
            seconds < 0 || seconds > (decimal)(long.MaxValue - _clip.Length) / owner.Project.SampleRate) return false;
        frame = (long)decimal.Round(seconds * owner.Project.SampleRate, 0, MidpointRounding.AwayFromZero);
        return true;
    }
    public string Error => _clip is null ? "" : !ValidName ? "Use a name of 1–120 characters." :
        !TryStart(out _) ? "Enter a valid, non-negative start time in seconds." : "";
    public bool HasError => Error.Length > 0;
    public event PropertyChangedEventHandler? PropertyChanged;
    public void BeginAdjustment(bool pan)
    {
        if (pan ? !CanPan : !CanEdit) return;
        _adjustingClip = _clip!.Id;
        if (pan) _previewPan = Pan; else _previewVolume = Volume;
    }
    public void EndAdjustment()
    {
        var volume = _previewVolume;
        var pan = _previewPan;
        var target = _adjustingClip;
        _previewVolume = _previewPan = null;
        _adjustingClip = null;
        if (target is { } id)
        {
            if (CanEdit && _clip!.Id == id) owner.SetClipMix(id, volume, pan);
            else owner.RestoreMixPreview();
        }
        Changed();
    }
    public void Refresh(AudioClip? clip)
    {
        var selectionChanged = clip?.Id != _clip?.Id;
        if (selectionChanged && _adjustingClip is not null)
        {
            _previewVolume = _previewPan = null;
            _adjustingClip = null;
            owner.RestoreMixPreview();
        }
        if (selectionChanged || clip?.Name != _clip?.Name) _name = clip?.Name ?? "";
        if (selectionChanged || clip?.Start != _clip?.Start && !_writingStart)
            _startText = clip is null ? "" : ((decimal)clip.Start / owner.Project.SampleRate).ToString("0.######", CultureInfo.CurrentCulture);
        _clip = clip;
        Changed();
    }
    private void Changed() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
}
