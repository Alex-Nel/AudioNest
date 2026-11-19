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
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            if (DataContext is ClipEffectEditorViewModel editor) editor.BeginAdjustment();
            else if (DataContext is EffectParameterViewModel parameter) parameter.BeginAdjustment();
        }, RoutingStrategies.Tunnel);
        void Finish()
        {
            var context = DataContext;
            Dispatcher.UIThread.Post(() =>
            {
                if (context is ClipEffectEditorViewModel editor) editor.EndAdjustment();
                else if (context is EffectParameterViewModel parameter) parameter.EndAdjustment();
            });
        }
        AddHandler(PointerReleasedEvent, (_, _) => Finish(), RoutingStrategies.Tunnel, true);
        AddHandler(PointerCaptureLostEvent, (_, _) => Finish(), RoutingStrategies.Direct, true);
    }
}
