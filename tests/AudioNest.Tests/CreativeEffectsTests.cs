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
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace AudioNest.Tests;

public sealed class CreativeEffectsTests
{
    private static IAudioProcessor Create(EffectSpec spec, int rate = 8000, int channels = 2) =>
        new BuiltInEffectFactory().Create(spec, new PcmFormat(rate, channels));
    private static EffectSpec Short(EffectKind kind) => BuiltInEffects.Create(kind) with
    {
        Value = kind == EffectKind.Echo ? 10 : kind == EffectKind.Reverb ? 0.2f : 12,
        Mix = 1
    };
    private static float[] Impulse(int frames, int channels = 2)
    {
        var samples = new float[frames * channels];
        samples[0] = 0.5f;
        if (channels == 2) samples[1] = 0.125f;
        return samples;
    }
    private static double Energy(float[] samples, int from, int to) =>
        samples.Skip(from * 2).Take((to - from) * 2).Sum(s => (double)s * s);

    [Fact]
    public void EchoHasExactDelayAndGeometricRepeatsWithoutStereoCrossfeed()
    {
        var spec = Short(EffectKind.Echo) with { Feedback = 0.5f };
        var samples = Impulse(400);
        Create(spec).Process(samples);
        for (var frame = 0; frame < 400; frame++)
        {
            var expected = frame > 0 && frame % 80 == 0 ? 0.5f * MathF.Pow(0.5f, frame / 80 - 1) : 0;
            Assert.Equal(expected, samples[frame * 2]);
            Assert.Equal(expected * 0.25f, samples[frame * 2 + 1]);
        }
        samples = Impulse(200);
        samples[1] = 0;
        Create(spec).Process(samples);
        Assert.All(samples.Where((_, i) => i % 2 == 1), sample => Assert.Equal(0, sample));
        samples = Impulse(400);
        Create(spec with { Feedback = 0 }).Process(samples);
        Assert.Equal(0.5f, samples[160]);
        Assert.All(samples.Skip(162), sample => Assert.Equal(0, sample));
    }

    [Fact]
    public void ReverbBuildsADecayingDiffuseTailWithIndependentStereoChannels()
    {
        var samples = Impulse(8000);
        Create(Short(EffectKind.Reverb)).Process(samples);
        Assert.All(samples.Take(400), sample => Assert.Equal(0, sample));
        Assert.True(Energy(samples, 200, 2000) > 0.001);
        Assert.True(Energy(samples, 2000, 4000) < Energy(samples, 200, 2000));
        Assert.All(samples.Skip(8100), sample => Assert.Equal(0, sample));
        for (var i = 0; i < samples.Length; i += 2) Assert.Equal(samples[i] * 0.25f, samples[i + 1], 6);
        var longDecay = Impulse(8000);
        Create(Short(EffectKind.Reverb) with { Value = 1 }).Process(longDecay);
        Assert.True(Energy(longDecay, 2000, 4000) > Energy(samples, 2000, 4000) * 2);
        var damped = Impulse(8000);
        Create(Short(EffectKind.Reverb) with { Damping = 0.95f }).Process(damped);
        Assert.NotEqual(samples, damped);
        samples = Impulse(4000);
        samples[1] = 0;
        Create(Short(EffectKind.Reverb)).Process(samples);
        Assert.All(samples.Where((_, i) => i % 2 == 1), sample => Assert.Equal(0, sample));
    }

    [Fact]
    public void DistortionUsesSymmetricSaturationWetTrimAndDryWetMix()
    {
        var spec = Short(EffectKind.Distortion) with { Value = 20, OutputDb = -12, Mix = 0.75f };
        float[] source = [-2, -0.2f, -0.01f, 0, 0.01f, 0.2f, 2];
        var samples = source.ToArray();
        Create(spec, channels: 1).Process(samples);
        for (var i = 0; i < source.Length; i++)
            Assert.Equal((float)(source[i] * 0.25 + Math.Tanh(source[i] * 10d) * Math.Pow(10, -12d / 20) * 0.75), samples[i]);
        Assert.Equal(-samples[0], samples[^1]);
        samples = source.ToArray();
        Create(spec with { Mix = 1, OutputDb = 0, Value = 36 }, channels: 1).Process(samples);
        Assert.All(samples, sample => Assert.InRange(sample, -1, 1));
        Assert.Equal(0, samples[3]);
    }

