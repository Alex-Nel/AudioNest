using System.ComponentModel;
using AudioNest.Domain;

namespace AudioNest.Desktop;

/// <summary>Common section lifecycle only. Future multi-parameter effects derive
/// directly from this class and supply their own view and parameter properties.</summary>
public abstract class ClipEffectEditorViewModel : INotifyPropertyChanged
{
    protected readonly MainViewModel Owner;
    protected EffectSpec Spec;
    protected ClipEffectEditorViewModel(MainViewModel owner, Guid clip, int index, EffectSpec spec)
    {
        Owner = owner;
        ClipId = clip;
        Index = index;
        Spec = spec;
        Remove = new(() => Commit(null), () => CanEdit);
    }
    public Guid ClipId { get; }
    public int Index { get; }
    public EffectKind Kind => Spec.Kind;
    public abstract string Name { get; }
    public bool CanEdit => Owner.HasSelectedClip && Owner.SelectedClipId == ClipId && Owner.Inspector.Effects.Contains(this);
    public bool Enabled
    {
        get => !Spec.Bypassed;
        set { if (CanEdit && value != Enabled) Commit(Spec with { Bypassed = !value }); }
    }
    public UiCommand Remove { get; }
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Changed() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    protected void Commit(EffectSpec? replacement)
    {
        if (CanEdit) Owner.ChangeClipEffect(ClipId, Index, Spec, replacement);
    }
    public virtual void Refresh(EffectSpec spec) { Spec = spec; Changed(); Remove.Refresh(); }
}

/// <summary>Reusable one-parameter gesture handling, separate from the section contract.</summary>
public abstract class SingleParameterEffectEditorViewModel : ClipEffectEditorViewModel
{
    private double? _preview;
    protected SingleParameterEffectEditorViewModel(MainViewModel owner, Guid clip, int index, EffectSpec spec)
        : base(owner, clip, index, spec) { }
    protected abstract double Minimum { get; }
    protected abstract double Maximum { get; }
    protected abstract double Step { get; }
    public double Value
    {
        get => _preview ?? Spec.Value;
        set
        {
            if (!CanEdit || !double.IsFinite(value)) return;
            value = Math.Clamp(Math.Round(value / Step, MidpointRounding.AwayFromZero) * Step, Minimum, Maximum);
            if (Math.Abs(value - Value) < 0.00001) return;
            var next = Spec with { Value = (float)value };
            if (_preview.HasValue)
            {
                _preview = value;
                Owner.PreviewClipEffect(ClipId, Index, Spec, next);
                Changed();
            }
            else Commit(next);
        }
    }
    public abstract string ValueText { get; }
    public void BeginAdjustment() { if (CanEdit) _preview = Value; }
    public void EndAdjustment()
    {
        var value = _preview;
        _preview = null;
        if (value is { } v)
        {
            if (CanEdit) Commit(Spec with { Value = (float)v });
            else Owner.RestoreMixPreview();
        }
        Changed();
    }
}

public sealed class GainEffectEditorViewModel : SingleParameterEffectEditorViewModel
{
    public GainEffectEditorViewModel(MainViewModel owner, Guid clip, int index, EffectSpec spec)
        : base(owner, clip, index, spec) { }
    public override string Name => "Gain";
    protected override double Minimum => 0;
    protected override double Maximum => 16;
    protected override double Step => 0.05;
    public override string ValueText => $"{Value:0.00}×";
}

public sealed class LimiterEffectEditorViewModel : SingleParameterEffectEditorViewModel
{
    public LimiterEffectEditorViewModel(MainViewModel owner, Guid clip, int index, EffectSpec spec)
        : base(owner, clip, index, spec) { }
    public override string Name => "Hard limiter";
    protected override double Minimum => 0.01;
    protected override double Maximum => 1;
    protected override double Step => 0.01;
    public override string ValueText => $"{20 * Math.Log10(Value):0.0} dBFS";
}

/// <summary>Explicit, AOT-safe editor registration; pair new entries with typed XAML templates.</summary>
public static class ClipEffectEditorFactory
{
    public static ClipEffectEditorViewModel Create(MainViewModel owner, Guid clip, int index, EffectSpec spec) => spec.Kind switch
    {
        EffectKind.Gain => new GainEffectEditorViewModel(owner, clip, index, spec),
        EffectKind.HardLimiter => new LimiterEffectEditorViewModel(owner, clip, index, spec),
        _ => throw new NotSupportedException($"No clip editor registered for {spec.Kind}.")
    };
}
