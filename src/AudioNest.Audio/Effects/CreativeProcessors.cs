using AudioNest.Domain;
using AudioNest.Hardware;

namespace AudioNest.Audio.Effects;

/// <summary>Bounded, preallocated delay state shared by echo and algorithmic reverb.
/// Clip effects are bounded by the clip; track tails are bounded by the project.</summary>
internal abstract class DelayEffectProcessor : IAudioProcessor
{
    private readonly EffectSpec _spec;
    private readonly PcmFormat _format;
    private long _silentFrames;
    private bool _empty = true;
    protected int Channels => _format.Channels;
    protected DelayEffectProcessor(EffectSpec spec, PcmFormat format, double tailSeconds)
    {
        format.Validate();
        _spec = spec;
        _format = format;
        TailFrames = spec.Mix == 0 ? 0 : (long)Math.Ceiling(tailSeconds * format.SampleRate);
    }
    public long TailFrames { get; }
    public void Process(Span<float> samples)
    {
        if (_spec.Mix == 0) return;
        for (var frame = 0; frame < samples.Length; frame += Channels)
        {
            var silent = true;
            for (var ch = 0; ch < Channels; ch++)
                if (samples[frame + ch] != 0) silent = false;
            if (!silent) { _silentFrames = 0; _empty = false; }
            else if (_empty) continue;
            else if (++_silentFrames > TailFrames)
            {
                Reset();
                continue;
            }
            for (var ch = 0; ch < Channels; ch++)
            {
                var dry = samples[frame + ch];
                var wet = Tick(dry, ch);
                samples[frame + ch] = dry * (1 - _spec.Mix) + wet * _spec.Mix;
            }
            NextFrame();
        }
    }
    protected abstract float Tick(float input, int channel);
    protected abstract void NextFrame();
    protected abstract void ClearBuffers();
    protected abstract void CopyBuffers(DelayEffectProcessor previous);
    public void Reset()
    {
        ClearBuffers();
        _silentFrames = 0;
        _empty = true;
    }
    public void AdvanceSilence(long frames)
    {
        if (frames <= 0 || _empty || _spec.Mix == 0) return;
        if (frames > TailFrames - _silentFrames) { Reset(); return; }
        Span<float> silence = stackalloc float[256];
        while (frames > 0)
        {
            var count = (int)Math.Min(frames, silence.Length / Channels);
            var block = silence[..(count * Channels)];
            block.Clear();
            Process(block);
            frames -= count;
        }
    }
    public void CopyStateFrom(IAudioProcessor previous)
    {
        if (previous is not DelayEffectProcessor old || old.GetType() != GetType() ||
            old._format != _format || old._spec != _spec) return;
        CopyBuffers(old);
        _silentFrames = old._silentFrames;
        _empty = old._empty;
    }
}

/// <summary>Same-channel feedback delay. Feedback is capped below unity and
/// zero-input tails are cut after at most 30 seconds.</summary>
internal sealed class EchoProcessor : DelayEffectProcessor
{
    private readonly float[] _delay;
    private readonly float _feedback;
    private int _position;
    private static double TailSeconds(EffectSpec spec)
    {
        var repeats = spec.Feedback == 0 ? 1 : 1 + Math.Ceiling(Math.Log(0.0001) / Math.Log(spec.Feedback));
        return Math.Min(30, spec.Value / 1000d * repeats);
    }
    public EchoProcessor(EffectSpec spec, PcmFormat format) : base(spec, format, TailSeconds(spec))
    {
        _delay = new float[Math.Max(1, (int)Math.Round(spec.Value * format.SampleRate / 1000d)) * format.Channels];
        _feedback = spec.Feedback;
    }
    protected override float Tick(float input, int channel)
    {
        var delayed = _delay[_position + channel];
        _delay[_position + channel] = input + delayed * _feedback;
        return delayed;
    }
    protected override void NextFrame() { _position += Channels; if (_position == _delay.Length) _position = 0; }
    protected override void ClearBuffers() { Array.Clear(_delay); _position = 0; }
    protected override void CopyBuffers(DelayEffectProcessor previous)
    {
        var old = (EchoProcessor)previous;
        Array.Copy(old._delay, _delay, _delay.Length);
        _position = old._position;
    }
}

