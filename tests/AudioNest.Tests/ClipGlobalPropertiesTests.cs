using System.Text.Json.Nodes;
using AudioNest.Application;
using AudioNest.Audio;
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

public sealed class ClipGlobalPropertiesTests
{
    private static MainViewModel Create(FakeBackend backend, TempDirectory directory) =>
        new(new ProjectSession(), new AudioLibrary(backend), new AudioTransport(backend), backend,
            new JsonProjectStore(), Path.Combine(directory.Path, "workspace"));
    private static void Layout() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
    private static void Click(Window window, Control control)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Layout();
    }
    private static void Drag(Window window, Control control, Point from, Vector delta)
    {
        var point = control.TranslatePoint(from, window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseMove(point + delta);
        window.MouseUp(point + delta, MouseButton.Left); Layout();
    }
    private static void Close(Window window, MainViewModel vm) { vm.New(); window.Close(); Layout(); }

    [Fact]
    public void ClipMixAndMuteAreIndependentOfOtherClipsAndTrackMixInPlaybackAndExport()
    {
        using var directory = new TempDirectory();
        var project = TestProjects.Create(10);
        var first = project.Tracks[0].Clips[0] with { Volume = 0.5f, Pan = 1 };
        var second = first with { Id = Guid.NewGuid(), Volume = 1, Pan = 0 };
        project = project with
        {
            Tracks = [project.Tracks[0] with { Clips = [first, second], Pan = -0.5f, Effects = [new EffectSpec(EffectKind.Gain, 0.5f)] }]
        };
        var buffer = TestProjects.Buffer();
        var output = new float[2];
        new RenderPlan(project, _ => buffer).Render(0, output);
        Assert.Equal(0.125f, output[0], 6);
        Assert.Equal(0.09375f, output[1], 6);
        var path = Path.Combine(directory.Path, "clipmix.wav");
        WaveExporter.Export(new RenderPlan(project, _ => buffer), path);
        var bytes = File.ReadAllBytes(path);
        Assert.InRange(BitConverter.ToInt16(bytes, 44) / 32768f, 0.1249f, 0.1251f);
        Assert.InRange(BitConverter.ToInt16(bytes, 46) / 32768f, 0.0936f, 0.0939f);
        var muted = ProjectEdits.UpdateClip(project, first.Id, c => c with { Muted = true });
        new RenderPlan(muted, _ => buffer).Render(0, output);
        Assert.Equal(0.125f, output[0], 6);
        Assert.Equal(0.0625f, output[1], 6);
        Assert.False(muted.Tracks[0].Muted);
        Assert.False(muted.Tracks[0].Clips[1].Muted);
        Assert.Equal(project.Duration, muted.Duration);
        var mono = project with { Tracks = [project.Tracks[0] with { Layout = TrackChannelLayout.Mono }] };
        new RenderPlan(mono, _ => buffer).Render(0, output);
        Assert.Equal(0.1875f, output[0], 6);
        Assert.Equal(output[0], output[1]);
    }

    [Fact]
    public void ClipVolumePrecedesItsEffectsAndClipBalancePrecedesTrackProcessing()
    {
        var project = TestProjects.Create(1);
        var clip = project.Tracks[0].Clips[0] with
        {
            Volume = 0.5f,
            Pan = 0.5f,
            Effects = [new EffectSpec(EffectKind.HardLimiter, 0.3f)]
        };
        project = project with { Tracks = [project.Tracks[0] with { Clips = [clip], Effects = [new EffectSpec(EffectKind.Gain, 2)] }] };
        var buffer = new AudioBuffer(new DecodedAudio(new PcmFormat(48000, 2), [0.8f, -0.2f]));
        var output = new float[2];
        new RenderPlan(project, _ => buffer).Render(0, output);
        Assert.Equal(0.3f, output[0], 6);
        Assert.Equal(-0.2f, output[1], 6);
        foreach (var invalid in new[] { float.NaN, float.PositiveInfinity, -0.05f, 2.05f })
            Assert.Throws<InvalidDataException>(() => ProjectEdits.UpdateClip(project, clip.Id, c => c with { Volume = invalid }).Validate());
        foreach (var invalid in new[] { float.NaN, float.NegativeInfinity, -1.05f, 1.05f })
            Assert.Throws<InvalidDataException>(() => ProjectEdits.UpdateClip(project, clip.Id, c => c with { Pan = invalid }).Validate());
    }

    [Fact]
    public void LockedClipsAnchorGroupedMovementAndPreventTimeAndTrimEdits()
    {
        var project = TestProjects.Create(100, 20);
        var first = project.Tracks[0].Clips[0] with { Locked = true, Muted = true, Volume = 0.5f, Pan = 0.25f };
        var second = first with { Id = Guid.NewGuid(), Locked = false, Start = 200 };
        project = project with { Tracks = [project.Tracks[0] with { Clips = [first, second] }, new AudioTrack()] };
        var ids = new HashSet<Guid> { first.Id, second.Id };
        Assert.Same(project, ProjectEdits.Move(project, first.Id, project.Tracks[1].Id, 50));
        Assert.Same(project, ProjectEdits.MoveClips(project, ids, 30, 1));
        Assert.Same(project, ProjectEdits.SetClipStart(project, first.Id, 50));
        Assert.Same(project, ProjectEdits.TrimStart(project, first.Id, 50));
        Assert.Same(project, ProjectEdits.TrimEnd(project, first.Id, 50));
        Assert.Equal(first, ClipClipboardItem.Capture(project, first.Id).Clip);
        var split = ProjectEdits.Split(project, first.Id, 50);
        Assert.All(split.Tracks[0].Clips.Take(2), c =>
        {
            Assert.True(c.Locked); Assert.True(c.Muted);
            Assert.Equal(0.5f, c.Volume); Assert.Equal(0.25f, c.Pan);
        });
        var unlocked = ProjectEdits.UpdateClip(project, first.Id, c => c with { Locked = false });
        var moved = ProjectEdits.MoveClips(unlocked, ids, 30, 1);
        Assert.Empty(moved.Tracks[0].Clips);
        Assert.Equal(50, moved.Tracks[1].Clips[0].Start);
        Assert.Equal(230, moved.Tracks[1].Clips[1].Start);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task LegacyProjectsDefaultToNeutralClipPropertiesAndUpgradeWithoutRewriting(int version)
    {
        using var directory = new TempDirectory();
        var source = TestProjects.WriteWave(directory.Path, 100);
        var project = TestProjects.Create(100) with { Assets = [new(TestProjects.AssetId, source, 100)] };
        var path = Path.Combine(directory.Path, "legacy.audionest");
        var store = new JsonProjectStore();
        await store.SaveAsync(project, path);
        var json = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        json["version"] = version;
        var clipJson = json["tracks"]![0]!["clips"]![0]!.AsObject();
        foreach (var key in new[] { "muted", "locked", "volume", "pan" }) clipJson.Remove(key);
        if (version == 1) json["tracks"]![0]!.AsObject().Remove("layout");
        var original = json.ToJsonString();
        await File.WriteAllTextAsync(path, original);
        var loaded = await store.LoadAsync(path);
        var clip = loaded.Tracks[0].Clips[0];
        Assert.Equal(AudioProject.CurrentVersion, loaded.Version);
        Assert.False(clip.Muted); Assert.False(clip.Locked);
        Assert.Equal(1, clip.Volume); Assert.Equal(0, clip.Pan);
        Assert.Equal(original, await File.ReadAllTextAsync(path));
        loaded = ProjectEdits.UpdateClip(loaded, clip.Id, c => c with { Muted = true, Locked = true, Volume = 0.65f, Pan = -0.35f });
        await store.SaveAsync(loaded, path);
        var restored = await store.LoadAsync(path);
        Assert.Equal(loaded.Tracks[0].Clips[0], restored.Tracks[0].Clips[0]);
    }

    [AvaloniaFact]
    public async Task GlobalControlsAreImmediateIndependentUndoableAndPlaybackSafe()
    {
        using var directory = new TempDirectory();
        using var backend = new FakeBackend { DecodedFrames = 48000 };
        using var vm = Create(backend, directory);
        await vm.ImportAsync(TestProjects.WriteWave(directory.Path));
        var id = vm.Project.Tracks[0].Clips[0].Id;
        vm.Select(vm.Project.Tracks[0].Id, id);
        var properties = vm.Inspector.Properties;
        properties.Name = "With ";
        properties.Name += "spaces";
        Assert.Equal("With spaces", vm.SelectedClip!.Name);
        properties.Name = "";
        Assert.True(properties.HasError);
        properties.Volume = 0.65; // Invalid name text must not prevent unrelated controls.
        Assert.Equal("With spaces", vm.SelectedClip.Name);
        Assert.Equal("", properties.Name);
        properties.Name = "With spaces";
        properties.StartText = "invalid";
        Assert.True(properties.HasError);
        properties.StartText = "-2";
        Assert.Equal(0, vm.SelectedClip.Start);
        properties.StartText = "0.125";
        Assert.False(properties.HasError);
        Assert.Equal(6000, vm.SelectedClip.Start);
        vm.Undo();
        Assert.Equal(0, vm.SelectedClip.Start);
        properties.Locked = true;
        Assert.False(properties.CanMove);
        properties.StartText = "0.25";
        Assert.Equal(0, vm.SelectedClip.Start);
        properties.Locked = false;
        properties.StartText = "0";
        properties.Volume = 1;
        vm.StartPlayback();
        var output = backend.Output!;
        var stops = output.Stops;
        var project = vm.Project;
        properties.BeginAdjustment(false);
        properties.Volume = 0.42;
        properties.Volume = 0;
        Assert.Same(project, vm.Project);
        Assert.All(output.Pull(16), sample => Assert.Equal(0, sample));
        properties.EndAdjustment();
        Assert.Equal(0, vm.SelectedClip.Volume);
        Assert.True(vm.CanPause);
        Assert.Equal(stops, output.Stops);
        vm.Undo();
        Assert.Equal(1, vm.SelectedClip.Volume);
        vm.StartPlayback();
        output = backend.Output!;
        stops = output.Stops;
        properties.Pan = 0.123;
        Assert.Equal(0.1f, vm.SelectedClip.Pan, 5);
        properties.Muted = true;
        Assert.All(output.Pull(16), sample => Assert.Equal(0, sample));
        properties.Muted = false;
        Assert.Contains(output.Pull(16), sample => sample != 0);
        Assert.True(vm.CanPause);
        Assert.Equal(stops, output.Stops);
        Assert.False(vm.SelectedTrack!.Muted);
        Assert.Equal(0, vm.SelectedTrack.Pan);
        Assert.Empty(vm.SelectedTrack.Effects);
        vm.Copy();
        vm.PasteAt(vm.SelectedTrack.Id, 60000);
        Assert.Equal(0.1f, vm.SelectedClip!.Pan, 5);
        Assert.Equal("With spaces", vm.SelectedClip.Name);
    }

    [AvaloniaFact]
    public async Task LockedSelectionCannotBeDraggedAndMonoDisablesClipPanning()
    {
        using var directory = new TempDirectory();
        using var backend = new FakeBackend { DecodedFrames = 96000 };
        using var vm = Create(backend, directory);
        await vm.ImportAsync(TestProjects.WriteWave(directory.Path));
        var first = vm.Project.Tracks[0].Clips[0].Id;
        vm.Select(vm.Project.Tracks[0].Id, first);
        vm.Inspector.Properties.Locked = true;
        vm.AddMonoTrack();
        await vm.ImportAsync(TestProjects.WriteWave(directory.Path));
        var second = vm.Project.Tracks[1].Clips[0].Id;
        vm.Select(vm.Project.Tracks[0].Id, first);
        vm.Select(vm.Project.Tracks[1].Id, second, additive: true);
        var window = new MainWindow(vm) { WindowState = WindowState.Normal };
        window.Show(); Layout();
        var timeline = window.FindControl<TimelineControl>("TimelineClips")!;
        var before = vm.Project;
        Drag(window, timeline, new Point(70, vm.TrackTop(1) + 65), new Vector(45, 0));
        Assert.Same(before, vm.Project);
        Assert.Equal(2, vm.SelectedClipIds.Count);
        vm.Inspector.Properties.Pan = 1;
        Assert.Equal(0, vm.SelectedClip!.Pan);
        Assert.False(vm.Inspector.Properties.CanPan);
        vm.Select(vm.Project.Tracks[0].Id, first);
        vm.Inspector.Properties.Locked = false;
        before = vm.Project;
        Drag(window, timeline, new Point(70, 65), new Vector(45, 0));
        Assert.NotSame(before, vm.Project);
        Assert.True(vm.SelectedClip!.Start > 0);
        vm.Undo();
        Assert.Equal(0, vm.SelectedClip.Start);
        Close(window, vm);
    }

    [AvaloniaFact]
    public async Task InspectorUsesUnboxedDividersAndLargerSeparatedMeters()
    {
        using var directory = new TempDirectory();
        using var backend = new FakeBackend { DecodedFrames = 48000 };
        using var vm = Create(backend, directory);
        await vm.ImportAsync(TestProjects.WriteWave(directory.Path));
        vm.Select(vm.Project.Tracks[0].Id, vm.Project.Tracks[0].Clips[0].Id);
        vm.AddClipEffect(EffectKind.HardLimiter);
        var window = new MainWindow(vm) { WindowState = WindowState.Normal, Height = 1000 };
        window.Show(); Layout();
        var inspector = window.GetVisualDescendants().OfType<ClipInspectorView>().Single();
        Assert.IsType<TextBox>(inspector.FindControl<Control>("ClipStartEditor"));
        Assert.Empty(inspector.GetVisualDescendants().OfType<NumericUpDown>());
        var buttons = inspector.GetVisualDescendants().OfType<Button>().Select(b => b.Content).OfType<string>().ToArray();
        foreach (var forbidden in new[] { "Apply properties", "Split at playhead", "Delete selected clips", "Add gain", "Add limiter", "Toggle track limiter", "← 0.1 s", "0.1 s →" })
            Assert.DoesNotContain(forbidden, buttons);
        var dividers = inspector.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("inspectorDivider")).ToArray();
        Assert.Equal(2, dividers.Length);
        Assert.All(dividers, b => { Assert.Equal(5, b.Bounds.Height); Assert.Equal(inspector.Bounds.Width, b.Bounds.Width); });
        var volume = inspector.FindControl<ClipMixSlider>("ClipVolumeSlider")!;
        var pan = inspector.FindControl<ClipMixSlider>("ClipPanSlider")!;
        Assert.Equal(0.05, volume.TickFrequency);
        Assert.Equal(0.05, pan.TickFrequency);
        Assert.All(new[] { volume, pan }, slider =>
        {
            var thumb = slider.GetVisualDescendants().OfType<Thumb>().Single();
            Assert.Equal(12, thumb.Bounds.Height);
            var y = thumb.TranslatePoint(default, slider)!.Value.Y;
            Assert.InRange(y, 0, slider.Bounds.Height - thumb.Bounds.Height);
        });
        Click(window, inspector.FindControl<CheckBox>("ClipMuteBox")!);
        Assert.True(vm.SelectedClip!.Muted);
        Click(window, inspector.FindControl<CheckBox>("ClipLockBox")!);
        Assert.True(vm.SelectedClip.Locked);
        Assert.False(inspector.FindControl<TextBox>("ClipStartEditor")!.IsEnabled);
        var toolbar = window.FindControl<Grid>("ToolbarGrid")!;
        Assert.Equal(300, toolbar.ColumnDefinitions[4].ActualWidth);
        var output = window.FindControl<AudioMeterControl>("OutputMeter")!;
        var input = window.FindControl<AudioMeterControl>("InputMeter")!;
        Assert.Equal(10, output.Bounds.Height);
        Assert.Equal(10, input.Bounds.Height);
        var outputTop = output.TranslatePoint(default, window)!.Value.Y;
        var inputTop = input.TranslatePoint(default, window)!.Value.Y;
        Assert.True(inputTop >= outputTop + output.Bounds.Height + 4);
        var artifactDirectory = Path.Combine(AppContext.BaseDirectory, "ui-artifacts");
        Directory.CreateDirectory(artifactDirectory);
        foreach (var dark in new[] { false, true })
        {
            vm.IsDarkMode = dark; Layout();
            using var frame = window.CaptureRenderedFrame();
            frame!.Save(Path.Combine(artifactDirectory, dark ? "inspector-global-dark.png" : "inspector-global-light.png"));
        }
        Close(window, vm);
    }

    [AvaloniaFact]
    public async Task ActualFieldsAndClipSliderApplyWithoutButtonsAndCancelOnSelectionChange()
    {
        using var directory = new TempDirectory();
        using var backend = new FakeBackend { DecodedFrames = 48000 };
        using var vm = Create(backend, directory);
        await vm.ImportAsync(TestProjects.WriteWave(directory.Path));
        var first = vm.Project.Tracks[0].Clips[0].Id;
        vm.SplitAt(first, 24000);
        var second = vm.Project.Tracks[0].Clips[1].Id;
        vm.Select(vm.Project.Tracks[0].Id, first);
        var window = new MainWindow(vm) { WindowState = WindowState.Normal };
        window.Show(); Layout();
        var inspector = window.GetVisualDescendants().OfType<ClipInspectorView>().Single();
        inspector.FindControl<TextBox>("ClipNameEditor")!.Text = "Typed clip";
        inspector.FindControl<TextBox>("ClipStartEditor")!.Text = "0.125";
        Layout();
        Assert.Equal("Typed clip", vm.SelectedClip!.Name);
        Assert.Equal(6000, vm.SelectedClip.Start);
        inspector.FindControl<TextBox>("ClipStartEditor")!.Text = "bad time";
        var project = vm.Project;
        Layout();
        Assert.Same(project, vm.Project);
        Assert.True(vm.Inspector.Properties.HasError);
        inspector.FindControl<TextBox>("ClipStartEditor")!.Text = "0";
        Layout(); // Removing the validation message changes the slider's position.
        vm.StartPlayback();
        var output = backend.Output!;
        var slider = inspector.FindControl<ClipMixSlider>("ClipVolumeSlider")!;
        var thumb = slider.GetVisualDescendants().OfType<Thumb>().Single();
        var point = thumb.TranslatePoint(new Point(thumb.Bounds.Width / 2, thumb.Bounds.Height / 2), window)!.Value;
        project = vm.Project;
        window.MouseDown(point, MouseButton.Left);
        window.MouseMove(point + new Vector(40, 0)); Layout();
        Assert.Same(project, vm.Project);
        window.MouseUp(point + new Vector(40, 0), MouseButton.Left); Layout();
        Assert.True(vm.SelectedClip.Volume > 1);
        Assert.True(vm.CanPause);
        Assert.Same(output, backend.Output);
        vm.Undo();
        Assert.Equal(1, vm.SelectedClip.Volume);
        Layout();
        vm.StartPlayback();
        output = backend.Output!;
        var panSlider = inspector.FindControl<ClipMixSlider>("ClipPanSlider")!;
        var panThumb = panSlider.GetVisualDescendants().OfType<Thumb>().Single();
        point = panThumb.TranslatePoint(new Point(panThumb.Bounds.Width / 2, panThumb.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseMove(point + new Vector(30, 0));
        window.MouseUp(point + new Vector(30, 0), MouseButton.Left); Layout();
        Assert.True(vm.SelectedClip.Pan > 0);
        Assert.Equal(0, vm.SelectedTrack!.Pan);
        Assert.True(vm.CanPause);
        Assert.Same(output, backend.Output);
        vm.Undo();
        Assert.Equal(0, vm.SelectedClip.Pan);
        vm.StartPlayback();
        output = backend.Output!;
        var properties = vm.Inspector.Properties;
        properties.BeginAdjustment(false); properties.Volume = 0;
        Assert.All(output.Pull(16), sample => Assert.Equal(0, sample));
        vm.Select(vm.Project.Tracks[0].Id, second);
        properties.EndAdjustment();
        Assert.Equal(1, properties.Volume);
        Assert.Equal(1, vm.Project.Tracks[0].Clips[0].Volume);
        Assert.Contains(output.Pull(16), sample => sample != 0);
        Assert.True(vm.CanPause);
        Close(window, vm);
    }
}
