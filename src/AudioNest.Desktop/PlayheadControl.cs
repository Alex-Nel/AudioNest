using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace AudioNest.Desktop;

/// <summary>Independent overlay: clock ticks never force waveform geometry to be redrawn.</summary>
public sealed class PlayheadControl : Control
{
    private MainViewModel? _vm;
    public PlayheadControl()
    {
        DataContextChanged += (_, _) => Bind();
        AttachedToVisualTree += (_, _) => Bind();
        DetachedFromVisualTree += (_, _) => { if (_vm is not null) _vm.ClockChanged -= Refresh; };
    }
    private void Bind()
    {
        if (_vm is not null) _vm.ClockChanged -= Refresh;
        _vm = DataContext as MainViewModel;
        if (_vm is not null) _vm.ClockChanged += Refresh;
        Refresh();
    }
    private void Refresh() => InvalidateVisual();
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_vm is null) return;
        var x = (double)_vm.Position / _vm.Project.SampleRate * _vm.PixelsPerSecond;
        var brush = TimelinePalette.For(_vm).Accent;
        context.DrawLine(new Pen(brush, 1.5), new Point(x, 0), new Point(x, Bounds.Height));
    }
}
