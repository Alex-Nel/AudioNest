using AudioNest.Domain;
using AudioNest.Hardware;

namespace AudioNest.Audio.Effects;

/// <summary>Control-thread preparation contract, never analyzed in the callback.</summary>
public interface IPeakNormalizer : IAudioProcessor
{
    void SetInputPeak(double peak);
}

internal sealed class PeakNormalizerProcessor(float target) : IPeakNormalizer
{
    private double _gain = 1;
    public void SetInputPeak(double peak) => _gain = peak > 0 ? target / peak : 1;
    public void Process(Span<float> samples)
    {
        for (var i = 0; i < samples.Length; i++) samples[i] = (float)(samples[i] * _gain);
    }
    public void Reset() { }
    public void AdvanceSilence(long frames) { }
}

/// <summary>Stereo-linked peak compressor, smoothed gain reduction in dB.
/// No lookahead, latency, tail, allocation, or per-block envelope resets.</summary>
internal sealed class CompressorProcessor : IAudioProcessor
{
    private readonly EffectSpec _spec;
    private readonly int _channels;
    private readonly double _attack;
    private readonly double _release;
    private double _reductionDb;
    public CompressorProcessor(EffectSpec spec, PcmFormat format)
    {
        _spec = spec;
        _channels = format.Channels;
        _attack = Math.Exp(-1 / (spec.AttackMs * 0.001 * format.SampleRate));
        _release = Math.Exp(-1 / (spec.ReleaseMs * 0.001 * format.SampleRate));
    }
    public void Process(Span<float> samples)
    {
        for (var frame = 0; frame < samples.Length; frame += _channels)
        {
            var peak = 0d;
            for (var ch = 0; ch < _channels; ch++) peak = Math.Max(peak, Math.Abs(samples[frame + ch]));
            var level = 20 * Math.Log10(Math.Max(peak, 1e-30));
            var over = Math.Max(0, level - _spec.Value);
            var target = -over * (1 - 1 / _spec.Ratio);
            var coefficient = target < _reductionDb ? _attack : _release;
            _reductionDb = target + coefficient * (_reductionDb - target);
            var gain = Math.Pow(10, (_reductionDb + _spec.MakeupDb) / 20);
            for (var ch = 0; ch < _channels; ch++) samples[frame + ch] *= (float)gain;
        }
    }
    public void Reset() => _reductionDb = 0;
    public void AdvanceSilence(long frames) => _reductionDb *= Math.Pow(_release, frames);
    public void CopyStateFrom(IAudioProcessor previous)
    {
        if (previous is CompressorProcessor compressor) _reductionDb = compressor._reductionDb;
    }
}

/// <summary>Zero-lookahead, stereo-linked peak limiter: instantaneous attenuation
/// to the ceiling and exponential recovery. The legacy clipper remains separate.</summary>
internal sealed class LimiterProcessor : IAudioProcessor
{
    private readonly float _ceiling;
    private readonly int _channels;
    private readonly double _release;
    private double _gain = 1;
    public LimiterProcessor(EffectSpec spec, PcmFormat format)
    {
        _ceiling = spec.Value;
        _channels = format.Channels;
        _release = Math.Exp(-1 / (spec.ReleaseMs * 0.001 * format.SampleRate));
    }
    public void Process(Span<float> samples)
    {
        for (var frame = 0; frame < samples.Length; frame += _channels)
        {
            var peak = 0d;
            for (var ch = 0; ch < _channels; ch++) peak = Math.Max(peak, Math.Abs(samples[frame + ch]));
            var required = peak > _ceiling ? _ceiling / peak : 1;
            _gain = Math.Min(required, 1 + _release * (_gain - 1));
            for (var ch = 0; ch < _channels; ch++) samples[frame + ch] *= (float)_gain;
        }
    }
    public void Reset() => _gain = 1;
    public void AdvanceSilence(long frames) => _gain = 1 + Math.Pow(_release, frames) * (_gain - 1);
    public void CopyStateFrom(IAudioProcessor previous)
    {
        if (previous is LimiterProcessor limiter) _gain = limiter._gain;
    }
}
