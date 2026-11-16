using AudioNest.Domain;
using AudioNest.Hardware;

namespace AudioNest.Audio.Effects;

/// <summary>
/// A prepared audio-thread-owned DSP instance. Implementations must not allocate,
/// block, perform I/O, or touch UI state in Process. Reset is called while stopped.
/// </summary>
public interface IAudioProcessor
{
    void Process(Span<float> interleavedSamples);
    void Reset();
}

/// <summary>Control-thread factory. Future DSP adapters are constructed outside callbacks.</summary>
public interface IAudioEffectFactory
{
    IAudioProcessor Create(EffectSpec description, PcmFormat format);
}

public sealed class BuiltInEffectFactory : IAudioEffectFactory
{
    public IAudioProcessor Create(EffectSpec description, PcmFormat format) => description.Kind switch
    {
        EffectKind.Gain => new GainProcessor(description.Value),
        EffectKind.HardLimiter => new HardLimiterProcessor(description.Value),
        _ => throw new NotSupportedException($"Unsupported effect: {description.Kind}")
    };

    private sealed class GainProcessor(float gain) : IAudioProcessor
    {
        public void Process(Span<float> samples)
        {
            for (var i = 0; i < samples.Length; i++) samples[i] *= gain;
        }
        public void Reset() { }
    }

    private sealed class HardLimiterProcessor(float ceiling) : IAudioProcessor
    {
        public void Process(Span<float> samples)
        {
            for (var i = 0; i < samples.Length; i++) samples[i] = Math.Clamp(samples[i], -ceiling, ceiling);
        }
        public void Reset() { }
    }
}
