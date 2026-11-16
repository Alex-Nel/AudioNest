using System.Globalization;
using AudioNest.Domain;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace AudioNest.Desktop;

/// <summary>Presentation-only timeline. Coordinates translate to domain frames at the boundary.</summary>
public sealed class TimelineControl : Control
{
    private const double Header = 180;
    private const double Ruler = 36;
    private const double Row = 112;
    private MainViewModel? _vm;
    private AudioClip? _dragClip;
    private Point _press;
    private Point _pointer;
    private bool _dragging;
    private static readonly IBrush[] ClipColors =
    [
        new SolidColorBrush(Color.Parse("#2F6050")), new SolidColorBrush(Color.Parse("#354E69")),
        new SolidColorBrush(Color.Parse("#62513D")), new SolidColorBrush(Color.Parse("#584666"))
    ];

    public TimelineControl()
    {
        Focusable = true;
        DataContextChanged += (_, _) => Bind();
        DetachedFromVisualTree += (_, _) => { if (_vm is not null) _vm.TimelineChanged -= Refresh; };
        AttachedToVisualTree += (_, _) => Bind();
    }

    private void Bind()
    {
        if (_vm is not null) _vm.TimelineChanged -= Refresh;
        _vm = DataContext as MainViewModel;
        if (_vm is not null) _vm.TimelineChanged += Refresh;
        Refresh();
    }
    private void Refresh() => InvalidateVisual();
    private double X(long frame) => Header + (double)frame / _vm!.Project.SampleRate * _vm.PixelsPerSecond;
    private long Frame(double x) => Math.Max(0, (long)((x - Header) / _vm!.PixelsPerSecond * _vm.Project.SampleRate));

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var vm = _vm;
        if (vm is null) return;
        context.FillRectangle(new SolidColorBrush(Color.Parse("#111D18")), Bounds.WithX(0).WithY(0));
        var gridPen = new Pen(new SolidColorBrush(Color.Parse("#26362D")), 1);
        var seconds = (Bounds.Width - Header) / vm.PixelsPerSecond;
        var step = vm.PixelsPerSecond < 30 ? 5 : 1;
        for (var second = 0; second <= seconds; second += step)
        {
            var x = Header + second * vm.PixelsPerSecond;
            context.DrawLine(gridPen, new Point(x, Ruler), new Point(x, Bounds.Height));
            Text(context, $"{second}s", new Point(x + 5, 10), Brushes.Gray, 11);
        }
        Text(context, "TRACKS", new Point(20, 10), Brushes.Gray, 11);
        for (var i = 0; i < vm.Project.Tracks.Length; i++)
        {
            var track = vm.Project.Tracks[i];
            var top = Ruler + i * Row;
            context.FillRectangle(new SolidColorBrush(Color.Parse(track.Id == vm.SelectedTrackId ? "#243D2F" : "#1A2A21")),
                new Rect(0, top, Header - 1, Row - 1));
            Text(context, track.Name, new Point(18, top + 18), Brushes.White, 13);
            Text(context, $"{(track.Muted ? "MUTED  " : "")}{(track.Solo ? "SOLO  " : "")}{track.Clips.Length} clips",
                new Point(18, top + 44), Brushes.Gray, 11);
            var gain = track.Effects.FirstOrDefault(e => e.Kind == EffectKind.Gain)?.Value ?? 1;
            Text(context, $"{20 * Math.Log10(Math.Max(0.0001, gain)):0.0} dB{(track.Effects.Any(e => e.Kind == EffectKind.HardLimiter) ? " · LIM" : "")}",
                new Point(18, top + 68), Brushes.Gray, 11);
            context.DrawLine(gridPen, new Point(0, top + Row - 1), new Point(Bounds.Width, top + Row - 1));
            foreach (var clip in track.Clips)
            {
                var rect = new Rect(X(clip.Start), top + 8, Math.Max(2, X(clip.End) - X(clip.Start)), Row - 16);
                using (context.PushClip(rect))
                {
                    context.FillRectangle(ClipColors[i % ClipColors.Length], rect);
                    var selected = clip.Id == vm.SelectedClipId;
                    context.DrawRectangle(new Pen(selected ? Brushes.PaleGreen : new SolidColorBrush(Color.Parse("#507460")), selected ? 2 : 1), rect.Deflate(1));
                    var clipGain = clip.Effects.FirstOrDefault(e => e.Kind == EffectKind.Gain)?.Value ?? 1;
                    var label = $"{clip.Name}  ·  {20 * Math.Log10(Math.Max(0.0001, clipGain)):0.0} dB";
                    if (clip.Effects.Any(e => e.Kind == EffectKind.HardLimiter)) label += "  · LIM";
                    Text(context, label, new Point(rect.X + 8, rect.Y + 6), Brushes.White, 11);
                    var buffer = vm.Buffer(clip.AssetId);
                    var pen = new Pen(new SolidColorBrush(Color.Parse("#B6E3CB")), 1);
                    var center = rect.Y + 59;
                    for (var pixel = 0; pixel < Math.Ceiling(rect.Width); pixel += 2)
                    {
                        var from = clip.SourceOffset + (long)(pixel / rect.Width * clip.Length);
                        var to = Math.Min(clip.SourceOffset + clip.Length,
                            clip.SourceOffset + (long)((pixel + 2) / rect.Width * clip.Length) + 1);
                        var peak = buffer.Peak(from, to);
                        var x = rect.X + pixel;
                        context.DrawLine(pen, new Point(x, center - peak.Maximum * 28),
                            new Point(x, center - peak.Minimum * 28 + 1));
                    }
                }
            }
        }
        if (vm.Project.Duration == 0)
            Text(context, "Your next sound starts here. Import a file or press Record.",
                new Point(Header + 36, Ruler + 150), new SolidColorBrush(Color.Parse("#6F8B79")), 15);
        if (_dragging && _dragClip is not null)
        {
            var start = Math.Max(0, _dragClip.Start + FrameDelta(_pointer.X - _press.X));
            var row = Math.Clamp((int)((_pointer.Y - Ruler) / Row), 0, vm.Project.Tracks.Length - 1);
            context.DrawRectangle(new Pen(Brushes.White, 2),
                new Rect(X(start), Ruler + row * Row + 8, X(_dragClip.Length) - Header, Row - 16));
        }
    }

    private static void Text(DrawingContext context, string value, Point point, IBrush brush, double size)
    {
        var text = new FormattedText(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Inter, Segoe UI, sans-serif"), size, brush);
        context.DrawText(text, point);
    }

    private long FrameDelta(double pixels) => (long)(pixels / _vm!.PixelsPerSecond * _vm.Project.SampleRate);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_vm is null || !_vm.CanEdit || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        Focus();
        _press = _pointer = e.GetPosition(this);
        _dragClip = null;
        _dragging = false;
        var row = (int)((_press.Y - Ruler) / Row);
        if (_press.Y >= Ruler && row >= 0 && row < _vm.Project.Tracks.Length)
        {
            var track = _vm.Project.Tracks[row];
            var frame = Frame(_press.X);
            var clip = _press.X >= Header ? track.Clips.LastOrDefault(c => c.Start <= frame && frame < c.End) : null;
            _vm.Select(track.Id, clip?.Id);
            _dragClip = clip;
        }
        if (_press.X >= Header) _vm.Seek(Frame(_press.X));
        e.Pointer.Capture(this);
        e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragClip is null || e.Pointer.Captured != this) return;
        _pointer = e.GetPosition(this);
        _dragging = _dragging || Math.Abs(_pointer.X - _press.X) > 4 || Math.Abs(_pointer.Y - _press.Y) > 4;
        Refresh();
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_vm is not null && _dragging && _dragClip is not null)
        {
            var row = Math.Clamp((int)((_pointer.Y - Ruler) / Row), 0, _vm.Project.Tracks.Length - 1);
            var track = _vm.Project.Tracks[row];
            _vm.Move(_dragClip.Id, track.Id, Math.Max(0, _dragClip.Start + FrameDelta(_pointer.X - _press.X)));
            _vm.Select(track.Id, _dragClip.Id);
        }
        _dragClip = null;
        _dragging = false;
        e.Pointer.Capture(null);
        Refresh();
    }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _dragClip = null;
        _dragging = false;
        Refresh();
    }
}
