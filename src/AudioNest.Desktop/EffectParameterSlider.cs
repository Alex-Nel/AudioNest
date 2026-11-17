using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace AudioNest.Desktop;

/// <summary>Preview on the live graph, commit one metadata edit per pointer gesture.</summary>
public sealed class EffectParameterSlider : Slider
{
    protected override Type StyleKeyOverride => typeof(Slider);
    public EffectParameterSlider()
    {
        AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed &&
                DataContext is SingleParameterEffectEditorViewModel editor) editor.BeginAdjustment();
        }, RoutingStrategies.Tunnel);
        void Finish()
        {
            var editor = DataContext as SingleParameterEffectEditorViewModel;
            Dispatcher.UIThread.Post(() => editor?.EndAdjustment());
        }
        AddHandler(PointerReleasedEvent, (_, _) => Finish(), RoutingStrategies.Tunnel, true);
        AddHandler(PointerCaptureLostEvent, (_, _) => Finish(), RoutingStrategies.Direct, true);
    }
}
