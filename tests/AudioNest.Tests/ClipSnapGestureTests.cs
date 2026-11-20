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

public sealed class ClipSnapGestureTests
{
    private static MainViewModel Create(FakeBackend backend, TempDirectory directory) =>
        new(new ProjectSession(), new AudioLibrary(backend), new AudioTransport(backend), backend,
            new JsonProjectStore(), Path.Combine(directory.Path, "workspace"));
    private static void Layout() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
    private static Point At(Control canvas, Window window, double x, double y = 70) =>
        canvas.TranslatePoint(new Point(x, y), window)!.Value;
    private static void Shift(Window window, bool down, bool right = false)
    {
        var key = right ? Key.RightShift : Key.LeftShift;
        var physical = right ? PhysicalKey.ShiftRight : PhysicalKey.ShiftLeft;
        if (down) window.KeyPress(key, RawInputModifiers.Shift, physical, null);
        else window.KeyRelease(key, RawInputModifiers.None, physical, null);
    }
    private static void Close(Window window, MainViewModel vm) { vm.New(); window.Close(); Layout(); }
    private static void Capture(Window window, string name)
    {
        var artifacts = Path.Combine(AppContext.BaseDirectory, "ui-artifacts");
        Directory.CreateDirectory(artifacts);
        using var frame = window.CaptureRenderedFrame();
        frame!.Save(Path.Combine(artifacts, name + ".png"));
    }
    private static async Task<AudioClip> Setup(MainViewModel vm, TempDirectory directory)
    {
        await vm.ImportAsync(TestProjects.WriteWave(directory.Path));
        var original = vm.Project.Tracks[0].Clips[0];
        vm.Edit("Test layout", p => p with
        {
            Tracks = [p.Tracks[0] with { Clips = [original with { Start = 24000, SourceOffset = 24000, Length = 96000 }] },
                new AudioTrack(), new AudioTrack { Clips = [original with { Id = Guid.NewGuid(), Start = 48000, Length = 24000 }] }]
        });
        vm.Select(vm.Project.Tracks[0].Id, original.Id);
        vm.Seek(160000); // Keep the cursor away from clip-boundary targets.
        return vm.SelectedClip!;
    }

