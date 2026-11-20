using AudioNest.Domain;
using AudioNest.Hardware;

namespace AudioNest.Audio.Effects;

/// <summary>
/// Causal, frequency-selective downward expansion: complementary bands use
/// stereo-linked soft suppression below a manually adjustable noise floor.
/// No learned profile, FFT latency, queued tail, callback allocation, or I/O.
/// </summary>
internal sealed class NoiseReductionProcessor : IAudioProcessor
{
    private readonly int _channels;
    private readonly int _sampleRate;
    private readonly int _bands;
    private readonly double[] _coefficients;
    private readonly double[] _lowpasses;
    private readonly double[] _noisePowers;
    private readonly double[] _gains;
    private readonly double _minimumGain;
    private readonly double _attack;
    private readonly double _release;
    private readonly bool _transparent;

    public NoiseReductionProcessor(EffectSpec spec, PcmFormat format)
    {
        format.Validate();
        _channels = format.Channels;
        _sampleRate = format.SampleRate;
        var nyquist = format.SampleRate / 2d;
        var cuts = new[] { 80d, 250d, 800d, 2500d, 7000d }.Where(f => f < nyquist * 0.8).ToArray();
        _bands = cuts.Length + 1;
        _coefficients = cuts.Select(f => Math.Exp(-2 * Math.PI * f / format.SampleRate)).ToArray();
        _lowpasses = new double[cuts.Length * _channels];
        _noisePowers = new double[_bands];
        _gains = new double[_bands];
        var noisePower = Math.Pow(10, spec.Value / 10d);
        for (var band = 0; band < _bands; band++)
        {
            var low = band == 0 ? 0 : cuts[band - 1];
            var high = band == cuts.Length ? nyquist : cuts[band];
            _noisePowers[band] = noisePower * (high - low) / nyquist;
        }
        _minimumGain = Math.Pow(10, -spec.ReductionDb / 20d);
        _attack = Math.Exp(-1 / (spec.AttackMs * 0.001 * format.SampleRate));
        _release = Math.Exp(-1 / (spec.ReleaseMs * 0.001 * format.SampleRate));
        _transparent = spec.ReductionDb == 0;
        Reset();
    }

    public void Process(Span<float> samples)
    {
        if (_transparent) return;
        // At most six bands and two channels; allocated once per invocation on
        // the stack, never once per frame or on the managed heap.
        Span<double> bands = stackalloc double[12];
        for (var frame = 0; frame < samples.Length; frame += _channels)
        {
            var silence = true;
            for (var ch = 0; ch < _channels; ch++)
            {
                double input = samples[frame + ch];
                if (input != 0) silence = false;
                var previous = 0d;
                for (var cut = 0; cut < _coefficients.Length; cut++)
                {
                    var index = cut * _channels + ch;
                    var low = input + _coefficients[cut] * (_lowpasses[index] - input);
                    _lowpasses[index] = low;
                    bands[index] = low - previous;
                    previous = low;
                }
                bands[(_bands - 1) * _channels + ch] = input - previous;
            }
            for (var band = 0; band < _bands; band++)
            {
                var peak = 0d;
                if (!silence)
                    for (var ch = 0; ch < _channels; ch++) peak = Math.Max(peak, Math.Abs(bands[band * _channels + ch]));
                var desired = Math.Sqrt(Math.Max(_minimumGain * _minimumGain, 1 - _noisePowers[band] / Math.Max(peak * peak, 1e-30)));
                var coefficient = desired > _gains[band] ? _attack : _release;
                _gains[band] = desired + coefficient * (_gains[band] - desired);
            }
            for (var ch = 0; ch < _channels; ch++)
            {
                var output = 0d;
                if (!silence)
                    for (var band = 0; band < _bands; band++) output += bands[band * _channels + ch] * _gains[band];
                // Do not print filter ringing into silent gaps or extend clips.
                samples[frame + ch] = (float)output;
            }
        }
    }

    public void Reset()
    {
        Array.Clear(_lowpasses);
        Array.Fill(_gains, _minimumGain);
    }

    public void AdvanceSilence(long frames)
    {
        if (frames <= 0 || _transparent) return;
        for (var cut = 0; cut < _coefficients.Length; cut++)
        {
            var decay = Math.Pow(_coefficients[cut], frames);
            for (var ch = 0; ch < _channels; ch++) _lowpasses[cut * _channels + ch] *= decay;
        }
        var release = Math.Pow(_release, frames);
        for (var band = 0; band < _bands; band++)
            _gains[band] = _minimumGain + release * (_gains[band] - _minimumGain);
    }

    public void CopyStateFrom(IAudioProcessor previous)
    {
        if (previous is not NoiseReductionProcessor noise || noise._sampleRate != _sampleRate || noise._channels != _channels) return;
        Array.Copy(noise._lowpasses, _lowpasses, _lowpasses.Length);
        Array.Copy(noise._gains, _gains, _gains.Length);
    }
}
