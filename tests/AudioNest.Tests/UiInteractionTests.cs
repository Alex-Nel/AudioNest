using AudioNest.Application;
using AudioNest.Audio;
using AudioNest.Desktop;
using AudioNest.Persistence;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace AudioNest.Tests;

public sealed class UiInteractionTests
{
    private static void Layout()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }
    private static Point InWindow(Control control, Window window, Point local) =>
        control.TranslatePoint(local, window) ?? throw new InvalidOperationException("Control is not in the test window.");
    private static void Click(Window window, Control control, Point local)
    {
        var point = InWindow(control, window, local);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Layout();
    }
    private static MainViewModel Create(FakeBackend backend, TempDirectory directory) =>
        new(new ProjectSession(), new AudioLibrary(backend), new AudioTransport(backend), backend,
            new JsonProjectStore(), Path.Combine(directory.Path, "workspace"));

    [AvaloniaFact]
    public async Task OnlyRulerSeeksAndDraggingClipsPreservesCursor()
    {
        using var directory = new TempDirectory();
        using var backend = new FakeBackend { DecodedFrames = 96000 };
        using var vm = Create(backend, directory);
        var source = TestProjects.WriteWave(directory.Path);
        await vm.ImportAsync(source);
        var window = new MainWindow(vm) { WindowState = WindowState.Normal };
        window.Show();
        Layout();
        var clips = window.FindControl<TimelineControl>("TimelineClips")!;
        var ruler = window.FindControl<TimelineRulerControl>("TimelineRuler")!;
        vm.Seek(24000);
        Click(window, clips, new Point(100, 55));
        Assert.Equal(24000, vm.Position);
        Assert.NotNull(vm.SelectedClip);
        Click(window, ruler, new Point(90, 12));
        Assert.Equal(48000, vm.Position);

        // Pointer capture continues scrubbing even if the pointer leaves the ruler.
        window.MouseDown(InWindow(ruler, window, new Point(45, 12)), MouseButton.Left);
        window.MouseMove(InWindow(ruler, window, new Point(135, 60)));
        window.MouseUp(InWindow(ruler, window, new Point(135, 60)), MouseButton.Left);
        Assert.Equal(72000, vm.Position);

        vm.IsSnappingEnabled = false;
        var original = vm.Project.Tracks[0].Clips[0];
        window.MouseDown(InWindow(clips, window, new Point(50, 55)), MouseButton.Left);
        window.MouseMove(InWindow(clips, window, new Point(95, 55)));
        window.MouseUp(InWindow(clips, window, new Point(95, 55)), MouseButton.Left);
        Assert.Equal(24000, vm.Project.Tracks[0].Clips[0].Start);
        Assert.Equal(72000, vm.Position);
        vm.Undo();
        Assert.Equal(original.Start, vm.Project.Tracks[0].Clips[0].Start);
        vm.New();
        window.Close();
        Layout();
    }

    [AvaloniaFact]
    public async Task TrackButtonsWorkAndDeletionCanBeUndoneAndSaved()
    {
        using var directory = new TempDirectory();
        using var backend = new FakeBackend();
        using var vm = Create(backend, directory);
        await vm.ImportAsync(TestProjects.WriteWave(directory.Path));
        var window = new MainWindow(vm) { WindowState = WindowState.Normal };
        window.Show();
        Layout();
        var header = window.GetVisualDescendants().OfType<TrackHeaderView>().Single();
        var track = vm.TrackHeaders.Single();
        var pan = header.FindControl<Slider>("PanSlider")!;
        pan.Value = 0.25;
        Assert.Equal(0.25f, vm.Project.Tracks[0].Pan);
        track.Quieter.Execute(null);
        Assert.Contains("-3.0", track.GainText);
        track.Mute.Execute(null);
        Assert.True(vm.Project.Tracks[0].Muted);
        track.Mute.Execute(null);
        var id = track.Id;
        track.Delete.Execute(null);
        Assert.Empty(vm.Project.Tracks);
        Assert.Empty(vm.TrackHeaders);
        vm.Undo();
        Assert.Equal(id, vm.Project.Tracks[0].Id);
        Assert.Equal(0.25f, vm.Project.Tracks[0].Pan);
        var path = Path.Combine(directory.Path, "balanced.audionest");
        await vm.SaveAsync(path);
        await vm.OpenAsync(path);
        Assert.Equal(0.25f, vm.Project.Tracks[0].Pan);
        window.Close();
        Layout();
    }

    [AvaloniaFact]
    public async Task NeutralThemesMenusAndViewportLayoutRespondToResize()
    {
        using var directory = new TempDirectory();
        using var backend = new FakeBackend();
        using var vm = Create(backend, directory);
        var window = new MainWindow(vm) { WindowState = WindowState.Normal };
        window.Show();
        Layout();
        Assert.Equal(ThemeVariant.Light, Avalonia.Application.Current!.RequestedThemeVariant);
        var menu = window.FindControl<Menu>("EditorMenuBar")!;
        Assert.Equal(new[] { "File", "Edit", "Effects", "Tools", "Help" },
            menu.Items.OfType<MenuItem>().Select(i => i.Header));
        var tools = menu.Items.OfType<MenuItem>().Single(i => (string)i.Header! == "Tools");
        var dark = tools.Items.OfType<MenuItem>().Single(i => (string)i.Header! == "Dark mode");
        dark.Command!.Execute(null);
        Assert.True(vm.IsDarkMode);
        Assert.True(dark.IsChecked);
        Assert.Equal(ThemeVariant.Dark, Avalonia.Application.Current.RequestedThemeVariant);
        Assert.False(vm.IsDirty);
        dark.Command.Execute(null);
        var scroll = window.FindControl<ScrollViewer>("TimelineScroll")!;
        var inspector = window.FindControl<Border>("Inspector")!;
        window.Width = 820;
        window.Height = 520;
        Layout();
        Assert.False(inspector.IsVisible);
        Assert.InRange(vm.TimelineWidth, scroll.Viewport.Width, Math.Max(scroll.Viewport.Width, 450) + 1);
        var narrow = scroll.Viewport.Width;
        window.Width = 1500;
        window.Height = 950;
        Layout();
        Assert.True(inspector.IsVisible);
        Assert.True(scroll.Viewport.Width > narrow);
        Assert.True(vm.TimelineHeight >= TimelineMetrics.TrackHeight);

        // Ruler and clips share an x offset; track controls are outside that scroll.
        vm.Zoom(10);
        Layout();
        scroll.Offset = new Vector(160, 0);
        Layout();
        Assert.Equal(scroll.Offset.X, window.FindControl<ScrollViewer>("RulerScroll")!.Offset.X);

        backend.DecodedFrames = 96000;
        var source = TestProjects.WriteWave(directory.Path);
        vm.SkipToStart();
        await vm.ImportAsync(source);
        vm.AddTrack();
        vm.Select(vm.Project.Tracks[1].Id, null);
        vm.Seek(72000);
        await vm.ImportAsync(source);
        vm.Select(vm.Project.Tracks[1].Id, vm.Project.Tracks[1].Clips[0].Id);
        vm.SetTrackPan(vm.Project.Tracks[1].Id, 0.25f);
        vm.FitProject();
        scroll.Offset = default;
        Layout();
        var frameDirectory = Path.Combine(AppContext.BaseDirectory, "ui-artifacts");
        Directory.CreateDirectory(frameDirectory);
        using (var frame = window.CaptureRenderedFrame()) frame!.Save(Path.Combine(frameDirectory, "editor-light.png"));
        vm.IsDarkMode = true;
        Layout();
        using (var frame = window.CaptureRenderedFrame()) frame!.Save(Path.Combine(frameDirectory, "editor-dark.png"));
        vm.IsDarkMode = false;

        vm.Zoom(2);
        for (var i = 0; i < 8; i++) vm.AddTrack();
        Layout();
        var ruler = window.FindControl<TimelineRulerControl>("TimelineRuler")!;
        var rulerTop = InWindow(ruler, window, default).Y;
        var horizontal = window.FindControl<Avalonia.Controls.Primitives.ScrollBar>("TimelineHorizontalBar")!;
        var barTop = InWindow(horizontal, window, default).Y;
        var vertical = window.FindControl<ScrollViewer>("TracksVerticalScroll")!;
        vertical.Offset = new Vector(0, 250);
        Layout();
        var firstHeader = window.GetVisualDescendants().OfType<TrackHeaderView>().First();
        var canvas = window.FindControl<TimelineControl>("TimelineClips")!;
        Assert.Equal(InWindow(firstHeader, window, default).Y, InWindow(canvas, window, default).Y);
        Assert.Equal(rulerTop, InWindow(ruler, window, default).Y);
        Assert.Equal(barTop, InWindow(horizontal, window, default).Y);
        vm.New();
        window.Close();
        Layout();
    }

    [AvaloniaFact]
    public async Task ActualClipDragSnapsAndAltBypassesTheSnap()
    {
        using var directory = new TempDirectory();
        using var backend = new FakeBackend { DecodedFrames = 96000 };
        using var vm = Create(backend, directory);
        await vm.ImportAsync(TestProjects.WriteWave(directory.Path));
        var window = new MainWindow(vm) { WindowState = WindowState.Normal };
        window.Show();
        Layout();
        var clips = window.FindControl<TimelineControl>("TimelineClips")!;
        vm.Seek(48000);
        void Drag(RawInputModifiers modifiers)
        {
            window.MouseDown(InWindow(clips, window, new Point(50, 55)), MouseButton.Left, modifiers);
            window.MouseMove(InWindow(clips, window, new Point(142, 55)), modifiers);
            window.MouseUp(InWindow(clips, window, new Point(142, 55)), MouseButton.Left, modifiers);
        }
        Drag(RawInputModifiers.None);
        Assert.Equal(48000, vm.Project.Tracks[0].Clips[0].Start);
        Assert.Equal(48000, vm.Position);
        vm.Undo();
        Drag(RawInputModifiers.Alt);
        Assert.Equal((long)Math.Round(92.0 / vm.PixelsPerSecond * vm.Project.SampleRate),
            vm.Project.Tracks[0].Clips[0].Start);
        Assert.Equal(48000, vm.Position);
        vm.New();
        window.Close();
        Layout();
    }

    [AvaloniaFact]
    public async Task SnappingToggleBypassTransportAndPreciseStatusWork()
    {
        using var directory = new TempDirectory();
        using var backend = new FakeBackend { DecodedFrames = 96000 };
        using var vm = Create(backend, directory);
        await vm.ImportAsync(TestProjects.WriteWave(directory.Path));
        var clip = vm.Project.Tracks[0].Clips[0];
        vm.Seek(48000);
        Assert.Equal(48000, vm.ResolveClipMove(clip, 47000).Start);
        vm.IsSnappingEnabled = false;
        Assert.Equal(47000, vm.ResolveClipMove(clip, 47000).Start);
        vm.IsSnappingEnabled = true;
        Assert.Equal(47000, vm.ResolveClipMove(clip, 47000, true).Start);
        vm.Seek(1);
        Assert.Equal("00:00:00.000020", vm.PositionText);
        Assert.Contains("1", vm.PositionFramesText);
        vm.StartPlayback();
        Assert.True(vm.CanPause);
        vm.Pause();
        Assert.True(vm.CanPlay);
        vm.SkipToEnd();
        Assert.Equal(vm.Project.Duration, vm.Position);
        vm.SkipToStart();
        Assert.Equal(0, vm.Position);
        vm.Seek(3 * vm.Project.SampleRate);
        Assert.Equal(144000, vm.Position);
        vm.SetTimelineViewportWidth(700);
        vm.FitProject();
        Assert.InRange(vm.TimelineWidth, 700, 701);
    }
}
