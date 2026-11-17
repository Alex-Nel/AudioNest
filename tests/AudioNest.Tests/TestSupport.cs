using AudioNest.Audio;
using AudioNest.Domain;
using AudioNest.Hardware;

namespace AudioNest.Tests;

internal static class TestProjects
{
    public static readonly Guid AssetId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    public static AudioProject Create(long length = 10, long start = 0) => new()
    {
        Assets = [new AudioAsset(AssetId, "source.wav", length)],
        Tracks = [new AudioTrack { Clips = [new AudioClip { AssetId = AssetId, Start = start, Length = length }] }]
    };

    public static AudioBuffer Buffer(int frames = 10, float value = 0.25f, int sampleRate = 48000) =>
        new(new DecodedAudio(new PcmFormat(sampleRate, 2), Enumerable.Repeat(value, frames * 2).ToArray()));

    public static string WriteWave(string directory, int frames = 10)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "source.wav");
        WaveExporter.Export(new RenderPlan(Create(frames), _ => Buffer(frames)), path);
        return path;
    }
}

internal sealed class TempDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "AudioNest.Tests", Guid.NewGuid().ToString("N"));
    public TempDirectory() => Directory.CreateDirectory(Path);
    public void Dispose() => Directory.Delete(Path, true); // Only this test's unique directory.
}

internal sealed class FakeBackend : IAudioBackend
{
    public FakeOutput? Output { get; private set; }
    public bool FailDecode { get; set; }
    public bool FailOutput { get; set; }
    public int RecordingStops { get; private set; }
    public int DecodedFrames { get; set; } = 10;
    public FakeRecording? Recording { get; private set; }
    public IReadOnlyList<AudioDevice> GetOutputDevices() => [new("0", "Test output", true)];
    public IReadOnlyList<AudioDevice> GetInputDevices() => [new("0", "Test input", true)];
    public DecodedAudio Decode(string path, PcmFormat targetFormat, CancellationToken cancellationToken = default)
    {
        if (FailDecode) throw new InvalidDataException("Test decode failure.");
        return new DecodedAudio(targetFormat, Enumerable.Range(0, DecodedFrames * targetFormat.Channels)
            .Select(i => 0.5f * MathF.Sin(i / (float)targetFormat.Channels * 0.04f)).ToArray());
    }
    public IAudioOutput OpenOutput(PcmFormat format, RenderAudio render, string? deviceId = null)
    {
        if (FailOutput) throw new InvalidOperationException("Test device unavailable.");
        return Output = new FakeOutput(render);
    }
    public IAudioRecording StartRecording(PcmFormat format, string path, string? deviceId = null)
    {
        File.WriteAllText(path, "fake recording decoded by fake backend");
        return Recording = new FakeRecording(() => RecordingStops++);
    }
    public void Dispose() { }

    public sealed class FakeOutput(RenderAudio render) : IAudioOutput
    {
        public bool Started { get; private set; }
        public void Start() => Started = true;
        public void Stop() => Started = false;
        public float[] Pull(int frames)
        {
            var output = new float[frames * 2];
            if (Started) render(output);
            return output;
        }
        public void Dispose() => Stop();
    }
    public sealed class FakeRecording(Action stop) : IAudioRecording
    {
        private bool _stopped;
        public AudioPeakMeter InputMeter { get; } = new();
        public void Stop() { if (!_stopped) { _stopped = true; stop(); } }
        public void Dispose() => Stop();
    }
}
