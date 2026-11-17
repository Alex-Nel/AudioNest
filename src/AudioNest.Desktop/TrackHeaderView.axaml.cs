using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace AudioNest.Desktop;

public partial class TrackHeaderView : UserControl
{
    public TrackHeaderView()
    {
        InitializeComponent();
        WireSlider(VolumeSlider, true);
        WireSlider(PanSlider, false);
        TrackResizeGrip.DragDelta += (_, e) =>
        {
            if (DataContext is not TrackHeaderViewModel track) return;
            // Thumb deltas are relative to its current layout, not the last event.
            track.Resize(Bounds.Height + e.Vector.Y);
        };
    }
    private void WireSlider(Slider slider, bool volume)
    {
        slider.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (!e.GetCurrentPoint(slider).Properties.IsLeftButtonPressed || DataContext is not TrackHeaderViewModel track) return;
            if (volume) track.BeginVolumeAdjustment(); else track.BeginPanAdjustment();
        }, RoutingStrategies.Tunnel);
        void Finish()
        {
            var track = DataContext as TrackHeaderViewModel;
            Dispatcher.UIThread.Post(() => track?.EndAdjustment());
        }
        slider.AddHandler(PointerReleasedEvent, (_, _) => Finish(), RoutingStrategies.Tunnel, true);
        slider.AddHandler(PointerCaptureLostEvent, (_, _) => Finish(), RoutingStrategies.Direct, true);
    }
    private void SelectTrack(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && DataContext is TrackHeaderViewModel track)
            track.Select();
    }
}
