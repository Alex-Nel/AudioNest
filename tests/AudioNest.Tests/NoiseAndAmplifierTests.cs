using System.Text.Json.Nodes;
using AudioNest.Application;
using AudioNest.Audio;
using AudioNest.Audio.Effects;
using AudioNest.Desktop;
using AudioNest.Domain;
using AudioNest.Hardware;
using AudioNest.Persistence;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace AudioNest.Tests;

public sealed class NoiseAndAmplifierTests
{
    private static IAudioProcessor Noise(float floor = -30, float reduction = 12, int rate = 48000, int channels = 2) =>
        new BuiltInEffectFactory().Create(BuiltInEffects.Create(EffectKind.NoiseReduction) with
        { Value = floor, ReductionDb = reduction }, new PcmFormat(rate, channels));
    private static float[] QuietNoise(int frames)
    {
        var random = new Random(123);
        var data = new float[frames * 2];
        for (var frame = 0; frame < frames; frame++)
        {
            data[frame * 2] = (float)((random.NextDouble() * 2 - 1) * 0.0001);
            data[frame * 2 + 1] = data[frame * 2] * 0.25f;
        }
        return data;
    }
    private static double Rms(float[] samples) => Math.Sqrt(samples.Select(s => (double)s * s).Average());
    private static double Amplitude(float[] data, double frequency, int firstFrame)
    {
        var sine = 0d;
        var cosine = 0d;
        var frames = data.Length / 2;
        for (var frame = firstFrame; frame < frames; frame++)
        {
            var phase = 2 * Math.PI * frequency * frame / 48000;
            sine += data[frame * 2] * Math.Sin(phase);
            cosine += data[frame * 2] * Math.Cos(phase);
        }
        return 2 * Math.Sqrt(sine * sine + cosine * cosine) / (frames - firstFrame);
    }

    [Theory]
    [InlineData(-60f)]
    [InlineData(0f)]
    [InlineData(3f)]
    [InlineData(36f)]
    public void AmplifierUsesDecibelsAndPreservesStereoAndPhase(float db)
    {
        var processor = new BuiltInEffectFactory().Create(new EffectSpec(EffectKind.Amplifier, db), new PcmFormat(48000, 2));
        var samples = new[] { 0.01f, -0.0025f, -0.005f, 0.00125f };
        var expected = samples.Select(s => s * (float)Math.Pow(10, db / 20d)).ToArray();
        processor.Process(samples);
        Assert.Equal(expected, samples);
        processor.Reset();
        processor.AdvanceSilence(1_000_000);
        var silence = new float[16];
        processor.Process(silence);
        Assert.All(silence, sample => Assert.Equal(0, sample));
    }

    [Fact]
    public void NoiseReductionAttenuatesQuietNoiseByTheSelectedDepth()
    {
        var samples = QuietNoise(48000);
        var before = Rms(samples);
        Noise().Process(samples);
        Assert.InRange(Rms(samples) / before, Math.Pow(10, -12d / 20) - 0.00001, Math.Pow(10, -12d / 20) + 0.00001);
        for (var frame = 0; frame < 48000; frame++)
            Assert.Equal(samples[frame * 2] * 0.25f, samples[frame * 2 + 1], 6);
    }

    [Fact]
    public void NoiseReductionSuppressesHighFrequencyNoiseWhileLowFrequencySignalContinues()
    {
        var data = new float[96000];
        for (var frame = 0; frame < 48000; frame++)
        {
            var sample = (float)(0.4 * Math.Sin(2 * Math.PI * 200 * frame / 48000) +
                0.01 * Math.Sin(2 * Math.PI * 10000 * frame / 48000));
            data[frame * 2] = sample;
            data[frame * 2 + 1] = sample * 0.25f;
        }
        Noise(-25, 18).Process(data);
        // A broadband gate would stay open for the loud 200 Hz signal and
        // leave the 10 kHz noise unchanged. Suppression must be band-selective.
        Assert.InRange(Amplitude(data, 200, 24000), 0.3, 0.45);
        Assert.True(Amplitude(data, 10000, 24000) < 0.007);
        for (var frame = 24000; frame < 48000; frame++)
            Assert.Equal(data[frame * 2] * 0.25f, data[frame * 2 + 1], 6);
    }

