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

public sealed class GroupAndRecordingTests
{
    private static MainViewModel Create(FakeBackend backend, TempDirectory directory) =>
        new(new ProjectSession(), new AudioLibrary(backend), new AudioTransport(backend), backend,
            new JsonProjectStore(), Path.Combine(directory.Path, "workspace"));
    private static void Layout() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
    private static Point At(Control control, Window window, Point p) => control.TranslatePoint(p, window)!.Value;
    private static void Click(Window window, Control control, Point p, MouseButton button = MouseButton.Left)
    {
        var point = At(control, window, p);
        window.MouseDown(point, button);
        window.MouseUp(point, button);
        Layout();
    }
    private static void Close(Window window, MainViewModel vm) { vm.New(); window.Close(); Layout(); }

    [Fact]
    public void PureGroupMoveKeepsTimingAndTrackOffsetsAndClampsAsAUnit()
    {
        var project = TestProjects.Create(100, 20);
        var a = project.Tracks[0].Clips[0];
        var b = a with { Id = Guid.NewGuid(), Start = 50 };
        project = project with { Tracks = project.Tracks.Add(new AudioTrack { Clips = [b] }).Add(new AudioTrack()) };
        var ids = new HashSet<Guid> { a.Id, b.Id };
        var moved = ProjectEdits.MoveClips(project, ids, -1000, 5);
        moved.Validate();
        Assert.Empty(moved.Tracks[0].Clips);
        Assert.Equal(0, moved.Tracks[1].Clips[0].Start);
        Assert.Equal(30, moved.Tracks[2].Clips[0].Start);
        Assert.Equal(a.SourceOffset, moved.Tracks[1].Clips[0].SourceOffset);
        Assert.Equal(20, project.Tracks[0].Clips[0].Start);
    }

