using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace AudioNest.Desktop;

/// <summary>The only pointer surface allowed to move the playhead; capture supports scrubbing.</summary>
public sealed class TimelineRulerControl : Control
{
    private MainViewModel? _vm;
    private bool _scrubbing;
    private Rect _viewport;
    public TimelineRulerControl()
    {
        Focusable = true;
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.SizeWestEast);
        DataContextChanged += (_, _) => Bind();
        AttachedToVisualTree += (_, _) => Bind();
        DetachedFromVisualTree += (_, _) => Unbind();
        EffectiveViewportChanged += (_, e) => { _viewport = e.EffectiveViewport; Refresh(); };
    }
    private void Unbind()
    {
        if (_vm is null) return;
        _vm.TimelineChanged -= Refresh;
        _vm.ClockChanged -= Refresh;
    }
    private void Bind()
    {
        Unbind();
        _vm = DataContext as MainViewModel;
        if (_vm is not null) { _vm.TimelineChanged += Refresh; _vm.ClockChanged += Refresh; }
        Refresh();
    }
    private void Refresh() => InvalidateVisual();
    internal static double TickStep(double pixelsPerSecond)
    {
        var ideal = 64 / pixelsPerSecond;
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(ideal)));
        var fraction = ideal / magnitude;
        return (fraction <= 1 ? 1 : fraction <= 2 ? 2 : fraction <= 5 ? 5 : 10) * magnitude;
    }
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_vm is null) return;
        var palette = TimelinePalette.For(_vm);
        context.FillRectangle(palette.Alternate, new Rect(Bounds.Size));
        var visible = _viewport.Width > 0 ? _viewport : new Rect(Bounds.Size);
        var step = TickStep(_vm.PixelsPerSecond);
        var first = Math.Floor(visible.Left / _vm.PixelsPerSecond / step) * step;
        for (var second = first; second <= visible.Right / _vm.PixelsPerSecond; second += step)
        {
            var x = second * _vm.PixelsPerSecond;
            context.DrawLine(new Pen(palette.Grid, 1), new Point(x, 23), new Point(x, Bounds.Height));
            TimelineControl.Text(context, second.ToString("0.###", CultureInfo.CurrentCulture) + " s",
                new Point(x + 5, 5), palette.Muted, 11);
        }
        var cursor = (double)_vm.Position / _vm.Project.SampleRate * _vm.PixelsPerSecond;
        context.FillRectangle(palette.Accent, new Rect(cursor - 3, 0, 6, Bounds.Height));
        context.DrawLine(new Pen(palette.Grid, 1), new Point(0, Bounds.Height - 1), new Point(Bounds.Width, Bounds.Height - 1));
    }
    private void Seek(Point point)
    {
        if (_vm is null) return;
        _vm.Seek(Math.Max(0, (long)Math.Round(point.X / _vm.PixelsPerSecond * _vm.Project.SampleRate)));
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_vm is null || !_vm.CanEdit || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        Focus();
        _scrubbing = true;
        Seek(e.GetPosition(this));
        e.Pointer.Capture(this);
        e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_scrubbing && e.Pointer.Captured == this) { Seek(e.GetPosition(this)); e.Handled = true; }
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_scrubbing) return;
        Seek(e.GetPosition(this));
        _scrubbing = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _scrubbing = false;
    }
}