/// <summary>Schroeder-style reverb: four damped parallel feedback combs and
/// two serial allpasses per channel. No convolution, crossfeed, or added dry latency.</summary>
internal sealed class ReverbProcessor : DelayEffectProcessor
{
    private readonly float[][] _buffers;
    private readonly int[] _positions = new int[6];
    private readonly float[] _feedback = new float[4];
    private readonly float[] _lowpasses;
    private readonly float _damping;
    public ReverbProcessor(EffectSpec spec, PcmFormat format) : base(spec, format, spec.Value * 2 + 0.1)
    {
        double[] seconds = [0.0297, 0.0371, 0.0411, 0.0437, 0.005, 0.0017];
        _buffers = seconds.Select(s => new float[Math.Max(1, (int)Math.Round(s * format.SampleRate)) * format.Channels]).ToArray();
        for (var i = 0; i < 4; i++)
            _feedback[i] = (float)Math.Pow(0.001, (_buffers[i].Length / (double)format.Channels) / (format.SampleRate * spec.Value));
        _lowpasses = new float[4 * format.Channels];
        _damping = spec.Damping;
    }
    protected override float Tick(float input, int channel)
    {
        var wet = 0f;
        for (var i = 0; i < 4; i++)
        {
            var delayed = _buffers[i][_positions[i] + channel];
            var state = i * Channels + channel;
            var filtered = delayed * (1 - _damping) + _lowpasses[state] * _damping;
            _lowpasses[state] = filtered;
            _buffers[i][_positions[i] + channel] = input + filtered * _feedback[i];
            wet += delayed * 0.25f;
        }
        for (var i = 4; i < 6; i++)
        {
            var delayed = _buffers[i][_positions[i] + channel];
            var output = delayed - 0.5f * wet;
            _buffers[i][_positions[i] + channel] = wet + 0.5f * output;
            wet = output;
        }
        return wet;
    }
    protected override void NextFrame()
    {
        for (var i = 0; i < _positions.Length; i++)
        {
            _positions[i] += Channels;
            if (_positions[i] == _buffers[i].Length) _positions[i] = 0;
        }
    }
    protected override void ClearBuffers()
    {
        foreach (var buffer in _buffers) Array.Clear(buffer);
        Array.Clear(_positions);
        Array.Clear(_lowpasses);
    }
    protected override void CopyBuffers(DelayEffectProcessor previous)
    {
        var old = (ReverbProcessor)previous;
        for (var i = 0; i < _buffers.Length; i++) Array.Copy(old._buffers[i], _buffers[i], _buffers[i].Length);
        Array.Copy(old._positions, _positions, _positions.Length);
        Array.Copy(old._lowpasses, _lowpasses, _lowpasses.Length);
    }
}

/// <summary>Memoryless symmetric tanh saturation. Wet output trim does not
/// affect the dry signal. No oversampling: high drive can alias.</summary>
internal sealed class DistortionProcessor : IAudioProcessor
{
    private readonly double _drive;
    private readonly double _output;
    private readonly float _mix;
    public DistortionProcessor(EffectSpec spec)
    {
        _drive = Math.Pow(10, spec.Value / 20d);
        _output = Math.Pow(10, spec.OutputDb / 20d);
        _mix = spec.Mix;
    }
    public void Process(Span<float> samples)
    {
        if (_mix == 0) return;
        for (var i = 0; i < samples.Length; i++)
            samples[i] = (float)(samples[i] * (1 - _mix) + Math.Tanh(samples[i] * _drive) * _output * _mix);
    }
    public void Reset() { }
}
