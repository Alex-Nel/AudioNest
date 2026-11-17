using System.ComponentModel;
using AudioNest.Domain;

namespace AudioNest.Desktop;

/// <summary>Draft global metadata is applied atomically, not on every keystroke.</summary>
public sealed class ClipPropertiesViewModel : INotifyPropertyChanged
{
    private readonly MainViewModel _owner;
    private AudioClip? _clip;
    private string _name = "";
    private decimal _startSeconds;
    public ClipPropertiesViewModel(MainViewModel owner)
    {
        _owner = owner;
        Apply = new(() =>
        {
            if (CanApply && _clip is { } clip)
                owner.UpdateClipProperties(clip.Id, Name, (long)decimal.Round(StartSeconds * owner.Project.SampleRate));
        }, () => CanApply);
    }
    public string Name { get => _name; set { _name = value; Changed(); } }
    public decimal StartSeconds { get => _startSeconds; set { _startSeconds = value; Changed(); } }
    public bool CanEdit => _owner.HasSelectedClip;
    public string Error => _clip is null ? "" : string.IsNullOrWhiteSpace(Name) || Name.Trim().Length > 120
        ? "Use a name of 1–120 characters." : StartSeconds < 0 || StartSeconds > (decimal)(long.MaxValue - _clip.Length) / _owner.Project.SampleRate
            ? "Start is outside the timeline bounds." : "";
    public bool CanApply => CanEdit && Error.Length == 0;
    public UiCommand Apply { get; }
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Refresh(AudioClip? clip)
    {
        if (clip?.Id != _clip?.Id || clip?.Name != _clip?.Name || clip?.Start != _clip?.Start)
        {
            _name = clip?.Name ?? "";
            _startSeconds = (decimal)(clip?.Start ?? 0) / _owner.Project.SampleRate;
        }
        _clip = clip;
        Changed();
    }
    private void Changed()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        Apply.Refresh();
    }
}
