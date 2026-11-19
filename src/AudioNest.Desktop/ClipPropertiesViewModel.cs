using System.ComponentModel;
using System.Globalization;
using AudioNest.Domain;

namespace AudioNest.Desktop;

/// <summary>Immediate focused clip/track properties; invalid text stays local, while slider
/// gestures preview the live graph and commit one undoable edit on release.</summary>
public sealed class ClipPropertiesViewModel(MainViewModel owner) : INotifyPropertyChanged
{
    private AudioClip? _clip;
    private AudioTrack? _track;
    private EffectTarget? Target => _track is { } track ? new(track.Id, true) : _clip is { } clip ? new(clip.Id) : null;
    private string _name = "";
    private string _startText = "";
    private bool _writingStart;
    private double? _previewVolume;
    private double? _previewPan;
    private EffectTarget? _adjustingTarget;
    public bool IsTrack => _track is not null;
    public bool ShowTimelineStart => !IsTrack;
    public string NameLabel => IsTrack ? "Track name" : "Clip name";
    public string VolumeLabel => IsTrack ? "Track volume" : "Clip volume";
    public string PanLabel => IsTrack ? "Track stereo balance" : "Clip stereo balance";
    public string Name
    {
        get => _name;
        set
        {
            if (_name == value) return;
            _name = value ?? "";
            if (CanEdit && ValidName)
            {
                if (_track is { } track) owner.SetTrackName(track.Id, _name);
                else owner.SetClipName(_clip!.Id, _name);
            }
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
        get => _track?.Muted ?? _clip?.Muted ?? false;
        set
        {
            if (!CanEdit || value == Muted) return;
            if (_track is { } track) owner.SetTrackMuted(track.Id, value);
            else owner.SetClipMuted(_clip!.Id, value);
        }
    }
    public bool Locked
    {
        get => _clip?.Locked ?? false;
        set { if (CanEdit && !IsTrack && value != Locked) owner.SetClipLocked(_clip!.Id, value); }
    }
    public bool Solo
    {
        get => _track?.Solo ?? false;
        set { if (CanEdit && _track is { } track && value != Solo) owner.SetTrackSolo(track.Id, value); }
    }
    public bool SecondaryState { get => IsTrack ? Solo : Locked; set { if (IsTrack) Solo = value; else Locked = value; } }
    public string SecondaryStateLabel => IsTrack ? "Solo" : "Locked";
    public string SecondaryStateHint => IsTrack ? "Only soloed tracks play" : "Prevent moving or trimming this clip";
    public bool CanEdit => owner.HasEffectTarget && owner.FocusedTarget == Target;
    public bool CanMove => CanEdit && !IsTrack && !Locked;
    public bool CanPan => CanEdit && owner.SelectedTrack?.Layout == TrackChannelLayout.Stereo;
    public double Volume
    {
        get => _previewVolume ?? Math.Clamp(_track?.Volume ?? _clip?.Volume ?? 1, 0, 2);
        set
        {
            if (!CanEdit || !double.IsFinite(value)) return;
            value = Step(value, 0, 2);
            if (Math.Abs(value - Volume) < 0.00001) return;
            if (_previewVolume.HasValue)
            {
                _previewVolume = value;
                PreviewMix();
                Changed();
            }
            else SetMix(value, null);
        }
    }
    public double Pan
    {
        get => CanPan ? _previewPan ?? _track?.Pan ?? _clip?.Pan ?? 0 : 0;
        set
        {
            if (!CanPan || !double.IsFinite(value)) return;
            value = Step(value, -1, 1);
            if (Math.Abs(value - Pan) < 0.00001) return;
            if (_previewPan.HasValue)
            {
                _previewPan = value;
                PreviewMix();
                Changed();
            }
            else SetMix(null, value);
        }
    }
    public string VolumeText => $"{_previewVolume ?? _track?.Volume ?? _clip?.Volume ?? 1:0.00}×";
    private void SetMix(double? volume, double? pan)
    {
        if (_track is { } track) owner.SetTrackMix(track.Id, volume, pan);
        else if (_clip is { } clip) owner.SetClipMix(clip.Id, volume, pan);
    }
    private void PreviewMix()
    {
        if (_track is { } track) owner.PreviewTrackMix(track.Id, _previewVolume, _previewPan);
        else if (_clip is { } clip) owner.PreviewClipMix(clip.Id, _previewVolume, _previewPan);
    }
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
    public string Error => Target is null ? "" : !ValidName ? "Use a name of 1–120 characters." :
        !IsTrack && !TryStart(out _) ? "Enter a valid, non-negative start time in seconds." : "";
    public bool HasError => Error.Length > 0;
    public event PropertyChangedEventHandler? PropertyChanged;
    public void BeginAdjustment(bool pan)
    {
        if (pan ? !CanPan : !CanEdit) return;
        _adjustingTarget = Target;
        if (pan) _previewPan = Pan; else _previewVolume = Volume;
    }
    public void EndAdjustment()
    {
        var volume = _previewVolume;
        var pan = _previewPan;
        var target = _adjustingTarget;
        _previewVolume = _previewPan = null;
        _adjustingTarget = null;
        if (target is not null)
        {
            if (CanEdit && Target == target) SetMix(volume, pan);
            else owner.RestoreMixPreview();
        }
        Changed();
    }
    public void Refresh(AudioClip? clip, AudioTrack? track = null)
    {
        EffectTarget? nextTarget = track is not null ? new(track.Id, true) : clip is not null ? new(clip.Id) : null;
        var selectionChanged = nextTarget != Target;
        if (selectionChanged && _adjustingTarget is not null)
        {
            _previewVolume = _previewPan = null;
            _adjustingTarget = null;
            owner.RestoreMixPreview();
        }
        if (selectionChanged || (track?.Name ?? clip?.Name) != (_track?.Name ?? _clip?.Name)) _name = track?.Name ?? clip?.Name ?? "";
        if (selectionChanged || clip?.Start != _clip?.Start && !_writingStart)
            _startText = clip is null ? "" : ((decimal)clip.Start / owner.Project.SampleRate).ToString("0.######", CultureInfo.CurrentCulture);
        _clip = clip;
        _track = track;
        Changed();
    }
    private void Changed() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
}
