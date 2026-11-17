using AudioNest.Application;
using AudioNest.Audio;
using AudioNest.Desktop;
using AudioNest.Persistence;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(AudioNest.Tests.TestAppBuilder))]

namespace AudioNest.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

public sealed class DesktopTests
{
    private static MainViewModel Create(FakeBackend backend, string workspace) => new(new ProjectSession(),
        new AudioLibrary(backend), new AudioTransport(backend), backend, new JsonProjectStore(), workspace);

    [AvaloniaFact]
    public async Task WindowAndFullEditingWorkflowWorkWithHeadlessBackend()
    {
        using var directory = new TempDirectory();
        var source = TestProjects.WriteWave(directory.Path);
        using var backend = new FakeBackend();
        using var vm = Create(backend, Path.Combine(directory.Path, "workspace"));
        var window = new MainWindow(vm) { WindowState = Avalonia.Controls.WindowState.Normal };
        window.Show();
        Assert.True(window.IsVisible);
        await vm.ImportAsync(source);
        Assert.DoesNotContain("Error:", vm.Status);
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using (var frame = window.CaptureRenderedFrame())
        {
            Assert.NotNull(frame);
            Assert.True(frame.PixelSize.Width >= 1000);
        }
        var clip = vm.Project.Tracks[0].Clips[0];
        vm.Select(vm.Project.Tracks[0].Id, clip.Id);
        vm.Seek(5);
        vm.Split();
        Assert.Equal(2, vm.Project.Tracks[0].Clips.Length);
        vm.ApplyGain(-3, false);
        vm.ToggleLimiter(false);
        vm.Undo();
        vm.Redo();
        var path = Path.Combine(directory.Path, "session.audionest");
        await vm.SaveAsync(path);
        Assert.False(vm.IsDirty);
        await vm.ExportAsync(Path.Combine(directory.Path, "mix.wav"));
        Assert.True(File.Exists(Path.Combine(directory.Path, "mix.wav")));
        vm.New();
        await vm.OpenAsync(path);
        Assert.Equal(2, vm.Project.Tracks[0].Clips.Length);
        Assert.False(vm.IsDirty);
        window.Close();
        Dispatcher.UIThread.RunJobs();
        Assert.False(window.IsVisible);
    }

    [AvaloniaFact]
    public async Task FailedOpenPreservesCurrentProjectAndRecordingFinalizesOnce()
    {
        using var directory = new TempDirectory();
        using var backend = new FakeBackend();
        using var vm = Create(backend, Path.Combine(directory.Path, "workspace"));
        var source = TestProjects.WriteWave(directory.Path);
        await vm.ImportAsync(source);
        var project = vm.Project;
        var path = Path.Combine(directory.Path, "session.audionest");
        await vm.SaveAsync(path);
        backend.FailDecode = true;
        await vm.OpenAsync(path);
        Assert.Same(project, vm.Project);
        Assert.Contains("Error:", vm.Status);
        backend.FailDecode = false;
        vm.Record();
        Assert.True(vm.IsRecording);
        Assert.False(vm.CanEdit);
        vm.AddTrack();
        Assert.Single(vm.Project.Tracks);
        await vm.StopAsync();
        Assert.False(vm.IsRecording);
        Assert.Equal(1, backend.RecordingStops);
        Assert.Equal(2, vm.Project.Tracks[0].Clips.Length);
        await vm.StopAsync();
        Assert.Equal(1, backend.RecordingStops);
    }
}
