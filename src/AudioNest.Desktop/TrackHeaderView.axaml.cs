using Avalonia.Controls;
using Avalonia.Input;

namespace AudioNest.Desktop;

public partial class TrackHeaderView : UserControl
{
    public TrackHeaderView() => InitializeComponent();
    private void SelectTrack(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && DataContext is TrackHeaderViewModel track)
            track.Select();
    }
}
