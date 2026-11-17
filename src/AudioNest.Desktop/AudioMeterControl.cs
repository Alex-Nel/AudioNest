using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace AudioNest.Desktop;

/// <summary>Skinny peak meter; its UI value is polled, never updated from the audio callback.</summary>
public sealed class AudioMeterControl : Control
{
    public static readonly StyledProperty<double> LevelProperty =
        AvaloniaProperty.Register<AudioMeterControl, double>(nameof(Level));
    public double Level { get => GetValue(LevelProperty); set => SetValue(LevelProperty, value); }
    static AudioMeterControl() => AffectsRender<AudioMeterControl>(LevelProperty);
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(new SolidColorBrush(Color.Parse("#70757A")), new Rect(Bounds.Size));
        var width = Bounds.Width * Math.Clamp(Level, 0, 1);
        context.FillRectangle(new SolidColorBrush(Color.Parse("#399853")), new Rect(0, 0, Math.Min(width, Bounds.Width * 0.75), Bounds.Height));
        if (Level > 0.75)
            context.FillRectangle(new SolidColorBrush(Color.Parse("#D8AD30")), new Rect(Bounds.Width * 0.75, 0, Math.Min(width, Bounds.Width * 0.95) - Bounds.Width * 0.75, Bounds.Height));
        if (Level > 0.95)
            context.FillRectangle(new SolidColorBrush(Color.Parse("#D84040")), new Rect(Bounds.Width * 0.95, 0, width - Bounds.Width * 0.95, Bounds.Height));
    }
}
