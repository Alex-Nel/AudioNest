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
using Xunit;

namespace AudioNest.Tests;

public sealed class TimelineBehaviorTests
{
    private static MainViewModel Create(FakeBackend backend, TempDirectory directory) =>
        new(new ProjectSession(), new AudioLibrary(backend), new AudioTransport(backend), backend,
            new JsonProjectStore(), Path.Combine(directory.Path, "workspace"));
    private static void Layout() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
    private static Point At(Control control, Window window, Point point) => control.TranslatePoint(point, window)!.Value;
    private static void Click(Window window, Control control, Point point, MouseButton button = MouseButton.Left)
    {
        var pos = At(control, window, point);
        window.MouseDown(pos, button);
        window.MouseUp(pos, button);
        Layout();
    }
    private static void Close(Window window, MainViewModel vm) { vm.New(); window.Close(); Layout(); }

    [AvaloniaFact]
    public async Task EdgeDragTrimsAndReexpandsWithOneUndoAndRazorSplitsAtMouseNotCursor()
    {
        using var directory = new TempDirectory();
        using var backend = new FakeBackend { DecodedFrames = 96000 };
        using var vm = Create(backend, directory);
        await vm.ImportAsync(TestProjects.WriteWave(directory.Path));
        var window = new MainWindow(vm) { WindowState = WindowState.Normal };
        window.Show(); Layout();
        var canvas = window.FindControl<TimelineControl>("TimelineClips")!;
        vm.Seek(48000);
        void Drag(double from, double to)
        {
            window.MouseDown(At(canvas, window, new Point(from, 70)), MouseButton.Left);
            window.MouseMove(At(canvas, window, new Point(to, 70)));
            window.MouseUp(At(canvas, window, new Point(to, 70)), MouseButton.Left);
            Layout();
        }
        Drag(0, 45);
        var clip = vm.Project.Tracks[0].Clips[0];
        Assert.Equal(24000, clip.Start);
        Assert.Equal(24000, clip.SourceOffset);
        Assert.Equal(72000, clip.Length);
        Assert.Equal(48000, vm.Position);
        Drag(45, 0);
        Assert.Equal(0, vm.Project.Tracks[0].Clips[0].SourceOffset);
        Drag(180, 135);
        Assert.Equal(72000, vm.Project.Tracks[0].Clips[0].Length);
        Drag(135, 180);
        Assert.Equal(96000, vm.Project.Tracks[0].Clips[0].Length);
        vm.Undo();
        Assert.Equal(72000, vm.Project.Tracks[0].Clips[0].Length);
        vm.Redo();
        window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.None);
        Assert.True(vm.IsRazorTool);
        Click(window, canvas, new Point(90, 70));
        Assert.Equal(2, vm.Project.Tracks[0].Clips.Length);
        Assert.Equal(48000, vm.Project.Tracks[0].Clips[1].SourceOffset);
        Assert.Equal(48000, vm.Position);
        vm.Undo();
        Assert.Single(vm.Project.Tracks[0].Clips);
        window.KeyPressQwerty(PhysicalKey.V, RawInputModifiers.None);
        Assert.True(vm.IsSelectionTool);
        Close(window, vm);
    }

    [AvaloniaFact]
    public async Task ClipboardShortcutsPreserveOffsetsEffectsAndSurviveProjectChanges()
    {
        using var directory = new TempDirectory();
        using var backend = new FakeBackend { DecodedFrames = 96000 };
        using var vm = Create(backend, directory);
        await vm.ImportAsync(TestProjects.WriteWave(directory.Path));
        var track = vm.Project.Tracks[0];
        var id = track.Clips[0].Id;
        vm.Trim(id, 24000, true);
        vm.Select(track.Id, id);
        vm.ApplyGain(-3, false);
        var source = vm.SelectedClip!;
        var window = new MainWindow(vm) { WindowState = WindowState.Normal };
        window.Show(); Layout();
        var canvas = window.FindControl<TimelineControl>("TimelineClips")!;
        canvas.Focus();
        var before = vm.Project;
        window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.Control);
        Assert.Same(before, vm.Project);
        Assert.True(vm.CanPaste);
        Assert.True(vm.IsSelectionTool);
        vm.Seek(144000);
        window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.Control);
        Assert.Empty(vm.Project.Tracks[0].Clips);
        Assert.Equal(144000, vm.Position);
        window.KeyPressQwerty(PhysicalKey.V, RawInputModifiers.Control);
        Assert.Single(vm.Project.Tracks[0].Clips);
        var pasted = vm.SelectedClip!;
        Assert.NotEqual(source.Id, pasted.Id);
        Assert.Equal(144000, pasted.Start);
        Assert.Equal(source.SourceOffset, pasted.SourceOffset);
        Assert.Equal(source.Effects, pasted.Effects);
        vm.Undo();
        Assert.Empty(vm.Project.Tracks[0].Clips);
        vm.Undo();
        Assert.Equal(source, vm.Project.Tracks[0].Clips[0]);
        vm.New();
        vm.Paste();
        Assert.Single(vm.Project.Assets);
        Assert.Single(vm.Project.Tracks[0].Clips);
        Assert.Equal(source.SourceOffset, vm.Project.Tracks[0].Clips[0].SourceOffset);
        var path = Path.Combine(directory.Path, "pasted.audionest");
        await vm.SaveAsync(path);
        await vm.OpenAsync(path);
        Assert.DoesNotContain("Error:", vm.Status);
        Close(window, vm);
    }

    [AvaloniaFact]
    public async Task ContextMenusTargetClickedTimeAndBlankHeaderCanCreateMonoTrack()
    {
        using var directory = new TempDirectory();
        using var backend = new FakeBackend { DecodedFrames = 96000 };
        using var vm = Create(backend, directory);
        await vm.ImportAsync(TestProjects.WriteWave(directory.Path));
        var window = new MainWindow(vm) { WindowState = WindowState.Normal };
        window.Show(); Layout();
        var canvas = window.FindControl<TimelineControl>("TimelineClips")!;
        vm.Seek(24000);
        Click(window, canvas, new Point(270, 70), MouseButton.Right);
        Assert.Equal(new[] { "Mute track" }, canvas.ContextMenu!.Items.OfType<MenuItem>().Select(i => i.Header));
        canvas.ContextMenu.Close();
        Click(window, canvas, new Point(90, 70), MouseButton.Right);
        Assert.NotNull(canvas.ContextMenu);
        var menu = canvas.ContextMenu!;
        var items = menu.Items.OfType<MenuItem>().ToArray();
        Assert.Equal(new[] { "Cut", "Copy", "Paste", "Split here", "Mute track" }, items.Select(i => i.Header));
        items.Single(i => (string)i.Header! == "Copy").Command!.Execute(null);
        items.Single(i => (string)i.Header! == "Split here").Command!.Execute(null);
        menu.Close();
        Assert.Equal(48000, vm.Project.Tracks[0].Clips[1].Start);
        Assert.Equal(24000, vm.Position);
        Click(window, canvas, new Point(270, 70), MouseButton.Right);
        items = canvas.ContextMenu!.Items.OfType<MenuItem>().ToArray();
        Assert.Equal(new[] { "Paste", "Mute track" }, items.Select(i => i.Header));
        items[0].Command!.Execute(null);
        items.Single(i => (string)i.Header! == "Mute track").Command!.Execute(null);
        Assert.True(vm.Project.Tracks[0].Muted);
        canvas.ContextMenu.Close();
        Assert.Contains(vm.Project.Tracks[0].Clips, c => c.Start == 144000);
        var header = window.FindControl<Border>("TrackHeadersPanel")!;
        Click(window, header, new Point(80, 200), MouseButton.Right);
        Assert.NotNull(header.ContextMenu);
        var mono = header.ContextMenu!.Items.OfType<MenuItem>().Single(i => (string)i.Header! == "Add mono track");
        mono.Command!.Execute(null);
        header.ContextMenu.Close();
        Assert.Equal(TrackChannelLayout.Mono, vm.Project.Tracks[1].Layout);
        Click(window, header, new Point(80, 35), MouseButton.Right);
        Assert.Null(header.ContextMenu);
        Close(window, vm);
    }

    [AvaloniaFact]
    public async Task ClipboardSnapshotIsIndependentAndPasteIntoNoTracksIsOneUndo()
    {
        using var directory = new TempDirectory();
        using var backend = new FakeBackend { DecodedFrames = 96000 };
        using var vm = Create(backend, directory);
        await vm.ImportAsync(TestProjects.WriteWave(directory.Path));
        var track = vm.Project.Tracks[0];
        vm.Select(track.Id, track.Clips[0].Id);
        vm.Copy();
        vm.Trim(track.Clips[0].Id, 48000, true);
        vm.DeleteTrack(track.Id);
        vm.Seek(96000);
        vm.Paste();
        Assert.Single(vm.Project.Tracks);
        Assert.Equal(0, vm.SelectedClip!.SourceOffset);
        Assert.Equal(96000, vm.SelectedClip.Length);
        Assert.Equal(96000, vm.SelectedClip.Start);
        vm.Undo();
        Assert.Empty(vm.Project.Tracks);
        vm.Redo();
        Assert.Single(vm.Project.Tracks);
        Assert.Single(vm.Project.Tracks[0].Clips);
    }

    [AvaloniaFact]
    public void ToolbarMenusAreGroupedUniformAndMetersResizeIndependently()
    {
        using var directory = new TempDirectory();
        using var backend = new FakeBackend();
        using var vm = Create(backend, directory);
        var window = new MainWindow(vm) { WindowState = WindowState.Normal };
        window.Show(); Layout();
        var menu = window.FindControl<Menu>("EditorMenuBar")!;
        Assert.All(menu.Items.OfType<MenuItem>(), section => Assert.Contains(section.Items, i => i is Separator));
        var file = menu.Items.OfType<MenuItem>().First();
        Assert.Contains(file.Items.OfType<MenuItem>(), i => (string)i.Header! == "Import audio…");
        foreach (var name in new[] { "ZoomInButton", "ZoomOutButton", "FitButton", "ImportButton" })
        {
            var button = window.FindControl<Button>(name)!;
            Assert.Equal(42, button.Bounds.Width);
            Assert.Equal(42, button.Bounds.Height);
        }
        var back = window.FindControl<Button>("SkipBackButton")!;
        var forward = window.FindControl<Button>("SkipForwardButton")!;
        Assert.Equal(back.Bounds.Right + 4, forward.Bounds.Left);
        var meters = window.FindControl<Grid>("LiveMeters")!;
        var splitter = window.FindControl<GridSplitter>("MeterSplitter")!;
        var old = meters.Bounds.Width;
        var point = At(splitter, window, new Point(2, splitter.Bounds.Height / 2));
        window.MouseDown(point, MouseButton.Left);
        window.MouseMove(point + new Vector(-60, 0));
        window.MouseUp(point + new Vector(-60, 0), MouseButton.Left);
        Layout();
        Assert.InRange(meters.Bounds.Width - old, 58, 62);
        window.Close(); Layout();
    }
}
