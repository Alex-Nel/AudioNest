using AudioNest.Audio;
using AudioNest.Hardware;
using AudioNest.Hardware.SoundFlow;
using Xunit;

namespace AudioNest.Tests;

public sealed class CodecTests
{
    [Theory]
    [Trait("Category", "NativeCodec")]
    [InlineData(AudioFileFormat.Wav)]
    [InlineData(AudioFileFormat.Mp3)]
    [InlineData(AudioFileFormat.Aac)]
    [InlineData(AudioFileFormat.Ogg)]
    [InlineData(AudioFileFormat.Flac)]
    public void NativeFormatsExportAndImportWithNonSilentAudio(AudioFileFormat format)
    {
        using var directory = new TempDirectory();
        using var backend = new SoundFlowBackend();
        var samples = Enumerable.Range(0, 48000 * 2).Select(i => 0.3f * MathF.Sin(i / 2 * 2 * MathF.PI * 440 / 48000)).ToArray();
        var buffer = new AudioBuffer(new DecodedAudio(new PcmFormat(48000, 2), samples));
        var path = Path.Combine(directory.Path, "roundtrip." + AudioFileType.For(format).Extension);
        AudioExporter.Export(new RenderPlan(TestProjects.Create(48000), _ => buffer), path, format, backend);
        Assert.True(new FileInfo(path).Length > 100);
        var decoded = backend.Decode(path, new PcmFormat(48000, 2));
        Assert.InRange(decoded.FrameCount, 46000, 53000);
        Assert.Contains(decoded.Samples, sample => Math.Abs(sample) > 0.1);
    }
}
