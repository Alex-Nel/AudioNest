using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace AudioNest.Desktop;

public partial class ExportOptionsWindow : Window
{
    private readonly ExportOptionsViewModel _vm;
    public ExportOptionsWindow(bool hasAudio)
    {
        InitializeComponent();
        DataContext = _vm = new ExportOptionsViewModel(hasAudio);
    }
    private async void BrowseClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var type = new FilePickerFileType(_vm.SelectedFormat.Name) { Patterns = ["*." + _vm.SelectedFormat.Extension] };
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                SuggestedFileName = System.IO.Path.GetFileName(_vm.Path),
                DefaultExtension = _vm.SelectedFormat.Extension,
                FileTypeChoices = [type],
                ShowOverwritePrompt = false
            });
            if (file?.TryGetLocalPath() is { } path) _vm.Path = path;
        }
        catch (Exception ex) { _vm.Path = ""; ToolTip.SetTip(this, ex.Message); }
    }
    private void CancelClick(object? sender, RoutedEventArgs e) => Close(null);
    private async void ExportClick(object? sender, RoutedEventArgs e)
    {
        if (!_vm.CanExport) return;
        if (File.Exists(_vm.Path))
        {
            var confirm = new Window { Title = "Replace existing file?", Width = 390, Height = 160, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var yes = new Button { Content = "Replace" };
            var no = new Button { Content = "Cancel" };
            yes.Click += (_, _) => confirm.Close(true);
            no.Click += (_, _) => confirm.Close(false);
            confirm.Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(18),
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = "An output file already exists. Replace it?" },
                    new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, Children = { no, yes } }
                }
            };
            if (!await confirm.ShowDialog<bool>(this)) return;
        }
        Close(new ExportRequest(System.IO.Path.GetFullPath(_vm.Path), _vm.SelectedFormat.Format));
    }
}
