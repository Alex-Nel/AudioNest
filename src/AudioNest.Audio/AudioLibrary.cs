using AudioNest.Hardware;

namespace AudioNest.Audio;

public readonly record struct WavePeak(float Minimum, float Maximum);

/// <summary>Decoded PCM and a coarse min/max cache, shared by split clips without copying.</summary>
public sealed class AudioBuffer
{
    private const int PeakFrames = 256;
    private readonly float[] _samples;
    private readonly WavePeak[] _peaks;
    private readonly WavePeak[] _monoPeaks;

    public AudioBuffer(DecodedAudio audio)
    {
        audio.Format.Validate();
        if (audio.Samples.Length == 0 || audio.Samples.Length % audio.Format.Channels != 0)
            throw new InvalidDataException("Audio contains no complete frames.");
        Format = audio.Format;
        _samples = audio.Samples;
        _peaks = new WavePeak[(FrameCount + PeakFrames - 1) / PeakFrames];
        _monoPeaks = new WavePeak[_peaks.Length];
        for (var i = 0; i < _peaks.Length; i++)
        {
            _peaks[i] = Scan((long)i * PeakFrames, Math.Min(FrameCount, (long)(i + 1) * PeakFrames));
            _monoPeaks[i] = Scan((long)i * PeakFrames, Math.Min(FrameCount, (long)(i + 1) * PeakFrames), true);
        }
    }

    public PcmFormat Format { get; }
    public long FrameCount => _samples.LongLength / Format.Channels;
    public float Sample(long frame, int channel) => _samples[checked((int)(frame * Format.Channels + channel))];

    public WavePeak Peak(long from, long to, bool mono = false)
    {
        from = Math.Clamp(from, 0, FrameCount);
        to = Math.Clamp(to, from, FrameCount);
        var min = 0f;
        var max = 0f;
        while (from < to)
        {
            var end = Math.Min(to, ((from / PeakFrames) + 1) * PeakFrames);
            var peak = from % PeakFrames == 0 && end - from == PeakFrames
                ? (mono ? _monoPeaks : _peaks)[from / PeakFrames] : Scan(from, end, mono);
            min = Math.Min(min, peak.Minimum);
            max = Math.Max(max, peak.Maximum);
            from = end;
        }
        return new WavePeak(min, max);
    }

    private WavePeak Scan(long from, long to, bool mono = false)
    {
        var min = 0f;
        var max = 0f;
        for (var frame = from; frame < to; frame++)
            for (var ch = 0; ch < (mono ? 1 : Format.Channels); ch++)
            {
                var sample = mono && Format.Channels == 2 ? Sample(frame, 0) * 0.5f + Sample(frame, 1) * 0.5f : Sample(frame, ch);
                if (!float.IsFinite(sample)) throw new InvalidDataException("Audio contains non-finite samples.");
                min = Math.Min(min, sample);
                max = Math.Max(max, sample);
            }
        return new WavePeak(min, max);
    }
}

/// <summary>Control-thread-owned cache. Prepare a render plan before entering the audio callback.</summary>
public sealed class AudioLibrary(IAudioBackend backend)
{
    private readonly Dictionary<(Guid Id, PcmFormat Format), AudioBuffer> _buffers = [];
    public AudioBuffer this[Guid assetId, PcmFormat format] => _buffers.TryGetValue((assetId, format), out var buffer)
        ? buffer : throw new InvalidOperationException("Audio asset has not been loaded.");
    public bool TryGet(Guid assetId, PcmFormat format, out AudioBuffer? buffer) => _buffers.TryGetValue((assetId, format), out buffer);

    public AudioBuffer Load(Guid assetId, string path, PcmFormat format, CancellationToken cancellationToken = default)
    {
        var buffer = new AudioBuffer(backend.Decode(path, format, cancellationToken));
        Add(assetId, buffer);
        return buffer;
    }

    public void Clear() => _buffers.Clear();
    public void Add(Guid assetId, AudioBuffer buffer) => _buffers[(assetId, buffer.Format)] = buffer;
}
