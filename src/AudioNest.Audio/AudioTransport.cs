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
        _plan = plan;
        // The edit cursor may be in silent timeline space. Rebuilding after a
        // cut/trim/delete must not silently relocate the next paste or recording.
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
        var plan = _plan;
        if (!IsPlaying || plan is null) { output.Clear(); return; }
        var position = Position;
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
