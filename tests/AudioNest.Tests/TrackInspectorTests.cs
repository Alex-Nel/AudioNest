using System.Collections.Immutable;
using System.Text.Json.Nodes;
using AudioNest.Application;
using AudioNest.Audio;
using AudioNest.Desktop;
using AudioNest.Domain;
using AudioNest.Persistence;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace AudioNest.Tests;

public sealed class TrackInspectorTests
{
    private static MainViewModel Create(FakeBackend backend, TempDirectory directory) =>
        new(new ProjectSession(), new AudioLibrary(backend), new AudioTransport(backend), backend,
            new JsonProjectStore(), Path.Combine(directory.Path, "workspace"));
    private static void Layout() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
    private static void Click(Window window, Control control, Point point)
    {
        point = control.TranslatePoint(point, window)!.Value;
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Layout();
    }
    private static void Capture(Window window, string name)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "ui-artifacts");
        Directory.CreateDirectory(directory);
        using var frame = window.CaptureRenderedFrame();
        frame!.Save(Path.Combine(directory, name + ".png"));
    }
    private static void Close(Window window, MainViewModel vm) { vm.New(); window.Close(); Layout(); }

    [AvaloniaFact]
    public async Task PointerFocusSwitchesEmptyClipAndTrackInspectorAndRoutesAddOnlyMenu()
    {
        using var directory = new TempDirectory();
        using var backend = new FakeBackend { DecodedFrames = 48000 };
        using var vm = Create(backend, directory);
        await vm.ImportAsync(TestProjects.WriteWave(directory.Path));
        vm.ClearSelection();
        var window = new MainWindow(vm) { WindowState = WindowState.Normal };
        window.Show(); Layout();
        var inspector = window.GetVisualDescendants().OfType<ClipInspectorView>().Single();
        var title = inspector.FindControl<TextBlock>("InspectorTitle")!;
        var menu = window.FindControl<Menu>("EditorMenuBar")!.Items.OfType<MenuItem>().Single(i => Equals(i.Header, "Effects"));
        var actions = menu.Items.OfType<MenuItem>().ToArray();
        Assert.Equal(new[] { "Add Gain", "Add Compressor", "Add Limiter", "Add Normalizer", "Add Noise Reduction", "Add Amplifier" },
            actions.Select(i => (string)i.Header!));
        Assert.DoesNotContain(menu.Items, item => item is Separator);
        Assert.Null(vm.FocusedTarget);
        Assert.Equal("Clip Inspector", title.Text);
        var project = vm.Project;
        foreach (var action in actions) { Assert.False(action.Command!.CanExecute(null)); action.Command.Execute(null); }
        Assert.Same(project, vm.Project);
        var timeline = window.FindControl<TimelineControl>("TimelineClips")!;
        Click(window, timeline, new Point(40, 65));
        Assert.NotNull(vm.SelectedClip);
        Assert.Equal("Clip Inspector", title.Text);
        Assert.True(actions.All(i => i.Command!.CanExecute(null)));
        actions[1].Command!.Execute(null); Layout();
        Assert.Equal(EffectKind.Compressor, vm.SelectedClip!.Effects.Single().Kind);
        Assert.Empty(vm.SelectedTrack!.Effects);
        var header = window.GetVisualDescendants().OfType<TrackHeaderView>().Single();
        Click(window, header, new Point(90, 15));
        Assert.Equal("Track Inspector", title.Text);
        Assert.True(vm.IsTrackFocused);
        Assert.Null(vm.SelectedClip);
        Assert.False(inspector.FindControl<TextBox>("ClipStartEditor")!.IsVisible);
        Assert.Equal("Solo", inspector.FindControl<CheckBox>("ClipLockBox")!.Content);
        actions[2].Command!.Execute(null); Layout();
        Assert.Equal(EffectKind.Limiter, vm.SelectedTrack.Effects.Single().Kind);
        Assert.Equal(EffectKind.Compressor, vm.SelectedTrack.Clips[0].Effects.Single().Kind);
        Click(window, timeline, new Point(400, 65));
        Assert.Null(vm.FocusedTarget);
        Assert.Equal("Clip Inspector", title.Text);
        Assert.Equal("", inspector.FindControl<TextBox>("ClipNameEditor")!.Text);
        Assert.False(vm.Inspector.Properties.CanEdit);
        Assert.Empty(vm.Inspector.Effects);
        Assert.False(vm.TrackHeaders[0].IsSelected);
        foreach (var action in actions) Assert.False(action.Command!.CanExecute(null));
        Click(window, header, new Point(90, 15));
        Click(window, timeline, new Point(400, vm.TrackContentHeight + 30));
        Assert.Null(vm.FocusedTarget);
        Close(window, vm);
    }

    [AvaloniaFact]
    public async Task TrackGlobalPropertiesSyncHeadersWithoutRewritingEffectsAndPreviewIsUndoable()
    {
        using var directory = new TempDirectory();
        using var backend = new FakeBackend { DecodedFrames = 48000 };
        using var vm = Create(backend, directory);
        await vm.ImportAsync(TestProjects.WriteWave(directory.Path));
        vm.Select(vm.Project.Tracks[0].Id, null);
        var properties = vm.Inspector.Properties;
        properties.Name = "Main bus";
        properties.Muted = true;
        properties.Solo = true;
        properties.Pan = 0.25;
        Assert.Equal("Main bus", vm.TrackHeaders[0].Name);
        Assert.True(vm.TrackHeaders[0].IsMuted);
        Assert.True(vm.TrackHeaders[0].IsSolo);
        Assert.Equal(0.25, vm.TrackHeaders[0].Pan);
        Assert.False(vm.Project.Tracks[0].Clips[0].Muted);
        properties.Muted = false;
        vm.AddEffect(EffectKind.Gain);
        var gain = Assert.IsType<GainEffectEditorViewModel>(vm.Inspector.Effects[0]);
        gain.Value = 0.75;
        properties.Volume = 0.5;
        Assert.Equal(0.75f, vm.SelectedTrack!.Effects[0].Value);
        Assert.Equal(0.5, vm.TrackHeaders[0].Volume);
        vm.TrackHeaders[0].Volume = 0.8;
        Assert.Equal(0.8, properties.Volume, 5);
        Assert.Same(gain, vm.Inspector.Effects[0]);
        vm.StartPlayback();
        var output = backend.Output!;
        var project = vm.Project;
        properties.BeginAdjustment(false); properties.Volume = 0.25; properties.Volume = 0;
        Assert.Same(project, vm.Project);
        Assert.All(output.Pull(16), sample => Assert.Equal(0, sample));
        properties.EndAdjustment();
        Assert.True(vm.CanPause);
        Assert.Same(output, backend.Output);
        vm.Undo();
        Assert.Equal(0.8f, vm.SelectedTrack.Volume, 5);
        Assert.Equal(0.75f, vm.SelectedTrack.Effects[0].Value);
        var clipId = vm.SelectedTrack.Clips[0].Id;
        vm.StartPlayback();
        gain.BeginAdjustment(); gain.Value = 0;
        vm.Select(vm.SelectedTrack.Id, clipId);
        gain.EndAdjustment();
        Assert.False(gain.CanEdit);
        Assert.Equal(0.75f, vm.SelectedTrack.Effects[0].Value);
        Assert.Empty(vm.SelectedClip!.Effects);
        Assert.Contains(backend.Output!.Pull(16), sample => sample != 0);
    }

    [AvaloniaFact]
    public async Task DynamicsEditorsHaveIndependentParametersHistoryAndCapturedScope()
    {
        using var directory = new TempDirectory();
        using var backend = new FakeBackend { DecodedFrames = 4800 };
        using var vm = Create(backend, directory);
        await vm.ImportAsync(TestProjects.WriteWave(directory.Path));
        var trackId = vm.Project.Tracks[0].Id;
        vm.Select(trackId, null);
        vm.AddEffect(EffectKind.Compressor);
        vm.AddEffect(EffectKind.Limiter);
        vm.AddEffect(EffectKind.Normalizer);
        var compressor = Assert.IsType<CompressorEffectEditorViewModel>(vm.Inspector.Effects[0]);
        Assert.Equal(5, compressor.Parameters.Count);
        compressor.Parameters[0].BeginAdjustment();
        compressor.Parameters[0].Value = -24;
        compressor.Parameters[0].Value = -30;
        Assert.Equal(-18, vm.SelectedTrack!.Effects[0].Value);
        compressor.Parameters[0].EndAdjustment();
        Assert.Equal(-30, vm.SelectedTrack.Effects[0].Value);
        vm.Undo();
        Assert.Equal(-18, vm.SelectedTrack.Effects[0].Value);
        compressor.Parameters[1].Value = 6;
        Assert.Equal(6, vm.SelectedTrack.Effects[0].Ratio);
        var limiter = Assert.IsType<LimiterEffectEditorViewModel>(vm.Inspector.Effects[1]);
        limiter.Value = 0.8;
        limiter.Parameters[0].Value = 200;
        var normalizer = Assert.IsType<NormalizerEffectEditorViewModel>(vm.Inspector.Effects[2]);
        normalizer.Value = 0.7;
        compressor.Enabled = false;
        var savedEffects = vm.SelectedTrack.Effects;
        var path = Path.Combine(directory.Path, "dynamics.audionest");
        await vm.SaveAsync(path); await vm.OpenAsync(path);
        Assert.Equal(savedEffects.ToArray(), vm.Project.Tracks[0].Effects.ToArray());
        vm.Select(trackId, null);
        var window = new MainWindow(vm) { WindowState = WindowState.Normal, Height = 1100 };
        window.Show(); Layout();
        Assert.Equal(8, window.GetVisualDescendants().OfType<EffectParameterSlider>().Count());
        var threshold = window.GetVisualDescendants().OfType<EffectParameterSlider>().First();
        var thumb = threshold.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.Thumb>().Single();
        var point = thumb.TranslatePoint(new Point(thumb.Bounds.Width / 2, thumb.Bounds.Height / 2), window)!.Value;
        var beforeDrag = vm.Project;
        window.MouseDown(point, MouseButton.Left);
        window.MouseMove(point + new Vector(20, 0)); Layout();
        Assert.Same(beforeDrag, vm.Project);
        window.MouseUp(point + new Vector(20, 0), MouseButton.Left); Layout();
        Assert.NotEqual(beforeDrag.Tracks[0].Effects[0].Value, vm.SelectedTrack!.Effects[0].Value);
        vm.Undo();
        Assert.Equal(beforeDrag.Tracks[0].Effects[0].Value, vm.SelectedTrack.Effects[0].Value);
        foreach (var dark in new[] { false, true })
        {
            vm.IsDarkMode = dark; Layout();
            Capture(window, dark ? "track-dynamics-dark" : "track-dynamics-light");
        }
        var stale = vm.Inspector.Effects[0];
        stale.Remove.Execute(null);
        Assert.Equal(2, vm.SelectedTrack!.Effects.Length);
        Assert.False(stale.Remove.CanExecute(null));
        vm.Undo();
        Assert.Equal(3, vm.SelectedTrack.Effects.Length);
        Close(window, vm);
    }

    [AvaloniaFact]
    public async Task FailedNormalizationPreparationDoesNotCommitOrStopThePlayingProject()
    {
        using var directory = new TempDirectory();
        using var backend = new FakeBackend { DecodedFrames = 1000 };
        using var vm = Create(backend, directory);
        await vm.ImportAsync(TestProjects.WriteWave(directory.Path));
        vm.Select(vm.Project.Tracks[0].Id, null);
        vm.StartPlayback();
        var project = vm.Project;
        vm.Edit("Unsafe chain", p => ProjectEdits.UpdateTrack(p, p.Tracks[0].Id, t => t with
        {
            Effects = Enumerable.Repeat(new EffectSpec(EffectKind.Gain, 16), 50)
                .Append(new EffectSpec(EffectKind.Normalizer, 0.8f)).ToImmutableArray()
        }), keepPlaying: true);
        Assert.Same(project, vm.Project);
        Assert.True(vm.CanPause);
        Assert.StartsWith("Error:", vm.Status);
        Assert.Contains(backend.Output!.Pull(16), sample => sample != 0);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task LegacyTrackVolumeAndEffectParametersLoadWithNeutralDefaults(int version)
    {
        using var directory = new TempDirectory();
        var source = TestProjects.WriteWave(directory.Path);
        var project = TestProjects.Create() with { Assets = [new(TestProjects.AssetId, source, 10)] };
        project = project with { Tracks = [project.Tracks[0] with { Effects = [new EffectSpec(EffectKind.Gain, 2)] }] };
        var store = new JsonProjectStore();
        var path = Path.Combine(directory.Path, "legacy.audionest");
        await store.SaveAsync(project, path);
        var json = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        json["version"] = version;
        var track = json["tracks"]![0]!.AsObject();
        track.Remove("volume");
        var effect = track["effects"]![0]!.AsObject();
        foreach (var name in new[] { "ratio", "attackMs", "releaseMs", "makeupDb" }) effect.Remove(name);
        var original = json.ToJsonString();
        await File.WriteAllTextAsync(path, original);
        var loaded = await store.LoadAsync(path);
        Assert.Equal(AudioProject.CurrentVersion, loaded.Version);
        Assert.Equal(1, loaded.Tracks[0].Volume);
        Assert.Equal(new EffectSpec(EffectKind.Gain, 2), loaded.Tracks[0].Effects[0]);
        var output = new float[2];
        new RenderPlan(loaded, _ => TestProjects.Buffer()).Render(0, output);
        Assert.Equal(0.5f, output[0]);
        Assert.Equal(original, await File.ReadAllTextAsync(path));
    }
}
