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
using Avalonia.VisualTree;
using Xunit;

namespace AudioNest.Tests;

public sealed class VisualPolishTests
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
    private static void Capture(Window window, string name)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "ui-artifacts");
        Directory.CreateDirectory(directory);
        using var frame = window.CaptureRenderedFrame();
        frame!.Save(Path.Combine(directory, name + ".png"));
    }

    [AvaloniaFact]
    public async Task CompactInspectorKeepsEdgeToEdgeDividersAndRightAlignedHeaderButtonsAtEveryWidth()
    {
        using var directory = new TempDirectory();
        using var backend = new FakeBackend();
        using var vm = Create(backend, directory);
        await vm.ImportAsync(TestProjects.WriteWave(directory.Path));
        vm.Select(vm.Project.Tracks[0].Id, vm.Project.Tracks[0].Clips[0].Id);
        vm.AddClipEffect(EffectKind.Gain);
        vm.AddClipEffect(EffectKind.HardLimiter);
        var window = new MainWindow(vm) { WindowState = WindowState.Normal };
        window.Show(); Layout();
        var inspector = window.GetVisualDescendants().OfType<ClipInspectorView>().Single();
        var scroll = inspector.FindControl<ScrollViewer>("InspectorScroll")!;
        var body = window.FindControl<Grid>("EditorBody")!;
        Assert.Equal("Clip Inspector", inspector.FindControl<TextBlock>("InspectorTitle")!.Text);
        Assert.DoesNotContain(inspector.GetVisualDescendants().OfType<TextBlock>(),
            text => text.Text?.Contains("IN PROCESSING ORDER", StringComparison.OrdinalIgnoreCase) == true);
        foreach (var dark in new[] { false, true })
            foreach (var width in new[] { 200, 280, 520 })
            {
                vm.IsDarkMode = dark;
                window.Height = 480; Layout(); // Force vertical scrolling as well as narrow layouts.
                body.ColumnDefinitions[2].Width = new GridLength(width);
                Layout();
                Assert.Equal(width, inspector.Bounds.Width);
                var dividers = inspector.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("inspectorDivider")).ToArray();
                Assert.Equal(3, dividers.Length);
                Assert.All(dividers, divider =>
                {
                    Assert.Equal(5, divider.Bounds.Height);
                    Assert.Equal(scroll.Viewport.Width, divider.Bounds.Width, 4);
                    Assert.Equal(0, divider.TranslatePoint(default, inspector)!.Value.X, 4);
                });
                foreach (var name in new[] { "ClipNameEditor", "ClipStartEditor" })
                {
                    var field = inspector.FindControl<TextBox>(name)!;
                    Assert.Equal(26, field.Bounds.Height);
                    Assert.Equal(12, field.FontSize);
                }
                foreach (var name in new[] { "ClipMuteBox", "ClipLockBox" })
                    Assert.True(inspector.FindControl<CheckBox>(name)!.Bounds.Height >= 32);
                foreach (var heading in inspector.GetVisualDescendants().OfType<Grid>().Where(g => g.Classes.Contains("effectHeading")))
                {
                    var name = heading.Children.OfType<TextBlock>().Single();
                    var remove = heading.Children.OfType<Button>().Single(b => b.Classes.Contains("removeEffect"));
                    var gap = remove.TranslatePoint(default, heading)!.Value.X - name.Bounds.Right;
                    Assert.InRange(gap, 0, 8);
                    Assert.Equal(heading.Bounds.Width, remove.Bounds.Right, 4);
                    Assert.True(name.Bounds.Left >= 26);
                }
            }
        window.Height = 1000; Layout();
        body.ColumnDefinitions[2].Width = new GridLength(280); Layout();
        var firstRemove = inspector.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("removeEffect"));
        Click(window, firstRemove);
        Assert.Single(vm.SelectedClip!.Effects);
        Assert.Equal(EffectKind.HardLimiter, vm.SelectedClip.Effects[0].Kind);
        vm.Undo();
        Assert.Equal(2, vm.SelectedClip.Effects.Length);
        vm.New(); window.Close(); Layout();
    }

    [AvaloniaFact]
    public async Task GlobalCheckboxGlyphsFitWithoutChangingTextFieldSizes()
    {
        using var directory = new TempDirectory();
        using var backend = new FakeBackend();
        using var vm = Create(backend, directory);
        await vm.ImportAsync(TestProjects.WriteWave(directory.Path));
        vm.Select(vm.Project.Tracks[0].Id, vm.Project.Tracks[0].Clips[0].Id);
        var window = new MainWindow(vm) { WindowState = WindowState.Normal };
        window.Show(); Layout();
        var inspector = window.GetVisualDescendants().OfType<ClipInspectorView>().Single();
        void AssertGlyphsFit()
        {
            foreach (var name in new[] { "ClipMuteBox", "ClipLockBox" })
            {
                var checkbox = inspector.FindControl<CheckBox>(name)!;
                var rectangle = checkbox.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "NormalRectangle");
                var top = rectangle.TranslatePoint(default, checkbox)!.Value.Y;
                Assert.True(top >= 0);
                Assert.True(top + rectangle.Bounds.Height <= checkbox.Bounds.Height,
                    $"{name}: glyph bottom {top + rectangle.Bounds.Height} exceeds control height {checkbox.Bounds.Height}.");
                Assert.True(double.IsNaN(checkbox.Height)); // Let the template determine its safe height.
            }
            Assert.Equal(26, inspector.FindControl<TextBox>("ClipNameEditor")!.Bounds.Height);
            Assert.Equal(26, inspector.FindControl<TextBox>("ClipStartEditor")!.Bounds.Height);
        }
        foreach (var dark in new[] { false, true })
            foreach (var check in new[] { false, true })
            {
                vm.IsDarkMode = dark;
                vm.Inspector.Properties.Muted = check;
                vm.Inspector.Properties.Locked = check;
                Layout();
                AssertGlyphsFit();
                Capture(window, $"checkboxes-{(dark ? "dark" : "light")}-{(check ? "checked" : "unchecked")}");
            }
        // A theme with a taller indicator row must be able to grow the controls.
        inspector.Resources["CheckBoxMinHeight"] = 40d; Layout();
        AssertGlyphsFit();
        Assert.True(inspector.FindControl<CheckBox>("ClipMuteBox")!.Bounds.Height >= 40);
        vm.New(); window.Close(); Layout();
    }

    [AvaloniaFact]
    public void UnsavedDialogCentersContentAndBothDecisionsStillWork()
    {
        using var directory = new TempDirectory();
        using var backend = new FakeBackend();
        using var vm = Create(backend, directory);
        vm.AddTrack();
        var window = new MainWindow(vm) { WindowState = WindowState.Normal };
        window.Show(); Layout();
        window.Close(); Layout();
        var dialog = window.OwnedWindows.Single();
        Assert.Equal("Unsaved changes", dialog.Title);
        Assert.Equal(360, dialog.ClientSize.Width);
        Assert.Equal(160, dialog.ClientSize.Height);
        foreach (var dark in new[] { false, true })
        {
            vm.IsDarkMode = dark; Layout();
            var controls = dialog.GetVisualDescendants().OfType<Control>().ToArray();
            var content = controls.Single(c => c.Name == "DiscardDialogContent");
            var message = controls.Single(c => c.Name == "DiscardDialogMessage");
            var actions = controls.Single(c => c.Name == "DiscardDialogActions");
            Point Center(Control control) => control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), dialog)!.Value;
            // Pixel-rounded odd-sized controls can land half a pixel off center.
            Assert.InRange(Center(content).X, dialog.ClientSize.Width / 2 - 1, dialog.ClientSize.Width / 2 + 1);
            Assert.InRange(Center(content).Y, dialog.ClientSize.Height / 2 - 1, dialog.ClientSize.Height / 2 + 1);
            Assert.InRange(Center(message).X, dialog.ClientSize.Width / 2 - 1, dialog.ClientSize.Width / 2 + 1);
            Assert.InRange(Center(actions).X, dialog.ClientSize.Width / 2 - 1, dialog.ClientSize.Width / 2 + 1);
            Assert.True(message.TranslatePoint(new Point(0, message.Bounds.Height), dialog)!.Value.Y <
                actions.TranslatePoint(default, dialog)!.Value.Y);
            foreach (var button in actions.GetVisualDescendants().OfType<Button>())
            {
                var point = button.TranslatePoint(default, dialog)!.Value;
                Assert.True(point.X >= 0 && point.X + button.Bounds.Width <= dialog.ClientSize.Width);
                Assert.True(point.Y >= 0 && point.Y + button.Bounds.Height <= dialog.ClientSize.Height);
            }
            Capture(dialog, dark ? "unsaved-changes-dark" : "unsaved-changes-light");
        }
        Click(dialog, dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "KeepEditingButton"));
        Assert.True(window.IsVisible);
        Assert.True(vm.IsDirty);
        Assert.Empty(window.OwnedWindows);
        window.Close(); Layout();
        dialog = window.OwnedWindows.Single();
        Click(dialog, dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "DiscardChangesButton"));
        Assert.False(window.IsVisible);
        Assert.Empty(window.OwnedWindows);
    }
}
