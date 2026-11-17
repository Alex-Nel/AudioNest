using System.Globalization;
using AudioNest.Application;
using AudioNest.Domain;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace AudioNest.Desktop;

/// <summary>Clip-only canvas. Ruler gestures and track controls are separate hit-test surfaces.</summary>
public sealed class TimelineControl : Control
{
    private MainViewModel? _vm;
    private AudioClip? _dragClip;
    private Point _press;
    private Point _pointer;
    private bool _dragging;
    private bool _bypassSnapping;
    private Rect _viewport;

    public TimelineControl()
    {
        Focusable = true;
        ClipToBounds = true;
        DataContextChanged += (_, _) => Bind();
        DetachedFromVisualTree += (_, _) => { if (_vm is not null) _vm.TimelineChanged -= Refresh; };
        AttachedToVisualTree += (_, _) => Bind();
        EffectiveViewportChanged += (_, e) => { _viewport = e.EffectiveViewport; Refresh(); };
    }
    private void Bind()
    {
        if (_vm is not null) _vm.TimelineChanged -= Refresh;
        _vm = DataContext as MainViewModel;
        if (_vm is not null) _vm.TimelineChanged += Refresh;
        Refresh();
    }
    private void Refresh() => InvalidateVisual();
    private double X(long frame) => (double)frame / _vm!.Project.SampleRate * _vm.PixelsPerSecond;
    private long Frame(double x) => Math.Max(0, (long)Math.Round(x / _vm!.PixelsPerSecond * _vm.Project.SampleRate));
    private long FrameDelta(double pixels) => (long)Math.Round(pixels / _vm!.PixelsPerSecond * _vm.Project.SampleRate);
    private ClipSnap DragPosition() => _vm!.ResolveClipMove(_dragClip!,
        Math.Max(0, _dragClip!.Start + FrameDelta(_pointer.X - _press.X)), _bypassSnapping);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var vm = _vm;
        if (vm is null) return;
        var palette = TimelinePalette.For(vm);
        var visible = _viewport.Width > 0 ? _viewport : new Rect(Bounds.Size);
        context.FillRectangle(palette.Surface, new Rect(Bounds.Size));
        for (var i = 1; i < vm.Project.Tracks.Length; i += 2)
        {
            var top = i * TimelineMetrics.TrackHeight;
            if (top + TimelineMetrics.TrackHeight >= visible.Top && top <= visible.Bottom)
                context.FillRectangle(palette.Alternate, new Rect(0, top, Bounds.Width, TimelineMetrics.TrackHeight));
        }
        var gridPen = new Pen(palette.Grid, 1);
        var step = TimelineRulerControl.TickStep(vm.PixelsPerSecond);
        var first = Math.Floor(visible.Left / vm.PixelsPerSecond / step) * step;
        for (var seconds = first; seconds <= visible.Right / vm.PixelsPerSecond; seconds += step)
        {
            var x = seconds * vm.PixelsPerSecond;
            context.DrawLine(gridPen, new Point(x, 0), new Point(x, Bounds.Height));
        }
        for (var i = 0; i < vm.Project.Tracks.Length; i++)
        {
            var top = i * TimelineMetrics.TrackHeight;
            if (top + TimelineMetrics.TrackHeight < visible.Top || top > visible.Bottom) continue;
            var track = vm.Project.Tracks[i];
            context.DrawLine(gridPen, new Point(0, top + TimelineMetrics.TrackHeight - 1),
                new Point(Bounds.Width, top + TimelineMetrics.TrackHeight - 1));
            foreach (var clip in track.Clips)
            {
                var rect = new Rect(X(clip.Start), top + 8, Math.Max(2, X(clip.End) - X(clip.Start)), TimelineMetrics.TrackHeight - 16);
                if (!rect.Intersects(visible)) continue;
                using (context.PushClip(rect))
                {
                    var selected = clip.Id == vm.SelectedClipId;
                    context.FillRectangle(selected ? palette.ClipSelected : palette.Clip, rect);
                    context.DrawRectangle(new Pen(selected ? palette.Accent : palette.Grid, selected ? 2 : 1), rect.Deflate(0.5));
                    var clipGain = clip.Effects.FirstOrDefault(e => e.Kind == EffectKind.Gain)?.Value ?? 1;
                    var label = $"{clip.Name}  ·  {20 * Math.Log10(Math.Max(0.0001, clipGain)):0.0} dB";
                    if (clip.Effects.Any(e => e.Kind == EffectKind.HardLimiter)) label += "  · LIM";
                    Text(context, label, new Point(Math.Max(rect.X + 8, visible.Left + 8), rect.Y + 8), palette.Text, 11);
                    var buffer = vm.Buffer(clip.AssetId);
                    var pen = new Pen(palette.Wave, 1);
                    var center = rect.Y + 78;
                    var firstPixel = Math.Max(0, (int)(visible.Left - rect.X));
                    var lastPixel = Math.Min(rect.Width, visible.Right - rect.X);
                    for (var pixel = firstPixel; pixel < lastPixel; pixel += 2)
                    {
                        var from = clip.SourceOffset + (long)(pixel / rect.Width * clip.Length);
                        var to = Math.Min(clip.SourceOffset + clip.Length,
                            clip.SourceOffset + (long)((pixel + 2) / rect.Width * clip.Length) + 1);
                        var peak = buffer.Peak(from, to);
                        var x = rect.X + pixel;
                        context.DrawLine(pen, new Point(x, center - peak.Maximum * 38),
                            new Point(x, center - peak.Minimum * 38 + 1));
                    }
                }
            }
        }
        if (vm.Project.Duration == 0)
            Text(context, "Import audio or record a take to begin.", new Point(visible.Left + 24, 54), palette.Muted, 13);
        if (_dragging && _dragClip is not null && vm.Project.Tracks.Length > 0)
        {
            var snap = DragPosition();
            var row = Math.Clamp((int)(_pointer.Y / TimelineMetrics.TrackHeight), 0, vm.Project.Tracks.Length - 1);
            context.DrawRectangle(new Pen(palette.Accent, 2),
                new Rect(X(snap.Start), row * TimelineMetrics.TrackHeight + 8, Math.Max(2, X(_dragClip.Length)), TimelineMetrics.TrackHeight - 16));
            if (snap.Target is { } target)
                context.DrawLine(new Pen(palette.Accent, 1), new Point(X(target), 0), new Point(X(target), Bounds.Height));
        }
    }
    internal static void Text(DrawingContext context, string value, Point point, IBrush brush, double size)
    {
        var text = new FormattedText(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI, sans-serif"), size, brush);
        context.DrawText(text, point);
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_vm is null || !_vm.CanEdit || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        Focus();
        _press = _pointer = e.GetPosition(this);
        _dragClip = null;
        _dragging = false;
        _bypassSnapping = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        var row = (int)(_press.Y / TimelineMetrics.TrackHeight);
        if (_press.Y >= 0 && row < _vm.Project.Tracks.Length)
        {
            var track = _vm.Project.Tracks[row];
            var frame = Frame(_press.X);
            var clip = track.Clips.LastOrDefault(c => c.Start <= frame && frame < c.End);
            _vm.Select(track.Id, clip?.Id);
            _dragClip = clip;
            if (clip is not null) e.Pointer.Capture(this);
        }
        // Deliberately never seek here: only the ruler owns cursor gestures.
        e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragClip is null || e.Pointer.Captured != this) return;
        _pointer = e.GetPosition(this);
        _bypassSnapping = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        _dragging = _dragging || Math.Abs(_pointer.X - _press.X) > 4 || Math.Abs(_pointer.Y - _press.Y) > 4;
        Refresh();
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_vm is not null && _dragging && _dragClip is not null && _vm.Project.Tracks.Length > 0)
        {
            _pointer = e.GetPosition(this);
            _bypassSnapping = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
            var row = Math.Clamp((int)(_pointer.Y / TimelineMetrics.TrackHeight), 0, _vm.Project.Tracks.Length - 1);
            var track = _vm.Project.Tracks[row];
            _vm.Move(_dragClip.Id, track.Id, DragPosition().Start);
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
