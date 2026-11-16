using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace AudioNest.Desktop;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly DispatcherTimer _timer;
    private bool _allowClose;
    private bool _confirmingClose;
    private static readonly FilePickerFileType ProjectType = new("AudioNest project") { Patterns = ["*.audionest"] };
    private static readonly FilePickerFileType AudioType = new("Audio files") { Patterns = ["*.wav", "*.mp3", "*.flac"] };
    private static readonly FilePickerFileType WaveType = new("WAV audio") { Patterns = ["*.wav"] };

    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        DataContext = _vm = vm;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Background, (_, _) => vm.Tick());
        _timer.Start();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        Closing += OnClosing;
        Closed += (_, _) => _timer.Stop();
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

    private async void NewClick(object? sender, RoutedEventArgs e) { if (await ConfirmDiscardAsync()) _vm.New(); }
    private async void OpenClick(object? sender, RoutedEventArgs e)
    {
        if (!await ConfirmDiscardAsync()) return;
        var path = await PickOpenAsync(ProjectType);
        if (path is not null) await _vm.OpenAsync(path);
    }
    private async void SaveClick(object? sender, RoutedEventArgs e) => await SaveAsync(false);
    private async void SaveAsClick(object? sender, RoutedEventArgs e) => await SaveAsync(true);
    private async void ImportClick(object? sender, RoutedEventArgs e)
    {
        var path = await PickOpenAsync(AudioType);
        if (path is not null) await _vm.ImportAsync(path);
    }
    private async void ExportClick(object? sender, RoutedEventArgs e)
    {
        var path = await PickSaveAsync(WaveType, "Mixdown.wav", "wav");
        if (path is not null) await _vm.ExportAsync(path);
    }
    private void PlayClick(object? sender, RoutedEventArgs e) => _vm.Play();
    private async void StopClick(object? sender, RoutedEventArgs e) => await _vm.StopAsync();
    private void RecordClick(object? sender, RoutedEventArgs e) => _vm.Record();
    private void AddTrackClick(object? sender, RoutedEventArgs e) => _vm.AddTrack();
    private void UndoClick(object? sender, RoutedEventArgs e) => _vm.Undo();
    private void RedoClick(object? sender, RoutedEventArgs e) => _vm.Redo();
    private void SplitClick(object? sender, RoutedEventArgs e) => _vm.Split();
    private void DeleteClick(object? sender, RoutedEventArgs e) => _vm.Delete();
    private void ZoomInClick(object? sender, RoutedEventArgs e) => _vm.Zoom(1.4);
    private void ZoomOutClick(object? sender, RoutedEventArgs e) => _vm.Zoom(1 / 1.4);
    private void MuteClick(object? sender, RoutedEventArgs e) => _vm.ToggleMute();
    private void SoloClick(object? sender, RoutedEventArgs e) => _vm.ToggleSolo();
    private void ClipQuieterClick(object? sender, RoutedEventArgs e) => _vm.ApplyGain(-3, false);
    private void ClipLouderClick(object? sender, RoutedEventArgs e) => _vm.ApplyGain(3, false);
    private void TrackQuieterClick(object? sender, RoutedEventArgs e) => _vm.ApplyGain(-3, true);
    private void TrackLouderClick(object? sender, RoutedEventArgs e) => _vm.ApplyGain(3, true);
    private void ClipLimiterClick(object? sender, RoutedEventArgs e) => _vm.ToggleLimiter(false);
    private void TrackLimiterClick(object? sender, RoutedEventArgs e) => _vm.ToggleLimiter(true);
    private void NudgeLeftClick(object? sender, RoutedEventArgs e) => _vm.Nudge(-0.1);
    private void NudgeRightClick(object? sender, RoutedEventArgs e) => _vm.Nudge(0.1);

    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Source is TextBox || !_vm.CanEdit) return;
        var control = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        if (control && e.Key == Key.S) await SaveAsync(e.KeyModifiers.HasFlag(KeyModifiers.Shift));
        else if (control && e.Key == Key.Z)
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) _vm.Redo(); else _vm.Undo();
        }
        else if (control && e.Key == Key.Y) _vm.Redo();
        else if (e.Key == Key.Space) _vm.Play();
        else if (e.Key == Key.S && !control) _vm.Split();
        else if (e.Key is Key.Delete or Key.Back) _vm.Delete();
        else if (e.Key == Key.Home) _vm.Seek(0);
        else return;
        e.Handled = true;
    }
}
