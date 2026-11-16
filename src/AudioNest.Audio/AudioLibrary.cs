using AudioNest.Hardware;

namespace AudioNest.Audio;

public readonly record struct WavePeak(float Minimum, float Maximum);

/// <summary>Decoded PCM and a coarse min/max cache, shared by split clips without copying.</summary>
public sealed class AudioBuffer
{
    private const int PeakFrames = 256;
    private readonly float[] _samples;
    private readonly WavePeak[] _peaks;

    public AudioBuffer(DecodedAudio audio)
    {
        audio.Format.Validate();
        if (audio.Samples.Length == 0 || audio.Samples.Length % audio.Format.Channels != 0)
            throw new InvalidDataException("Audio contains no complete frames.");
        Format = audio.Format;
        _samples = audio.Samples;
        _peaks = new WavePeak[(FrameCount + PeakFrames - 1) / PeakFrames];
        for (var i = 0; i < _peaks.Length; i++)
            _peaks[i] = Scan((long)i * PeakFrames, Math.Min(FrameCount, (long)(i + 1) * PeakFrames));
    }

    public PcmFormat Format { get; }
    public long FrameCount => _samples.LongLength / Format.Channels;
    public float Sample(long frame, int channel) => _samples[checked((int)(frame * Format.Channels + channel))];

    public WavePeak Peak(long from, long to)
    {
        from = Math.Clamp(from, 0, FrameCount);
        to = Math.Clamp(to, from, FrameCount);
        var min = 0f;
        var max = 0f;
        while (from < to)
        {
            var end = Math.Min(to, ((from / PeakFrames) + 1) * PeakFrames);
            var peak = from % PeakFrames == 0 && end - from == PeakFrames
                ? _peaks[from / PeakFrames] : Scan(from, end);
            min = Math.Min(min, peak.Minimum);
            max = Math.Max(max, peak.Maximum);
            from = end;
        }
        return new WavePeak(min, max);
    }

    private WavePeak Scan(long from, long to)
    {
        var min = 0f;
        var max = 0f;
        for (var frame = from; frame < to; frame++)
            for (var ch = 0; ch < Format.Channels; ch++)
            {
                var sample = Sample(frame, ch);
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
    private readonly Dictionary<Guid, AudioBuffer> _buffers = [];
    public AudioBuffer this[Guid assetId] => _buffers.TryGetValue(assetId, out var buffer)
        ? buffer : throw new InvalidOperationException("Audio asset has not been loaded.");

    public AudioBuffer Load(Guid assetId, string path, PcmFormat format, CancellationToken cancellationToken = default)
    {
        var buffer = new AudioBuffer(backend.Decode(path, format, cancellationToken));
        _buffers[assetId] = buffer;
        return buffer;
    }

    public void Clear() => _buffers.Clear();
    public void Add(Guid assetId, AudioBuffer buffer) => _buffers[assetId] = buffer;
}
