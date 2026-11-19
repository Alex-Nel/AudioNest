using System.ComponentModel;
using AudioNest.Domain;

namespace AudioNest.Desktop;

/// <summary>Common section lifecycle only. Future multi-parameter effects derive
/// directly from this class and supply their own view and parameter properties.</summary>
public abstract class ClipEffectEditorViewModel : INotifyPropertyChanged
{
    protected readonly MainViewModel Owner;
    protected EffectSpec Spec;
    private EffectSpec? _preview;
    protected EffectSpec Current => _preview ?? Spec;
    protected ClipEffectEditorViewModel(MainViewModel owner, Guid clip, int index, EffectSpec spec, bool isTrack = false)
    {
        Owner = owner;
        ClipId = clip;
        Target = new(clip, isTrack);
        Index = index;
        Spec = spec;
        Remove = new(() => Commit(null), () => CanEdit);
    }
    public Guid ClipId { get; }
    public EffectTarget Target { get; }
    public int Index { get; }
    public EffectKind Kind => Spec.Kind;
    public abstract string Name { get; }
    public bool CanEdit => Owner.HasEffectTarget && Owner.FocusedTarget == Target && Owner.Inspector.Effects.Contains(this);
    public IReadOnlyList<EffectParameterViewModel> Parameters { get; protected set; } = [];
    public bool Enabled
    {
        get => !Spec.Bypassed;
        set { if (CanEdit && value != Enabled) Commit(Spec with { Bypassed = !value }); }
    }
    public UiCommand Remove { get; }
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Changed()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        foreach (var parameter in Parameters) parameter.Refresh();
    }
    protected void Commit(EffectSpec? replacement)
    {
        if (CanEdit) Owner.ChangeEffect(Target, Index, Spec, replacement);
    }
    internal double Read(Func<EffectSpec, float> read) => read(Current);
    internal void Set(Func<EffectSpec, float, EffectSpec> change, float value)
    {
        if (!CanEdit) return;
        var next = change(Current, value);
        if (next == Current) return;
        if (_preview is not null)
        {
            _preview = next;
            Owner.PreviewEffect(Target, Index, Spec, next);
            Changed();
        }
        else Commit(next);
    }
    public void BeginAdjustment() { if (CanEdit) _preview = Spec; }
    public void CancelAdjustment()
    {
        if (_preview is null) return;
        _preview = null;
        Owner.RestoreMixPreview();
        Changed();
    }
    public void EndAdjustment()
    {
        var next = _preview;
        _preview = null;
        if (next is not null)
        {
            if (CanEdit) Commit(next);
            else Owner.RestoreMixPreview();
        }
        Changed();
    }
    public virtual void Refresh(EffectSpec spec) { Spec = spec; Changed(); Remove.Refresh(); }
}

/// <summary>Reusable one-parameter gesture handling, separate from the section contract.</summary>
public abstract class SingleParameterEffectEditorViewModel : ClipEffectEditorViewModel
{
    protected SingleParameterEffectEditorViewModel(MainViewModel owner, Guid clip, int index, EffectSpec spec, bool isTrack = false)
        : base(owner, clip, index, spec, isTrack) { }
    protected abstract double Minimum { get; }
    protected abstract double Maximum { get; }
    protected abstract double Step { get; }
    public double Value
    {
        get => Current.Value;
        set
        {
            if (!CanEdit || !double.IsFinite(value)) return;
            value = Math.Clamp(Math.Round(value / Step, MidpointRounding.AwayFromZero) * Step, Minimum, Maximum);
            if (Math.Abs(value - Value) < 0.00001) return;
            Set((spec, v) => spec with { Value = v }, (float)value);
        }
    }
    public abstract string ValueText { get; }
}

public sealed class GainEffectEditorViewModel : SingleParameterEffectEditorViewModel
{
    public GainEffectEditorViewModel(MainViewModel owner, Guid clip, int index, EffectSpec spec, bool isTrack = false)
        : base(owner, clip, index, spec, isTrack) { }
    public override string Name => "Gain";
    protected override double Minimum => 0;
    protected override double Maximum => 16;
    protected override double Step => 0.05;
    public override string ValueText => $"{Value:0.00}×";
}

