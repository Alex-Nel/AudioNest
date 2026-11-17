using Avalonia.Controls;
using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace AudioNest.Desktop;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly DispatcherTimer _timer;
    private bool _allowClose;
    private bool _confirmingClose;
    private bool _synchronizingScroll;
    private double _inspectorWidth = 280;
    private double _meterWidth = 184;
    private bool _altZoomUsed;
    private bool _spacePressed;
    private static readonly FilePickerFileType ProjectType = new("AudioNest project") { Patterns = ["*.audionest"] };
    private static readonly FilePickerFileType AudioType = new("Audio files") { Patterns = ["*.wav", "*.mp3", "*.aac", "*.m4a", "*.ogg", "*.oga", "*.flac"] };

    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        DataContext = _vm = vm;
        BuildMenus();
        PropertyChanged += (_, e) =>
        {
            if (e.Property == NativeMenu.IsNativeMenuExportedProperty) UpdateMenuExport();
        };
        _vm.PropertyChanged += OnViewModelChanged;
        ApplyTheme();
        SizeChanged += (_, _) => UpdateResponsiveLayout();
        InspectorSplitter.DragCompleted += (_, _) => _inspectorWidth = EditorBody.ColumnDefinitions[2].ActualWidth;
        MeterSplitter.DragCompleted += (_, _) => _meterWidth = ToolbarGrid.ColumnDefinitions[4].ActualWidth;
        TrackHeadersPanel.ContextRequested += (_, e) =>
        {
            TrackHeadersPanel.ContextMenu?.Close();
            TrackHeadersPanel.ContextMenu = null;
            e.Handled = true;
            if (!_vm.CanEdit || !e.TryGetPosition(TrackHeadersPanel, out var point)) return;
            var row = _vm.TrackIndexAt(point.Y);
            if (row >= 0)
            {
                var track = _vm.Project.Tracks[row];
                var menu = new ContextMenu();
                menu.Items.Add(new MenuItem { Header = "Rename track…", Command = new UiCommand(() => RunMenuAsync(() => RenameTrackAsync(track.Id))) });
                menu.Items.Add(new MenuItem
                {
                    Header = "Mute track",
                    Command = new UiCommand(() => _vm.ToggleTrackMute(track.Id)),
                    ToggleType = MenuItemToggleType.CheckBox,
                    IsChecked = track.Muted
                });
                menu.Items.Add(new Separator());
                menu.Items.Add(new MenuItem { Header = "Delete track", Command = new UiCommand(() => _vm.DeleteTrack(track.Id)) });
                TrackHeadersPanel.ContextMenu = menu;
            }
            else TrackHeadersPanel.ContextMenu = TimelineContextMenus.TrackCreation(_vm);
            TrackHeadersPanel.ContextMenu.Open(TrackHeadersPanel);
        };
        TimelineArea.AddHandler(PointerWheelChangedEvent, OnTimelineWheel, RoutingStrategies.Tunnel);
        TimelineScroll.ScrollChanged += (_, _) => SynchronizeTimeline();
        RulerScroll.ScrollChanged += (_, _) =>
        {
            if (!_synchronizingScroll) TimelineScroll.Offset = new Vector(RulerScroll.Offset.X, 0);
        };
        TimelineScroll.PropertyChanged += (_, e) =>
        {
            if (e.Property == ScrollViewer.ViewportProperty || e.Property == ScrollViewer.ExtentProperty) SynchronizeTimeline();
        };
        TracksVerticalScroll.PropertyChanged += (_, e) =>
        {
            if (e.Property == ScrollViewer.ViewportProperty)
                _vm.SetTimelineViewportHeight(TracksVerticalScroll.Viewport.Height);
        };
        TimelineHorizontalBar.PropertyChanged += (_, e) =>
        {
            if (e.Property == Avalonia.Controls.Primitives.RangeBase.ValueProperty && !_synchronizingScroll)
                TimelineScroll.Offset = new Vector(TimelineHorizontalBar.Value, 0);
        };
        Opened += (_, _) => { UpdateResponsiveLayout(); SynchronizeTimeline(); UpdateMenuExport(); };
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Background, (_, _) => vm.Tick());
        _timer.Start();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnKeyUp, RoutingStrategies.Tunnel, true);
        Deactivated += (_, _) => { _vm.IsLeftAltDown = _vm.IsLeftShiftDown = false; _altZoomUsed = _spacePressed = false; };
        Closing += OnClosing;
        Closed += (_, _) => { _timer.Stop(); _vm.PropertyChanged -= OnViewModelChanged; };
    }

    private async Task RenameTrackAsync(Guid id)
    {
        var track = _vm.Project.Tracks.FirstOrDefault(t => t.Id == id);
        if (track is null) return;
        var dialog = new Window { Title = "Rename track", Width = 360, Height = 160, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var name = new TextBox { Name = "TrackNameBox", Text = track.Name, MaxLength = 120 };
        var accept = new Button { Name = "RenameTrackButton", Content = "Rename", IsDefault = true };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        accept.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(name.Text)) dialog.Close(name.Text.Trim());
        };
        cancel.Click += (_, _) => dialog.Close(null);
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 12,
            Children = { name, new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, Children = { accept, cancel } } }
        };
        dialog.Opened += (_, _) => { name.Focus(); name.SelectAll(); };
        var result = await dialog.ShowDialog<string?>(this);
        if (result is not null) _vm.RenameTrack(id, result);
    }

    private void OnViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not null) return;
        ApplyTheme();
        RefreshMenus();
    }
    private void ApplyTheme()
    {
        var theme = _vm.IsDarkMode ? ThemeVariant.Dark : ThemeVariant.Light;
        if (Avalonia.Application.Current is { } app && app.RequestedThemeVariant != theme)
            app.RequestedThemeVariant = theme;
    }
    private void UpdateResponsiveLayout()
    {
        var showInspector = Bounds.Width >= 1100;
        Inspector.IsVisible = showInspector;
        InspectorSplitter.IsVisible = showInspector;
        var column = EditorBody.ColumnDefinitions[2];
        column.MinWidth = showInspector ? 200 : 0;
        column.MaxWidth = showInspector ? Math.Min(520, Math.Max(200, Bounds.Width - 600)) : 0;
        column.Width = new GridLength(showInspector ? Math.Clamp(_inspectorWidth, column.MinWidth, column.MaxWidth) : 0);
        EditorBody.ColumnDefinitions[1].Width = new GridLength(showInspector ? 5 : 0);
        EditorBody.ColumnDefinitions[0].MinWidth = showInspector ? 560 : 0;
        var meters = ToolbarGrid.ColumnDefinitions[4];
        meters.MinWidth = 140;
        meters.MaxWidth = Math.Max(140, Math.Min(360, Bounds.Width - 500));
        meters.Width = new GridLength(Math.Clamp(_meterWidth, meters.MinWidth, meters.MaxWidth));
        ToolbarGrid.ColumnDefinitions[2].MinWidth = 80;
    }

    public void ZoomAroundPlayhead(double multiplier)
    {
        var frame = _vm.Position;
        var screenX = (double)frame / _vm.Project.SampleRate * _vm.PixelsPerSecond - TimelineScroll.Offset.X;
        if (screenX < 0 || screenX > TimelineScroll.Viewport.Width) screenX = TimelineScroll.Viewport.Width / 2;
        ZoomAroundFrame(multiplier, frame, screenX);
    }
    private void ZoomAroundFrame(double multiplier, double frame, double screenX)
    {
        _vm.Zoom(multiplier);
        // Apply the new extent before assigning Offset, otherwise ScrollViewer
        // would clamp the requested anchor against the previous zoom's extent.
        UpdateLayout();
        var offset = (double)frame / _vm.Project.SampleRate * _vm.PixelsPerSecond - screenX;
        TimelineScroll.Offset = new Vector(Math.Max(0, offset), 0);
        SynchronizeTimeline();
    }
    private void OnTimelineWheel(object? sender, PointerWheelEventArgs e)
    {
        if (_vm.IsLeftAltDown && e.Delta.Y != 0)
        {
            var screenX = Math.Clamp(e.GetPosition(TimelineScroll).X, 0, TimelineScroll.Viewport.Width);
            var frame = (TimelineScroll.Offset.X + screenX) / _vm.PixelsPerSecond * _vm.Project.SampleRate;
            ZoomAroundFrame(Math.Pow(1.2, Math.Clamp(e.Delta.Y, -8, 8)), frame, screenX);
            _altZoomUsed = true;
        }
        else
        {
            // Hidden synchronized scroll viewers do not provide native wheel
            // fallback. Scroll tracks vertically when possible, otherwise pan time.
            var vertical = Math.Max(0, TracksVerticalScroll.Extent.Height - TracksVerticalScroll.Viewport.Height);
            var overRuler = e.GetPosition(TimelineArea).Y < TimelineMetrics.RulerHeight;
            if (e.Delta.X == 0 && vertical > 0 && !overRuler && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                TracksVerticalScroll.Offset = new Vector(0, Math.Clamp(TracksVerticalScroll.Offset.Y - e.Delta.Y * 48, 0, vertical));
            else
            {
                var delta = e.Delta.X != 0 ? e.Delta.X : e.Delta.Y;
                var maximum = Math.Max(0, TimelineScroll.Extent.Width - TimelineScroll.Viewport.Width);
                TimelineScroll.Offset = new Vector(Math.Clamp(TimelineScroll.Offset.X - delta * 48, 0, maximum), 0);
            }
        }
        e.Handled = true;
    }
    private static bool LeftAlt(KeyEventArgs e) => e.PhysicalKey == PhysicalKey.None
        ? e.Key == Key.LeftAlt : e.PhysicalKey == PhysicalKey.AltLeft;
    private static bool LeftShift(KeyEventArgs e) => e.PhysicalKey == PhysicalKey.None
        ? e.Key == Key.LeftShift : e.PhysicalKey == PhysicalKey.ShiftLeft;
    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            _spacePressed = false;
            // Buttons activate on key-up, even if key-down was handled by transport.
            if (!IsTextInput(e.Source)) e.Handled = true;
        }
        if (LeftAlt(e))
        {
            _vm.IsLeftAltDown = false;
            if (_altZoomUsed) e.Handled = true;
            _altZoomUsed = false;
        }
        if (LeftShift(e)) _vm.IsLeftShiftDown = false;
    }
    private static bool IsTextInput(object? source) => source is TextBox ||
        source is Visual visual && visual.GetVisualAncestors().Any(v => v is TextBox);
    private void SynchronizeTimeline()
    {
        if (_synchronizingScroll) return;
        _synchronizingScroll = true;
        try
        {
            var width = TimelineScroll.Viewport.Width;
            _vm.SetTimelineViewportWidth(width);
            RulerScroll.Width = Math.Max(0, width);
            RulerScroll.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
            RulerScroll.Offset = new Vector(TimelineScroll.Offset.X, 0);
            TimelineHorizontalBar.Maximum = Math.Max(0, TimelineScroll.Extent.Width - width);
            TimelineHorizontalBar.ViewportSize = width;
            TimelineHorizontalBar.LargeChange = Math.Max(1, width * 0.9);
            TimelineHorizontalBar.SmallChange = 50;
            TimelineHorizontalBar.Value = TimelineScroll.Offset.X;
            TimelineHorizontalBar.IsVisible = TimelineHorizontalBar.Maximum > 0.5;
        }
        finally { _synchronizingScroll = false; }
    }

    private async Task<bool> ConfirmDiscardAsync()
    {
        if (!_vm.IsDirty) return true;
        var dialog = new Window { Title = "Unsaved changes", Width = 390, Height = 175, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var cancel = new Button { Content = "Keep editing" };
        var discard = new Button { Content = "Discard changes" };
        cancel.Click += (_, _) => dialog.Close(false);
        discard.Click += (_, _) => dialog.Close(true);
        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(24),
            Spacing = 20,
            Children =
            {
                new TextBlock { Text = "Discard unsaved project changes?" },
                new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 12, Children = { cancel, discard } }
            }
        };
        return await dialog.ShowDialog<bool>(this);
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        if (_confirmingClose) return;
        _confirmingClose = true;
        // Never re-enter Close inside the original Closing callback, even when the
        // confirmation task completes synchronously (e.g. a clean new project).
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                if (_vm.IsBusy) return;
                if (_vm.IsRecording) await _vm.StopAsync();
                if (await ConfirmDiscardAsync()) { _allowClose = true; Close(); }
            }
            finally { _confirmingClose = false; }
        });
    }

    private async Task<string?> PickOpenAsync(FilePickerFileType type)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        { AllowMultiple = false, FileTypeFilter = [type] });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }
    private async Task<string?> PickSaveAsync(FilePickerFileType type, string name, string extension)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        { SuggestedFileName = name, DefaultExtension = extension, FileTypeChoices = [type], ShowOverwritePrompt = true });
        return file?.TryGetLocalPath();
    }
    private async Task SaveAsync(bool saveAs)
    {
        var path = !saveAs ? _vm.ProjectPath : null;
        path ??= await PickSaveAsync(ProjectType, "Untitled.audionest", "audionest");
        if (path is not null) await _vm.SaveAsync(path);
    }

    private async Task NewProjectAsync() { if (_vm.CanEdit && await ConfirmDiscardAsync()) _vm.New(); }
    private async Task OpenProjectAsync()
    {
        if (!await ConfirmDiscardAsync()) return;
        var path = await PickOpenAsync(ProjectType);
        if (path is not null) await _vm.OpenAsync(path);
    }
    private async void ImportClick(object? sender, RoutedEventArgs e) => await ImportAudioAsync();
    private async Task ImportAudioAsync()
    {
        var path = await PickOpenAsync(AudioType);
        if (path is not null) await _vm.ImportAsync(path);
    }
    private async Task ExportAudioAsync()
    {
        var request = await new ExportOptionsWindow(_vm.Project.Duration > 0).ShowDialog<ExportRequest?>(this);
        if (request is not null) await _vm.ExportAsync(request.Path, request.Format);
    }
    private void PlayClick(object? sender, RoutedEventArgs e) => _vm.StartPlayback();
    private void PauseClick(object? sender, RoutedEventArgs e) => _vm.Pause();
    private void SkipBackClick(object? sender, RoutedEventArgs e) => _vm.SkipToStart();
    private void SkipForwardClick(object? sender, RoutedEventArgs e) => _vm.SkipToEnd();
    private async void StopClick(object? sender, RoutedEventArgs e) => await _vm.StopAsync();
    private void RecordClick(object? sender, RoutedEventArgs e) => _vm.Record();
    private void AddTrackClick(object? sender, RoutedEventArgs e) => _vm.AddTrack();
    private void UndoClick(object? sender, RoutedEventArgs e) => _vm.Undo();
    private void RedoClick(object? sender, RoutedEventArgs e) => _vm.Redo();
    private void SplitClick(object? sender, RoutedEventArgs e) => _vm.Split();
    private void DeleteClick(object? sender, RoutedEventArgs e) => _vm.Delete();
    private void ZoomInClick(object? sender, RoutedEventArgs e) => ZoomAroundPlayhead(1.4);
    private void ZoomOutClick(object? sender, RoutedEventArgs e) => ZoomAroundPlayhead(1 / 1.4);
    private void FitClick(object? sender, RoutedEventArgs e) { _vm.FitProject(); TimelineScroll.Offset = default; }
    private void ClipQuieterClick(object? sender, RoutedEventArgs e) => _vm.ApplyGain(-3, false);
    private void ClipLouderClick(object? sender, RoutedEventArgs e) => _vm.ApplyGain(3, false);
    private void ClipLimiterClick(object? sender, RoutedEventArgs e) => _vm.ToggleLimiter(false);
    private void TrackLimiterClick(object? sender, RoutedEventArgs e) => _vm.ToggleLimiter(true);
    private void NudgeLeftClick(object? sender, RoutedEventArgs e) => _vm.Nudge(-0.1);
    private void NudgeRightClick(object? sender, RoutedEventArgs e) => _vm.Nudge(0.1);

    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (LeftAlt(e)) _vm.IsLeftAltDown = true;
        if (LeftShift(e)) _vm.IsLeftShiftDown = true;
        if (e.Key == Key.Space && !IsTextInput(e.Source))
        {
            e.Handled = true;
            if (!_spacePressed && e.KeyModifiers == KeyModifiers.None && _vm.CanEdit) _vm.Play();
            _spacePressed = true;
            return;
        }
        if (e.Handled || IsTextInput(e.Source) || !_vm.CanEdit) return;
        var control = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        if (control && e.Key == Key.S) await SaveAsync(e.KeyModifiers.HasFlag(KeyModifiers.Shift));
        else if (control && e.Key == Key.N) await NewProjectAsync();
        else if (control && e.Key == Key.O) await OpenProjectAsync();
        else if (control && e.Key == Key.I) await ImportAudioAsync();
        else if (control && e.Key == Key.C) _vm.Copy();
        else if (control && e.Key == Key.X) _vm.Cut();
        else if (control && e.Key == Key.V) _vm.Paste();
        else if (control && e.Key == Key.Z)
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) _vm.Redo(); else _vm.Undo();
        }
        else if (control && e.Key == Key.Y) _vm.Redo();
        else if (e.Key == Key.S && !control) _vm.Split();
        else if (e.Key == Key.V && !control) _vm.SetTool(TimelineTool.Selection);
        else if (e.Key == Key.C && !control) _vm.SetTool(TimelineTool.Razor);
        else if (e.Key is Key.Delete or Key.Back) _vm.Delete();
        else if (e.Key == Key.Home) _vm.Seek(0);
        else if (e.Key == Key.End) _vm.SkipToEnd();
        else return;
        e.Handled = true;
    }
}
