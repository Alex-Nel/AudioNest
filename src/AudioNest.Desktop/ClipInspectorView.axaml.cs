using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AudioNest.Desktop;

public partial class ClipInspectorView : UserControl
{
    private ClipInspectorViewModel? _vm;
    private ClipEffectEditorViewModel? _dragged;
    private ClipEffectEditorViewModel[] _chain = [];
    private AudioNest.Domain.EffectSpec[] _descriptions = [];
    private IPointer? _pointer;
    private Point _press;
    private Point _position;
    private bool _dragging;
    private int _destination;
    private readonly DispatcherTimer _scrollTimer;

    public ClipInspectorView()
    {
        InitializeComponent();
        Focusable = true;
        _scrollTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(50), DispatcherPriority.Background, (_, _) => AutoScroll());
        _scrollTimer.Stop();
        DataContextChanged += (_, _) => Bind();
        AttachedToVisualTree += (_, _) => Bind();
        DetachedFromVisualTree += (_, _) => { CancelDrag(); Unbind(); };
    }
    private void Unbind()
    {
        if (_vm is not null) _vm.PropertyChanged -= OnInspectorChanged;
    }
    private void Bind()
    {
        CancelDrag();
        Unbind();
        _vm = DataContext as ClipInspectorViewModel;
        if (_vm is not null) _vm.PropertyChanged += OnInspectorChanged;
    }
    private void OnInspectorChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_dragged is not null && !ValidDrag()) CancelDrag();
    }
    private bool ValidDrag() => _dragged?.CanEdit == true && _vm is not null &&
        _vm.Effects.Count == _chain.Length && _vm.Effects.Where((editor, i) =>
            !ReferenceEquals(editor, _chain[i]) || !ReferenceEquals(editor.Description, _descriptions[i])).Any() == false;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_vm is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || e.Source is not Control source) return;
        var ancestors = source.GetVisualAncestors().Prepend(source).ToArray();
        // Arrow/remove controls own their clicks and must never start a drag.
        if (ancestors.OfType<Button>().Any()) return;
        var header = ancestors.OfType<Border>().FirstOrDefault(b => b.Classes.Contains("effectHeader"));
        if (header?.DataContext is not ClipEffectEditorViewModel { CanEdit: true } editor) return;
        CancelDrag();
        Focus();
        _dragged = editor;
        _chain = _vm.Effects.ToArray();
        _descriptions = _chain.Select(c => c.Description).ToArray();
        _press = _position = e.GetPosition(this);
        _destination = editor.Index;
        _pointer = e.Pointer;
        e.Pointer.Capture(this);
        e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragged is null || e.Pointer.Captured != this) return;
        if (!ValidDrag()) { CancelDrag(); return; }
        _position = e.GetPosition(this);
        if (!_dragging && (Math.Abs(_position.X - _press.X) > 4 || Math.Abs(_position.Y - _press.Y) > 4))
        {
            _dragging = true;
            _dragged.IsReordering = true;
            _scrollTimer.Start();
        }
        if (_dragging) UpdateDestination();
        e.Handled = true;
    }
    private void UpdateDestination()
    {
        var sections = EffectSections.GetVisualDescendants().OfType<StackPanel>()
            .Where(p => p.Classes.Contains("effectSection") && p.DataContext is ClipEffectEditorViewModel editor && editor != _dragged)
            .OrderBy(p => ((ClipEffectEditorViewModel)p.DataContext!).Index).ToArray();
        _destination = sections.Length;
        double guide = 0;
        for (var i = 0; i < sections.Length; i++)
        {
            var top = sections[i].TranslatePoint(default, this)!.Value.Y;
            guide = top + sections[i].Bounds.Height;
            var header = sections[i].Children.OfType<Border>().First(b => b.Classes.Contains("effectHeader"));
            var middle = header.TranslatePoint(new Point(0, header.Bounds.Height / 2), this)!.Value.Y;
            if (_position.Y >= middle) continue;
            _destination = i;
            guide = top - 3;
            break;
        }
        EffectDropIndicator.IsVisible = _dragged is not null && _destination != _dragged.Index && Contains(_position);
        EffectDropIndicator.Margin = new Thickness(8, Math.Clamp(guide, 0, Math.Max(0, Bounds.Height - 3)), 8, 0);
    }
    private bool Contains(Point point) => point.X >= 0 && point.X <= Bounds.Width && point.Y >= 0 && point.Y <= Bounds.Height;
    private void AutoScroll()
    {
        if (!_dragging || !ValidDrag()) { CancelDrag(); return; }
        if (!Contains(_position)) { EffectDropIndicator.IsVisible = false; return; }
        var delta = _position.Y < 28 ? -18 : _position.Y > Bounds.Height - 28 ? 18 : 0;
        if (delta != 0)
        {
            var maximum = Math.Max(0, InspectorScroll.Extent.Height - InspectorScroll.Viewport.Height);
            InspectorScroll.Offset = new Vector(0, Math.Clamp(InspectorScroll.Offset.Y + delta, 0, maximum));
        }
        UpdateDestination();
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragged is null || e.InitialPressMouseButton != MouseButton.Left) return;
        _position = e.GetPosition(this);
        var editor = _dragged;
        var commit = _dragging && ValidDrag() && Contains(_position);
        if (commit) UpdateDestination();
        var destination = _destination;
        CancelDrag();
        if (commit) editor.MoveTo(destination);
        e.Handled = true;
    }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        CancelDrag();
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape && _dragged is not null) { CancelDrag(); e.Handled = true; }
    }
    private void CancelDrag()
    {
        var pointer = _pointer;
        var editor = _dragged;
        _dragged = null;
        _pointer = null;
        _chain = [];
        _descriptions = [];
        _dragging = false;
        _scrollTimer.Stop();
        EffectDropIndicator.IsVisible = false;
        if (editor is not null) editor.IsReordering = false;
        if (pointer?.Captured == this) pointer.Capture(null);
    }
}