    [Fact]
    public void ZeroReductionIsTransparentAndSilenceHasNoPrintedFilterTail()
    {
        var source = QuietNoise(1000);
        var transparent = source.ToArray();
        Noise(reduction: 0).Process(transparent);
        Assert.Equal(source, transparent);
        var processor = Noise();
        var loud = Enumerable.Repeat(0.8f, 2000).ToArray();
        processor.Process(loud);
        var silence = new float[1000];
        processor.Process(silence);
        Assert.All(silence, sample => Assert.Equal(0, sample));
        processor.Reset();
        var reset = source.ToArray();
        processor.Process(reset);
        var fresh = source.ToArray();
        Noise().Process(fresh);
        Assert.Equal(fresh, reset);
    }

    [Theory]
    [InlineData(8000, 1)]
    [InlineData(44100, 2)]
    [InlineData(48000, 1)]
    [InlineData(192000, 2)]
    public void NoiseFilterBankAdaptsToSupportedFormats(int rate, int channels)
    {
        var samples = Enumerable.Range(0, 1000 * channels).Select(i => (float)(0.2 * Math.Sin(i / channels * 0.03))).ToArray();
        Noise(rate: rate, channels: channels).Process(samples);
        Assert.All(samples, sample => Assert.True(float.IsFinite(sample)));
        Assert.Contains(samples, sample => sample != 0);
    }