    [Theory]
    [InlineData(EffectKind.Reverb)]
    [InlineData(EffectKind.Echo)]
    [InlineData(EffectKind.Distortion)]
    public void ZeroMixIsExactlyTransparentAndFreshSilenceRemainsSilent(EffectKind kind)
    {
        var samples = Impulse(1000);
        var expected = samples.ToArray();
        var processor = Create(Short(kind) with { Mix = 0 });
        Assert.Equal(0, processor.TailFrames);
        processor.Process(samples);
        Assert.Equal(expected, samples);
        samples = new float[2000];
        Create(Short(kind)).Process(samples);
        Assert.All(samples, sample => Assert.Equal(0, sample));
    }

    [Theory]
    [InlineData(EffectKind.Echo, 8000, 1)]
    [InlineData(EffectKind.Echo, 44100, 2)]
    [InlineData(EffectKind.Echo, 192000, 2)]
    [InlineData(EffectKind.Reverb, 8000, 1)]
    [InlineData(EffectKind.Reverb, 44100, 2)]
    [InlineData(EffectKind.Reverb, 192000, 2)]
    public void DelayNetworksSupportProjectRatesAndMonoOrStereo(EffectKind kind, int rate, int channels)
    {
        var data = Impulse(rate / 2, channels);
        Create(Short(kind), rate, channels).Process(data);
        Assert.All(data, sample => Assert.True(float.IsFinite(sample)));
        Assert.Contains(data, sample => sample != 0);
    }

    [Theory]
    [InlineData(EffectKind.Echo)]
    [InlineData(EffectKind.Reverb)]
    public void DelayStateCopiesAndSilenceAdvanceMatchContinuousProcessingWithoutAllocating(EffectKind kind)
    {
        var spec = Short(kind);
        var processor = Create(spec);
        processor.Process(Impulse(350));
        var copied = Create(spec);
        var before = GC.GetAllocatedBytesForCurrentThread();
        copied.CopyStateFrom(processor);
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
        processor.Process(new float[222]);
        copied.AdvanceSilence(111);
        var expected = new float[3000];
        var actual = new float[3000];
        processor.Process(expected);
        copied.Process(actual);
        Assert.Equal(expected, actual);
        copied.AdvanceSilence(copied.TailFrames + 1);
        actual = Impulse(4000);
        expected = actual.ToArray();
        copied.Process(actual);
        Create(spec).Process(expected);
        Assert.Equal(expected, actual);
        copied.Reset();
        var silence = new float[1000];
        copied.Process(silence);
        Assert.All(silence, sample => Assert.Equal(0, sample));
    }

    [Theory]
    [InlineData(EffectKind.Echo)]
    [InlineData(EffectKind.Reverb)]
    [InlineData(EffectKind.Distortion)]
    public void AudibleOutputAndTailCutoffAreIndependentOfBlockSize(EffectKind kind)
    {
        var spec = Short(kind) with { Feedback = 0.9f };
        var source = Impulse(16000);
        source[18000] = 0.25f; // Retrigger after the first bounded tail is empty.
        source[18001] = -0.125f;
        var full = source.ToArray();
        Create(spec).Process(full);
        var chunked = source.ToArray();
        var processor = Create(spec);
        for (var at = 0; at < chunked.Length;)
        {
            var count = Math.Min(74, chunked.Length - at);
            processor.Process(chunked.AsSpan(at, count));
            at += count;
        }
        Assert.Equal(full, chunked);
        Assert.Contains(full, sample => sample != 0);
        Assert.All(full.Skip(16000).Take(2000), sample => Assert.Equal(0, sample));
        processor.Reset();
        chunked = source.ToArray();
        processor.Process(chunked);
        Assert.Equal(full, chunked);
    }

    private static (AudioProject Project, AudioBuffer Buffer) TailProject(EffectKind kind)
    {
        var project = TestProjects.Create(12000);
        var first = project.Tracks[0].Clips[0] with { Length = 1 };
        project = project with
        {
            Tracks = [project.Tracks[0] with
            {
                Clips = [first, first with { Id = Guid.NewGuid(), Start = 480 }],
                Effects = [Short(kind) with { Feedback = 0.8f }]
            }, new AudioTrack { Volume = 0, Clips = [new AudioClip { AssetId = TestProjects.AssetId, Length = 12000 }] }]
        };
        return (project, new AudioBuffer(new DecodedAudio(new PcmFormat(48000, 2), Impulse(12000))));
    }

