namespace AudioNest.Hardware;

/// <summary>Lock-free peak handoff from an audio callback to a UI reader. No samples are retained.</summary>
public sealed class AudioPeakMeter
{
    private float _peak;

    public void Measure(ReadOnlySpan<float> samples)
    {
        var peak = 0f;
        foreach (var sample in samples)
            if (float.IsFinite(sample)) peak = Math.Max(peak, Math.Abs(sample));
        var observed = Volatile.Read(ref _peak);
        while (peak > observed)
        {
            var previous = Interlocked.CompareExchange(ref _peak, peak, observed);
            if (previous == observed) break;
            observed = previous;
        }
    }

    public float ReadAndReset() => Interlocked.Exchange(ref _peak, 0);
    public void Clear() => Interlocked.Exchange(ref _peak, 0);
}
