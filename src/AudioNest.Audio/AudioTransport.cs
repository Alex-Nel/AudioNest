using AudioNest.Hardware;

namespace AudioNest.Audio;

public interface ITransportClock
{
    long Position { get; }
    bool IsPlaying { get; }
}

/// <summary>
/// Device-frame-driven transport, independent of UI timers. Controls are serialized
/// on the control thread; Stop must quiesce the device before changing callback state.
/// </summary>
public sealed class AudioTransport(IAudioBackend backend) : ITransportClock, IDisposable
{
    private IAudioOutput? _output;
    private RenderPlan? _plan;
    private RenderPlan? _renderedPlan;
    private long _position;
    private int _playing;
    public long Position => Interlocked.Read(ref _position);
    public bool IsPlaying => Volatile.Read(ref _playing) != 0;
    public AudioPeakMeter OutputMeter { get; } = new();

    public void SetPlan(RenderPlan plan)
    {
        Pause();
        _output?.Dispose();
        _output = null;
        Volatile.Write(ref _plan, plan);
        // The edit cursor may be in silent timeline space. Rebuilding after a
        // cut/trim/delete must not silently relocate the next paste or recording.
    }

    /// <summary>Publish a freshly prepared same-format graph at the next callback.
    /// Never mutate or render the published graph from the control thread.</summary>
    public void UpdatePlan(RenderPlan plan)
    {
        if (_plan is { } current && plan.Format != current.Format)
            throw new ArgumentException("Live plan updates must preserve the output format.", nameof(plan));
        Volatile.Write(ref _plan, plan);
    }

    public void Play()
    {
        if (_plan is null || _plan.Duration == 0 || IsPlaying) return;
        if (Position >= _plan.Duration)
        {
            _output?.Stop();
            _plan.Reset();
            Interlocked.Exchange(ref _position, 0);
        }
        _output ??= backend.OpenOutput(_plan.Format, Render);
        Volatile.Write(ref _playing, 1);
        try { _output.Start(); }
        catch { Volatile.Write(ref _playing, 0); throw; }
    }

    public void Pause()
    {
        Volatile.Write(ref _playing, 0);
        _output?.Stop();
        _renderedPlan = null;
        OutputMeter.Clear();
    }

    public void Seek(long frame)
    {
        var resume = IsPlaying;
        Pause();
        _plan?.Reset();
        Interlocked.Exchange(ref _position, Math.Max(0, frame));
        if (resume && Position < (_plan?.Duration ?? 0)) Play();
    }

    public void Stop()
    {
        Pause();
        _plan?.Reset();
        Interlocked.Exchange(ref _position, 0);
    }

    private void Render(Span<float> output)
    {
        var plan = Volatile.Read(ref _plan);
        if (!IsPlaying || plan is null) { output.Clear(); return; }
        var position = Position;
        if (position >= plan.Duration)
        {
            output.Clear();
            Volatile.Write(ref _playing, 0);
            return;
        }
        if (!ReferenceEquals(plan, _renderedPlan))
        {
            if (_renderedPlan is { } previous) plan.AdoptStateFrom(previous);
            _renderedPlan = plan;
        }
        plan.Render(position, output);
        OutputMeter.Measure(output);
        var next = Math.Min(plan.Duration, position + output.Length / plan.Format.Channels);
        Interlocked.Exchange(ref _position, next);
        if (next >= plan.Duration) Volatile.Write(ref _playing, 0);
    }

    public void Dispose()
    {
        Pause();
        _output?.Dispose();
        _output = null;
    }
}