    [Theory]
    [InlineData(EffectKind.Echo)]
    [InlineData(EffectKind.Reverb)]
    [InlineData(EffectKind.Distortion)]
    public void TrackNormalizerIncludesGapTailsAndPlaybackMatchesWaveExport(EffectKind kind)
    {
        using var directory = new TempDirectory();
        var (project, buffer) = TailProject(kind);
        var raw = new float[24000];
        new RenderPlan(project, _ => buffer).Render(0, raw);
        var peak = raw.Max(s => Math.Abs(s));
        Assert.True(peak > 0);
        var normalized = project with
        {
            Tracks = project.Tracks.SetItem(0, project.Tracks[0] with
            {
                Effects = project.Tracks[0].Effects.Add(new EffectSpec(EffectKind.Normalizer, 0.7f))
            })
        };
        var output = new float[24000];
        new RenderPlan(normalized, _ => buffer).Render(0, output);
        Assert.InRange(output.Max(s => Math.Abs(s)), 0.69999f, 0.70001f);
        for (var i = 0; i < output.Length; i++) Assert.Equal(raw[i] * (0.7f / peak), output[i], 5);
        var path = Path.Combine(directory.Path, "creative.wav");
        WaveExporter.Export(new RenderPlan(normalized, _ => buffer), path);
        var bytes = File.ReadAllBytes(path);
        Assert.Equal(44 + output.Length * 2, bytes.Length);
        for (var i = 0; i < output.Length; i++)
            Assert.InRange(Math.Abs(output[i] - BitConverter.ToInt16(bytes, 44 + i * 2) / 32768f), 0, 0.00004);
        if (kind != EffectKind.Distortion) Assert.Contains(output.Skip(962), sample => sample != 0);
    }

    [Fact]
    public void SerialDelaysDrainBeforeSparseNormalizationSkipsDistantGaps()
    {
        var (project, buffer) = TailProject(EffectKind.Echo);
        var track = project.Tracks[0] with
        {
            Effects = [Short(EffectKind.Echo), Short(EffectKind.Reverb), new EffectSpec(EffectKind.Normalizer, 0.7f)],
            Clips = project.Tracks[0].Clips.Add(project.Tracks[0].Clips[0] with { Id = Guid.NewGuid(), Start = 1_000_000_000_000 })
        };
        project = project with { Tracks = [track] };
        var output = new float[24000];
        new RenderPlan(project, _ => buffer).Render(0, output);
        Assert.InRange(output.Max(s => Math.Abs(s)), 0.69999f, 0.70001f);
    }

    [Theory]
    [InlineData(EffectKind.Echo)]
    [InlineData(EffectKind.Reverb)]
    public void ClipTailsStopAtClipEdgeAndTrackTailsStopAtProjectEnd(EffectKind kind)
    {
        var (project, buffer) = TailProject(kind);
        var first = project.Tracks[0].Clips[0] with { Effects = [Short(kind)] };
        project = project with { Tracks = project.Tracks.SetItem(0, project.Tracks[0] with { Clips = [first], Effects = [] }) };
        var output = new float[26000];
        var plan = new RenderPlan(project, _ => buffer);
        plan.Render(0, output);
        Assert.All(output, sample => Assert.Equal(0, sample)); // Fully wet delay starts after this 1-frame clip.
        project = project with
        {
            Tracks = project.Tracks.SetItem(0, project.Tracks[0] with
            {
                Clips = [first with { Effects = [] }],
                Effects = [Short(kind)]
            })
        };
        plan = new RenderPlan(project, _ => buffer);
        plan.Render(0, output);
        Assert.Contains(output, sample => sample != 0);
        Assert.Equal(project.Duration, plan.Duration);
        Assert.All(output.Skip(24000), sample => Assert.Equal(0, sample));
    }

    [Fact]
    public void NormalizationCacheInvalidatesWhenAnotherTrackExtendsTheTailWindow()
    {
        var (project, buffer) = TailProject(EffectKind.Echo);
        project = project with
        {
            Tracks = [project.Tracks[0] with
            {
                Effects = project.Tracks[0].Effects.Add(new EffectSpec(EffectKind.Normalizer, 0.7f))
            }]
        };
        var previous = new RenderPlan(project, _ => buffer);
        var extended = project with
        {
            Tracks = project.Tracks.Add(new AudioTrack
            {
                Volume = 0,
                Clips = [new AudioClip { AssetId = TestProjects.AssetId, Length = 12000 }]
            })
        };
        var plan = new RenderPlan(extended, _ => buffer, previous: previous);
        Assert.Equal(1, plan.NormalizationAnalysisCount);
        var output = new float[24000];
        plan.Render(0, output);
        Assert.InRange(output.Max(s => Math.Abs(s)), 0.69999f, 0.70001f);
    }

