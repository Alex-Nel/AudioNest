using AudioNest.Application;
using AudioNest.Audio;
using AudioNest.Desktop;
using AudioNest.Domain;
using AudioNest.Hardware;
using AudioNest.Hardware.SoundFlow;
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

public sealed class SampleRateTests
{
    private static MainViewModel Create(FakeBackend backend, TempDirectory directory) =>
        new(new ProjectSession(), new AudioLibrary(backend), new AudioTransport(backend), backend,
            new JsonProjectStore(), Path.Combine(directory.Path, "workspace"));
    private static DecodedAudio Tone(PcmFormat format) => new(format,
        Enumerable.Range(0, format.SampleRate * format.Channels)
            .Select(i => (float)(0.2 * Math.Sin(2 * Math.PI * 1000 * (i / format.Channels) / format.SampleRate))).ToArray());
    private static FakeBackend Backend() => new() { DecodeAudio = (_, format) => Tone(format) };
    private static void Layout() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
    private static void Click(Window window, Control control)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Layout();
    }
    private static void Capture(Window window, string name)
    {
        var artifacts = Path.Combine(AppContext.BaseDirectory, "ui-artifacts");
        Directory.CreateDirectory(artifacts);
        using var frame = window.CaptureRenderedFrame();
        frame!.Save(Path.Combine(artifacts, name + ".png"));
    }

    [Theory]
    [InlineData("44100", 44100)]
    [InlineData("44.1 kHz", 44100)]
    [InlineData("48KHZ", 48000)]
    [InlineData("12.345k", 12345)]
    [InlineData(" 9999 Hz ", 9999)]
    [InlineData("8 kHz", 8000)]
    [InlineData("192000", 192000)]
    public void RateBoxAcceptsPopularAndCustomWholeNumberRates(string text, int expected)
    {
        var model = new SampleRateViewModel(48000) { Text = text };
        Assert.Equal(expected, model.SampleRate);
        Assert.Equal(expected != 48000, model.CanApply);
        Assert.Empty(model.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a rate")]
    [InlineData("7999")]
    [InlineData("192001")]
    [InlineData("-44100")]
    [InlineData("44.1001kHz")]
    [InlineData("NaN")]
    [InlineData("1e100")]
    public void RateBoxRejectsUnsupportedOrFractionalHzWithoutThrowing(string text)
    {
        var model = new SampleRateViewModel(48000) { Text = text };
        Assert.Null(model.SampleRate);
        Assert.False(model.CanApply);
        Assert.NotEmpty(model.Error);
    }

    [Fact]
    public void ClockConversionPreservesTimingSourceRegionsAndAllOtherProperties()
    {
        var project = TestProjects.Create(48000);
        var clip = project.Tracks[0].Clips[0] with
        {
            Start = 24000,
            SourceOffset = 12000,
            Length = 24000,
            Locked = true,
            Muted = true,
            Volume = 0.5f,
            Pan = -0.25f,
            Effects = [BuiltInEffects.Create(EffectKind.Echo)]
        };
        project = project with { Tracks = [project.Tracks[0] with { Clips = [clip], Effects = [BuiltInEffects.Create(EffectKind.Reverb)] }] };
        var next = ProjectSampleRate.Change(project, 44100, new Dictionary<Guid, long> { [TestProjects.AssetId] = 44100 });
        next.Validate();
        Assert.Equal(44100, next.SampleRate);
        Assert.Equal(44100, next.Assets[0].FrameCount);
        Assert.Equal(clip with { Start = 22050, SourceOffset = 11025, Length = 22050 }, next.Tracks[0].Clips[0]);
        Assert.Equal(project.Tracks[0].Effects, next.Tracks[0].Effects);
        Assert.Same(project, ProjectSampleRate.Change(project, 48000, new Dictionary<Guid, long>()));
        Assert.Equal(48000, project.SampleRate);
    }

    [Fact]
    public void QuantizationKeepsTinyClipsValidAndRejectsMissingChangedOrOverflowingMedia()
    {
        var project = TestProjects.Create(48000);
        var clip = project.Tracks[0].Clips[0] with { Start = 3, SourceOffset = 47999, Length = 1 };
        project = project with { Tracks = [project.Tracks[0] with { Clips = [clip] }] };
        var frames = new Dictionary<Guid, long> { [TestProjects.AssetId] = 8000 };
        var next = ProjectSampleRate.Change(project, 8000, frames);
        next.Validate();
        Assert.Equal(1, next.Tracks[0].Clips[0].Length);
        Assert.Equal(7999, next.Tracks[0].Clips[0].SourceOffset);
        Assert.Throws<InvalidDataException>(() => ProjectSampleRate.Change(project, 8000, new Dictionary<Guid, long>()));
        Assert.Throws<InvalidDataException>(() => ProjectSampleRate.Change(project, 8000, new Dictionary<Guid, long> { [TestProjects.AssetId] = 4000 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => ProjectSampleRate.Change(project, 7000, frames));
        Assert.Equal(long.MaxValue, ProjectSampleRate.Cursor(long.MaxValue, 8000, 192000));
        Assert.Throws<OverflowException>(() => ProjectSampleRate.Frame(long.MaxValue, 8000, 192000));
        var extreme = project with { Tracks = [project.Tracks[0] with { Clips = [clip with { Start = long.MaxValue - 1 }] }] };
        Assert.Throws<OverflowException>(() => ProjectSampleRate.Change(extreme, 96000,
            new Dictionary<Guid, long> { [TestProjects.AssetId] = 96000 }));
    }

    [Theory]
    [InlineData(44100)]
    [InlineData(96000)]
    [InlineData(12345)]
    public async Task RateChangeIsUndoableKeepsCursorSecondsAndSelectsMatchingPcmForEveryHistoryState(int rate)
    {
        using var directory = new TempDirectory();
        using var backend = Backend();
        using var vm = Create(backend, directory);
        await vm.ImportAsync(TestProjects.WriteWave(directory.Path));
        vm.Select(vm.Project.Tracks[0].Id, vm.Project.Tracks[0].Clips[0].Id);
        var id = vm.SelectedClip!.Id;
        vm.Trim(id, 12000, true);
        vm.Trim(id, 36000, false);
        vm.AddEffect(EffectKind.Echo);
        vm.Seek(96000); // Preserve cursor time beyond occupied timeline.
        vm.Copy();
        var original = vm.Project;
        var oldBuffer = vm.Buffer(original.Assets[0].Id);
        await vm.ChangeSampleRateAsync(rate);
        Assert.Equal(rate, vm.Project.SampleRate);
        Assert.Equal(rate * 2L, vm.Position);
        Assert.Equal(new PcmFormat(rate, 2), vm.Buffer(original.Assets[0].Id).Format);
        Assert.Equal(id, vm.SelectedClipId);
        Assert.False(vm.CanPaste); // Clipboard remains in its original clock.
        var changed = vm.Project;
        var newBuffer = vm.Buffer(original.Assets[0].Id);
        Assert.Equal(ProjectSampleRate.Frame(12000, 48000, rate), vm.SelectedClip!.SourceOffset);
        Assert.Equal(ProjectSampleRate.Frame(36000, 48000, rate), vm.SelectedClip.End);
        Assert.Same(original.Tracks[0].Clips[0].Effects[0], vm.SelectedClip.Effects[0]);
        var decodes = backend.DecodeCalls;
        vm.Undo();
        Assert.Same(original, vm.Project);
        Assert.Equal(96000, vm.Position);
        Assert.Same(oldBuffer, vm.Buffer(original.Assets[0].Id));
        Assert.True(vm.CanPaste);
        vm.Redo();
        Assert.Same(changed, vm.Project);
        Assert.Same(newBuffer, vm.Buffer(original.Assets[0].Id));
        Assert.Equal(decodes, backend.DecodeCalls);
        await vm.ChangeSampleRateAsync(rate);
        Assert.Equal(decodes, backend.DecodeCalls);
        vm.Undo();
        Assert.Same(original, vm.Project); // Same-rate Apply added no history.
    }

    [Fact]
    public async Task ConversionPausesPlaybackAndReopensOutputAndRecordingAtTheNewRate()
    {
        using var directory = new TempDirectory();
        using var backend = Backend();
        using var vm = Create(backend, directory);
        await vm.ImportAsync(TestProjects.WriteWave(directory.Path));
        vm.StartPlayback();
        backend.Output!.Pull(4800);
        await vm.ChangeSampleRateAsync(44100);
        Assert.False(vm.CanPause);
        Assert.False(backend.Output.Started);
        Assert.Equal(4410, vm.Position);
        vm.StartPlayback();
        Assert.Equal(new PcmFormat(44100, 2), backend.OutputFormat);
        Assert.Equal(2, backend.OutputOpens);
        var audio = backend.Output!.Pull(441);
        Assert.Contains(audio, s => s != 0);
        vm.Pause();
        vm.Record();
        Assert.Equal(new PcmFormat(44100, 1), backend.RecordingFormat);
        await vm.ChangeSampleRateAsync(96000);
        Assert.Equal(44100, vm.Project.SampleRate);
        await vm.StopAsync();
    }

    [Fact]
    public async Task BusyConversionDisablesEditsAndCannotStartAnotherConversionOrRecording()
    {
        using var directory = new TempDirectory();
        using var backend = Backend();
        using var vm = Create(backend, directory);
        await vm.ImportAsync(TestProjects.WriteWave(directory.Path));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        backend.DecodeAudio = (_, format) =>
        {
            entered.TrySetResult();
            release.Task.GetAwaiter().GetResult();
            return Tone(format);
        };
        var change = vm.ChangeSampleRateAsync(44100);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(vm.IsBusy);
            Assert.False(vm.CanEdit);
            Assert.False(vm.CanUndo);
            vm.Record();
            Assert.False(vm.IsRecording);
            await vm.ChangeSampleRateAsync(96000);
            Assert.Equal(48000, vm.Project.SampleRate);
        }
        finally { release.TrySetResult(); }
        await change;
        Assert.Equal(44100, vm.Project.SampleRate);
        Assert.False(vm.IsBusy);
    }

    [Theory]
    [InlineData("decode")]
    [InlineData("length")]
    [InlineData("format")]
    [InlineData("analysis")]
    public async Task FailedConversionLeavesProjectCursorCacheAndUndoHistoryIntact(string failure)
    {
        using var directory = new TempDirectory();
        using var backend = Backend();
        using var vm = Create(backend, directory);
        await vm.ImportAsync(TestProjects.WriteWave(directory.Path));
        vm.Select(vm.Project.Tracks[0].Id, vm.Project.Tracks[0].Clips[0].Id);
        if (failure == "analysis")
        {
            vm.AddEffect(EffectKind.Gain);
            vm.ApplyGain(24, false);
            vm.AddEffect(EffectKind.Normalizer);
        }
        vm.Seek(12345);
        var original = vm.Project;
        var buffer = vm.Buffer(original.Assets[0].Id);
        backend.DecodeAudio = (_, format) => failure switch
        {
            "decode" => throw new IOException("Missing original media."),
            "length" => new DecodedAudio(format, new float[20]),
            "format" => Tone(new PcmFormat(48000, 2)),
            _ => new DecodedAudio(format, Enumerable.Repeat(float.MaxValue, format.SampleRate * 2).ToArray())
        };
        await vm.ChangeSampleRateAsync(44100);
        Assert.Same(original, vm.Project);
        Assert.Same(buffer, vm.Buffer(original.Assets[0].Id));
        Assert.Equal(12345, vm.Position);
        Assert.StartsWith("Error:", vm.Status);
        Assert.False(vm.IsBusy);
        vm.StartPlayback();
        Assert.Contains(backend.Output!.Pull(100), s => s != 0);
        vm.Pause();
        vm.Undo();
        if (failure != "analysis") Assert.Empty(vm.Project.Tracks[0].Clips);
        else Assert.Single(vm.Project.Tracks[0].Clips[0].Effects);
    }

    [Fact]
    public async Task EmptyProjectSupportsCustomRateAndInvalidChangesDoNotCreateHistory()
    {
        using var directory = new TempDirectory();
        using var backend = Backend();
        using var vm = Create(backend, directory);
        await vm.ChangeSampleRateAsync(7999);
        Assert.False(vm.CanUndo);
        Assert.Equal(48000, vm.Project.SampleRate);
        await vm.ChangeSampleRateAsync(12345);
        Assert.Equal(12345, vm.Project.SampleRate);
        Assert.Equal(0, backend.DecodeCalls);
        vm.Undo();
        Assert.Equal(48000, vm.Project.SampleRate);
    }

    [Fact]
    [Trait("Category", "NativeCodec")]
    public async Task RealResamplingPreservesToneDurationOriginalFilesAndSaveReopenExport()
    {
        using var directory = new TempDirectory();
        var source = Path.Combine(directory.Path, "tone.wav");
        var initial = TestProjects.Create(48000);
        var pcm = new AudioBuffer(Tone(new PcmFormat(48000, 2)));
        WaveExporter.Export(new RenderPlan(initial, _ => pcm), source);
        var originalBytes = await File.ReadAllBytesAsync(source);
        using var backend = new SoundFlowBackend();
        using var vm = new MainViewModel(new ProjectSession(), new AudioLibrary(backend), new AudioTransport(backend), backend,
            new JsonProjectStore(), Path.Combine(directory.Path, "workspace"));
        await vm.ImportAsync(source);
        vm.Select(vm.Project.Tracks[0].Id, vm.Project.Tracks[0].Clips[0].Id);
        vm.AddEffect(EffectKind.Echo);
        vm.AddEffect(EffectKind.Normalizer);
        await vm.ChangeSampleRateAsync(44100);
        Assert.Equal(44100, vm.Project.SampleRate);
        Assert.InRange(vm.Project.Duration, 44098, 44102);
        var buffer = vm.Buffer(vm.Project.Assets[0].Id);
        var crossings = Enumerable.Range(1, (int)buffer.FrameCount - 1).Count(i => buffer.Sample(i - 1, 0) <= 0 && buffer.Sample(i, 0) > 0);
        Assert.InRange(crossings, 998, 1002);
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(source));
        var path = Path.Combine(directory.Path, "rate.audionest");
        await vm.SaveAsync(path);
        var saved = vm.Project;
        await vm.OpenAsync(path);
        Assert.Equal(saved.SampleRate, vm.Project.SampleRate);
        Assert.Equal(saved.Tracks[0].Effects.ToArray(), vm.Project.Tracks[0].Effects.ToArray());
        var expectedClip = saved.Tracks[0].Clips[0];
        var reopenedClip = vm.Project.Tracks[0].Clips[0];
        Assert.Equal(expectedClip.Effects.ToArray(), reopenedClip.Effects.ToArray());
        Assert.Equal(expectedClip with { Effects = reopenedClip.Effects }, reopenedClip);
        Assert.DoesNotContain("Error:", vm.Status);
        var output = Path.Combine(directory.Path, "output.wav");
        await vm.ExportAsync(output);
        var bytes = await File.ReadAllBytesAsync(output);
        Assert.Equal(44100, BitConverter.ToInt32(bytes, 24));
        Assert.Equal(44 + vm.Project.Duration * 4, bytes.LongLength);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SampleRateMenuOpensAnEditablePresetDialogAndCancelLeavesTheProjectUntouched(bool dark)
    {
        using var directory = new TempDirectory();
        using var backend = Backend();
        using var vm = Create(backend, directory);
        vm.IsDarkMode = dark;
        var window = new MainWindow(vm) { WindowState = WindowState.Normal };
        window.Show(); Layout();
        var edit = window.FindControl<Menu>("EditorMenuBar")!.Items.OfType<MenuItem>().Single(i => Equals(i.Header, "Edit"));
        var item = edit.Items.OfType<MenuItem>().Single(i => Equals(i.Header, "Project sample rate…"));
        Assert.True(item.Command!.CanExecute(null));
        item.Command.Execute(null); Layout();
        var dialog = Assert.IsType<SampleRateWindow>(Assert.Single(window.OwnedWindows));
        var model = Assert.IsType<SampleRateViewModel>(dialog.DataContext);
        var box = dialog.FindControl<AutoCompleteBox>("SampleRateBox")!;
        Assert.Equal("48000 Hz", box.Text);
        Assert.Contains("44.1 kHz", model.Presets);
        Assert.Contains("48 kHz", model.Presets);
        Assert.False(dialog.FindControl<Button>("ApplySampleRateButton")!.IsEnabled);
        Click(dialog, dialog.FindControl<Button>("SampleRatePresetsButton")!);
        Assert.True(box.IsDropDownOpen);
        Layout();
        Capture(dialog, dark ? "sample-rate-presets-dark" : "sample-rate-presets-light");
        var popup = box.GetVisualDescendants().OfType<Popup>().Single();
        var list = popup.Child!.GetVisualDescendants().OfType<ListBox>().FirstOrDefault() ?? popup.Child as ListBox;
        Assert.NotNull(list);
        Assert.Contains(list!.Items.OfType<string>(), value => value == "44.1 kHz");
        box.IsDropDownOpen = false;
        box.Text = "12345"; Layout();
        Assert.Equal(12345, model.SampleRate);
        Assert.True(dialog.FindControl<Button>("ApplySampleRateButton")!.IsEnabled);
        Capture(dialog, dark ? "sample-rate-custom-dark" : "sample-rate-custom-light");
        var before = vm.Project;
        Click(dialog, dialog.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Cancel")));
        Assert.Same(before, vm.Project);
        Assert.False(vm.IsDirty);
        item.Command.Execute(null); Layout();
        dialog = Assert.IsType<SampleRateWindow>(Assert.Single(window.OwnedWindows));
        box = dialog.FindControl<AutoCompleteBox>("SampleRateBox")!;
        box.Text = "96000"; Layout();
        var converted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.PropertyChanged += (_, _) =>
        {
            if (!vm.IsBusy && vm.Project.SampleRate == 96000) converted.TrySetResult();
        };
        Click(dialog, dialog.FindControl<Button>("ApplySampleRateButton")!);
        await converted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Layout();
        Assert.Equal(96000, vm.Project.SampleRate);
        vm.New(); window.Close(); Layout();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResizeGripIsFlushWithTheFullWidthTrackBorderAndStillResizes(bool dark)
    {
        using var directory = new TempDirectory();
        using var backend = Backend();
        using var vm = Create(backend, directory);
        vm.IsDarkMode = dark;
        await vm.ImportAsync(TestProjects.WriteWave(directory.Path));
        vm.AddTrack();
        var window = new MainWindow(vm) { WindowState = WindowState.Normal };
        window.Show(); Layout();
        var headers = window.GetVisualDescendants().OfType<TrackHeaderView>().ToArray();
        var grip = headers[0].FindControl<Thumb>("TrackResizeGrip")!;
        var bounds = grip.TranslatePoint(default, headers[0])!.Value;
        Assert.Equal(0, bounds.X);
        Assert.Equal(headers[0].Bounds.Height, bounds.Y + grip.Bounds.Height);
        Assert.InRange(grip.Bounds.Width, headers[0].Bounds.Width - 2, headers[0].Bounds.Width);
        var before = vm.Project;
        var point = grip.TranslatePoint(new Point(80, 3), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseMove(point + new Vector(0, 40));
        window.MouseUp(point + new Vector(0, 40), MouseButton.Left);
        Layout();
        Assert.Equal(TimelineMetrics.TrackHeight + 40, vm.TrackHeight(vm.Project.Tracks[0].Id));
        Assert.Equal(vm.TrackTop(1), headers[1].TranslatePoint(default, headers[0])!.Value.Y);
        Assert.Same(before, vm.Project);
        Capture(window, dark ? "track-bottom-border-dark" : "track-bottom-border-light");
        vm.New(); window.Close(); Layout();
    }
}
