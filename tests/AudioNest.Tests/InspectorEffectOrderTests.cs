using AudioNest.Application;
using AudioNest.Audio;
using AudioNest.Desktop;
using AudioNest.Domain;
using AudioNest.Persistence;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace AudioNest.Tests;

public sealed class InspectorEffectOrderTests
{
    private static MainViewModel Create(FakeBackend backend, TempDirectory directory) =>
        new(new ProjectSession(), new AudioLibrary(backend), new AudioTransport(backend), backend,
            new JsonProjectStore(), Path.Combine(directory.Path, "workspace"));
    private static async Task Setup(MainViewModel vm, TempDirectory directory, bool track)
    {
        await vm.ImportAsync(TestProjects.WriteWave(directory.Path));
        vm.Select(vm.Project.Tracks[0].Id, track ? null : vm.Project.Tracks[0].Clips[0].Id);
        vm.AddEffect(EffectKind.Gain);
        ((GainEffectEditorViewModel)vm.Inspector.Effects[0]).Value = 2;
        vm.AddEffect(EffectKind.HardLimiter);
        ((LimiterEffectEditorViewModel)vm.Inspector.Effects[1]).Value = 0.2;
        vm.AddEffect(EffectKind.Gain);
        ((GainEffectEditorViewModel)vm.Inspector.Effects[2]).Value = 0.5;
    }
    private static EffectSpec[] Chain(MainViewModel vm, bool track) =>
        (track ? vm.SelectedTrack!.Effects : vm.SelectedClip!.Effects).ToArray();
    private static void Layout() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
    private static ClipInspectorView Inspector(Window window) => window.GetVisualDescendants().OfType<ClipInspectorView>().Single();
    private static Border Header(Window window, ClipEffectEditorViewModel editor) => Inspector(window).GetVisualDescendants()
        .OfType<Border>().Single(b => b.Classes.Contains("effectHeader") && b.DataContext == editor);
    private static Point PointAt(Control control, Window window, double x = 60, double? y = null) =>
        control.TranslatePoint(new Point(x, y ?? control.Bounds.Height / 2), window)!.Value;
    private static void Click(Window window, Control control)
    {
        var point = PointAt(control, window, control.Bounds.Width / 2);
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
    private static void Close(Window window, MainViewModel vm) { vm.New(); window.Close(); Layout(); }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task HeadersStayVisibleWhileArrowCollapsesPropertiesWithoutDirtyingTheProject(bool track, bool dark)
    {
        using var directory = new TempDirectory();
        using var backend = new FakeBackend { DecodedFrames = 48000 };
        using var vm = Create(backend, directory);
        await Setup(vm, directory, track);
        await vm.SaveAsync(Path.Combine(directory.Path, "clean.audionest"));
        vm.IsDarkMode = dark;
        var window = new MainWindow(vm) { WindowState = WindowState.Normal, Height = 1000 };
        window.Show(); Layout();
        Assert.All(vm.Inspector.Effects, e => Assert.True(e.IsExpanded));
        var editor = vm.Inspector.Effects[0];
        var header = Header(window, editor);
        var arrow = header.GetVisualDescendants().OfType<ToggleButton>().Single();
        var remove = header.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("removeEffect"));
        var body = Inspector(window).GetVisualDescendants().OfType<StackPanel>()
            .Single(p => p.Classes.Contains("effectBody") && p.DataContext == editor);
        var before = vm.Project;
        Click(window, arrow);
        Assert.False(editor.IsExpanded);
        Assert.False(body.IsVisible);
        Assert.True(header.IsVisible);
        Assert.True(remove.IsVisible);
        Assert.Equal("▸", arrow.Content);
        Assert.Same(before, vm.Project);
        Assert.False(vm.IsDirty);
        Click(window, header); // The title/header is a drag surface, not a collapse button.
        Assert.False(editor.IsExpanded);
        Click(window, arrow);
        Assert.True(editor.IsExpanded);
        Assert.True(body.IsVisible);
        Assert.False(vm.IsDirty);
        editor.IsExpanded = false;
        ((GainEffectEditorViewModel)editor).Value = 3;
        Assert.Same(editor, vm.Inspector.Effects[0]);
        Assert.False(editor.IsExpanded);
        vm.Undo();
        Assert.False(vm.IsDirty);
        Assert.False(editor.IsExpanded);
        foreach (var effect in vm.Inspector.Effects.Skip(1)) effect.IsExpanded = false;
        Layout();
        Capture(window, $"effect-headers-{(track ? "track" : "clip")}-{(dark ? "dark" : "light")}");
        Click(window, remove); // Removal remains available when collapsed.
        Assert.Equal(2, vm.Inspector.Effects.Count);
        vm.Undo();
        Assert.Same(editor, vm.Inspector.Effects[0]);
        Assert.False(editor.IsExpanded);
        Close(window, vm);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DuplicateEffectsKeepTheirEditorsParametersAndCollapseStateThroughReorderUndoAndRemoval(bool track)
    {
        using var directory = new TempDirectory();
        using var backend = new FakeBackend { DecodedFrames = 48000 };
        using var vm = Create(backend, directory);
        await Setup(vm, directory, track);
        var original = Chain(vm, track);
        var editors = vm.Inspector.Effects.ToArray();
        editors[0].IsExpanded = false;
        editors[0].MoveTo(2);
        Assert.Equal(new[] { original[1], original[2], original[0] }, Chain(vm, track));
        Assert.Equal(new[] { editors[1], editors[2], editors[0] }, vm.Inspector.Effects.ToArray());
        Assert.Equal(new[] { 0, 1, 2 }, vm.Inspector.Effects.Select(e => e.Index));
        Assert.False(editors[0].IsExpanded);
        Assert.True(editors[2].IsExpanded);
        vm.Undo();
        Assert.Equal(original, Chain(vm, track));
        Assert.Equal(editors, vm.Inspector.Effects.ToArray());
        vm.Redo();
        Assert.Same(editors[0], vm.Inspector.Effects[2]);
        ((GainEffectEditorViewModel)editors[0]).Value = 3;
        Assert.Equal(3, Chain(vm, track)[2].Value);
        Assert.Equal(0.5f, Chain(vm, track)[1].Value);
        editors[0].Remove.Execute(null);
        Assert.Equal(2, Chain(vm, track).Length);
        Assert.Equal(0.5f, Chain(vm, track)[1].Value);
        vm.Undo();
        Assert.Same(editors[0], vm.Inspector.Effects[2]);
        Assert.False(editors[0].IsExpanded);
        var before = vm.Project;
        editors[0].MoveTo(2);
        editors[0].MoveTo(-1);
        editors[0].MoveTo(50);
        Assert.Same(before, vm.Project);
    }

    [AvaloniaTheory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task HeaderDragReordersExpandedAndCollapsedEffectsWithOneCommit(bool track, bool collapsed, bool upward)
    {
        using var directory = new TempDirectory();
        using var backend = new FakeBackend { DecodedFrames = 48000 };
        using var vm = Create(backend, directory);
        await Setup(vm, directory, track);
        foreach (var effect in vm.Inspector.Effects) effect.IsExpanded = !collapsed;
        var window = new MainWindow(vm) { WindowState = WindowState.Normal, Height = 1200 };
        window.Show(); Layout();
        var editors = vm.Inspector.Effects.ToArray();
        var from = upward ? 2 : 0;
        var to = upward ? 0 : 2;
        var source = Header(window, editors[from]);
        var target = Header(window, editors[to]);
        var press = PointAt(source, window);
        var release = PointAt(target, window, y: upward ? 2 : target.Bounds.Height - 2);
        var before = vm.Project;
        window.MouseDown(press, MouseButton.Left);
        window.MouseMove(release);
        Layout();
        Assert.Same(before, vm.Project);
        Assert.True(editors[from].IsReordering);
        Assert.True(Inspector(window).FindControl<Border>("EffectDropIndicator")!.IsVisible);
        if (track && collapsed && !upward) Capture(window, "effect-reorder-insertion-guide");
        window.MouseUp(release, MouseButton.Left);
        Layout();
        Assert.Same(editors[from], vm.Inspector.Effects[to]);
        Assert.Equal(!collapsed, editors[from].IsExpanded);
        Assert.False(editors[from].IsReordering);
        Assert.False(Inspector(window).FindControl<Border>("EffectDropIndicator")!.IsVisible);
        vm.Undo();
        Assert.Same(before, vm.Project);
        Assert.Equal(editors, vm.Inspector.Effects.ToArray());
        vm.Redo();
        Assert.Same(editors[from], vm.Inspector.Effects[to]);
        Close(window, vm);
    }

    [AvaloniaTheory]
    [InlineData("escape")]
    [InlineData("outside")]
    [InlineData("capture")]
    [InlineData("selection")]
    [InlineData("parameter")]
    public async Task CancelledOrStaleHeaderDragsCannotReorderEffects(string reason)
    {
        using var directory = new TempDirectory();
        using var backend = new FakeBackend { DecodedFrames = 48000 };
        using var vm = Create(backend, directory);
        await Setup(vm, directory, false);
        foreach (var effect in vm.Inspector.Effects) effect.IsExpanded = false;
        var window = new MainWindow(vm) { WindowState = WindowState.Normal, Height = 1000 };
        window.Show(); Layout();
        var inspector = Inspector(window);
        IPointer? pointer = null;
        inspector.AddHandler(InputElement.PointerPressedEvent, (_, e) => pointer = e.Pointer, RoutingStrategies.Tunnel);
        var editor = vm.Inspector.Effects[0];
        var press = PointAt(Header(window, editor), window);
        var target = Header(window, vm.Inspector.Effects[2]);
        var release = PointAt(target, window, y: target.Bounds.Height - 2);
        var before = vm.Project;
        window.MouseDown(press, MouseButton.Left);
        window.MouseMove(release); Layout();
        Assert.True(editor.IsReordering);
        if (reason == "escape") window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        else if (reason == "capture") pointer!.Capture(null);
        else if (reason == "selection") vm.Select(vm.Project.Tracks[0].Id, null);
        else if (reason == "parameter") ((GainEffectEditorViewModel)editor).Value = 3;
        else release = inspector.TranslatePoint(new Point(-40, 450), window)!.Value;
        window.MouseUp(release, MouseButton.Left);
        Layout();
        Assert.False(editor.IsReordering);
        Assert.False(inspector.FindControl<Border>("EffectDropIndicator")!.IsVisible);
        if (reason != "parameter") Assert.Same(before, vm.Project);
        Assert.Equal(new[] { EffectKind.Gain, EffectKind.HardLimiter, EffectKind.Gain },
            vm.Project.Tracks[0].Clips[0].Effects.Select(e => e.Kind));
        Close(window, vm);
    }

    [AvaloniaFact]
    public async Task ArrowAndRemoveButtonsNeverStartHeaderReordering()
    {
        using var directory = new TempDirectory();
        using var backend = new FakeBackend { DecodedFrames = 48000 };
        using var vm = Create(backend, directory);
        await Setup(vm, directory, false);
        foreach (var effect in vm.Inspector.Effects) effect.IsExpanded = false;
        var window = new MainWindow(vm) { WindowState = WindowState.Normal, Height = 1000 };
        window.Show(); Layout();
        var original = vm.Project;
        var editor = vm.Inspector.Effects[0];
        var header = Header(window, editor);
        foreach (var button in header.GetVisualDescendants().OfType<Button>())
        {
            var press = PointAt(button, window, button.Bounds.Width / 2);
            window.MouseDown(press, MouseButton.Left);
            window.MouseMove(press + new Vector(0, 100)); Layout();
            Assert.False(editor.IsReordering);
            window.MouseUp(press + new Vector(0, 100), MouseButton.Left);
        }
        Assert.Same(original, vm.Project);
        Assert.Equal(3, vm.Inspector.Effects.Count);
        Close(window, vm);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReorderingChangesPlaybackAndWaveOutputWithoutStoppingOrReopeningTheDevice(bool track)
    {
        using var directory = new TempDirectory();
        using var backend = new FakeBackend { DecodedFrames = 48000 };
        using var vm = Create(backend, directory);
        await Setup(vm, directory, track);
        vm.StartPlayback();
        var output = backend.Output!;
        Assert.InRange(output.Pull(1000).Max(s => Math.Abs(s)), 0.0999f, 0.1001f);
        var position = vm.Position;
        var stops = output.Stops;
        var opens = backend.OutputOpens;
        vm.Inspector.Effects[0].MoveTo(2);
        Assert.True(vm.CanPause);
        Assert.Equal(position, vm.Position);
        Assert.Equal(stops, output.Stops);
        Assert.Equal(opens, backend.OutputOpens);
        var samples = new float[2000];
        var before = GC.GetAllocatedBytesForCurrentThread();
        output.Pull(samples);
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
        Assert.InRange(samples.Max(s => Math.Abs(s)), 0.1999f, 0.2001f);
        var path = Path.Combine(directory.Path, "reordered.wav");
        await vm.ExportAsync(path);
        var bytes = File.ReadAllBytes(path);
        var expected = new float[96000];
        new RenderPlan(vm.Project, vm.Buffer).Render(0, expected);
        for (var i = 0; i < expected.Length; i++)
            Assert.InRange(Math.Abs(expected[i] - BitConverter.ToInt16(bytes, 44 + i * 2) / 32768f), 0, 0.00004);
        var saved = Chain(vm, track);
        await vm.SaveAsync(Path.Combine(directory.Path, "order.audionest"));
        await vm.OpenAsync(vm.ProjectPath!);
        vm.Select(vm.Project.Tracks[0].Id, track ? null : vm.Project.Tracks[0].Clips[0].Id);
        Assert.Equal(saved, Chain(vm, track));
        Assert.All(vm.Inspector.Effects, e => Assert.True(e.IsExpanded));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReorderingBeforeNormalizerReanalyzesItsActualNewPrefix(bool track)
    {
        using var directory = new TempDirectory();
        using var backend = new FakeBackend { DecodedFrames = 48000 };
        using var vm = Create(backend, directory);
        await vm.ImportAsync(TestProjects.WriteWave(directory.Path));
        vm.Select(vm.Project.Tracks[0].Id, track ? null : vm.Project.Tracks[0].Clips[0].Id);
        vm.AddEffect(EffectKind.Gain);
        ((GainEffectEditorViewModel)vm.Inspector.Effects[0]).Value = 2;
        vm.AddEffect(EffectKind.Normalizer);
        ((NormalizerEffectEditorViewModel)vm.Inspector.Effects[1]).Value = 0.4;
        var samples = new float[96000];
        new RenderPlan(vm.Project, vm.Buffer).Render(0, samples);
        Assert.InRange(samples.Max(s => Math.Abs(s)), 0.3999f, 0.4001f);
        vm.Inspector.Effects[0].MoveTo(1);
        new RenderPlan(vm.Project, vm.Buffer).Render(0, samples);
        Assert.InRange(samples.Max(s => Math.Abs(s)), 0.7999f, 0.8001f);
    }

    [Fact]
    public async Task CollapseStateIsTargetSpecificAndStaleEditorsCannotReorderAnotherTarget()
    {
        using var directory = new TempDirectory();
        using var backend = new FakeBackend { DecodedFrames = 48000 };
        using var vm = Create(backend, directory);
        await Setup(vm, directory, false);
        var clip = vm.SelectedClip!;
        var clipEditor = vm.Inspector.Effects[0];
        clipEditor.IsExpanded = false;
        vm.Select(vm.Project.Tracks[0].Id, null);
        vm.AddEffect(EffectKind.Gain);
        vm.AddEffect(EffectKind.Limiter);
        var before = vm.Project;
        clipEditor.MoveTo(1);
        Assert.Same(before, vm.Project);
        Assert.All(vm.Inspector.Effects, e => Assert.True(e.IsExpanded));
        vm.Select(vm.Project.Tracks[0].Id, clip.Id);
        Assert.Same(clipEditor, vm.Inspector.Effects[0]);
        Assert.False(clipEditor.IsExpanded);
        vm.ClearSelection();
        Assert.Empty(vm.Inspector.Effects);
        clipEditor.MoveTo(1);
        Assert.Same(before, vm.Project);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReorderCancelsParameterPreviewsWithoutCommittingThemAtTheNewIndex(bool track)
    {
        using var directory = new TempDirectory();
        using var backend = new FakeBackend { DecodedFrames = 48000 };
        using var vm = Create(backend, directory);
        await Setup(vm, directory, track);
        var before = vm.Project;
        var gain = (GainEffectEditorViewModel)vm.Inspector.Effects[0];
        gain.BeginAdjustment();
        gain.Value = 3;
        Assert.Same(before, vm.Project);
        gain.MoveTo(2);
        gain.EndAdjustment(); // A queued slider release must not commit stale values.
        Assert.Equal(2, Chain(vm, track)[2].Value);
        Assert.Equal(2, gain.Value);
        vm.Undo();
        Assert.Same(before, vm.Project);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedNormalizerPreparationDoesNotPublishAReorder(bool track)
    {
        using var directory = new TempDirectory();
        using var backend = new FakeBackend
        {
            DecodeAudio = (_, format) => new(format, Enumerable.Repeat(float.MaxValue, 2000).ToArray())
        };
        using var vm = Create(backend, directory);
        await vm.ImportAsync(TestProjects.WriteWave(directory.Path));
        vm.Select(vm.Project.Tracks[0].Id, track ? null : vm.Project.Tracks[0].Clips[0].Id);
        vm.AddEffect(EffectKind.Normalizer);
        vm.AddEffect(EffectKind.Gain);
        ((GainEffectEditorViewModel)vm.Inspector.Effects[1]).Value = 16;
        var editor = vm.Inspector.Effects[0];
        editor.IsExpanded = false;
        var before = vm.Project;
        editor.MoveTo(1);
        Assert.Same(before, vm.Project);
        Assert.StartsWith("Error:", vm.Status);
        Assert.Same(editor, vm.Inspector.Effects[0]);
        Assert.False(editor.IsExpanded);
        vm.Undo();
        Assert.Equal(1, Chain(vm, track)[1].Value);
    }

    [AvaloniaFact]
    public async Task HeaderDragAutoScrollsLongChainsAndEscapeCancelsWithoutAnEdit()
    {
        using var directory = new TempDirectory();
        using var backend = new FakeBackend { DecodedFrames = 48000 };
        using var vm = Create(backend, directory);
        await Setup(vm, directory, false);
        for (var i = 0; i < 9; i++) vm.AddEffect(EffectKind.Gain);
        foreach (var editor in vm.Inspector.Effects) editor.IsExpanded = false;
        var window = new MainWindow(vm) { WindowState = WindowState.Normal, Height = 800 };
        window.Show(); Layout();
        var inspector = Inspector(window);
        var scroll = inspector.FindControl<ScrollViewer>("InspectorScroll")!;
        var editorToDrag = vm.Inspector.Effects[0];
        var press = PointAt(Header(window, editorToDrag), window);
        var release = inspector.TranslatePoint(new Point(60, inspector.Bounds.Height - 8), window)!.Value;
        var before = vm.Project;
        window.MouseDown(press, MouseButton.Left);
        window.MouseMove(release);
        await Task.Delay(200);
        Layout();
        Assert.True(editorToDrag.IsReordering);
        Assert.True(scroll.Offset.Y > 0);
        window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        window.MouseUp(release, MouseButton.Left);
        Assert.Same(before, vm.Project);
        Assert.False(editorToDrag.IsReordering);
        Close(window, vm);
    }
}