    [Theory]
    [InlineData(EffectKind.Echo)]
    [InlineData(EffectKind.Reverb)]
    [InlineData(EffectKind.Distortion)]
    public void BypassAndLiveGraphHandoffPreserveExpectedAudio(EffectKind kind)
    {
        var (project, buffer) = TailProject(kind);
        using var backend = new FakeBackend();
        using var transport = new AudioTransport(backend);
        transport.SetPlan(new RenderPlan(project, _ => buffer));
        transport.Play();
        backend.Output!.Pull(2000);
        var reference = new RenderPlan(project, _ => buffer);
        reference.Render(0, new float[4000]);
        var changed = project with { Tracks = project.Tracks.SetItem(0, project.Tracks[0] with { Pan = 0.2f }) };
        var expectedPlan = new RenderPlan(changed, _ => buffer);
        expectedPlan.AdoptStateFrom(reference);
        var expected = new float[1000];
        expectedPlan.Render(2000, expected);
        transport.UpdatePlan(new RenderPlan(changed, _ => buffer));
        var actual = new float[1000];
        var before = GC.GetAllocatedBytesForCurrentThread();
        backend.Output.Pull(actual);
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
        Assert.Equal(expected, actual);
        Assert.Equal(1, backend.OutputOpens);
        var bypassed = project with
        {
            Tracks = project.Tracks.SetItem(0, project.Tracks[0] with
            {
                Effects = [project.Tracks[0].Effects[0] with { Bypassed = true }]
            })
        };
        actual = new float[24000];
        new RenderPlan(bypassed, _ => buffer).Render(0, actual);
        Assert.Equal(0.5f, actual[0]);
        Assert.All(actual.Skip(962), sample => Assert.Equal(0, sample));
    }

