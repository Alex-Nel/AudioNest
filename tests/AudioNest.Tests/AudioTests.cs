using AudioNest.Audio;
using AudioNest.Domain;
using AudioNest.Hardware;
using AudioNest.Hardware.SoundFlow;
using Xunit;

namespace AudioNest.Tests;

public sealed class AudioTests
{
    [Theory]
    [InlineData(-1, 0.25f, 0)]
    [InlineData(0, 0.25f, 0.25f)]
    [InlineData(1, 0, 0.25f)]
    [InlineData(0.5f, 0.125f, 0.25f)]
    public void TrackBalanceAffectsBothPlaybackAndExportGraph(float pan, float left, float right)
    {
        var project = TestProjects.Create();
        project = project with { Tracks = [project.Tracks[0] with { Pan = pan }] };
        var output = new float[2];
        new RenderPlan(project, _ => TestProjects.Buffer()).Render(0, output);
        Assert.Equal(left, output[0]);
        Assert.Equal(right, output[1]);
        Assert.Throws<InvalidDataException>(() =>
            (project with { Tracks = [project.Tracks[0] with { Pan = float.NaN }] }).Validate());
    }

    [Fact]
    public void RenderUsesPreparedScratchWithoutManagedAllocations()
    {
        var plan = new RenderPlan(TestProjects.Create(3000), _ => TestProjects.Buffer(3000));
        var output = new float[4096];
        plan.Render(0, output);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 5; i++) plan.Render(0, output);
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
    }

    [Fact]
    public void MixRespectsStartSourceOffsetAndSilence()
    {
        var project = TestProjects.Create(4, 2);
        var clip = project.Tracks[0].Clips[0] with { SourceOffset = 1, Length = 2 };
        project = project with { Tracks = [project.Tracks[0] with { Clips = [clip] }] };
        var buffer = new AudioBuffer(new DecodedAudio(new PcmFormat(48000, 2), [0, 0, 0.1f, 0.2f, 0.3f, 0.4f, 0, 0]));
        var output = new float[12];
        new RenderPlan(project, _ => buffer).Render(0, output);
        Assert.Equal(new[] { 0f, 0, 0, 0, 0.1f, 0.2f, 0.3f, 0.4f, 0, 0, 0, 0 }, output);
    }

    [Fact]
    public void EffectsRunInOrderThenTrackEffectsThenSafetyCeiling()
    {
        var project = TestProjects.Create();
        var track = project.Tracks[0];
        var clip = track.Clips[0] with { Effects = [new(EffectKind.Gain, 4), new(EffectKind.HardLimiter, 0.6f)] };
        project = project with { Tracks = [track with { Clips = [clip, clip with { Id = Guid.NewGuid() }], Effects = [new(EffectKind.Gain, 0.5f)] }] };
        var plan = new RenderPlan(project, _ => TestProjects.Buffer(value: 0.5f));
        var output = new float[2];
        plan.Render(0, output);
        Assert.Equal(0.6f, output[0], 5);
    }

    [Fact]
    public void SoloAndMuteAreRespected()
    {
        var project = TestProjects.Create();
        var track = project.Tracks[0];
        project = project with
        {
            Tracks = [track with { Solo = true, Muted = true }, track with { Id = Guid.NewGuid(), Clips = [track.Clips[0] with { Id = Guid.NewGuid() }] }]
        };
        var output = new float[2];
        new RenderPlan(project, _ => TestProjects.Buffer()).Render(0, output);
        Assert.All(output, sample => Assert.Equal(0, sample));
    }

    [Fact]
    public void PeaksCoverChannelsAndRespectPartialBins()
    {
        var samples = new float[1200];
        samples[10] = -0.8f;
        samples[999] = 0.9f;
        var buffer = new AudioBuffer(new DecodedAudio(new PcmFormat(48000, 2), samples));
        Assert.Equal(new WavePeak(-0.8f, 0.9f), buffer.Peak(0, 600));
        Assert.Equal(new WavePeak(0, 0), buffer.Peak(6, 10));
        Assert.Throws<InvalidDataException>(() => new AudioBuffer(new DecodedAudio(new PcmFormat(48000, 2), [float.NaN, 0])));
    }

    [Fact]
    public void TransportAdvancesOnlyFromDeviceFramesAndSeeksWithoutUITimer()
    {
        using var backend = new FakeBackend();
        using var transport = new AudioTransport(backend);
        transport.SetPlan(new RenderPlan(TestProjects.Create(), _ => TestProjects.Buffer()));
        transport.Play();
        Assert.Equal(0, transport.Position);
        Assert.Equal(0.25f, backend.Output!.Pull(4)[0]);
        Assert.Equal(4, transport.Position);
        transport.Pause();
        backend.Output.Pull(4);
        Assert.Equal(4, transport.Position);
        transport.Seek(8);
        transport.Play();
        var end = backend.Output.Pull(4);
        Assert.Equal(10, transport.Position);
        Assert.False(transport.IsPlaying);
        Assert.Equal(0, end[7]);
        transport.Play();
        Assert.Equal(0, transport.Position);
        transport.Stop();
        Assert.False(transport.IsPlaying);
    }

    [Fact]
    public void DeviceFailureDoesNotLeaveTransportPlaying()
    {
        using var backend = new FakeBackend { FailOutput = true };
        using var transport = new AudioTransport(backend);
        transport.SetPlan(new RenderPlan(TestProjects.Create(), _ => TestProjects.Buffer()));
        Assert.Throws<InvalidOperationException>(transport.Play);
        Assert.False(transport.IsPlaying);
    }

    [Fact]
    public void ExportWritesExpectedWaveAndCancellationPreservesExistingFile()
    {
        using var directory = new TempDirectory();
        var plan = new RenderPlan(TestProjects.Create(), _ => TestProjects.Buffer());
        var path = Path.Combine(directory.Path, "export.wav");
        WaveExporter.Export(plan, path);
        var bytes = File.ReadAllBytes(path);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.Equal(84, bytes.Length);
        Assert.Equal(48000, BitConverter.ToInt32(bytes, 24));
        Assert.Equal(8192, BitConverter.ToInt16(bytes, 44));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => WaveExporter.Export(plan, path, cancellation.Token));
        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Single(Directory.GetFiles(directory.Path));
    }

    [Fact]
    [Trait("Category", "NativeCodec")]
    public void SoundFlowDecodesExportAndResamplesWithoutOpeningAudioDevices()
    {
        using var directory = new TempDirectory();
        var path = TestProjects.WriteWave(directory.Path, 480);
        using var backend = new SoundFlowBackend();
        var decoded = backend.Decode(path, new PcmFormat(48000, 2));
        Assert.Equal(480, decoded.FrameCount);
        Assert.InRange(decoded.Samples[0], 0.249f, 0.251f);
        var resampled = backend.Decode(path, new PcmFormat(24000, 1));
        Assert.InRange(resampled.FrameCount, 230, 250);
    }
}