    [AvaloniaTheory]
    [InlineData("move", false, false)]
    [InlineData("move", false, true)]
    [InlineData("move", true, false)]
    [InlineData("move", true, true)]
    [InlineData("start", false, false)]
    [InlineData("start", false, true)]
    [InlineData("start", true, false)]
    [InlineData("start", true, true)]
    [InlineData("end", false, false)]
    [InlineData("end", false, true)]
    [InlineData("end", true, false)]
    [InlineData("end", true, true)]
    public async Task LeftShiftInvertsSnapForMovesAndBothResizeEdgesAcrossThreeTracks(string gesture, bool snap, bool shift)
    {
        using var directory = new TempDirectory();
        using var backend = new FakeBackend { DecodedFrames = 192000 };
        using var vm = Create(backend, directory);
        var original = await Setup(vm, directory);
        vm.IsSnappingEnabled = snap;
        var window = new MainWindow(vm) { WindowState = WindowState.Normal };
        window.Show(); Layout();
        var canvas = window.FindControl<TimelineControl>("TimelineClips")!;
        var scale = vm.PixelsPerSecond / vm.Project.SampleRate;
        var target = gesture == "end" ? 72000L : 48000L;
        var proposed = target - 1600; // Three pixels from the target at default zoom.
        var initialEdge = gesture == "end" ? original.End : original.Start;
        var press = gesture == "move" ? (original.Start + 12000) * scale : initialEdge * scale;
        var release = press + (proposed - initialEdge) * scale;
        var before = vm.Project;
        var others = vm.Project.Tracks[2].Clips;
        if (shift) Shift(window, true);
        var modifiers = shift ? RawInputModifiers.Shift : RawInputModifiers.None;
        window.MouseDown(At(canvas, window, press), MouseButton.Left, modifiers);
        window.MouseMove(At(canvas, window, release), modifiers);
        Layout();
        Assert.Same(before, vm.Project); // Previews never edit project/history.
        Assert.True(vm.IsClipSelected(original.Id));
        window.MouseUp(At(canvas, window, release), MouseButton.Left, modifiers);
        if (shift) Shift(window, false);
        Layout();
        var edited = vm.SelectedClip!;
        Assert.Equal(snap != shift ? target : proposed, gesture == "end" ? edited.End : edited.Start);
        Assert.Equal(others, vm.Project.Tracks[2].Clips);
        Assert.Equal(160000, vm.Position);
        Assert.Equal(snap, vm.IsSnappingEnabled); // The modifier is temporary.
        if (gesture == "start")
        {
            Assert.Equal(original.End, edited.End);
            Assert.Equal(original.SourceOffset + edited.Start - original.Start, edited.SourceOffset);
        }
        else
        {
            Assert.Equal(original.SourceOffset, edited.SourceOffset);
            Assert.Equal(gesture == "move" ? original.Length : original.Start, gesture == "move" ? edited.Length : edited.Start);
        }
        vm.Undo();
        Assert.Equal(original, vm.SelectedClip);
        vm.Redo();
        Assert.Equal(edited, vm.SelectedClip);
        Close(window, vm);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ModifierChangesDuringCapturedResizeRefreshPreviewAndApplyAtRelease(bool snap)
    {
        using var directory = new TempDirectory();
        using var backend = new FakeBackend { DecodedFrames = 192000 };
        using var vm = Create(backend, directory);
        var original = await Setup(vm, directory);
        vm.IsSnappingEnabled = snap;
        var window = new MainWindow(vm) { WindowState = WindowState.Normal };
        window.Show(); Layout();
        var canvas = window.FindControl<TimelineControl>("TimelineClips")!;
        var scale = vm.PixelsPerSecond / vm.Project.SampleRate;
        window.MouseDown(At(canvas, window, original.End * scale), MouseButton.Left);
        window.MouseMove(At(canvas, window, 70400 * scale));
        Layout();
        if (snap) Capture(window, "resize-snapped-preview");
        var refreshes = 0;
        vm.TimelineChanged += () => refreshes++;
        Shift(window, true);
        Layout();
        if (snap) Capture(window, "resize-free-preview");
        Assert.True(refreshes > 0);
        Assert.Equal(!snap ? 72000 : 70400, vm.ResolveClipTrim(original, 70400, false).Frame);
        Shift(window, false);
        Assert.Equal(snap ? 72000 : 70400, vm.ResolveClipTrim(original, 70400, false).Frame);
        Shift(window, true);
        // No additional pointer move: release must still use the current modifier.
        window.MouseUp(At(canvas, window, 70400 * scale), MouseButton.Left, RawInputModifiers.Shift);
        Shift(window, false);
        Assert.Equal(!snap ? 72000 : 70400, vm.SelectedClip!.End);
        Close(window, vm);
    }

    [AvaloniaTheory]
    [InlineData("move", false)]
    [InlineData("move", true)]
    [InlineData("start", false)]
    [InlineData("start", true)]
    [InlineData("end", false)]
    [InlineData("end", true)]
    public async Task RightShiftDoesNotInvertClipSnapping(string gesture, bool snap)
    {
        using var directory = new TempDirectory();
        using var backend = new FakeBackend { DecodedFrames = 192000 };
        using var vm = Create(backend, directory);
        var original = await Setup(vm, directory);
        vm.IsSnappingEnabled = snap;
        var window = new MainWindow(vm) { WindowState = WindowState.Normal };
        window.Show(); Layout();
        var canvas = window.FindControl<TimelineControl>("TimelineClips")!;
        var scale = vm.PixelsPerSecond / vm.Project.SampleRate;
        var press = gesture == "move" ? original.Start + 12000 : gesture == "end" ? original.End : original.Start;
        var target = gesture == "end" ? 72000L : 48000L;
        var release = press + target - 1600 - (gesture == "end" ? original.End : original.Start);
        Shift(window, true, right: true);
        window.MouseDown(At(canvas, window, press * scale), MouseButton.Left, RawInputModifiers.Shift);
        window.MouseMove(At(canvas, window, release * scale), RawInputModifiers.Shift);
        window.MouseUp(At(canvas, window, release * scale), MouseButton.Left, RawInputModifiers.Shift);
        Shift(window, false, right: true);
        Assert.False(vm.IsLeftShiftDown);
        Assert.Equal(snap ? target : target - 1600, gesture == "end" ? vm.SelectedClip!.End : vm.SelectedClip!.Start);
        Close(window, vm);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RulerOnlySnapsWithLeftShiftRegardlessOfSnapButton(bool snap)
    {
        using var directory = new TempDirectory();
        using var backend = new FakeBackend { DecodedFrames = 192000 };
        using var vm = Create(backend, directory);
        await Setup(vm, directory);
        vm.IsSnappingEnabled = snap;
        var window = new MainWindow(vm) { WindowState = WindowState.Normal };
        window.Show(); Layout();
        var ruler = window.FindControl<TimelineRulerControl>("TimelineRuler")!;
        var point = At(ruler, window, 70400d / vm.Project.SampleRate * vm.PixelsPerSecond, 12);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Assert.Equal(70400, vm.Position);
        Shift(window, true);
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.Shift);
        window.MouseMove(point, RawInputModifiers.Shift);
        window.MouseUp(point, MouseButton.Left, RawInputModifiers.Shift);
        Shift(window, false);
        Assert.Equal(72000, vm.Position);
        Shift(window, true, right: true);
        window.MouseDown(point, MouseButton.Left, RawInputModifiers.Shift);
        window.MouseUp(point, MouseButton.Left, RawInputModifiers.Shift);
        Shift(window, false, right: true);
        Assert.Equal(70400, vm.Position);
        Close(window, vm);
    }

    [AvaloniaFact]
    public async Task ShiftClickStillTogglesMembershipButShiftDraggingPreservesTheGroup()
    {
        using var directory = new TempDirectory();
        using var backend = new FakeBackend { DecodedFrames = 192000 };
        using var vm = Create(backend, directory);
        var original = await Setup(vm, directory);
        var other = vm.Project.Tracks[2].Clips[0];
        vm.Select(vm.Project.Tracks[2].Id, other.Id, additive: true);
        vm.Select(vm.Project.Tracks[0].Id, original.Id, preserveGroup: true);
        var window = new MainWindow(vm) { WindowState = WindowState.Normal, Height = 1000 };
        window.Show(); Layout();
        var canvas = window.FindControl<TimelineControl>("TimelineClips")!;
        Shift(window, true);
        var press = At(canvas, window, 65);
        window.MouseDown(press, MouseButton.Left, RawInputModifiers.Shift);
        window.MouseMove(press + new Vector(30, 0), RawInputModifiers.Shift);
        window.MouseUp(press + new Vector(30, 0), MouseButton.Left, RawInputModifiers.Shift);
        Assert.Equal(2, vm.SelectedClipIds.Count);
        var delta = (long)Math.Round(30 / vm.PixelsPerSecond * vm.Project.SampleRate);
        Assert.Equal(original.Start + delta, vm.Project.Tracks[0].Clips[0].Start);
        Assert.Equal(other.Start + delta, vm.Project.Tracks[2].Clips[0].Start);
        vm.Undo();
        window.MouseDown(press, MouseButton.Left, RawInputModifiers.Shift);
        window.MouseUp(press, MouseButton.Left, RawInputModifiers.Shift);
        Assert.False(vm.IsClipSelected(original.Id));
        Assert.True(vm.IsClipSelected(other.Id));
        window.MouseDown(press, MouseButton.Left, RawInputModifiers.Shift);
        window.MouseUp(press, MouseButton.Left, RawInputModifiers.Shift);
        Assert.Equal(2, vm.SelectedClipIds.Count);
        Shift(window, false);
        Close(window, vm);
    }
}
