using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AudioNest.Desktop;

public partial class SampleRateWindow : Window
{
    private readonly SampleRateViewModel _vm;
    public SampleRateWindow(int currentRate)
    {
        InitializeComponent();
        DataContext = _vm = new SampleRateViewModel(currentRate);
    }
    private void PresetsClick(object? sender, RoutedEventArgs e)
    {
        SampleRateBox.Focus();
        SampleRateBox.IsDropDownOpen = !SampleRateBox.IsDropDownOpen;
    }
    private void CancelClick(object? sender, RoutedEventArgs e) => Close(null);
    private void ApplyClick(object? sender, RoutedEventArgs e)
    {
        if (_vm.CanApply) Close(_vm.SampleRate);
    }
}
