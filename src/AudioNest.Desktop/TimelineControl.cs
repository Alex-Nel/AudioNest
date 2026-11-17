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
    private enum Gesture { Move, TrimStart, TrimEnd }
    private Gesture _gesture;

    public TimelineControl()
    {
        Focusable = true;
        ClipToBounds = true;
        DataContextChanged += (_, _) => Bind();
        DetachedFromVisualTree += (_, _) => { if (_vm is not null) _vm.TimelineChanged -= Refresh; };
        AttachedToVisualTree += (_, _) => Bind();
        EffectiveViewportChanged += (_, e) => { _viewport = e.EffectiveViewport; Refresh(); };
        ContextRequested += OnContextRequested;
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
    private long TrimFrame() => (_gesture == Gesture.TrimStart ? _dragClip!.Start : _dragClip!.End) +
        FrameDelta(_pointer.X - _press.X);
    private AudioClip? HitClip(Point point)
    {
        if (_vm is null || point.Y < 0) return null;
        var row = _vm.TrackIndexAt(point.Y);
        if (row < 0 || point.Y - _vm.TrackTop(row) < 8 ||
            point.Y - _vm.TrackTop(row) > _vm.TrackHeight(_vm.Project.Tracks[row].Id) - 8)
            return null;
        var clips = _vm.Project.Tracks[row].Clips;
        // Prefer the visible interior so a neighboring clip's resize halo cannot
        // steal clicks just inside an adjoining edge.
        return clips.LastOrDefault(c => point.X >= X(c.Start) && point.X < Math.Max(X(c.End), X(c.Start) + 2))
            ?? clips.LastOrDefault(c => point.X >= X(c.Start) - 3 && point.X <= Math.Max(X(c.End), X(c.Start) + 2) + 3);
    }
    private Gesture GestureAt(AudioClip clip, double x)
    {
        var left = Math.Abs(x - X(clip.Start));
        var end = Math.Max(X(clip.End), X(clip.Start) + 2);
        var right = Math.Abs(x - end);
        if (Math.Min(left, right) > Math.Min(6, (end - X(clip.Start)) / 4)) return Gesture.Move;
        return left <= right ? Gesture.TrimStart : Gesture.TrimEnd;
    }

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
            var top = vm.TrackTop(i);
            var height = vm.TrackHeight(vm.Project.Tracks[i].Id);
            if (top + height >= visible.Top && top <= visible.Bottom)
                context.FillRectangle(palette.Alternate, new Rect(0, top, Bounds.Width, height));
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
            var track = vm.Project.Tracks[i];
            var top = vm.TrackTop(i);
            var height = vm.TrackHeight(track.Id);
            if (top + height < visible.Top || top > visible.Bottom) continue;
            context.DrawLine(gridPen, new Point(0, top + height - 1),
                new Point(Bounds.Width, top + height - 1));
            foreach (var original in track.Clips)
            {
                var clip = _dragging && _dragClip?.Id == original.Id && _gesture != Gesture.Move
                    ? vm.PreviewTrim(original, TrimFrame(), _gesture == Gesture.TrimStart) : original;
                var rect = new Rect(X(clip.Start), top + 8, Math.Max(2, X(clip.End) - X(clip.Start)), height - 16);
                if (!rect.Intersects(visible)) continue;
                using (context.PushClip(rect))
                {
                    var selected = vm.IsClipSelected(clip.Id);
                    context.FillRectangle(selected ? palette.ClipSelected : palette.Clip, rect);
                    context.DrawRectangle(new Pen(selected ? palette.Accent : palette.Grid, selected ? 2 : 1), rect.Deflate(0.5));
                    if (selected && !clip.Locked && vm.IsSelectionTool && rect.Width >= 12)
                    {
                        context.DrawLine(new Pen(palette.Accent, 2), new Point(rect.Left + 4, rect.Y + 42), new Point(rect.Left + 4, rect.Bottom - 18));
                        context.DrawLine(new Pen(palette.Accent, 2), new Point(rect.Right - 4, rect.Y + 42), new Point(rect.Right - 4, rect.Bottom - 18));
                    }
                    var clipGain = clip.Volume * (clip.Effects.FirstOrDefault(e => e.Kind == EffectKind.Gain)?.Value ?? 1);
                    var label = $"{clip.Name}  ·  {20 * Math.Log10(Math.Max(0.0001, clipGain)):0.0} dB";
                    if (clip.Muted) label += "  · MUTED";
                    if (clip.Locked) label += "  · LOCKED";
                    if (clip.Effects.Any(e => e.Kind == EffectKind.HardLimiter)) label += "  · LIM";
                    Text(context, label, new Point(Math.Max(rect.X + 8, visible.Left + 8), rect.Y + 8), palette.Text, 11);
                    var buffer = vm.Buffer(clip.AssetId);
                    var pen = new Pen(palette.Wave, 1);
                    var center = rect.Y + 30 + (rect.Height - 30) / 2;
                    var amplitude = Math.Max(8, (rect.Height - 44) / 2);
                    var firstPixel = Math.Max(0, (int)(visible.Left - rect.X));
                    var lastPixel = Math.Min(rect.Width, visible.Right - rect.X);
                    for (var pixel = firstPixel; pixel < lastPixel; pixel += 2)
                    {
                        var from = clip.SourceOffset + (long)(pixel / rect.Width * clip.Length);
                        var to = Math.Min(clip.SourceOffset + clip.Length,
                            clip.SourceOffset + (long)((pixel + 2) / rect.Width * clip.Length) + 1);
                        var peak = buffer.Peak(from, to, track.Layout == TrackChannelLayout.Mono);
                        var x = rect.X + pixel;
                        context.DrawLine(pen, new Point(x, center - peak.Maximum * amplitude),
                            new Point(x, center - peak.Minimum * amplitude + 1));
                    }
                }
            }
        }
        DrawRecording(context, vm, palette, visible);
        if (vm.Project.Duration == 0 && !vm.IsRecording)
            Text(context, "Import audio or record a take to begin.", new Point(visible.Left + 24, 54), palette.Muted, 13);
        if (_dragging && _gesture == Gesture.Move && _dragClip is not null && vm.Project.Tracks.Length > 0)
        {
            var snap = DragPosition();
            var row = TargetRow(_pointer.Y);
            var moved = vm.PreviewMove(_dragClip.Id, vm.Project.Tracks[row].Id, snap.Start);
            for (var i = 0; i < moved.Tracks.Length; i++)
                foreach (var clip in moved.Tracks[i].Clips.Where(c => vm.IsClipSelected(c.Id)))
                    context.DrawRectangle(new Pen(palette.Accent, 2),
                        new Rect(X(clip.Start), vm.TrackTop(i) + 8, Math.Max(2, X(clip.Length)), vm.TrackHeight(moved.Tracks[i].Id) - 16));
            if (snap.Target is { } target)
                context.DrawLine(new Pen(palette.Accent, 1), new Point(X(target), 0), new Point(X(target), Bounds.Height));
        }
    }
    private int TargetRow(double y) => y < 0 ? 0 : _vm!.TrackIndexAt(y) is var row && row >= 0 ? row : _vm!.Project.Tracks.Length - 1;
    private void DrawRecording(DrawingContext context, MainViewModel vm, TimelinePalette palette, Rect visible)
    {
        var snapshot = vm.LiveRecording;
        var row = vm.TrackIndex(vm.RecordingTrackId);
        if (snapshot is null || row < 0) return;
        var rect = new Rect(X(vm.RecordingStart), vm.TrackTop(row) + 8, Math.Max(2, X(snapshot.Frames)), vm.TrackHeight(vm.RecordingTrackId) - 16);
        if (!rect.Intersects(visible)) return;
        using (context.PushClip(rect))
        {
            context.FillRectangle(palette.ClipSelected, rect);
            context.DrawRectangle(new Pen(Brushes.IndianRed, 2), rect.Deflate(0.5));
            Text(context, "● Recording…", new Point(Math.Max(rect.X + 8, visible.Left + 8), rect.Y + 8), palette.Text, 11);
            var center = rect.Y + 30 + (rect.Height - 30) / 2;
            var amplitude = Math.Max(8, (rect.Height - 44) / 2);
            for (var pixel = Math.Max(0, (int)(visible.Left - rect.Left)); pixel < Math.Min(rect.Width, visible.Right - rect.Left); pixel += 2)
            {
                var from = (int)(pixel / vm.PixelsPerSecond * snapshot.SampleRate / snapshot.FramesPerPeak);
                var to = (int)((pixel + 2) / vm.PixelsPerSecond * snapshot.SampleRate / snapshot.FramesPerPeak) + 1;
                var min = 0f; var max = 0f;
                for (var i = from; i < Math.Min(to, snapshot.Peaks.Length); i++)
                { min = Math.Min(min, snapshot.Peaks[i].Minimum); max = Math.Max(max, snapshot.Peaks[i].Maximum); }
                context.DrawLine(new Pen(palette.Wave, 1), new Point(rect.X + pixel, center - max * amplitude),
                    new Point(rect.X + pixel, center - min * amplitude + 1));
            }
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
        var row = _vm.TrackIndexAt(_press.Y);
        if (row >= 0)
        {
            var track = _vm.Project.Tracks[row];
            var clip = HitClip(_press);
            _vm.Select(track.Id, clip?.Id, _vm.IsSelectionTool && _vm.IsLeftShiftDown,
                preserveGroup: clip is not null && _vm.IsClipSelected(clip.Id));
            if (_vm.IsRazorTool)
            {
                if (clip is not null) _vm.SplitAt(clip.Id, Frame(_press.X));
                e.Handled = true;
                return;
            }
            _dragClip = clip;
            if (clip is not null && !_vm.IsClipSelected(clip.Id)) { e.Handled = true; return; }
            if (clip is not null && (clip.Locked ||
                GestureAt(clip, _press.X) == Gesture.Move && _vm.Project.Tracks.SelectMany(t => t.Clips).Any(c => c.Locked && _vm.IsClipSelected(c.Id))))
            { _dragClip = null; e.Handled = true; return; }
            if (clip is not null) _gesture = GestureAt(clip, _press.X);
            if (clip is not null) e.Pointer.Capture(this);
        }
        // Deliberately never seek here: only the ruler owns cursor gestures.
        e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragClip is null || e.Pointer.Captured != this)
        {
            var clip = HitClip(e.GetPosition(this));
            Cursor = new Cursor(_vm?.IsRazorTool == true ? StandardCursorType.Cross :
                clip is { Locked: false } && GestureAt(clip, e.GetPosition(this).X) != Gesture.Move ? StandardCursorType.SizeWestEast : StandardCursorType.Arrow);
            return;
        }
        _pointer = e.GetPosition(this);
        _bypassSnapping = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        _dragging = _dragging || Math.Abs(_pointer.X - _press.X) > 4 || Math.Abs(_pointer.Y - _press.Y) > 4;
        Refresh();
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.InitialPressMouseButton != MouseButton.Left) return;
        if (_vm is not null && _dragging && _dragClip is not null && _vm.Project.Tracks.Length > 0)
        {
            _pointer = e.GetPosition(this);
            _bypassSnapping = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
            if (_gesture != Gesture.Move) _vm.Trim(_dragClip.Id, TrimFrame(), _gesture == Gesture.TrimStart);
            else
            {
                var row = TargetRow(_pointer.Y);
                var track = _vm.Project.Tracks[row];
                _vm.Move(_dragClip.Id, track.Id, DragPosition().Start);
                _vm.Select(ProjectEdits.FindTrack(_vm.Project, _dragClip.Id).Id, _dragClip.Id, preserveGroup: true);
            }
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
    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        ContextMenu?.Close();
        ContextMenu = null;
        e.Handled = true;
        if (_vm is null || !_vm.CanEdit || !e.TryGetPosition(this, out var point) || point.Y < 0) return;
        var row = _vm.TrackIndexAt(point.Y);
        if (row < 0) return;
        var track = _vm.Project.Tracks[row];
        var clip = HitClip(point);
        _vm.Select(track.Id, clip?.Id, preserveGroup: clip is not null && _vm.IsClipSelected(clip.Id));
        ContextMenu = TimelineContextMenus.Clip(_vm, track.Id, clip, Frame(point.X));
        ContextMenu.Open(this);
    }
}