    [Fact]
    public void CreativeParametersRejectNonFiniteAndOutOfRangeValues()
    {
        var echo = Short(EffectKind.Echo);
        foreach (var spec in new[]
        {
            echo with { Value = 9 }, echo with { Value = 2001 }, echo with { Value = float.NaN },
            echo with { Feedback = -0.01f }, echo with { Feedback = 0.91f }, echo with { Feedback = float.PositiveInfinity },
            echo with { Mix = -0.01f }, echo with { Mix = 1.01f }, echo with { Mix = float.NaN },
            Short(EffectKind.Reverb) with { Value = 0.1f }, Short(EffectKind.Reverb) with { Value = 10.1f },
            echo with { Damping = -1 }, echo with { Damping = 0.96f }, echo with { Damping = float.NaN },
            Short(EffectKind.Distortion) with { Value = -1 }, Short(EffectKind.Distortion) with { Value = 37 },
            echo with { OutputDb = -37 }, echo with { OutputDb = 1 }, echo with { OutputDb = float.NaN }
        })
        {
            var project = TestProjects.Create();
            Assert.Throws<InvalidDataException>(() => (project with { Tracks = [project.Tracks[0] with { Effects = [spec] }] }).Validate());
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task LegacyProjectsReceiveOptionalCreativeDefaultsWithoutChangingFiles(int version)
    {
        using var directory = new TempDirectory();
        var project = TestProjects.Create() with { Assets = [new(TestProjects.AssetId, TestProjects.WriteWave(directory.Path), 10)] };
        project = project with { Tracks = [project.Tracks[0] with { Effects = [new EffectSpec(EffectKind.Gain, 2)] }] };
        var store = new JsonProjectStore();
        var path = Path.Combine(directory.Path, "legacy.audionest");
        await store.SaveAsync(project, path);
        var json = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        json["version"] = version;
        var effect = json["tracks"]![0]!["effects"]![0]!.AsObject();
        foreach (var field in new[] { "mix", "feedback", "damping", "outputDb" }) effect.Remove(field);
        var original = json.ToJsonString();
        await File.WriteAllTextAsync(path, original);
        var loaded = await store.LoadAsync(path);
        Assert.Equal(AudioProject.CurrentVersion, loaded.Version);
        Assert.Equal(new EffectSpec(EffectKind.Gain, 2), loaded.Tracks[0].Effects[0]);
        Assert.Equal(original, await File.ReadAllTextAsync(path));
    }

    [AvaloniaTheory]
    [InlineData(EffectKind.Reverb, false)]
    [InlineData(EffectKind.Reverb, true)]
    [InlineData(EffectKind.Echo, false)]
    [InlineData(EffectKind.Echo, true)]
    [InlineData(EffectKind.Distortion, false)]
    [InlineData(EffectKind.Distortion, true)]
    public async Task CreativeEditorsSupportTargetedAddPreviewUndoBypassRemovalAndPersistence(EffectKind kind, bool track)
    {
        using var directory = new TempDirectory();
        using var backend = new FakeBackend { DecodedFrames = 4800 };
        using var vm = new MainViewModel(new ProjectSession(), new AudioLibrary(backend), new AudioTransport(backend), backend,
            new JsonProjectStore(), Path.Combine(directory.Path, "workspace"));
        await vm.ImportAsync(TestProjects.WriteWave(directory.Path));
        var trackId = vm.Project.Tracks[0].Id;
        var clipId = vm.Project.Tracks[0].Clips[0].Id;
        vm.Select(trackId, track ? null : clipId);
        var window = new MainWindow(vm) { WindowState = WindowState.Normal, Height = 1000 };
        window.Show(); Layout();
        var effects = window.FindControl<Menu>("EditorMenuBar")!.Items.OfType<MenuItem>().Single(m => Equals(m.Header, "Effects"));
        var add = effects.Items.OfType<MenuItem>().Single(m => Equals(m.Header, "Add " + BuiltInEffects.Name(kind)));
        add.Command!.Execute(null); Layout();
        var editor = Assert.Single(vm.Inspector.Effects);
        Assert.Equal(kind, editor.Kind);
        Assert.Equal(3, editor.Parameters.Count);
        Assert.Equal(kind switch
        {
            EffectKind.Reverb => typeof(ReverbEffectEditorViewModel),
            EffectKind.Echo => typeof(EchoEffectEditorViewModel),
            _ => typeof(DistortionEffectEditorViewModel)
        }, editor.GetType());
        var original = editor.Parameters[0].Value;
        var slider = window.GetVisualDescendants().OfType<EffectParameterSlider>().First();
        var thumb = slider.GetVisualDescendants().OfType<Thumb>().Single();
        var point = thumb.TranslatePoint(new Point(thumb.Bounds.Width / 2, thumb.Bounds.Height / 2), window)!.Value;
        var before = vm.Project;
        window.MouseDown(point, MouseButton.Left);
        window.MouseMove(point + new Vector(25, 0)); Layout();
        Assert.Same(before, vm.Project);
        window.MouseUp(point + new Vector(25, 0), MouseButton.Left); Layout();
        Assert.NotEqual(original, editor.Parameters[0].Value);
        vm.Undo();
        Assert.Equal(original, editor.Parameters[0].Value);
        editor.Parameters[0].Value += editor.Parameters[0].Step * 2;
        editor.Parameters[1].Value = editor.Parameters[1].Minimum;
        editor.Parameters[2].Value = 0.6;
        editor.Enabled = false;
        var saved = (track ? vm.SelectedTrack!.Effects : vm.SelectedClip!.Effects).ToArray();
        var path = Path.Combine(directory.Path, "creative.audionest");
        await vm.SaveAsync(path); await vm.OpenAsync(path);
        vm.Select(trackId, track ? null : clipId); Layout();
        Assert.Equal(saved, (track ? vm.SelectedTrack!.Effects : vm.SelectedClip!.Effects).ToArray());
        Assert.Empty(track ? vm.SelectedTrack!.Clips[0].Effects : vm.SelectedTrack!.Effects);
        var artifacts = Path.Combine(AppContext.BaseDirectory, "ui-artifacts");
        Directory.CreateDirectory(artifacts);
        using (var frame = window.CaptureRenderedFrame())
            frame!.Save(Path.Combine(artifacts, $"{kind}-{(track ? "track" : "clip")}.png"));
        vm.Inspector.Effects[0].Remove.Execute(null);
        Assert.Empty(vm.Inspector.Effects);
        vm.Undo();
        Assert.Single(vm.Inspector.Effects);
        add.Command.Execute(null); Layout();
        Assert.Equal(2, vm.Inspector.Effects.Count);
        Assert.Equal(BuiltInEffects.Create(kind), (track ? vm.SelectedTrack!.Effects : vm.SelectedClip!.Effects)[1]);
        Assert.NotEqual(vm.Inspector.Effects[0].Parameters[2].Value, vm.Inspector.Effects[1].Parameters[2].Value);
        vm.ClearSelection();
        Assert.False(add.Command.CanExecute(null));
        editor.Parameters[0].Value = original;
        Assert.False(editor.CanEdit);
        vm.New(); window.Close(); Layout();
    }

    private static void Layout() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
}
