using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace AudioNest.Desktop;

/// <summary>Clip volume/balance slider with one edit per pointer gesture.</summary>
public sealed class ClipMixSlider : Slider
{
    public static readonly StyledProperty<bool> IsPanProperty = AvaloniaProperty.Register<ClipMixSlider, bool>(nameof(IsPan));
    public bool IsPan { get => GetValue(IsPanProperty); set => SetValue(IsPanProperty, value); }
    protected override Type StyleKeyOverride => typeof(Slider);
    public ClipMixSlider()
    {
        AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && DataContext is ClipPropertiesViewModel properties)
                properties.BeginAdjustment(IsPan);
        }, RoutingStrategies.Tunnel);
        void Finish()
        {
            var properties = DataContext as ClipPropertiesViewModel;
            Dispatcher.UIThread.Post(() => properties?.EndAdjustment());
        }
        AddHandler(PointerReleasedEvent, (_, _) => Finish(), RoutingStrategies.Tunnel, true);
        AddHandler(PointerCaptureLostEvent, (_, _) => Finish(), RoutingStrategies.Direct, true);
    }
}