    [Fact]
    public void SparseSilenceAdvanceAndStateHandoffMatchContinuousProcessing()
    {
        var warm = Enumerable.Range(0, 4000).Select(i => (float)(0.5 * Math.Sin(i / 2 * 0.05))).ToArray();
        var original = Noise();
        original.Process(warm);
        var advanced = Noise();
        advanced.CopyStateFrom(original);
        var silence = new float[2000];
        original.Process(silence);
        advanced.AdvanceSilence(1000);
        var expected = QuietNoise(2000);
        var actual = expected.ToArray();
        original.Process(expected);
        advanced.Process(actual);
        for (var i = 0; i < expected.Length; i++) Assert.Equal(expected[i], actual[i], 6);
        var copied = Noise();
        copied.CopyStateFrom(advanced);
        expected = QuietNoise(200);
        actual = expected.ToArray();
        advanced.Process(expected);
        copied.Process(actual);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void NewEffectsRespectBypassAndUseTheSamePlaybackAndExportChain()
    {
        using var directory = new TempDirectory();
        var project = TestProjects.Create(1000);
        var noise = BuiltInEffects.Create(EffectKind.NoiseReduction) with { Value = -30 };
        var amplifier = BuiltInEffects.Create(EffectKind.Amplifier) with { Value = 6 };
        project = project with
        {
            Tracks = [project.Tracks[0] with
            {
                Clips = [project.Tracks[0].Clips[0] with { Effects = [noise, amplifier] }]
            }]
        };
        var pcm = QuietNoise(1000);
        var buffer = new AudioBuffer(new DecodedAudio(new PcmFormat(48000, 2), pcm));
        var expected = new float[2000];
        new RenderPlan(project, _ => buffer).Render(0, expected);
        var path = Path.Combine(directory.Path, "new-effects.wav");
        WaveExporter.Export(new RenderPlan(project, _ => buffer), path);
        var bytes = File.ReadAllBytes(path);
        for (var i = 0; i < expected.Length; i++)
            Assert.InRange(Math.Abs(expected[i] - BitConverter.ToInt16(bytes, 44 + i * 2) / 32768f), 0, 0.00004f);
        var bypassed = project with
        {
            Tracks = [project.Tracks[0] with
            {
                Clips = [project.Tracks[0].Clips[0] with { Effects = [noise with { Bypassed = true }, amplifier with { Bypassed = true }] }]
            }]
        };
        new RenderPlan(bypassed, _ => buffer).Render(0, expected);
        Assert.Equal(pcm, expected);
    }

    [Fact]
    public void NewParametersRejectNonFiniteAndOutOfRangeValues()
    {
        var noise = BuiltInEffects.Create(EffectKind.NoiseReduction);
        foreach (var spec in new[]
        {
            noise with { Value = -91 }, noise with { Value = -9 }, noise with { Value = float.NaN },
            noise with { ReductionDb = -1 }, noise with { ReductionDb = 37 }, noise with { ReductionDb = float.PositiveInfinity },
            new EffectSpec(EffectKind.Amplifier, -61), new EffectSpec(EffectKind.Amplifier, 37),
            new EffectSpec(EffectKind.Amplifier, float.NegativeInfinity)
        })
        {
            var project = TestProjects.Create();
            Assert.Throws<InvalidDataException>(() => (project with { Tracks = [project.Tracks[0] with { Effects = [spec] }] }).Validate());
        }
    }

    [Fact]
    public void SparseNormalizationWithNoiseReductionMatchesTheDenseRenderedPeak()
    {
        var project = TestProjects.Create(2000);
        var first = project.Tracks[0].Clips[0];
        project = project with
        {
            Tracks = [project.Tracks[0] with
            {
                Clips = [first, first with { Id = Guid.NewGuid(), Start = 2500 }],
                Effects = [BuiltInEffects.Create(EffectKind.NoiseReduction), new EffectSpec(EffectKind.Normalizer, 0.7f)]
            }]
        };
        var pcm = Enumerable.Range(0, 4000).Select(i => (float)(0.2 * Math.Sin(i / 2 * 0.03))).ToArray();
        var buffer = new AudioBuffer(new DecodedAudio(new PcmFormat(48000, 2), pcm));
        var output = new float[9000];
        new RenderPlan(project, _ => buffer).Render(0, output);
        Assert.InRange(output.Max(sample => Math.Abs(sample)), 0.69999f, 0.70001f);
        Assert.All(output.Skip(4000).Take(1000), sample => Assert.Equal(0, sample));
    }

    [Fact]
    public void LivePlanSwapPreservesNoiseFilterStateWithoutCallbackAllocation()
    {
        using var backend = new FakeBackend();
        using var transport = new AudioTransport(backend);
        var project = TestProjects.Create(2000);
        project = project with { Tracks = [project.Tracks[0] with { Effects = [BuiltInEffects.Create(EffectKind.NoiseReduction)] }] };
        var pcm = Enumerable.Range(0, 4000).Select(i => (float)(0.2 * Math.Sin(i / 2 * 0.03))).ToArray();
        var buffer = new AudioBuffer(new DecodedAudio(new PcmFormat(48000, 2), pcm));
        var reference = new RenderPlan(project, _ => buffer);
        reference.Render(0, new float[256]);
        transport.SetPlan(new RenderPlan(project, _ => buffer));
        transport.Play();
        backend.Output!.Pull(128);
        var changed = project with { Tracks = [project.Tracks[0] with { Volume = 0.5f }] };
        var expectedPlan = new RenderPlan(changed, _ => buffer);
        expectedPlan.AdoptStateFrom(reference);
        var expected = new float[256];
        expectedPlan.Render(128, expected);
        transport.UpdatePlan(new RenderPlan(changed, _ => buffer));
        var actual = new float[256];
        var before = GC.GetAllocatedBytesForCurrentThread();
        backend.Output.Pull(actual);
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
        Assert.Equal(expected, actual);
        Assert.Equal(1, backend.OutputOpens);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task OlderProjectsKeepTheirEffectsAndReceiveHarmlessReductionDefaults(int version)
    {
        using var directory = new TempDirectory();
        var source = TestProjects.WriteWave(directory.Path);
        var project = TestProjects.Create() with { Assets = [new(TestProjects.AssetId, source, 10)] };
        project = project with { Tracks = [project.Tracks[0] with { Effects = [new EffectSpec(EffectKind.Gain, 2)] }] };
        var store = new JsonProjectStore();
        var path = Path.Combine(directory.Path, "old.audionest");
        await store.SaveAsync(project, path);
        var json = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        json["version"] = version;
        json["tracks"]![0]!["effects"]![0]!.AsObject().Remove("reductionDb");
        var original = json.ToJsonString();
        await File.WriteAllTextAsync(path, original);
        var loaded = await store.LoadAsync(path);
        Assert.Equal(5, loaded.Version);
        Assert.Equal(new EffectSpec(EffectKind.Gain, 2), loaded.Tracks[0].Effects[0]);
        Assert.Equal(original, await File.ReadAllTextAsync(path));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DarkStartupAndNewEffectEditorsWorkForClipsAndTracks(bool track)
    {
        using var directory = new TempDirectory();
        using var backend = new FakeBackend { DecodedFrames = 4800 };
        using var vm = new MainViewModel(new ProjectSession(), new AudioLibrary(backend), new AudioTransport(backend), backend,
            new JsonProjectStore(), Path.Combine(directory.Path, "workspace"));
        await vm.ImportAsync(TestProjects.WriteWave(directory.Path));
        var trackId = vm.Project.Tracks[0].Id;
        var clipId = vm.Project.Tracks[0].Clips[0].Id;
        vm.Select(trackId, track ? null : clipId);
        var window = new MainWindow(vm) { WindowState = WindowState.Normal, Height = 1100 };
        window.Show(); Layout();
        Assert.True(vm.IsDarkMode);
        Assert.Equal(ThemeVariant.Dark, Avalonia.Application.Current!.RequestedThemeVariant);
        var effects = window.FindControl<Menu>("EditorMenuBar")!.Items.OfType<MenuItem>().Single(m => Equals(m.Header, "Effects"));
        effects.Items.OfType<MenuItem>().Single(m => Equals(m.Header, "Add Noise Reduction")).Command!.Execute(null);
        effects.Items.OfType<MenuItem>().Single(m => Equals(m.Header, "Add Amplifier")).Command!.Execute(null);
        Layout();
        var reduction = Assert.IsType<NoiseReductionEffectEditorViewModel>(vm.Inspector.Effects[0]);
        var amp = Assert.IsType<AmplifierEffectEditorViewModel>(vm.Inspector.Effects[1]);
        Assert.Equal(4, reduction.Parameters.Count);
        Assert.Equal(3, amp.Value);
        var slider = window.GetVisualDescendants().OfType<EffectParameterSlider>().First();
        var thumb = slider.GetVisualDescendants().OfType<Thumb>().Single();
        var point = thumb.TranslatePoint(new Point(thumb.Bounds.Width / 2, thumb.Bounds.Height / 2), window)!.Value;
        var project = vm.Project;
        window.MouseDown(point, MouseButton.Left);
        window.MouseMove(point - new Vector(20, 0)); Layout();
        Assert.Same(project, vm.Project);
        window.MouseUp(point - new Vector(20, 0), MouseButton.Left); Layout();
        Assert.NotEqual(-45, reduction.Parameters[0].Value);
        vm.Undo();
        Assert.Equal(-45, reduction.Parameters[0].Value);
        reduction.Parameters[1].Value = 24;
        reduction.Parameters[2].Value = 3.5;
        reduction.Parameters[3].Value = 150;
        amp.Value = 6;
        reduction.Enabled = false;
        var saved = (track ? vm.SelectedTrack!.Effects : vm.SelectedClip!.Effects).ToArray();
        var path = Path.Combine(directory.Path, "new.audionest");
        await vm.SaveAsync(path); await vm.OpenAsync(path);
        vm.Select(trackId, track ? null : clipId); Layout();
        Assert.Equal(saved, (track ? vm.SelectedTrack!.Effects : vm.SelectedClip!.Effects).ToArray());
        Assert.Empty(track ? vm.SelectedTrack!.Clips[0].Effects : vm.SelectedTrack!.Effects);
        var artifactDirectory = Path.Combine(AppContext.BaseDirectory, "ui-artifacts");
        Directory.CreateDirectory(artifactDirectory);
        using (var frame = window.CaptureRenderedFrame()) frame!.Save(Path.Combine(artifactDirectory, track ? "new-track-effects.png" : "new-clip-effects.png"));
        vm.Inspector.Effects[0].Remove.Execute(null);
        Assert.Single(vm.Inspector.Effects);
        vm.Undo();
        Assert.Equal(2, vm.Inspector.Effects.Count);
        vm.ClearSelection();
        reduction.Parameters[0].Value = -30;
        Assert.False(reduction.CanEdit);
        Assert.Empty(vm.Inspector.Effects);
        vm.New(); window.Close(); Layout();
    }

    private static void Layout() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
}