    [AvaloniaFact]
    public async Task ShiftSelectionMovesCutsCopiesAndPastesTheWholeGroupInOneEdit()
    {
        using var directory = new TempDirectory();
        using var backend = new FakeBackend { DecodedFrames = 96000 };
        using var vm = Create(backend, directory);
        var source = TestProjects.WriteWave(directory.Path);
        await vm.ImportAsync(source);
        vm.AddTrack();
        vm.Seek(24000);
        await vm.ImportAsync(source);
        var window = new MainWindow(vm) { WindowState = WindowState.Normal };
        window.Show(); Layout();
        var canvas = window.FindControl<TimelineControl>("TimelineClips")!;
        Click(window, canvas, new Point(70, 65));
        window.KeyPress(Key.LeftShift, RawInputModifiers.Shift, PhysicalKey.ShiftLeft, null);
        Click(window, canvas, new Point(100, vm.TrackTop(1) + 65));
        window.KeyRelease(Key.LeftShift, RawInputModifiers.None, PhysicalKey.ShiftLeft, null);
        Assert.Equal(2, vm.SelectedClipIds.Count);
        vm.IsSnappingEnabled = false;
        window.MouseDown(At(canvas, window, new Point(70, 65)), MouseButton.Left);
        window.MouseMove(At(canvas, window, new Point(115, 65)));
        window.MouseUp(At(canvas, window, new Point(115, 65)), MouseButton.Left);
        Layout();
        Assert.Equal(24000, vm.Project.Tracks[0].Clips[0].Start);
        Assert.Equal(48000, vm.Project.Tracks[1].Clips[0].Start);
        Assert.Equal(2, vm.SelectedClipIds.Count);
        vm.Undo();
        Assert.Equal(0, vm.Project.Tracks[0].Clips[0].Start);
        vm.Redo();
        window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.Control);
        window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.Control);
        Assert.All(vm.Project.Tracks, t => Assert.Empty(t.Clips));
        vm.PasteAt(vm.Project.Tracks[0].Id, 144000);
        Assert.Equal(144000, vm.Project.Tracks[0].Clips[0].Start);
        Assert.Equal(168000, vm.Project.Tracks[1].Clips[0].Start);
        Assert.Equal(2, vm.SelectedClipIds.Count);
        vm.Undo();
        Assert.All(vm.Project.Tracks, t => Assert.Empty(t.Clips));
        vm.Undo();
        Assert.Equal(24000, vm.Project.Tracks[0].Clips[0].Start);
        Close(window, vm);
    }

    [AvaloniaFact]
    public async Task TrackRenameDialogAndHeightGripKeepCanvasAndHeadersAligned()
    {
        using var directory = new TempDirectory();
        using var backend = new FakeBackend { DecodedFrames = 96000 };
        using var vm = Create(backend, directory);
        await vm.ImportAsync(TestProjects.WriteWave(directory.Path));
        vm.AddTrack();
        var window = new MainWindow(vm) { WindowState = WindowState.Normal };
        window.Show(); Layout();
        var headers = window.GetVisualDescendants().OfType<TrackHeaderView>().ToArray();
        var first = headers[0];
        var grip = first.FindControl<Thumb>("TrackResizeGrip")!;
        var start = At(grip, window, new Point(80, 3));
        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(start + new Vector(0, 40));
        window.MouseMove(start + new Vector(0, 80));
        window.MouseUp(start + new Vector(0, 80), MouseButton.Left);
        Layout();
        Assert.InRange(vm.TrackHeight(vm.Project.Tracks[0].Id), TimelineMetrics.TrackHeight + 78, TimelineMetrics.TrackHeight + 82);
        Assert.Equal(vm.TrackTop(1), headers[1].TranslatePoint(default, first)!.Value.Y);
        start = At(grip, window, new Point(80, 3));
        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(start + new Vector(0, 20)); Layout();
        window.MouseMove(start + new Vector(0, 40)); Layout();
        window.MouseUp(start + new Vector(0, 40), MouseButton.Left);
        Assert.InRange(vm.TrackHeight(vm.Project.Tracks[0].Id), TimelineMetrics.TrackHeight + 118, TimelineMetrics.TrackHeight + 122);
        vm.SetTrackHeight(vm.Project.Tracks[0].Id, 1);
        Assert.Equal(TimelineMetrics.TrackHeight, vm.TrackHeight(vm.Project.Tracks[0].Id));
        Layout();
        var panel = window.FindControl<Border>("TrackHeadersPanel")!;
        Click(window, panel, new Point(80, 35), MouseButton.Right);
        panel.ContextMenu!.Items.OfType<MenuItem>().Single(i => (string)i.Header! == "Rename track…").Command!.Execute(null);
        panel.ContextMenu.Close();
        Layout();
        var dialog = window.OwnedWindows.Single();
        dialog.GetVisualDescendants().OfType<TextBox>().Single(c => c.Name == "TrackNameBox").Text = "Narration";
        Click(dialog, dialog.GetVisualDescendants().OfType<Button>().Single(c => c.Name == "RenameTrackButton"), new Point(20, 12));
        Layout();
        Assert.Equal("Narration", vm.Project.Tracks[0].Name);
        vm.Undo();
        Assert.Equal("Track 1", vm.Project.Tracks[0].Name);
        vm.Redo();
        var path = Path.Combine(directory.Path, "named.audionest");
        await vm.SaveAsync(path);
        await vm.OpenAsync(path);
        Assert.Equal("Narration", vm.Project.Tracks[0].Name);
        Close(window, vm);
    }

    [AvaloniaFact]
    public async Task SlidersAreFivePercentSteppedCompactAndMonoPanIsDisabled()
    {
        using var directory = new TempDirectory();
        using var backend = new FakeBackend();
        using var vm = Create(backend, directory);
        vm.AddMonoTrack();
        var headerVm = vm.TrackHeaders[0];
        headerVm.Volume = 1.023;
        Assert.Equal(1, headerVm.Volume);
        headerVm.Volume = 1.077;
        Assert.Equal(1.1, headerVm.Volume, 5);
        headerVm.Pan = 0.123;
        Assert.Equal(0.1, headerVm.Pan, 5);
        var window = new MainWindow(vm) { WindowState = WindowState.Normal };
        window.Show(); Layout();
        var headers = window.GetVisualDescendants().OfType<TrackHeaderView>().ToArray();
        var slider = headers[0].FindControl<Slider>("VolumeSlider")!;
        Assert.True(slider.IsSnapToTickEnabled);
        Assert.Equal(0.05, slider.TickFrequency);
        var thumb = slider.GetVisualDescendants().OfType<Thumb>().Single();
        Assert.Equal(12, thumb.Bounds.Height);
        var top = thumb.TranslatePoint(default, slider)!.Value.Y;
        Assert.True(top >= 0 && top + thumb.Bounds.Height <= slider.Bounds.Height);
        Assert.False(headers[1].FindControl<Slider>("PanSlider")!.IsEnabled);
        vm.TrackHeaders[1].Pan = 1;
        Assert.Equal(0, vm.TrackHeaders[1].Pan);
        vm.New(); window.Close(); Layout();
        await Task.CompletedTask;
    }

    [Fact]
    public void MonoRendererIgnoresLegacyPanAndPreservesActualMonoSignal()
    {
        var project = TestProjects.Create(1);
        project = project with { Tracks = [project.Tracks[0] with { Layout = TrackChannelLayout.Mono, Pan = 1 }] };
        var buffer = new AudioBuffer(new DecodedAudio(new PcmFormat(48000, 2), [0.8f, -0.2f]));
        var output = new float[2];
        new RenderPlan(project, _ => buffer).Render(0, output);
        Assert.Equal(0.3f, output[0], 5);
        Assert.Equal(output[0], output[1]);
    }

    [AvaloniaFact]
    public async Task CapturedFramesAppearAsALiveDraftAndBecomeOneUndoableClipOnStop()
    {
        using var directory = new TempDirectory();
        using var backend = new FakeBackend { DecodedFrames = 48000 };
        using var vm = Create(backend, directory);
        var window = new MainWindow(vm) { WindowState = WindowState.Normal };
        window.Show(); Layout();
        vm.Record();
        var samples = Enumerable.Range(0, 48000).Select(i => 0.4f * MathF.Sin(i * 0.01f)).ToArray();
        backend.Recording!.Preview.Append(samples);
        vm.Tick(); Layout();
        Assert.NotNull(vm.LiveRecording);
        Assert.Equal(48000, vm.LiveRecording.Frames);
        Assert.Equal(40, vm.LiveRecording.Peaks.Length);
        Assert.Contains(vm.LiveRecording.Peaks, p => p.Maximum > 0.3);
        Assert.Empty(vm.Project.Tracks[0].Clips);
        var directoryPath = Path.Combine(AppContext.BaseDirectory, "ui-artifacts");
        Directory.CreateDirectory(directoryPath);
        using (var frame = window.CaptureRenderedFrame()) frame!.Save(Path.Combine(directoryPath, "recording-live.png"));
        await vm.StopAsync();
        Assert.Null(vm.LiveRecording);
        Assert.Equal(48000, vm.Project.Tracks[0].Clips.Single().Length);
        vm.Undo();
        Assert.Empty(vm.Project.Tracks[0].Clips);
        Close(window, vm);
    }

    [Fact]
    public void RecordingEnvelopeAppendDoesNotAllocateAndSnapshotIsIndependent()
    {
        var preview = new RecordingPreview(new PcmFormat(48000, 1));
        var data = Enumerable.Repeat(0.4f, 1200).ToArray();
        preview.Append(data);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10; i++) preview.Append(data);
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
        var snapshot = preview.Snapshot();
        preview.Append(data);
        Assert.Equal(11, snapshot.Peaks.Length);
        Assert.Equal(12, preview.Snapshot().Peaks.Length);
        Assert.Equal(13200, snapshot.Frames);
    }

    [AvaloniaFact]
    public void ExportOptionsChooseAllFormatsAndValidateDestination()
    {
        using var directory = new TempDirectory();
        var model = new ExportOptionsViewModel(true) { Path = Path.Combine(directory.Path, "mix.wav") };
        Assert.True(model.CanExport);
        foreach (var format in model.Formats)
        {
            model.SelectedFormat = format;
            Assert.EndsWith("." + format.Extension, model.Path);
            Assert.True(model.CanExport);
        }
        model.Path = Path.Combine(directory.Path, "missing", "mix.flac");
        Assert.False(model.CanExport);
        Assert.False(new ExportOptionsViewModel(false).CanExport);
    }

    [AvaloniaFact]
    public void ExportDialogShowsDestinationFormatAndActions()
    {
        using var directory = new TempDirectory();
        var window = new ExportOptionsWindow(true);
        var model = (ExportOptionsViewModel)window.DataContext!;
        model.Path = Path.Combine(directory.Path, "mix.wav");
        window.Show(); Layout();
        var formats = window.FindControl<ComboBox>("FormatBox")!;
        formats.SelectedItem = AudioFileType.For(AudioFileFormat.Mp3);
        Layout();
        Assert.EndsWith(".mp3", window.FindControl<TextBox>("OutputPathBox")!.Text);
        Assert.True(window.FindControl<Button>("ExportButton")!.IsEnabled);
        var artifactDirectory = Path.Combine(AppContext.BaseDirectory, "ui-artifacts");
        Directory.CreateDirectory(artifactDirectory);
        using (var frame = window.CaptureRenderedFrame()) frame!.Save(Path.Combine(artifactDirectory, "export-options.png"));
        Click(window, window.FindControl<Button>("ExportButton")!, new Point(20, 12));
        Assert.False(window.IsVisible);
    }
}
