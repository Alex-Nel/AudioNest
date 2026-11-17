using Avalonia.Controls;
using Avalonia.Input;

namespace AudioNest.Desktop;

public partial class MainWindow
{
    private readonly List<UiCommand> _menuCommands = [];
    private readonly List<(MenuItem Item, NativeMenuItem Native, Func<bool> Checked)> _menuChecks = [];

    // The same command objects and enabled/check state drive both menu implementations.
    private void BuildMenus()
    {
        var native = new NativeMenu();
        var modifier = OperatingSystem.IsMacOS() ? "Meta" : "Ctrl";
        bool Editable() => _vm.CanEdit;
        bool Clip() => _vm.HasSelectedClip;
        bool Track() => _vm.HasSelectedTrack;

        (MenuItem Item, NativeMenu Native) Section(string header)
        {
            var item = new MenuItem { Header = header };
            var submenu = new NativeMenu();
            EditorMenuBar.Items.Add(item);
            native.Items.Add(new NativeMenuItem { Header = header, Menu = submenu });
            return (item, submenu);
        }
        void Item((MenuItem Item, NativeMenu Native) section, string header, Action action,
            Func<bool>? enabled = null, string? shortcut = null, Func<bool>? check = null)
        {
            var command = new UiCommand(action, enabled);
            _menuCommands.Add(command);
            var gesture = shortcut is null ? null : KeyGesture.Parse(shortcut);
            var item = new MenuItem { Header = header, Command = command, InputGesture = gesture };
            var nativeItem = new NativeMenuItem { Header = header, Command = command, Gesture = gesture };
            if (check is not null)
            {
                item.ToggleType = MenuItemToggleType.CheckBox;
                nativeItem.ToggleType = NativeMenuItemToggleType.CheckBox;
                _menuChecks.Add((item, nativeItem, check));
            }
            section.Item.Items.Add(item);
            section.Native.Items.Add(nativeItem);
        }
        void AsyncItem((MenuItem Item, NativeMenu Native) section, string header, Func<Task> action,
            Func<bool>? enabled = null, string? shortcut = null) =>
            Item(section, header, () => RunMenuAsync(action), enabled, shortcut);

        var file = Section("File");
        AsyncItem(file, "New project", NewProjectAsync, Editable, $"{modifier}+N");
        AsyncItem(file, "Open project…", OpenProjectAsync, Editable, $"{modifier}+O");
        AsyncItem(file, "Save", () => SaveAsync(false), Editable, $"{modifier}+S");
        AsyncItem(file, "Save as…", () => SaveAsync(true), Editable, $"{modifier}+Shift+S");
        AsyncItem(file, "Import audio…", ImportAudioAsync, Editable, $"{modifier}+I");
        AsyncItem(file, "Export WAV…", ExportAudioAsync, Editable);
        Item(file, "Close window", Close, () => !_vm.IsBusy);

        var edit = Section("Edit");
        Item(edit, "Undo", _vm.Undo, () => _vm.CanUndo, $"{modifier}+Z");
        Item(edit, "Redo", _vm.Redo, () => _vm.CanRedo, $"{modifier}+Shift+Z");
        Item(edit, "Split clip at playhead", _vm.Split, Clip);
        Item(edit, "Delete clip", _vm.Delete, Clip);
        Item(edit, "Add track", _vm.AddTrack, Editable);
        Item(edit, "Delete selected track", _vm.DeleteSelectedTrack, Track);

        var effects = Section("Effects");
        Item(effects, "Clip gain −3 dB", () => _vm.ApplyGain(-3, false), Clip);
        Item(effects, "Clip gain +3 dB", () => _vm.ApplyGain(3, false), Clip);
        Item(effects, "Clip hard limiter", () => _vm.ToggleLimiter(false), Clip,
            check: () => _vm.SelectedClip?.Effects.Any(e => e.Kind == Domain.EffectKind.HardLimiter) ?? false);
        Item(effects, "Track gain −3 dB", () => _vm.ApplyGain(-3, true), Track);
        Item(effects, "Track gain +3 dB", () => _vm.ApplyGain(3, true), Track);
        Item(effects, "Track hard limiter", () => _vm.ToggleLimiter(true), Track,
            check: () => _vm.SelectedTrack?.Effects.Any(e => e.Kind == Domain.EffectKind.HardLimiter) ?? false);

        var tools = Section("Tools");
        Item(tools, "Zoom in", () => _vm.Zoom(1.4));
        Item(tools, "Zoom out", () => _vm.Zoom(1 / 1.4));
        Item(tools, "Fit project", () => { _vm.FitProject(); TimelineScroll.Offset = default; });
        Item(tools, "Snap clips", () => _vm.IsSnappingEnabled = !_vm.IsSnappingEnabled, check: () => _vm.IsSnappingEnabled);
        Item(tools, "Dark mode", () => _vm.IsDarkMode = !_vm.IsDarkMode, check: () => _vm.IsDarkMode);

        var help = Section("Help");
        AsyncItem(help, "Keyboard shortcuts", () => ShowInformationAsync("Keyboard shortcuts",
            "Space — Play / pause\nHome / End — Go to start / end\nS — Split selected clip at playhead\nDelete — Delete selected clip\nCtrl/Cmd+Z — Undo\nCtrl/Cmd+Shift+Z or Ctrl/Cmd+Y — Redo\nCtrl/Cmd+N / O / S — New / Open / Save\nCtrl/Cmd+Shift+S — Save as\nCtrl/Cmd+I — Import audio\n\nDrag the time ruler to move the playhead.\nDrag clips to move them; Alt bypasses snapping.\nTrack-header L / C / R controls adjust stereo balance."));
        AsyncItem(help, "About AudioNest", () => ShowInformationAsync("About AudioNest",
            "AudioNest\nNon-destructive multitrack recording and editing.\n\nBuilt with .NET, Avalonia, and SoundFlow / MiniAudio.\nGain and hard limiting are included.\nAdvanced DSP and VST hosting are not yet implemented."));

        if (OperatingSystem.IsMacOS()) NativeMenu.SetMenu(this, native);
        RefreshMenus();
    }
    private void UpdateMenuExport() => EditorMenuBar.IsVisible =
        !OperatingSystem.IsMacOS() || !NativeMenu.GetIsNativeMenuExported(this);

    private void RefreshMenus()
    {
        foreach (var command in _menuCommands) command.Refresh();
        foreach (var (item, native, check) in _menuChecks) item.IsChecked = native.IsChecked = check();
    }
    private async void RunMenuAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) { _vm.ReportError(ex); }
    }
    private async Task ShowInformationAsync(string title, string text)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 470,
            Height = 440,
            MinWidth = 350,
            MinHeight = 250,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var close = new Button { Content = "Close", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
        close.Click += (_, _) => dialog.Close();
        dialog.Content = new DockPanel
        {
            Margin = new Avalonia.Thickness(20),
            Children =
            {
                close,
                new ScrollViewer { Content = new TextBlock { Text = text, TextWrapping = Avalonia.Media.TextWrapping.Wrap, LineHeight = 24 } }
            }
        };
        DockPanel.SetDock(close, Dock.Bottom);
        await dialog.ShowDialog(this);
    }
}
