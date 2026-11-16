namespace AudioNest.Hardware;

public readonly record struct PcmFormat(int SampleRate, int Channels)
{
    public void Validate()
    {
        if (SampleRate is < 8000 or > 192000 || Channels is < 1 or > 2)
            throw new ArgumentOutOfRangeException(nameof(PcmFormat), "Only mono/stereo 8–192 kHz PCM is supported.");
    }
}

/// <summary>Interleaved, normalized float PCM; ownership transfers to the caller.</summary>
public sealed record DecodedAudio(PcmFormat Format, float[] Samples)
{
    public long FrameCount => Samples.LongLength / Format.Channels;
}

public sealed record AudioDevice(string Id, string Name, bool IsDefault);

// Callbacks run on the device thread: no UI, I/O, blocking, or unbounded allocation.
public delegate void RenderAudio(Span<float> output);

public interface IAudioOutput : IDisposable
{
    void Start();
    void Stop();
}

public interface IAudioRecording : IDisposable
{
    // Finalizes the output file and releases the input device.
    void Stop();
}

public interface IAudioBackend : IDisposable
{
    IReadOnlyList<AudioDevice> GetOutputDevices();
    IReadOnlyList<AudioDevice> GetInputDevices();
    IAudioOutput OpenOutput(PcmFormat format, RenderAudio render, string? deviceId = null);
    IAudioRecording StartRecording(PcmFormat format, string path, string? deviceId = null);
    DecodedAudio Decode(string path, PcmFormat targetFormat, CancellationToken cancellationToken = default);
}
