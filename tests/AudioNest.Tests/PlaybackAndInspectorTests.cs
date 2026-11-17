using AudioNest.Application;
using AudioNest.Audio;
using AudioNest.Audio.Effects;
using AudioNest.Desktop;
using AudioNest.Domain;
using AudioNest.Hardware;
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

public sealed class PlaybackAndInspectorTests
{
    private static MainViewModel Create(FakeBackend backend, TempDirectory directory) =>
        new(new ProjectSession(), new AudioLibrary(backend), new AudioTransport(backend), backend,
            new JsonProjectStore(), Path.Combine(directory.Path, "workspace"));
    private static void Layout() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
    private static void Click(Window window, Control control, MouseButton button = MouseButton.Left)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, button); window.MouseUp(point, button); Layout();
    }
    private static void Space(Window window)
    {
        window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
        Layout();
    }
    private static void Close(Window window, MainViewModel vm) { vm.New(); window.Close(); Layout(); }

    [Fact]
    public void LivePlanHandoffKeepsDeviceAndClockAndDoesNotAllocateInCallback()
    {
        using var backend = new FakeBackend();
        using var transport = new AudioTransport(backend);
        var project = TestProjects.Create(10000);
        var buffer = TestProjects.Buffer(10000);
        transport.SetPlan(new RenderPlan(project, _ => buffer));
        transport.Play();
        var output = backend.Output!;
        Assert.Equal(0.25f, output.Pull(32)[0]);
        var stops = output.Stops;
        var muted = project with { Tracks = [project.Tracks[0] with { Muted = true }] };
        transport.UpdatePlan(new RenderPlan(muted, _ => buffer));
        Assert.True(transport.IsPlaying);
        Assert.Equal(32, transport.Position);
        Assert.All(output.Pull(32), sample => Assert.Equal(0, sample));
        transport.UpdatePlan(new RenderPlan(project, _ => buffer));
        var samples = new float[32];
        output.Pull(samples);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 20; i++) output.Pull(samples);
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
        Assert.Equal(400, transport.Position);
        Assert.All(samples, sample => Assert.Equal(0.25f, sample));
        Assert.Same(output, backend.Output);
        Assert.Equal(1, backend.OutputOpens);
        Assert.Equal(stops, output.Stops);
        var otherRate = project with { SampleRate = 44100 };
        Assert.Throws<ArgumentException>(() => transport.UpdatePlan(
            new RenderPlan(otherRate, _ => TestProjects.Buffer(10000, sampleRate: 44100))));
        Assert.True(transport.IsPlaying);
        transport.UpdatePlan(new RenderPlan(new AudioProject(), _ => buffer));
        Assert.All(output.Pull(16), sample => Assert.Equal(0, sample));
        Assert.False(transport.IsPlaying);
        Assert.Equal(400, transport.Position); // Deleting the last track must not rewind.
    }

    [Fact]
    public async Task InFlightCallbackFinishesItsOldGraphBeforeReadingTheNewGraph()
    {
        using var backend = new FakeBackend();
        using var transport = new AudioTransport(backend);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var project = TestProjects.Create(1000);
        project = project with { Tracks = [project.Tracks[0] with { Effects = [new EffectSpec(EffectKind.Gain, 1)] }] };
        var buffer = TestProjects.Buffer(1000);
        transport.SetPlan(new RenderPlan(project, _ => buffer, new GatedEffects(entered, release)));
        transport.Play();
        var callback = Task.Run(() => backend.Output!.Pull(16));
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            var muted = project with { Tracks = [project.Tracks[0] with { Muted = true }] };
            transport.UpdatePlan(new RenderPlan(muted, _ => buffer));
        }
        finally { release.Set(); }
        Assert.All(await callback, sample => Assert.Equal(0.25f, sample));
        Assert.Equal(16, transport.Position);
        Assert.All(backend.Output!.Pull(16), sample => Assert.Equal(0, sample));
        Assert.Equal(32, transport.Position);
    }

    private sealed class GatedEffects(ManualResetEventSlim entered, ManualResetEventSlim release) : IAudioEffectFactory
    {
        public IAudioProcessor Create(EffectSpec description, PcmFormat format) => new Gate(entered, release);
        private sealed class Gate(ManualResetEventSlim entered, ManualResetEventSlim release) : IAudioProcessor
        {
            public void Process(Span<float> samples)
            {
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Test callback was not released.");
            }
            public void Reset() { }
        }
    }

    [AvaloniaFact]
    public async Task TrackControlsPreviewAndCommitWithoutStoppingOrReopeningOutput()
    {
        using var directory = new TempDirectory();
        using var backend = new FakeBackend { DecodedFrames = 48000 };
        using var vm = Create(backend, directory);
        await vm.ImportAsync(TestProjects.WriteWave(directory.Path));
        vm.AddTrack();
        await vm.ImportAsync(TestProjects.WriteWave(directory.Path));
        vm.StartPlayback();
        var output = backend.Output!;
        output.Pull(32);
        var stops = output.Stops;
        var opens = backend.OutputOpens;
        void Continues()
        {
            var position = vm.Position;
            Assert.True(vm.CanPause);
            var expected = new float[32];
            new RenderPlan(vm.Project, vm.Buffer).Render(position, expected);
            Assert.Equal(expected, output.Pull(16));
            Assert.Equal(position + 16, vm.Position);
            Assert.Same(output, backend.Output);
            Assert.Equal(stops, output.Stops);
            Assert.Equal(opens, backend.OutputOpens);
        }
        var track = vm.TrackHeaders[0];
        track.Mute.Execute(null); Continues();
        track.Mute.Execute(null); Continues();
        track.Solo.Execute(null); Continues();
        track.Solo.Execute(null); Continues();
        track.Volume = 0.5; Continues();
        track.Pan = 0.5; Continues();
        track.Quieter.Execute(null); Continues();
        vm.ToggleLimiter(true); Continues();
        vm.RenameTrack(track.Id, "Playing"); Continues();
        track.Solo.Execute(null); Continues();
        var original = vm.Project;
        var previousVolume = track.Volume;
        track.BeginVolumeAdjustment();
        track.Volume = 0;
        Assert.Same(original, vm.Project);
        Assert.All(output.Pull(16), sample => Assert.Equal(0, sample));
        Assert.True(vm.CanPause);
        track.EndAdjustment();
        Continues();
        vm.Undo();
        Assert.Equal(previousVolume, vm.TrackHeaders[0].Volume, 5);
        vm.StartPlayback();
        output = backend.Output!;
        stops = output.Stops;
        opens = backend.OutputOpens;
        track.BeginPanAdjustment(); track.Pan = -1; track.EndAdjustment(); Continues();
        track.Delete.Execute(null); Continues();
        Assert.Single(vm.Project.Tracks);
    }

    [AvaloniaFact]
    public async Task SpaceDownAndUpOnlyControlTransportWhenButtonsHaveFocus()
    {
        using var directory = new TempDirectory();
        using var backend = new FakeBackend { DecodedFrames = 48000 };
        using var vm = Create(backend, directory);
        await vm.ImportAsync(TestProjects.WriteWave(directory.Path));
        var clip = vm.Project.Tracks[0].Clips[0];
        vm.Select(vm.Project.Tracks[0].Id, clip.Id);
        var window = new MainWindow(vm) { WindowState = WindowState.Normal };
        window.Show(); Layout();
        var header = window.GetVisualDescendants().OfType<TrackHeaderView>().Single();
        var mute = header.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Mute"));
        var solo = header.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Solo"));
        Click(window, mute);
        Assert.True(vm.Project.Tracks[0].Muted);
        vm.Select(vm.Project.Tracks[0].Id, clip.Id);
        var inspector = window.GetVisualDescendants().OfType<ClipInspectorView>().Single();
        var buttons = new[]
        {
            mute, solo, window.FindControl<Button>("SnapButton")!, window.FindControl<Button>("FitButton")!,
            window.FindControl<Button>("RazorToolButton")!, window.FindControl<Button>("StopButton")!,
            window.FindControl<Button>("RecordButton")!, inspector.FindControl<Button>("ApplyClipPropertiesButton")!
        };
        foreach (var button in buttons)
        {
            Assert.True(button.Focus());
            var project = vm.Project;
            var clicks = 0;
            button.Click += (_, _) => clicks++;
            window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
            window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None); // Key repeat must not pause again.
            window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
            Layout();
            Assert.True(vm.CanPause);
            Assert.Same(project, vm.Project);
            Assert.Equal(0, clicks);
            Assert.True(vm.IsSnappingEnabled);
            Assert.True(vm.IsSelectionTool);
            Assert.False(vm.IsRecording);
            Space(window);
            Assert.False(vm.CanPause);
        }
        var name = inspector.FindControl<TextBox>("ClipNameEditor")!;
        Assert.True(name.Focus());
        name.Text = "Two words";
        name.CaretIndex = 3;
        window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
        window.KeyTextInput(" ");
        window.KeyReleaseQwerty(PhysicalKey.Space, RawInputModifiers.None);
        Assert.Equal("Two  words", name.Text);
        Assert.False(vm.CanPause);
        Close(window, vm);
    }

    [AvaloniaFact]
    public async Task HeaderDeleteMenuTargetsClickedTrackAndTooltipIsPlain()
    {
        using var directory = new TempDirectory();
        using var backend = new FakeBackend();
        using var vm = Create(backend, directory);
        await vm.ImportAsync(TestProjects.WriteWave(directory.Path));
        vm.AddTrack();
        var removed = vm.Project.Tracks[0].Id;
        var window = new MainWindow(vm) { WindowState = WindowState.Normal };
        window.Show(); Layout();
        var header = window.GetVisualDescendants().OfType<TrackHeaderView>().First();
        var delete = header.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "×"));
        Assert.Equal("Delete track", ToolTip.GetTip(delete));
        Click(window, header, MouseButton.Right);
        var panel = window.FindControl<Border>("TrackHeadersPanel")!;
        panel.ContextMenu!.Items.OfType<MenuItem>().Single(i => Equals(i.Header, "Delete track")).Command!.Execute(null);
        panel.ContextMenu.Close(); Layout();
        Assert.DoesNotContain(vm.Project.Tracks, t => t.Id == removed);
        vm.Undo();
        Assert.Equal(removed, vm.Project.Tracks[0].Id);
        Close(window, vm);
    }

    [AvaloniaFact]
    public async Task InspectorEditsIndividualEffectsAndPropertiesWithHistoryAndPersistence()
    {
        using var directory = new TempDirectory();
        using var backend = new FakeBackend { DecodedFrames = 48000 };
        using var vm = Create(backend, directory);
        await vm.ImportAsync(TestProjects.WriteWave(directory.Path));
        var clip = vm.Project.Tracks[0].Clips[0];
        vm.Select(vm.Project.Tracks[0].Id, clip.Id);
        vm.Inspector.AddGain.Execute(null);
        vm.Inspector.AddLimiter.Execute(null);
        vm.Inspector.AddGain.Execute(null); // Repeated kinds remain independent instances.
        var editors = vm.Inspector.Effects.ToArray();
        var gain = Assert.IsType<GainEffectEditorViewModel>(editors[0]);
        var limiter = Assert.IsType<LimiterEffectEditorViewModel>(editors[1]);
        var other = Assert.IsType<GainEffectEditorViewModel>(editors[2]);
        gain.Value = 0.5;
        limiter.Value = 0.75;
        other.Value = 1.5;
        Assert.Equal(editors, vm.Inspector.Effects);
        vm.Seek(100); vm.Tick();
        Assert.Same(gain, vm.Inspector.Effects[0]);
        limiter.Enabled = false;
        Assert.True(vm.SelectedClip!.Effects[1].Bypassed);
        gain.Remove.Execute(null);
        Assert.Equal(new[] { EffectKind.HardLimiter, EffectKind.Gain }, vm.SelectedClip.Effects.Select(e => e.Kind));
        Assert.False(gain.Remove.CanExecute(null));
        gain.Value = 2; // Detached editors cannot edit a different effect at the old index.
        Assert.Equal(0.75f, vm.SelectedClip.Effects[0].Value);
        vm.Undo();
        Assert.Equal(3, vm.SelectedClip.Effects.Length);
        Assert.Equal(0.5f, vm.SelectedClip.Effects[0].Value);
        var properties = vm.Inspector.Properties;
        properties.Name = "";
        Assert.False(properties.Apply.CanExecute(null));
        properties.Name = "Edited clip";
        properties.StartSeconds = -1;
        Assert.False(properties.Apply.CanExecute(null));
        properties.StartSeconds = 1.25m;
        vm.Tick(); vm.Seek(0); // Presentation notifications must not erase drafts.
        Assert.Equal("Edited clip", properties.Name);
        properties.Apply.Execute(null);
        Assert.Equal("Edited clip", vm.SelectedClip.Name);
        Assert.Equal(60000, vm.SelectedClip.Start);
        Assert.Equal(clip.Length, vm.SelectedClip.Length);
        Assert.Equal(clip.SourceOffset, vm.SelectedClip.SourceOffset);
        vm.Undo();
        Assert.Equal(clip.Name, vm.SelectedClip.Name);
        Assert.Equal(0, vm.SelectedClip.Start);
        vm.Redo();
        var path = Path.Combine(directory.Path, "inspector.audionest");
        await vm.SaveAsync(path); await vm.OpenAsync(path);
        var restored = vm.Project.Tracks[0].Clips[0];
        Assert.Equal("Edited clip", restored.Name);
        Assert.Equal(60000, restored.Start);
        Assert.True(restored.Effects[1].Bypassed);
        vm.Select(vm.Project.Tracks[0].Id, restored.Id);
        var window = new MainWindow(vm) { WindowState = WindowState.Normal, Height = 1100 };
        window.Show(); Layout();
        Assert.Equal(3, window.GetVisualDescendants().OfType<EffectParameterSlider>().Count());
        Assert.Equal(2, window.GetVisualDescendants().OfType<EffectParameterSlider>().Count(s => s.DataContext is GainEffectEditorViewModel));
        var artifactDirectory = Path.Combine(AppContext.BaseDirectory, "ui-artifacts");
        Directory.CreateDirectory(artifactDirectory);
        using (var frame = window.CaptureRenderedFrame()) frame!.Save(Path.Combine(artifactDirectory, "inspector-sections.png"));
        Close(window, vm);
    }

    [AvaloniaFact]
    public async Task InspectorSliderPreviewIsLiveAndCommitsOneUndoStep()
    {
        using var directory = new TempDirectory();
        using var backend = new FakeBackend { DecodedFrames = 48000 };
        using var vm = Create(backend, directory);
        await vm.ImportAsync(TestProjects.WriteWave(directory.Path));
        vm.Select(vm.Project.Tracks[0].Id, vm.Project.Tracks[0].Clips[0].Id);
        vm.Inspector.AddGain.Execute(null);
        var gain = Assert.IsType<GainEffectEditorViewModel>(vm.Inspector.Effects[0]);
        vm.StartPlayback();
        var output = backend.Output!;
        var project = vm.Project;
        gain.BeginAdjustment(); gain.Value = 0.5; gain.Value = 0;
        Assert.Same(project, vm.Project);
        Assert.All(output.Pull(16), sample => Assert.Equal(0, sample));
        Assert.True(vm.CanPause);
        gain.EndAdjustment();
        Assert.Same(output, backend.Output);
        Assert.Equal(0, vm.SelectedClip!.Effects[0].Value);
        vm.Undo();
        Assert.Equal(1, vm.SelectedClip.Effects[0].Value);
        vm.Undo();
        Assert.Empty(vm.SelectedClip.Effects);
    }

    [AvaloniaFact]
    public async Task DetachedPreviewRestoresProjectAudioWithoutEditingAnotherSelection()
    {
        using var directory = new TempDirectory();
        using var backend = new FakeBackend { DecodedFrames = 48000 };
        using var vm = Create(backend, directory);
        await vm.ImportAsync(TestProjects.WriteWave(directory.Path));
        vm.Select(vm.Project.Tracks[0].Id, vm.Project.Tracks[0].Clips[0].Id);
        vm.Inspector.AddGain.Execute(null);
        var gain = Assert.IsType<GainEffectEditorViewModel>(vm.Inspector.Effects[0]);
        vm.StartPlayback();
        gain.BeginAdjustment(); gain.Value = 0;
        Assert.All(backend.Output!.Pull(16), sample => Assert.Equal(0, sample));
        vm.Select(vm.Project.Tracks[0].Id, null);
        gain.EndAdjustment();
        Assert.Empty(vm.Inspector.Effects);
        Assert.Equal(1, vm.Project.Tracks[0].Clips[0].Effects[0].Value);
        Assert.Contains(backend.Output.Pull(16), sample => sample != 0);
        Assert.True(vm.CanPause);
    }

    [AvaloniaFact]
    public async Task ActualEffectSliderDragUsesTypedEditorAndOneUndoStep()
    {
        using var directory = new TempDirectory();
        using var backend = new FakeBackend { DecodedFrames = 48000 };
        using var vm = Create(backend, directory);
        await vm.ImportAsync(TestProjects.WriteWave(directory.Path));
        vm.Select(vm.Project.Tracks[0].Id, vm.Project.Tracks[0].Clips[0].Id);
        vm.Inspector.AddGain.Execute(null);
        var window = new MainWindow(vm) { WindowState = WindowState.Normal, Height = 1000 };
        window.Show(); Layout();
        vm.StartPlayback();
        var output = backend.Output!;
        var project = vm.Project;
        var slider = window.GetVisualDescendants().OfType<EffectParameterSlider>().Single();
        var thumb = slider.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.Thumb>().Single();
        var point = thumb.TranslatePoint(new Point(thumb.Bounds.Width / 2, thumb.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseMove(point + new Vector(40, 0)); Layout();
        window.MouseMove(point + new Vector(70, 0)); Layout();
        Assert.Same(project, vm.Project);
        window.MouseUp(point + new Vector(70, 0), MouseButton.Left); Layout();
        Assert.True(vm.SelectedClip!.Effects[0].Value > 1);
        Assert.True(vm.CanPause);
        Assert.Same(output, backend.Output);
        vm.Undo();
        Assert.Equal(1, vm.SelectedClip.Effects[0].Value);
        vm.Undo();
        Assert.Empty(vm.SelectedClip.Effects);
        Close(window, vm);
    }
}
