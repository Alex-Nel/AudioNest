using System.Collections.Immutable;
using AudioNest.Audio;
using AudioNest.Audio.Effects;
using AudioNest.Domain;
using AudioNest.Hardware;
using Xunit;

namespace AudioNest.Tests;

public sealed class DynamicsTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void CompressorUsesThresholdRatioAndStereoLinkedGain(int channels)
    {
        var effect = BuiltInEffects.Create(EffectKind.Compressor) with { Value = -20, AttackMs = 0.1f };
        var processor = new BuiltInEffectFactory().Create(effect, new PcmFormat(48000, channels));
        var samples = Enumerable.Range(0, 48000 * channels).Select(i => i % channels == 0 ? 0.8f : 0.2f).ToArray();
        processor.Process(samples);
        var expected = Math.Pow(10, (-20 + (20 * Math.Log10(0.8) + 20) / 4) / 20);
        Assert.InRange(samples[^channels], expected - 0.00001, expected + 0.00001);
        if (channels == 2) Assert.Equal(samples[^2] * 0.25f, samples[^1], 6);
        processor.Reset();
        Array.Fill(samples, 0.01f);
        processor.Process(samples);
        Assert.All(samples, sample => Assert.Equal(0.01f, sample));
    }

    [Fact]
    public void LimiterHasInstantLinkedCeilingReleaseAndReset()
    {
        var spec = BuiltInEffects.Create(EffectKind.Limiter) with { Value = 0.5f, ReleaseMs = 100 };
        var processor = new BuiltInEffectFactory().Create(spec, new PcmFormat(48000, 2));
        var peak = new[] { 2f, 0.5f };
        processor.Process(peak);
        Assert.Equal(0.5f, peak[0], 6);
        Assert.Equal(0.125f, peak[1], 6);
        var quiet = Enumerable.Range(0, 96000).Select(i => i % 2 == 0 ? 0.1f : 0.025f).ToArray();
        processor.Process(quiet);
        Assert.InRange(quiet[0], 0.0249f, 0.026f);
        Assert.InRange(quiet[^2], 0.0999f, 0.1f);
        Assert.Equal(quiet[^2] * 0.25f, quiet[^1], 6);
        processor.Reset();
        var afterReset = new[] { 0.1f, 0.025f };
        processor.Process(afterReset);
        Assert.Equal(0.1f, afterReset[0]);
    }

    [Theory]
    [InlineData(EffectKind.Compressor)]
    [InlineData(EffectKind.Limiter)]
    public void DynamicsAreBlockIndependentAndResetOnDiscontinuousRender(EffectKind kind)
    {
        var project = TestProjects.Create(4800);
        project = project with { Tracks = [project.Tracks[0] with { Effects = [BuiltInEffects.Create(kind)] }] };
        var data = Enumerable.Range(0, 9600).Select(i => i / 2 % 200 < 40 ? 0.9f : 0.05f).ToArray();
        var buffer = new AudioBuffer(new DecodedAudio(new PcmFormat(48000, 2), data));
        var full = new float[9600];
        new RenderPlan(project, _ => buffer).Render(0, full);
        var chunked = new float[9600];
        var plan = new RenderPlan(project, _ => buffer);
        for (var frame = 0; frame < 4800;)
        {
            var frames = Math.Min(37, 4800 - frame);
            plan.Render(frame, chunked.AsSpan(frame * 2, frames * 2));
            frame += frames;
        }
        Assert.Equal(full, chunked);
        plan.Reset();
        plan.Render(0, chunked);
        Assert.Equal(full, chunked);
        var seeked = new float[100];
        plan.Render(2400, seeked);
        var fresh = new float[100];
        new RenderPlan(project, _ => buffer).Render(2400, fresh);
        Assert.Equal(fresh, seeked);
    }

    [Fact]
    public void NormalizerAnalyzesTrimmedClipVolumeAndPrecedingEffectsNotRawSource()
    {
        var project = TestProjects.Create(1000);
        var clip = project.Tracks[0].Clips[0] with
        {
            SourceOffset = 250,
            Length = 500,
            Volume = 0.5f,
            Effects = [new EffectSpec(EffectKind.Gain, 2), new EffectSpec(EffectKind.Normalizer, 0.5f)]
        };
        project = project with { Tracks = [project.Tracks[0] with { Clips = [clip] }] };
        var samples = Enumerable.Range(0, 2000).Select(i => i / 2 < 500 ? 0.1f : 0.8f).ToArray();
        var buffer = new AudioBuffer(new DecodedAudio(new PcmFormat(48000, 2), samples));
        var output = new float[1000];
        new RenderPlan(project, _ => buffer).Render(0, output);
        Assert.Equal(0.0625f, output[0], 6);
        Assert.Equal(0.5f, output[^1], 6);
        var reordered = project with { Tracks = [project.Tracks[0] with { Clips = [clip with { Effects = clip.Effects.Reverse().ToImmutableArray() }] }] };
        new RenderPlan(reordered, _ => buffer).Render(0, output);
        Assert.Equal(1, output[^1]);
        var bypassed = project with { Tracks = [project.Tracks[0] with { Clips = [clip with { Effects = clip.Effects.SetItem(1, clip.Effects[1] with { Bypassed = true }) }] }] };
        new RenderPlan(bypassed, _ => buffer).Render(0, output);
        Assert.Equal(0.8f, output[^1], 6);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(1e-40f)]
    public void NormalizerHandlesSilenceAndExtremelyQuietPcm(float amplitude)
    {
        var project = TestProjects.Create(10);
        project = project with { Tracks = [project.Tracks[0] with { Effects = [new EffectSpec(EffectKind.Normalizer, 0.5f)] }] };
        var output = new float[20];
        new RenderPlan(project, _ => TestProjects.Buffer(10, amplitude)).Render(0, output);
        Assert.All(output, sample => Assert.Equal(amplitude == 0 ? 0 : 0.5f, sample, 6));
    }

    [Fact]
    public void TrackNormalizerIncludesOverlapsAndSkipsDistantSilentRanges()
    {
        var project = TestProjects.Create(100);
        var first = project.Tracks[0].Clips[0];
        var second = first with { Id = Guid.NewGuid() };
        var distant = first with { Id = Guid.NewGuid(), Start = 1_000_000_000_000 };
        project = project with
        {
            Tracks = [project.Tracks[0] with
            {
                Clips = [first, second, distant],
                Volume = 0.5f,
                Effects = [new EffectSpec(EffectKind.Gain, 2), new EffectSpec(EffectKind.Normalizer, 0.8f)]
            }]
        };
        var buffer = TestProjects.Buffer(100);
        var plan = new RenderPlan(project, _ => buffer);
        var output = new float[20];
        plan.Render(0, output);
        Assert.All(output, sample => Assert.Equal(0.8f, sample, 6));
        plan.Render(distant.Start, output);
        Assert.All(output, sample => Assert.Equal(0.4f, sample, 6));
    }

    [Theory]
    [InlineData(EffectKind.Compressor)]
    [InlineData(EffectKind.Limiter)]
    [InlineData(EffectKind.Normalizer)]
    public void PreparedDynamicsDoNotAllocateInAudioCallback(EffectKind kind)
    {
        var project = TestProjects.Create(10000);
        project = project with { Tracks = [project.Tracks[0] with { Effects = [BuiltInEffects.Create(kind)] }] };
        var buffer = TestProjects.Buffer(10000);
        var plan = new RenderPlan(project, _ => buffer);
        var block = new float[128];
        plan.Render(0, block);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 1; i < 50; i++) plan.Render(i * 64, block);
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
    }

    [Fact]
    public void LiveHandoffPreservesUnchangedLimiterEnvelopeWithoutCallbackAllocation()
    {
        using var backend = new FakeBackend();
        using var transport = new AudioTransport(backend);
        var project = TestProjects.Create(1000);
        project = project with { Tracks = [project.Tracks[0] with { Effects = [new EffectSpec(EffectKind.Limiter, 0.5f, ReleaseMs: 1000)] }] };
        var samples = Enumerable.Repeat(0.1f, 2000).ToArray();
        samples[0] = 2;
        var buffer = new AudioBuffer(new DecodedAudio(new PcmFormat(48000, 2), samples));
        transport.SetPlan(new RenderPlan(project, _ => buffer));
        transport.Play();
        backend.Output!.Pull(1);
        var changed = project with { Tracks = [project.Tracks[0] with { Volume = 0.5f }] };
        transport.UpdatePlan(new RenderPlan(changed, _ => buffer));
        var block = new float[2];
        backend.Output.Pull(block); // Warm handoff path.
        transport.UpdatePlan(new RenderPlan(changed, _ => buffer));
        var before = GC.GetAllocatedBytesForCurrentThread();
        backend.Output.Pull(block);
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
        Assert.InRange(block[0], 0.0124f, 0.013f);
        Assert.Equal(1, backend.OutputOpens);
        Assert.True(transport.IsPlaying);
    }

    [Fact]
    public void DynamicsPlaybackAndWavExportUseTheSameStatefulChain()
    {
        using var directory = new TempDirectory();
        var project = TestProjects.Create(1000);
        project = project with
        {
            Tracks = [project.Tracks[0] with
            {
                Effects = [BuiltInEffects.Create(EffectKind.Compressor), new EffectSpec(EffectKind.Limiter, 0.5f), new EffectSpec(EffectKind.Normalizer, 0.6f)]
            }]
        };
        var buffer = TestProjects.Buffer(1000, 0.9f);
        var expected = new float[2000];
        new RenderPlan(project, _ => buffer).Render(0, expected);
        var path = Path.Combine(directory.Path, "dynamics.wav");
        WaveExporter.Export(new RenderPlan(project, _ => buffer), path);
        var bytes = File.ReadAllBytes(path);
        for (var i = 0; i < expected.Length; i++)
            Assert.InRange(Math.Abs(expected[i] - BitConverter.ToInt16(bytes, 44 + i * 2) / 32768f), 0, 0.00004f);
    }

    [Fact]
    public void DefaultsAreValidAndMalformedDynamicsAreRejected()
    {
        foreach (var kind in BuiltInEffects.Available)
        {
            var project = TestProjects.Create();
            (project with { Tracks = [project.Tracks[0] with { Effects = [BuiltInEffects.Create(kind)] }] }).Validate();
        }
        var compressor = BuiltInEffects.Create(EffectKind.Compressor);
        foreach (var invalid in new[]
        {
            compressor with { Value = float.NaN }, compressor with { Ratio = 0 },
            compressor with { AttackMs = 0 }, compressor with { ReleaseMs = float.PositiveInfinity },
            compressor with { MakeupDb = 25 }, compressor with { Value = -61 }
        })
        {
            var project = TestProjects.Create();
            Assert.Throws<InvalidDataException>(() => (project with { Tracks = [project.Tracks[0] with { Effects = [invalid] }] }).Validate());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NormalizationCacheReusesOnlyIdenticalAudioInputs(bool track)
    {
        var project = TestProjects.Create(100);
        var gain = new EffectSpec(EffectKind.Gain, 2);
        var normalize = new EffectSpec(EffectKind.Normalizer, 0.8f);
        AudioProject WithEffects(AudioProject source, EffectSpec a, EffectSpec b) => track
            ? source with { Tracks = [source.Tracks[0] with { Effects = [a, b] }] }
            : source with { Tracks = [source.Tracks[0] with { Clips = [source.Tracks[0].Clips[0] with { Effects = [a, b] }] }] };
        project = WithEffects(project, gain, normalize);
        var buffer = TestProjects.Buffer(100);
        var first = new RenderPlan(project, _ => buffer);
        Assert.Equal(1, first.NormalizationAnalysisCount);
        var renamed = project with { Tracks = [project.Tracks[0] with { Name = "Renamed", Clips = [project.Tracks[0].Clips[0] with { Name = "Also renamed" }] }] };
        var namePlan = new RenderPlan(renamed, _ => buffer, previous: first);
        Assert.Equal(0, namePlan.NormalizationAnalysisCount);
        var changedTarget = WithEffects(renamed, gain, normalize with { Value = 0.6f });
        var targetPlan = new RenderPlan(changedTarget, _ => buffer, previous: namePlan);
        Assert.Equal(0, targetPlan.NormalizationAnalysisCount);
        var output = new float[2];
        targetPlan.Render(0, output);
        Assert.Equal(0.6f, output[0], 6);
        var changedPrefix = WithEffects(changedTarget, gain with { Value = 3 }, normalize);
        var prefixPlan = new RenderPlan(changedPrefix, _ => buffer, previous: targetPlan);
        Assert.Equal(1, prefixPlan.NormalizationAnalysisCount);
        var replacedPcm = TestProjects.Buffer(100, 0.125f);
        var reloadPlan = new RenderPlan(changedPrefix, _ => replacedPcm, previous: prefixPlan);
        Assert.Equal(1, reloadPlan.NormalizationAnalysisCount);
    }

    [Fact]
    public void SparseNormalizationAdvancesDynamicsReleaseAcrossLongGaps()
    {
        var project = TestProjects.Create(100);
        var first = project.Tracks[0].Clips[0];
        var distant = first with { Id = Guid.NewGuid(), Start = 1_000_000_000_000 };
        project = project with
        {
            Tracks = [project.Tracks[0] with
            {
                Clips = [first, distant],
                Effects = [BuiltInEffects.Create(EffectKind.Compressor), new EffectSpec(EffectKind.Normalizer, 0.7f)]
            }]
        };
        var buffer = TestProjects.Buffer(100, 0.8f);
        var plan = new RenderPlan(project, _ => buffer);
        var early = new float[200];
        var late = new float[200];
        plan.Render(0, early);
        plan.Render(distant.Start, late);
        Assert.Equal(early, late);
        Assert.InRange(early.Max(), 0.69999f, 0.70001f);
    }
}
