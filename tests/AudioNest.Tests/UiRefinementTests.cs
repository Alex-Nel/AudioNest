using AudioNest.Application;
using AudioNest.Audio;
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

public sealed class UiRefinementTests
{
    private static void Layout()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }
    private static MainViewModel Create(FakeBackend backend, TempDirectory directory) =>
        new(new ProjectSession(), new AudioLibrary(backend), new AudioTransport(backend), backend,
            new JsonProjectStore(), Path.Combine(directory.Path, "workspace"));
    private static Point InWindow(Control control, Window window, Point local) => control.TranslatePoint(local, window)!.Value;
    private static void Click(Window window, Control control)
    {
        var point = InWindow(control, window, new Point(control.Bounds.Width / 2, control.Bounds.Height / 2));
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Layout();
    }
    private static void CloseClean(Window window, MainViewModel vm)
    {
        vm.New();
        window.Close();
        Layout();
    }

    [Fact]
    public void PeakHandoffMeasuresActualSamplesAccumulatesAndClearsWithoutAllocation()
    {
        var meter = new AudioPeakMeter();
        meter.Measure([0.1f, -0.6f, 0.4f]);
        meter.Measure([0.2f, float.NaN, float.PositiveInfinity]);
        Assert.Equal(0.6f, meter.ReadAndReset());
        Assert.Equal(0, meter.ReadAndReset());
        var samples = new[] { 0.5f, -1f };
        meter.Measure(samples);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10; i++) meter.Measure(samples);
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
        Assert.Equal(1, meter.ReadAndReset());
        meter.Measure(samples);
        meter.Clear();
        Assert.Equal(0, meter.ReadAndReset());
    }

    [AvaloniaFact]
    public void StartupIsMaximizedAndTransportIsIconOnlyInOneCompactToolbar()
    {
        using var directory = new TempDirectory();
        using var backend = new FakeBackend();
        using var vm = Create(backend, directory);
        var window = new MainWindow(vm);
        Assert.Equal(WindowState.Maximized, window.WindowState);
        window.WindowState = WindowState.Normal;
        window.Show();
        Layout();
        var menu = window.FindControl<Menu>("EditorMenuBar")!;
        Assert.InRange(menu.Bounds.Height, 20, 25);
        foreach (var name in new[] { "PlayButton", "PauseButton", "StopButton", "SkipBackButton", "SkipForwardButton", "RecordButton" })
        {
            var button = window.FindControl<Button>(name)!;
            Assert.IsType<PathIcon>(button.Content);
            Assert.Equal(button.Bounds.Height, button.Bounds.Width);
            Assert.True(button.Bounds.Height >= 40);
        }
        Assert.NotNull(window.FindControl<Button>("ZoomInButton")!.Content);
        Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "TIMELINE");
        Assert.Null(backend.Recording);
        Assert.Equal(0, vm.InputLevel);
        window.Close();
        Layout();
    }

    [AvaloniaFact]
    public async Task SliderPreviewCommitsOneEditAndVolumeUsesZeroToTwoMultiplier()
    {
        using var directory = new TempDirectory();
        using var backend = new FakeBackend();
        using var vm = Create(backend, directory);
        await vm.ImportAsync(TestProjects.WriteWave(directory.Path));
        var track = vm.TrackHeaders.Single();
        Assert.Equal(1, track.Volume);
        track.BeginVolumeAdjustment();
        track.Volume = 0;
        track.Volume = 1.4;
        track.Volume = 2;
        Assert.Empty(vm.Project.Tracks[0].Effects);
        track.EndAdjustment();
        Assert.Equal(2, vm.Project.Tracks[0].Effects.Single().Value);
        vm.Undo();
        Assert.Empty(vm.Project.Tracks[0].Effects);
        vm.Redo();
        Assert.Equal(2, track.Volume);
        track.Volume = 0;
        Assert.Equal(0, vm.Project.Tracks[0].Effects.Single().Value);
        track.Pan = -1;
        Assert.Equal(-1, vm.Project.Tracks[0].Pan);
        track.Pan = 1;
        Assert.Equal(1, vm.Project.Tracks[0].Pan);
        var window = new MainWindow(vm) { WindowState = WindowState.Normal };
        window.Show();
        Layout();
        var header = window.GetVisualDescendants().OfType<TrackHeaderView>().Single();
        var slider = header.FindControl<Slider>("VolumeSlider")!;
        Assert.Equal(0, slider.Minimum);
        Assert.Equal(2, slider.Maximum);
        var point = InWindow(slider, window, new Point(slider.Bounds.Width * 0.7, slider.Bounds.Height / 2));
        window.MouseDown(point, MouseButton.Left);
        window.MouseMove(point + new Vector(-20, 0));
        window.MouseMove(point + new Vector(-10, 0));
        window.MouseUp(point + new Vector(-10, 0), MouseButton.Left);
        Layout();
        Assert.True(vm.Project.Tracks[0].Effects.Single().Value > 0);
        vm.Undo();
        Assert.Equal(0, vm.Project.Tracks[0].Effects.Single().Value);
        IPointer? captured = null;
        slider.AddHandler(InputElement.PointerPressedEvent, (_, e) => captured = e.Pointer,
            Avalonia.Interactivity.RoutingStrategies.Tunnel, true);
        window.MouseDown(point, MouseButton.Left);
        slider.Value = 1.25;
        Assert.NotNull(captured);
        captured.Capture(null);
        Layout();
        Assert.Equal(1.25f, vm.Project.Tracks[0].Effects.Single().Value);
        window.MouseUp(point, MouseButton.Left);
        Layout();
        vm.Undo();
        Assert.Equal(0, vm.Project.Tracks[0].Effects.Single().Value);
        CloseClean(window, vm);
    }

    [AvaloniaFact]
    public async Task ZoomButtonsAndLeftAltWheelKeepPlayheadScreenPosition()
    {
        using var directory = new TempDirectory();
        using var backend = new FakeBackend { DecodedFrames = 48000 * 20 };
        using var vm = Create(backend, directory);
        await vm.ImportAsync(TestProjects.WriteWave(directory.Path));
        var window = new MainWindow(vm) { WindowState = WindowState.Normal };
        window.Show();
        Layout();
        vm.Seek(48000 * 8);
        var scroll = window.FindControl<ScrollViewer>("TimelineScroll")!;
        scroll.Offset = new Vector(400, 0);
        Layout();
        double ScreenX() => (double)vm.Position / vm.Project.SampleRate * vm.PixelsPerSecond - scroll.Offset.X;
        var before = ScreenX();
        Click(window, window.FindControl<Button>("ZoomInButton")!);
        Assert.InRange(Math.Abs(ScreenX() - before), 0, 1);
        Click(window, window.FindControl<Button>("ZoomOutButton")!);
        Assert.InRange(Math.Abs(ScreenX() - before), 0, 1);
        var canvas = window.FindControl<TimelineControl>("TimelineClips")!;
        var mouse = InWindow(canvas, window, new Point(scroll.Offset.X + 50, 50));
        window.KeyPress(Key.LeftAlt, RawInputModifiers.Alt, PhysicalKey.AltLeft, null);
        var scale = vm.PixelsPerSecond;
        window.MouseWheel(mouse, new Vector(0, 1), RawInputModifiers.Alt);
        window.KeyRelease(Key.LeftAlt, RawInputModifiers.None, PhysicalKey.AltLeft, null);
        Layout();
        Assert.True(vm.PixelsPerSecond > scale);
        Assert.InRange(Math.Abs(ScreenX() - before), 0, 1);

        scale = vm.PixelsPerSecond;
        window.KeyPress(Key.RightAlt, RawInputModifiers.Alt, PhysicalKey.AltRight, null);
        window.MouseWheel(mouse, new Vector(0, -1), RawInputModifiers.Alt);
        window.KeyRelease(Key.RightAlt, RawInputModifiers.None, PhysicalKey.AltRight, null);
        Assert.Equal(scale, vm.PixelsPerSecond);
        var oldOffset = scroll.Offset;
        window.MouseWheel(mouse, new Vector(0, -1));
        Layout();
        Assert.Equal(scale, vm.PixelsPerSecond);
        Assert.NotEqual(oldOffset, scroll.Offset);
        for (var i = 0; i < 5; i++) vm.AddTrack();
        Layout();
        var tracks = window.FindControl<ScrollViewer>("TracksVerticalScroll")!;
        var oldVertical = tracks.Offset.Y;
        window.MouseWheel(mouse, new Vector(0, -1));
        Layout();
        Assert.True(tracks.Offset.Y > oldVertical);
        CloseClean(window, vm);
    }

    [AvaloniaFact]
    public async Task LeftShiftRulerSnapsAcrossTracksAndSelectionStatusNamesClipAndTrack()
    {
        using var directory = new TempDirectory();
        using var backend = new FakeBackend { DecodedFrames = 96000 };
        using var vm = Create(backend, directory);
        await vm.ImportAsync(TestProjects.WriteWave(directory.Path));
        vm.AddTrack();
        vm.Select(vm.Project.Tracks[1].Id, null);
        vm.Seek(144000);
        await vm.ImportAsync(TestProjects.WriteWave(directory.Path));
        var clip = vm.Project.Tracks[1].Clips[0];
        vm.Select(vm.Project.Tracks[1].Id, clip.Id);
        Assert.Contains(vm.Project.Tracks[1].Name, vm.StatusBarText);
        Assert.Contains(clip.Name, vm.StatusBarText);
        var window = new MainWindow(vm) { WindowState = WindowState.Normal };
        window.Show();
        Layout();
        var ruler = window.FindControl<TimelineRulerControl>("TimelineRuler")!;
        var point = InWindow(ruler, window, new Point(5 * vm.PixelsPerSecond - 4, 12));
        window.KeyPress(Key.LeftShift, RawInputModifiers.Shift, PhysicalKey.ShiftLeft, null);
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.Shift);
        window.MouseUp(point, MouseButton.Left, RawInputModifiers.Shift);
        window.KeyRelease(Key.LeftShift, RawInputModifiers.None, PhysicalKey.ShiftLeft, null);
        Assert.Equal(240000, vm.Position);
        window.KeyPress(Key.RightShift, RawInputModifiers.Shift, PhysicalKey.ShiftRight, null);
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.Shift);
        window.MouseUp(point, MouseButton.Left, RawInputModifiers.Shift);
        window.KeyRelease(Key.RightShift, RawInputModifiers.None, PhysicalKey.ShiftRight, null);
        Assert.NotEqual(240000, vm.Position);
        Assert.True(vm.Inspector.HasClip);
        vm.ApplyGain(-3, false);
        Assert.Single(vm.Inspector.Effects);
        Assert.Equal("Gain", vm.Inspector.Effects[0].Name);
        CloseClean(window, vm);
    }

    [AvaloniaFact]
    public async Task RealCallbackSamplesDriveMetersAndInputIsOnlyLiveWhenRecording()
    {
        using var directory = new TempDirectory();
        using var backend = new FakeBackend { DecodedFrames = 48000 };
        using var vm = Create(backend, directory);
        await vm.ImportAsync(TestProjects.WriteWave(directory.Path));
        vm.Tick();
        Assert.Equal(0, vm.OutputLevel);
        Assert.Equal(0, vm.InputLevel);
        Assert.Null(backend.Recording);
        vm.StartPlayback();
        backend.Output!.Pull(1024);
        vm.Tick();
        Assert.True(vm.OutputLevel > 0);
        Assert.Equal(0, vm.InputLevel);
        vm.Pause();
        Assert.Equal(0, vm.OutputLevel);
        vm.Record();
        backend.Recording!.InputMeter.Measure([0.1f, -0.75f]);
        vm.Tick();
        Assert.True(vm.InputLevel > 0);
        Assert.Contains("Microphone:", vm.InputMeterText);
        await vm.StopAsync();
        Assert.Equal(0, vm.InputLevel);
        Assert.Contains("inactive", vm.InputMeterText);
        Assert.Equal(1, backend.RecordingStops);
    }

    [AvaloniaFact]
    public void InspectorDividerResizesTimelineAndRemembersWidthAfterCompactWindow()
    {
        using var directory = new TempDirectory();
        using var backend = new FakeBackend();
        using var vm = Create(backend, directory);
        var window = new MainWindow(vm) { WindowState = WindowState.Normal };
        window.Show();
        Layout();
        var inspector = window.FindControl<Border>("Inspector")!;
        var splitter = window.FindControl<GridSplitter>("InspectorSplitter")!;
        var scroll = window.FindControl<ScrollViewer>("TimelineScroll")!;
        var oldWidth = inspector.Bounds.Width;
        var oldViewport = scroll.Viewport.Width;
        var point = InWindow(splitter, window, new Point(splitter.Bounds.Width / 2, 120));
        window.MouseDown(point, MouseButton.Left);
        window.MouseMove(point + new Vector(-70, 0));
        window.MouseUp(point + new Vector(-70, 0), MouseButton.Left);
        Layout();
        Assert.InRange(inspector.Bounds.Width - oldWidth, 68, 72);
        Assert.True(scroll.Viewport.Width < oldViewport);
        Assert.NotNull(window.GetVisualDescendants().OfType<ClipInspectorView>().SingleOrDefault());
        var width = inspector.Bounds.Width;
        window.Width = 820;
        Layout();
        Assert.False(inspector.IsVisible);
        window.Width = 1280;
        Layout();
        Assert.True(inspector.IsVisible);
        Assert.InRange(Math.Abs(width - inspector.Bounds.Width), 0, 1);
        window.Close();
        Layout();
    }
}