public sealed class LimiterEffectEditorViewModel : SingleParameterEffectEditorViewModel
{
    public LimiterEffectEditorViewModel(MainViewModel owner, Guid clip, int index, EffectSpec spec, bool isTrack = false)
        : base(owner, clip, index, spec, isTrack)
    {
        if (spec.Kind == EffectKind.Limiter)
            Parameters = [EffectParameterViewModel.Release(this)];
    }
    public override string Name => BuiltInEffects.Name(Kind);
    protected override double Minimum => 0.01;
    protected override double Maximum => 1;
    protected override double Step => 0.01;
    public override string ValueText => $"{20 * Math.Log10(Value):0.0} dBFS";
}

/// <summary>Explicit, AOT-safe editor registration; pair new entries with typed XAML templates.</summary>
public static class ClipEffectEditorFactory
{
    public static ClipEffectEditorViewModel Create(MainViewModel owner, Guid clip, int index, EffectSpec spec, bool isTrack = false) => spec.Kind switch
    {
        EffectKind.Gain => new GainEffectEditorViewModel(owner, clip, index, spec, isTrack),
        EffectKind.HardLimiter or EffectKind.Limiter => new LimiterEffectEditorViewModel(owner, clip, index, spec, isTrack),
        EffectKind.Compressor => new CompressorEffectEditorViewModel(owner, clip, index, spec, isTrack),
        EffectKind.Normalizer => new NormalizerEffectEditorViewModel(owner, clip, index, spec, isTrack),
        _ => throw new NotSupportedException($"No clip editor registered for {spec.Kind}.")
    };
}

public sealed class EffectParameterViewModel(ClipEffectEditorViewModel owner, string name, double minimum, double maximum,
    double step, Func<EffectSpec, float> read, Func<EffectSpec, float, EffectSpec> write, Func<double, string> display) : INotifyPropertyChanged
{
    public string Name => name;
    public double Minimum => minimum;
    public double Maximum => maximum;
    public double Step => step;
    public double Value
    {
        get => owner.Read(read);
        set
        {
            if (!double.IsFinite(value)) return;
            owner.Set(write, (float)Math.Clamp(Math.Round(value / step, MidpointRounding.AwayFromZero) * step, minimum, maximum));
        }
    }
    public string ValueText => display(Value);
    public void BeginAdjustment() => owner.BeginAdjustment();
    public void EndAdjustment() => owner.EndAdjustment();
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    public static EffectParameterViewModel Release(ClipEffectEditorViewModel owner) =>
        new(owner, "Release", 1, 2000, 1, s => s.ReleaseMs, (s, v) => s with { ReleaseMs = v }, v => $"{v:0} ms");
}

public sealed class CompressorEffectEditorViewModel : ClipEffectEditorViewModel
{
    public CompressorEffectEditorViewModel(MainViewModel owner, Guid id, int index, EffectSpec spec, bool isTrack = false)
        : base(owner, id, index, spec, isTrack)
    {
        Parameters =
        [
            new(this, "Threshold", -60, 0, 0.5, s => s.Value, (s, v) => s with { Value = v }, v => $"{v:0.0} dB"),
            new(this, "Ratio", 1, 20, 0.1, s => s.Ratio, (s, v) => s with { Ratio = v }, v => $"{v:0.0}:1"),
            new(this, "Attack", 0.1, 200, 0.1, s => s.AttackMs, (s, v) => s with { AttackMs = v }, v => $"{v:0.0} ms"),
            EffectParameterViewModel.Release(this),
            new(this, "Makeup", 0, 24, 0.5, s => s.MakeupDb, (s, v) => s with { MakeupDb = v }, v => $"{v:0.0} dB")
        ];
    }
    public override string Name => "Compressor";
}

public sealed class NormalizerEffectEditorViewModel : SingleParameterEffectEditorViewModel
{
    public NormalizerEffectEditorViewModel(MainViewModel owner, Guid id, int index, EffectSpec spec, bool isTrack = false)
        : base(owner, id, index, spec, isTrack) { }
    public override string Name => "Normalizer";
    protected override double Minimum => 0.01;
    protected override double Maximum => 1;
    protected override double Step => 0.01;
    public override string ValueText => $"{20 * Math.Log10(Value):0.0} dBFS";
}
