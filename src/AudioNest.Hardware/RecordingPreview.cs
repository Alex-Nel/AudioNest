namespace AudioNest.Hardware;

public readonly record struct RecordingPeak(float Minimum, float Maximum);
public sealed record RecordingSnapshot(long Frames, int SampleRate, int FramesPerPeak, RecordingPeak[] Peaks);

/// <summary>Single audio-thread producer, UI snapshots. Fixed-size envelope storage; no raw PCM retained.</summary>
public sealed class RecordingPreview
{
    private readonly RecordingPeak[] _peaks = new RecordingPeak[40 * 600];
    private readonly int _channels;
    private int _published;
    private int _partialFrames;
    private float _min;
    private float _max;
    private long _frames;
    public RecordingPreview(PcmFormat format)
    {
        format.Validate();
        SampleRate = format.SampleRate;
        FramesPerPeak = Math.Max(1, SampleRate / 40);
        _channels = format.Channels;
    }
    public int SampleRate { get; }
    public int FramesPerPeak { get; }
    public long Frames => Interlocked.Read(ref _frames);
    public void Append(ReadOnlySpan<float> samples)
    {
        for (var frame = 0; frame < samples.Length / _channels; frame++)
        {
            for (var channel = 0; channel < _channels; channel++)
            {
                var sample = samples[frame * _channels + channel];
                if (!float.IsFinite(sample)) continue;
                _min = Math.Min(_min, sample);
                _max = Math.Max(_max, sample);
            }
            if (++_partialFrames < FramesPerPeak) continue;
            if (_published < _peaks.Length)
            {
                _peaks[_published] = new RecordingPeak(_min, _max);
                Volatile.Write(ref _published, _published + 1);
            }
            _partialFrames = 0;
            _min = _max = 0;
        }
        Interlocked.Add(ref _frames, samples.Length / _channels);
    }
    public RecordingSnapshot Snapshot()
    {
        var count = Volatile.Read(ref _published);
        return new(Frames, SampleRate, FramesPerPeak, _peaks.AsSpan(0, count).ToArray());
    }
}
