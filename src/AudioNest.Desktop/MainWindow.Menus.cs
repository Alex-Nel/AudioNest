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
            item.Classes.Add("topLevel");
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
        void Separate((MenuItem Item, NativeMenu Native) section)
        {
            section.Item.Items.Add(new Separator());
            section.Native.Items.Add(new NativeMenuItemSeparator());
        }

        var file = Section("File");
        AsyncItem(file, "New project", NewProjectAsync, Editable, $"{modifier}+N");
        AsyncItem(file, "Open project…", OpenProjectAsync, Editable, $"{modifier}+O");
        Separate(file);
        AsyncItem(file, "Save", () => SaveAsync(false), Editable, $"{modifier}+S");
        AsyncItem(file, "Save as…", () => SaveAsync(true), Editable, $"{modifier}+Shift+S");
        Separate(file);
        AsyncItem(file, "Import audio…", ImportAudioAsync, Editable, $"{modifier}+I");
        AsyncItem(file, "Export…", ExportAudioAsync, Editable);
        Separate(file);
        Item(file, "Close window", Close, () => !_vm.IsBusy);

        var edit = Section("Edit");
        Item(edit, "Undo", _vm.Undo, () => _vm.CanUndo, $"{modifier}+Z");
        Item(edit, "Redo", _vm.Redo, () => _vm.CanRedo, $"{modifier}+Shift+Z");
        Separate(edit);
        Item(edit, "Cut", _vm.Cut, Clip, $"{modifier}+X");
        Item(edit, "Copy", _vm.Copy, Clip, $"{modifier}+C");
        Item(edit, "Paste", _vm.Paste, () => _vm.CanPaste, $"{modifier}+V");
        Separate(edit);
        Item(edit, "Split clip at playhead", _vm.Split, Clip);
        Item(edit, "Delete clip", _vm.Delete, Clip);
        Separate(edit);
        Item(edit, "Add stereo track", _vm.AddTrack, Editable);
        Item(edit, "Add mono track", _vm.AddMonoTrack, Editable);
        Item(edit, "Delete selected track", _vm.DeleteSelectedTrack, Track);

        var effects = Section("Effects");
        foreach (var kind in Domain.BuiltInEffects.Available)
            Item(effects, $"Add {Domain.BuiltInEffects.Name(kind)}", () => _vm.AddEffect(kind), () => _vm.HasEffectTarget);

        var tools = Section("Tools");
        Item(tools, "Selection tool", () => _vm.SetTool(TimelineTool.Selection), Editable, "V", () => _vm.IsSelectionTool);
        Item(tools, "Razor tool", () => _vm.SetTool(TimelineTool.Razor), Editable, "C", () => _vm.IsRazorTool);
        Separate(tools);
        Item(tools, "Zoom in", () => ZoomAroundPlayhead(1.4));
        Item(tools, "Zoom out", () => ZoomAroundPlayhead(1 / 1.4));
        Item(tools, "Fit project", () => { _vm.FitProject(); TimelineScroll.Offset = default; });
        Separate(tools);
        Item(tools, "Snap clips", () => _vm.IsSnappingEnabled = !_vm.IsSnappingEnabled, check: () => _vm.IsSnappingEnabled);
        Item(tools, "Dark mode", () => _vm.IsDarkMode = !_vm.IsDarkMode, check: () => _vm.IsDarkMode);

        var help = Section("Help");
        AsyncItem(help, "Keyboard shortcuts", () => ShowInformationAsync("Keyboard shortcuts",
            "Space — Play / pause\nHome / End — Go to start / end\nV / C — Selection / Razor tool\nS — Split selected clip at playhead\nDelete — Delete selected clip\nCtrl/Cmd+C / X / V — Copy / Cut / Paste clip\nCtrl/Cmd+Z — Undo\nCtrl/Cmd+Shift+Z or Ctrl/Cmd+Y — Redo\nCtrl/Cmd+N / O / S — New / Open / Save\nCtrl/Cmd+Shift+S — Save as\nCtrl/Cmd+I — Import audio\n\nLeft Alt + wheel — Zoom under mouse pointer\nToolbar magnifiers — Zoom around playhead\nLeft Shift + ruler drag — Snap to clip boundaries\nSelection tool: drag clip edges to trim, middle to move.\nRight-click clips/tracks for contextual editing.\nRight-click empty track-header space to add tracks.\nDrag the inspector or meter divider to resize it."));
        Separate(help);
        AsyncItem(help, "About AudioNest", () => ShowInformationAsync("About AudioNest",
            "AudioNest\nNon-destructive multitrack recording and editing.\n\nBuilt with .NET, Avalonia, and SoundFlow / MiniAudio.\nGain, compressor, limiter, and peak normalizer are included.\nVST hosting is not yet implemented."));

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
