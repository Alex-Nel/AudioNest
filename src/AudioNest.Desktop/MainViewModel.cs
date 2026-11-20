using System.Collections.Immutable;
using System.ComponentModel;
using System.Collections.ObjectModel;
using AudioNest.Application;
using AudioNest.Audio;
using AudioNest.Domain;
using AudioNest.Hardware;

namespace AudioNest.Desktop;

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ProjectSession _session;
    private readonly AudioLibrary _library;
    private readonly AudioTransport _transport;
    private readonly IAudioBackend _backend;
    private readonly IProjectStore _store;
    private IAudioRecording? _recording;
    private string? _recordingPath;
    private Guid _recordingTrack;
    private long _recordingStart;
    private string? _projectPath;
    private string _status = "Ready. Import audio or record your first take.";
    private readonly string _workspace;
    private double _timelineViewportWidth = 600;
    private double _timelineViewportHeight = 300;
    private bool _isSnappingEnabled = true;
    private bool _isLeftShiftDown;
    private bool _isDarkMode = true;
    private float _outputPeak;
    private float _inputPeak;
    private ClipClipboardGroup? _clipboard;
    private readonly Dictionary<Guid, AudioBuffer> _clipboardBuffers = [];
    private readonly HashSet<Guid> _selected = [];
    private readonly Dictionary<Guid, double> _trackHeights = [];
    private TimelineTool _tool;
    private bool _keepPlayingEdit;
    private bool _trackFocused;
    private RenderPlan? _preparedPlan;
    private RenderPlan? _pendingEditPlan;
    private int _clockRate = 48000;

    public MainViewModel(ProjectSession session, AudioLibrary library, AudioTransport transport,
        IAudioBackend backend, IProjectStore store, string? workspace = null)
    {
        _session = session;
        _library = library;
        _transport = transport;
        _backend = backend;
        _store = store;
        _workspace = workspace ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AudioNest", "Media");
        Inspector = new ClipInspectorViewModel(this);
        SelectionTool = new UiCommand(() => SetTool(TimelineTool.Selection), () => CanEdit);
        RazorTool = new UiCommand(() => SetTool(TimelineTool.Razor), () => CanEdit);
        _session.Changed += OnProjectChanged;
        _session.Open(ProjectEdits.AddTrack(new AudioProject()));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? TimelineChanged;
    public event Action? ClockChanged;
    public ObservableCollection<TrackHeaderViewModel> TrackHeaders { get; } = [];
    public ClipInspectorViewModel Inspector { get; }
    public UiCommand SelectionTool { get; }
    public UiCommand RazorTool { get; }
    public TimelineTool Tool => _tool;
    public bool IsSelectionTool => Tool == TimelineTool.Selection;
    public bool IsRazorTool => Tool == TimelineTool.Razor;
    public bool CanPaste => CanEdit && _clipboard is not null && _clipboard.SampleRate == Project.SampleRate;
    public AudioProject Project => _session.Project;
    public AudioClip? SelectedClip => Project.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Id == SelectedClipId);
    public AudioTrack? SelectedTrack => Project.Tracks.FirstOrDefault(t => t.Id == SelectedTrackId);
    public Guid? SelectedClipId { get; private set; }
    public IReadOnlySet<Guid> SelectedClipIds => _selected;
    public bool IsClipSelected(Guid id) => _selected.Contains(id);
    public Guid? SelectedTrackId { get; private set; }
    public bool IsTrackFocused => _trackFocused && SelectedTrack is not null && SelectedClip is null;
    public EffectTarget? FocusedTarget => SelectedClip is { } clip ? new(clip.Id) :
        IsTrackFocused && SelectedTrack is { } track ? new(track.Id, true) : null;
    public bool HasEffectTarget => CanEdit && FocusedTarget is not null;
    public long Position => _transport.Position;
    public bool IsDirty => _session.IsDirty;
    public bool IsBusy { get; private set; }
    public bool IsRecording => _recording is not null;
    public bool CanEdit => !IsBusy && !IsRecording;
    public bool CanUndo => CanEdit && _session.CanUndo;
    public bool CanRedo => CanEdit && _session.CanRedo;
    public bool CanPlay => CanEdit && !_transport.IsPlaying && Project.Duration > 0;
    public bool CanPause => CanEdit && _transport.IsPlaying;
    public bool HasSelectedClip => CanEdit && SelectedClip is not null;
    public bool HasSelectedTrack => CanEdit && SelectedTrack is not null && FocusedTarget is not null;
    public bool IsSnappingEnabled
    {
        get => _isSnappingEnabled;
        set { if (_isSnappingEnabled != value) { _isSnappingEnabled = value; Notify(); } }
    }
    public bool IsDarkMode
    {
        get => _isDarkMode;
        set { if (_isDarkMode != value) { _isDarkMode = value; Notify(); } }
    }
    public string Title => $"{Project.Name}{(IsDirty ? " •" : "")} — AudioNest";
    public string Status => _status;
    public string SelectionStatus => SelectedClip is { } clip
        ? $"{(_selected.Count > 1 ? $"{_selected.Count} clips selected — " : "")}{ProjectEdits.FindTrack(Project, clip.Id).Name}  ·  {clip.Name}" : "";
    public string StatusBarText => SelectedClip is null ? Status :
        IsBusy || IsRecording || Status.StartsWith("Error:", StringComparison.Ordinal)
            ? $"{SelectionStatus} — {Status}" : SelectionStatus;
    public bool IsLeftAltDown { get; set; }
    public bool IsLeftShiftDown
    {
        get => _isLeftShiftDown;
        set
        {
            if (_isLeftShiftDown == value) return;
            _isLeftShiftDown = value;
            // Re-evaluate a captured clip's preview even without pointer movement.
            TimelineChanged?.Invoke();
        }
    }
    private bool SnapClips => IsSnappingEnabled != IsLeftShiftDown;
    private long SnapTolerance => (long)(TimelineMetrics.SnapPixels / PixelsPerSecond * Project.SampleRate);
    public double OutputLevel => MeterLevel(_outputPeak);
    public double InputLevel => MeterLevel(_inputPeak);
    public string OutputMeterText => $"Timeline output: {Decibels(_outputPeak)}";
    public string InputMeterText => IsRecording ? $"Microphone: {Decibels(_inputPeak)}" : "Microphone inactive";
    public string OutputDecibels => Decibels(_outputPeak);
    public string InputDecibels => IsRecording ? Decibels(_inputPeak) : "idle";
    public string PlayLabel => _transport.IsPlaying ? "Pause" : "Play";
    public string RecordLabel => IsRecording ? "Recording…" : "● Record";
    public string PositionText
    {
        get
        {
            var whole = Position / Project.SampleRate;
            var micros = Position % Project.SampleRate * 1_000_000 / Project.SampleRate;
            return $"{whole / 3600:00}:{whole / 60 % 60:00}:{whole % 60:00}.{micros:000000}";
        }
    }
    public string PositionFramesText => $"Frame {Position:N0}";
    public string ProjectInfo => $"{Project.Tracks.Length} tracks  /  {Project.SampleRate:N0} Hz  /  Stereo";
    public string SelectionText => SelectedClip is { } c
        ? $"{c.Name}\nStart: {(double)c.Start / Project.SampleRate:0.000} s\nLength: {(double)c.Length / Project.SampleRate:0.000} s\nSource in: {(double)c.SourceOffset / Project.SampleRate:0.000} s"
        : "Select a clip to edit it.\nDrag to move between tracks.";
    public string TrackText => SelectedTrack is { } t
        ? $"{t.Name}{(t.Muted ? " · muted" : "")}{(t.Solo ? " · solo" : "")}" : "No track selected";
    public double PixelsPerSecond { get; private set; } = 90;
    public RecordingSnapshot? LiveRecording { get; private set; }
    public Guid RecordingTrackId => _recordingTrack;
    public long RecordingStart => _recordingStart;
    public double TimelineWidth => Math.Max(_timelineViewportWidth, ((double)Math.Max(Math.Max(Project.Duration, Position),
        LiveRecording is null ? 0 : RecordingStart + LiveRecording.Frames) / Project.SampleRate + 5) * PixelsPerSecond);
    public double TrackContentHeight => Project.Tracks.Sum(t => TrackHeight(t.Id));
    public double TimelineHeight => Math.Max(_timelineViewportHeight, Math.Max(TimelineMetrics.TrackHeight, TrackContentHeight));
    public double TrackHeight(Guid id) => _trackHeights.GetValueOrDefault(id, TimelineMetrics.TrackHeight);
    public double TrackTop(int index) => Project.Tracks.Take(index).Sum(t => TrackHeight(t.Id));
    public int TrackIndex(Guid id)
    {
        for (var i = 0; i < Project.Tracks.Length; i++) if (Project.Tracks[i].Id == id) return i;
        return -1;
    }
    public int TrackIndexAt(double y)
    {
        if (y < 0) return -1;
        for (var i = 0; i < Project.Tracks.Length; i++)
        {
            y -= TrackHeight(Project.Tracks[i].Id);
            if (y < 0) return i;
        }
        return -1;
    }
    public void SetTrackHeight(Guid id, double height)
    {
        if (!double.IsFinite(height)) return;
        _trackHeights[id] = Math.Clamp(height, TimelineMetrics.TrackHeight, TimelineMetrics.MaximumTrackHeight);
        Notify();
    }
    public void RenameTrack(Guid id, string name)
    {
        name = name.Trim();
        if (name.Length is < 1 or > 120) { ReportError(new ArgumentException("Track name must contain 1–120 characters.")); return; }
        Edit("Rename track", p => ProjectEdits.UpdateTrack(p, id, t => t with { Name = name }), keepPlaying: true);
    }
    public AudioBuffer Buffer(Guid asset) => _library[asset, Format];
    private RenderPlan PreparePlan(AudioProject project)
    {
        var next = new RenderPlan(project, id => _library[id, new PcmFormat(project.SampleRate, 2)], previous: _preparedPlan);
        _preparedPlan = next;
        return next;
    }
    private PcmFormat Format => new(Project.SampleRate, 2);

    private void Notify()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        TimelineChanged?.Invoke();
        ClockChanged?.Invoke();
        foreach (var track in TrackHeaders) track.Refresh();
        Inspector.Refresh();
        SelectionTool.Refresh();
        RazorTool.Refresh();
    }

    private void OnProjectChanged()
    {
        var rateChanged = _clockRate != Project.SampleRate;
        var cursor = rateChanged ? ProjectSampleRate.Cursor(Position, _clockRate, Project.SampleRate) : Position;
        if (SelectedTrack is null) { _trackFocused = false; SelectedTrackId = Project.Tracks.FirstOrDefault()?.Id; }
        var valid = Project.Tracks.SelectMany(t => t.Clips).Select(c => c.Id).ToHashSet();
        _selected.RemoveWhere(id => !valid.Contains(id));
        if (SelectedClip is null) SelectedClipId = _selected.Cast<Guid?>().FirstOrDefault();
        if (SelectedClipId is { } selected) SelectedTrackId = ProjectEdits.FindTrack(Project, selected).Id;
        SyncTrackHeaders();
        var plan = _pendingEditPlan ?? PreparePlan(Project);
        if (_keepPlayingEdit) _transport.UpdatePlan(plan);
        else _transport.SetPlan(plan);
        if (rateChanged)
        {
            _transport.Seek(cursor);
            _outputPeak = _inputPeak = 0;
            _clockRate = Project.SampleRate;
        }
        Notify();
    }

    public void Tick()
    {
        if (IsRecording)
        {
            LiveRecording = _recording!.Preview.Snapshot();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TimelineWidth)));
            TimelineChanged?.Invoke();
        }
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PositionText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PlayLabel)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PositionFramesText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanPlay)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanPause)));
        _outputPeak = Math.Max(_transport.OutputMeter.ReadAndReset(), _outputPeak * 0.78f);
        _inputPeak = IsRecording ? Math.Max(_recording!.InputMeter.ReadAndReset(), _inputPeak * 0.78f) : 0;
        if (_outputPeak < 0.0001f) _outputPeak = 0;
        if (_inputPeak < 0.0001f) _inputPeak = 0;
        foreach (var property in new[] { nameof(OutputLevel), nameof(InputLevel), nameof(OutputMeterText),
            nameof(InputMeterText), nameof(OutputDecibels), nameof(InputDecibels) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        ClockChanged?.Invoke();
    }
    private static double MeterLevel(float peak) => peak <= 0 ? 0 : Math.Clamp((20 * Math.Log10(peak) + 60) / 60, 0, 1);
    private static string Decibels(float peak) => peak <= 0 ? "−∞ dBFS" : $"{20 * Math.Log10(peak):0.0} dBFS";

    public void Select(Guid? track, Guid? clip, bool additive = false, bool preserveGroup = false, bool focusTrack = true)
    {
        if (!CanEdit) return;
        if (additive && clip is { } toggle)
        {
            if (!_selected.Add(toggle)) _selected.Remove(toggle);
            clip = _selected.Contains(toggle) ? toggle : _selected.Cast<Guid?>().FirstOrDefault();
        }
        else if (!(preserveGroup && clip is { } existing && _selected.Contains(existing)))
        {
            _selected.Clear();
            if (clip is { } id) _selected.Add(id);
        }
        SelectedTrackId = track;
        SelectedClipId = clip;
        if (clip is { } primary) SelectedTrackId = ProjectEdits.FindTrack(Project, primary).Id;
        _trackFocused = clip is null && track is not null && focusTrack;
        Notify();
    }
    public void ClearSelection(Guid? destination = null) => Select(destination ?? SelectedTrackId, null, focusTrack: false);

    public void Seek(long frame) { if (CanEdit) { _transport.Seek(frame); Notify(); } }
    public void SeekFromRuler(long frame) => Seek(IsLeftShiftDown
        ? ClipSnapping.SnapPlayhead(Project, frame, SnapTolerance)
        : frame);
    public void SetTimelineViewportWidth(double width)
    {
        if (!double.IsFinite(width) || width <= 0 || Math.Abs(width - _timelineViewportWidth) < 0.5) return;
        _timelineViewportWidth = width;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TimelineWidth)));
    }
    public void SetTimelineViewportHeight(double height)
    {
        if (!double.IsFinite(height) || height <= 0 || Math.Abs(height - _timelineViewportHeight) < 0.5) return;
        _timelineViewportHeight = height;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TimelineHeight)));
    }
    public void FitProject()
    {
        PixelsPerSecond = Math.Clamp(Math.Max(1, _timelineViewportWidth - 1) /
            ((double)Math.Max(Project.Duration, Position) / Project.SampleRate + 5), 0.1, 800);
        Notify();
    }
    public void Zoom(double multiplier)
    {
        PixelsPerSecond = Math.Clamp(PixelsPerSecond * multiplier, 0.1, 800);
        Notify();
    }

    public void Edit(string name, Func<AudioProject, AudioProject> edit, bool keepPlaying = false)
    {
        if (!CanEdit) return;
        try
        {
            if (!keepPlaying) _transport.Pause();
            _keepPlayingEdit = keepPlaying;
            var next = edit(Project);
            if (next != Project)
            {
                next.Validate();
                // Analysis/processor preparation can fail. Do it before history
                // publication so the project and playing graph remain in sync.
                _pendingEditPlan = PreparePlan(next);
                _session.Apply(name, _ => next);
            }
            _status = name;
            Notify();
        }
        catch (Exception ex) { ReportError(ex); }
        finally { _keepPlayingEdit = false; _pendingEditPlan = null; }
    }

    public void AddTrack() => AddTrack(TrackChannelLayout.Stereo);
    public void AddMonoTrack() => AddTrack(TrackChannelLayout.Mono);
    private void AddTrack(TrackChannelLayout layout)
    {
        var count = Project.Tracks.Length;
        Edit(layout == TrackChannelLayout.Mono ? "Add mono track" : "Add stereo track", p => ProjectEdits.AddTrack(p, layout));
        if (Project.Tracks.Length > count) Select(Project.Tracks[^1].Id, null);
    }
    public void SetTool(TimelineTool tool) { if (CanEdit) { _tool = tool; Notify(); } }
    public void SplitAt(Guid clip, long frame) => Edit("Split clip", p => ProjectEdits.Split(p, clip, frame));
    public AudioClip PreviewTrim(AudioClip clip, long frame, bool start) => start
        ? ClipTrimming.Start(clip, frame)
        : ClipTrimming.End(clip, Project.Assets.First(a => a.Id == clip.AssetId), frame);
    public ClipTrimSnap ResolveClipTrim(AudioClip clip, long frame, bool start, bool bypassSnapping = false)
    {
        if (SnapClips && !bypassSnapping)
            return ClipSnapping.ResolveTrim(Project, clip, frame, start, Position, SnapTolerance);
        var bounded = PreviewTrim(clip, frame, start);
        return new(start ? bounded.Start : bounded.End, null);
    }
    public void Trim(Guid clip, long frame, bool start) => Edit(start ? "Trim clip start" : "Trim clip end",
        p => start ? ProjectEdits.TrimStart(p, clip, frame) : ProjectEdits.TrimEnd(p, clip, frame));
    public void Copy()
    {
        if (!HasSelectedClip) return;
        _clipboard = ClipClipboardGroup.Capture(Project, _selected);
        _clipboardBuffers.Clear();
        foreach (var item in _clipboard.Clips) _clipboardBuffers[item.Item.Asset.Id] = _library[item.Item.Asset.Id, Format];
        _status = $"{_clipboard.Clips.Length} clip(s) copied.";
        Notify();
    }
    public void Cut() { if (HasSelectedClip) { Copy(); Delete(); } }
    public void Paste() => PasteAt(SelectedTrackId, Position);
    public void PasteAt(Guid? destination, long frame)
    {
        if (!CanPaste) return;
        var group = _clipboard!;
        var baseIndex = destination is { } dest ? TrackIndex(dest) : 0;
        if (baseIndex < 0) baseIndex = Project.Tracks.Length;
        var inserted = new List<Guid>();
        var assets = new Dictionary<Guid, AudioAsset>();
        foreach (var item in group.Clips)
        {
            var source = item.Item.Asset;
            if (assets.ContainsKey(source.Id)) continue;
            var asset = Project.Assets.FirstOrDefault(a => a == source) ?? source with { Id = Guid.NewGuid() };
            assets[source.Id] = asset;
            _library.Add(asset.Id, _clipboardBuffers[source.Id]);
        }
        Edit("Paste clips", p =>
        {
            while (p.Tracks.Length < baseIndex + group.Layouts.Length)
                p = ProjectEdits.AddTrack(p, group.Layouts[Math.Clamp(p.Tracks.Length - baseIndex, 0, group.Layouts.Length - 1)]);
            foreach (var item in group.Clips)
            {
                var asset = assets[item.Item.Asset.Id];
                var clip = item.Item.Clip with { Id = Guid.NewGuid(), AssetId = asset.Id, Start = checked(Math.Max(0, frame) + item.Item.Clip.Start - group.Origin) };
                inserted.Add(clip.Id);
                p = ProjectEdits.InsertClip(p, asset, clip, p.Tracks[baseIndex + item.TrackOffset].Id);
            }
            return p;
        });
        _selected.Clear();
        _selected.UnionWith(inserted.Where(id => Project.Tracks.Any(t => t.Clips.Any(c => c.Id == id))));
        SelectedClipId = _selected.Cast<Guid?>().FirstOrDefault();
        if (SelectedClipId is { } first) SelectedTrackId = ProjectEdits.FindTrack(Project, first).Id;
        Notify();
    }
    public void DeleteTrack(Guid id) => Edit("Delete track", p => ProjectEdits.DeleteTrack(p, id), keepPlaying: true);
    public void DeleteSelectedTrack() { if (SelectedTrackId is { } id) DeleteTrack(id); }
    public void Split() { if (SelectedClipId is { } id) Edit("Split clip", p => ProjectEdits.Split(p, id, Position)); }
    public void Delete() { if (_selected.Count > 0) { var ids = _selected.ToHashSet(); Edit("Delete clips", p => ProjectEdits.DeleteClips(p, ids)); } }
    public AudioProject PreviewMove(Guid clip, Guid track, long frame)
    {
        var anchor = ProjectEdits.FindTrack(Project, clip);
        var original = anchor.Clips.First(c => c.Id == clip);
        var ids = _selected.Contains(clip) ? _selected : new HashSet<Guid> { clip };
        return ProjectEdits.MoveClips(Project, ids, frame - original.Start,
            TrackIndex(track) - TrackIndex(anchor.Id));
    }
    public void Move(Guid clip, Guid track, long frame) => Edit("Move clips", _ => PreviewMove(clip, track, frame));
    public ClipSnap ResolveClipMove(AudioClip clip, long start, bool bypassSnapping = false)
    {
        var members = Project.Tracks.SelectMany(t => t.Clips).Where(c => _selected.Contains(clip.Id) ? _selected.Contains(c.Id) : c.Id == clip.Id).ToArray();
        var minimum = clip.Start - members.Min(c => c.Start);
        var maximum = long.MaxValue - (members.Max(c => c.End) - clip.Start);
        start = Math.Clamp(start, minimum, maximum);
        var snap = SnapClips && !bypassSnapping
            ? ClipSnapping.Resolve(Project, clip, start, Position, SnapTolerance, _selected)
            : new ClipSnap(start, null);
        var bounded = Math.Clamp(snap.Start, minimum, maximum);
        return bounded == snap.Start ? snap : new ClipSnap(bounded, null);
    }

    public void ChangeTrackGain(Guid id, float decibels) => Edit("Track gain",
        p => ProjectEdits.UpdateTrack(p, id, t => t with { Volume = Math.Clamp(t.Volume * MathF.Pow(10, decibels / 20), 0, 2) }), keepPlaying: true);
    public void SetTrackVolume(Guid id, float volume) => Edit("Track volume",
        p => ProjectEdits.UpdateTrack(p, id, t => t with { Volume = float.IsFinite(volume) ? Math.Clamp(volume, 0, 2) : 1 }), keepPlaying: true);
    public void PreviewTrackMix(Guid id, double? volume, double? pan)
    {
        if (!CanEdit) return;
        var preview = ProjectEdits.UpdateTrack(Project, id, t => t with
        {
            Volume = (float)(volume ?? t.Volume),
            Pan = t.Layout == TrackChannelLayout.Mono ? 0 : (float)(pan ?? t.Pan)
        });
        _transport.UpdatePlan(PreparePlan(preview));
    }

    public void SetClipName(Guid clip, string name) => Edit("Clip name",
        p => ProjectEdits.UpdateClip(p, clip, c => c with { Name = name }), keepPlaying: true);
    public void SetClipStart(Guid clip, long start) => Edit("Clip start", p => ProjectEdits.SetClipStart(p, clip, start));
    public void SetClipMuted(Guid clip, bool muted) => Edit("Clip mute",
        p => ProjectEdits.UpdateClip(p, clip, c => c with { Muted = muted }), keepPlaying: true);
    public void SetClipLocked(Guid clip, bool locked) => Edit("Clip lock",
        p => ProjectEdits.UpdateClip(p, clip, c => c with { Locked = locked }), keepPlaying: true);
    private static AudioClip ClipMix(AudioClip clip, double? volume, double? pan) => clip with
    {
        Volume = (float)(volume ?? clip.Volume),
        Pan = (float)(pan ?? clip.Pan)
    };
    public void SetClipMix(Guid clip, double? volume, double? pan) => Edit("Clip mix",
        p => ProjectEdits.UpdateClip(p, clip, c => ClipMix(c, volume, pan)), keepPlaying: true);
    public void PreviewClipMix(Guid clip, double? volume, double? pan)
    {
        if (!CanEdit) return;
        var preview = ProjectEdits.UpdateClip(Project, clip, c => ClipMix(c, volume, pan));
        _transport.UpdatePlan(PreparePlan(preview));
    }
    public void AddClipEffect(EffectKind kind)
    {
        if (SelectedClipId is not { } clip) return;
        var effect = BuiltInEffects.Create(kind);
        Edit("Add clip effect", p => ProjectEdits.UpdateClip(p, clip, c => c with { Effects = c.Effects.Add(effect) }), keepPlaying: true);
    }
    public void AddEffect(EffectKind kind)
    {
        if (!HasEffectTarget || FocusedTarget is not { } target) return;
        var effect = BuiltInEffects.Create(kind);
        Edit($"Add {BuiltInEffects.Name(kind)}", p => UpdateEffects(p, target, chain => chain.Add(effect)), keepPlaying: true);
    }
    private static AudioProject UpdateEffects(AudioProject project, EffectTarget target, Func<ImmutableArray<EffectSpec>, ImmutableArray<EffectSpec>> change) =>
        target.IsTrack ? ProjectEdits.UpdateTrack(project, target.Id, t =>
        {
            var next = change(t.Effects);
            return next == t.Effects ? t : t with { Effects = next };
        }) : ProjectEdits.UpdateClip(project, target.Id, c =>
        {
            var next = change(c.Effects);
            return next == c.Effects ? c : c with { Effects = next };
        });
    public void ChangeEffect(EffectTarget target, int index, EffectSpec expected, EffectSpec? replacement)
    {
        if (!HasEffectTarget || FocusedTarget != target) return;
        Edit(replacement is null ? "Remove effect" : "Change effect", p => UpdateEffects(p, target, chain =>
            index < 0 || index >= chain.Length || !ReferenceEquals(chain[index], expected) || replacement == expected ? chain :
                replacement is null ? chain.RemoveAt(index) : chain.SetItem(index, replacement)), keepPlaying: true);
    }
    public void PreviewEffect(EffectTarget target, int index, EffectSpec expected, EffectSpec replacement)
    {
        if (!HasEffectTarget || FocusedTarget != target) return;
        var preview = UpdateEffects(Project, target, chain =>
            index < 0 || index >= chain.Length || !ReferenceEquals(chain[index], expected) ? chain : chain.SetItem(index, replacement));
        _transport.UpdatePlan(PreparePlan(preview));
    }
    public void SetTrackName(Guid id, string name) => Edit("Track name",
        p => ProjectEdits.UpdateTrack(p, id, t => t with { Name = name }), keepPlaying: true);
    public void SetTrackMuted(Guid id, bool muted) => Edit("Track mute",
        p => ProjectEdits.UpdateTrack(p, id, t => t with { Muted = muted }), keepPlaying: true);
    public void SetTrackSolo(Guid id, bool solo) => Edit("Track solo",
        p => ProjectEdits.UpdateTrack(p, id, t => t with { Solo = solo }), keepPlaying: true);
    public void SetTrackMix(Guid id, double? volume, double? pan) => Edit("Track mix",
        p => ProjectEdits.UpdateTrack(p, id, t => t with { Volume = (float)(volume ?? t.Volume), Pan = (float)(pan ?? t.Pan) }), keepPlaying: true);
    public void ChangeClipEffect(Guid clip, int index, EffectSpec expected, EffectSpec? replacement)
    {
        Edit(replacement is null ? "Remove clip effect" : "Change clip effect", p => ProjectEdits.UpdateClip(p, clip, c =>
            index < 0 || index >= c.Effects.Length || !ReferenceEquals(c.Effects[index], expected) || replacement == expected ? c
                : c with { Effects = replacement is null ? c.Effects.RemoveAt(index) : c.Effects.SetItem(index, replacement) }), keepPlaying: true);
    }
    public void PreviewClipEffect(Guid clip, int index, EffectSpec expected, EffectSpec replacement)
    {
        if (!CanEdit) return;
        var preview = ProjectEdits.UpdateClip(Project, clip, c =>
            index < 0 || index >= c.Effects.Length || !ReferenceEquals(c.Effects[index], expected) ? c
                : c with { Effects = c.Effects.SetItem(index, replacement) });
        _transport.UpdatePlan(PreparePlan(preview));
    }
    public void RestoreMixPreview()
    {
        if (CanEdit) _transport.UpdatePlan(PreparePlan(Project));
    }
    public void SetTrackPan(Guid id, float pan) => Edit("Track stereo balance",
        p => ProjectEdits.UpdateTrack(p, id, t => t with { Pan = t.Layout == TrackChannelLayout.Mono ? 0 : Math.Clamp(pan, -1, 1) }), keepPlaying: true);
    public void ToggleTrackMute(Guid id) => Edit("Toggle track mute",
        p => ProjectEdits.UpdateTrack(p, id, t => t with { Muted = !t.Muted }), keepPlaying: true);
    public void ToggleTrackSolo(Guid id) => Edit("Toggle track solo",
        p => ProjectEdits.UpdateTrack(p, id, t => t with { Solo = !t.Solo }), keepPlaying: true);
    public void Nudge(double seconds)
    {
        if (SelectedClip is { } c && SelectedTrackId is { } track)
            Move(c.Id, track, Math.Max(0, c.Start + (long)(seconds * Project.SampleRate)));
    }
    public void ToggleMute()
    {
        if (SelectedTrackId is { } id) ToggleTrackMute(id);
    }
    public void ToggleSolo()
    {
        if (SelectedTrackId is { } id) ToggleTrackSolo(id);
    }
    public void ApplyGain(float decibels, bool track)
    {
        ImmutableArray<EffectSpec> Change(ImmutableArray<EffectSpec> effects)
        {
            var gain = effects.FirstOrDefault(e => e.Kind == EffectKind.Gain)?.Value ?? 1;
            return effects.RemoveAll(e => e.Kind == EffectKind.Gain)
                .Insert(0, new EffectSpec(EffectKind.Gain, Math.Clamp(gain * MathF.Pow(10, decibels / 20), 0, track ? 2 : 16)));
        }
        if (track && SelectedTrackId is { } tid)
            Edit("Track gain", p => ProjectEdits.UpdateTrack(p, tid, t => t with { Effects = Change(t.Effects) }), keepPlaying: true);
        else if (!track && SelectedClipId is { } cid)
            Edit("Clip gain", p => ProjectEdits.UpdateClip(p, cid, c => c with { Effects = Change(c.Effects) }));
    }
    public void ToggleLimiter(bool track)
    {
        static ImmutableArray<EffectSpec> Change(ImmutableArray<EffectSpec> effects) =>
            effects.Any(e => e.Kind == EffectKind.HardLimiter)
                ? effects.RemoveAll(e => e.Kind == EffectKind.HardLimiter)
                : effects.Add(new EffectSpec(EffectKind.HardLimiter, 0.9f));
        if (track && SelectedTrackId is { } tid)
            Edit("Toggle track hard limiter", p => ProjectEdits.UpdateTrack(p, tid, t => t with { Effects = Change(t.Effects) }), keepPlaying: true);
        else if (!track && SelectedClipId is { } cid)
            Edit("Toggle clip hard limiter", p => ProjectEdits.UpdateClip(p, cid, c => c with { Effects = Change(c.Effects) }));
    }

    public void Undo() { if (CanUndo) { _transport.Pause(); _session.Undo(); } }
    public void Redo() { if (CanRedo) { _transport.Pause(); _session.Redo(); } }
    public async Task ChangeSampleRateAsync(int rate)
    {
        if (!CanEdit || rate == Project.SampleRate) return;
        await RunAsync(async () =>
        {
            ProjectSampleRate.Validate(rate);
            _transport.Pause();
            _outputPeak = 0;
            var format = new PcmFormat(rate, 2);
            var staged = new Dictionary<Guid, AudioBuffer>();
            foreach (var asset in Project.Assets)
            {
                var buffer = _library.TryGet(asset.Id, format, out var cached) ? cached! :
                    await Task.Run(() => new AudioBuffer(_backend.Decode(asset.Path, format)));
                if (buffer.Format != format) throw new InvalidDataException("Decoded audio does not match the requested sample rate.");
                staged.Add(asset.Id, buffer);
            }
            var next = ProjectSampleRate.Change(Project, rate, staged.ToDictionary(p => p.Key, p => p.Value.FrameCount));
            // Stage decoding, validation, and normalization before publishing either
            // metadata or cache variants. Failure leaves the old session playable.
            var plan = await Task.Run(() => new RenderPlan(next, id => staged[id]));
            foreach (var (id, buffer) in staged) _library.Add(id, buffer);
            _pendingEditPlan = _preparedPlan = plan;
            try { _session.Apply("Change project sample rate", _ => next); }
            finally { _pendingEditPlan = null; }
            _status = $"Project sample rate changed to {rate:N0} Hz. Playback is paused.";
        });
    }
    public void Play()
    {
        if (!CanEdit) return;
        try { if (_transport.IsPlaying) _transport.Pause(); else _transport.Play(); Notify(); }
        catch (Exception ex) { ReportError(ex); }
    }
    public void StartPlayback() { if (CanPlay) Play(); }
    public void Pause() { if (CanEdit) { _transport.Pause(); _outputPeak = 0; Notify(); } }
    public void SkipToStart() => Seek(0);
    public void SkipToEnd() => Seek(Project.Duration);

    public async Task StopAsync()
    {
        if (IsBusy) return;
        if (_recording is null) { _transport.Stop(); _outputPeak = 0; Notify(); return; }
        var recording = _recording;
        var path = _recordingPath!;
        var track = _recordingTrack;
        var at = _recordingStart;
        await RunAsync(async () =>
        {
            try { await Task.Run(recording.Stop); }
            finally { _recording = null; _inputPeak = 0; LiveRecording = null; }
            await ImportCoreAsync(path, track, at);
            _status = "Recording added. The original take is preserved.";
        });
    }

    public void Record()
    {
        if (!CanEdit) return;
        try
        {
            _transport.Pause();
            _outputPeak = _inputPeak = 0;
            if (SelectedTrackId is null) AddTrack();
            Directory.CreateDirectory(_workspace);
            _recordingPath = Path.Combine(_workspace, $"Take-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.wav");
            _recordingStart = Position;
            _recordingTrack = SelectedTrackId!.Value;
            // Mono capture avoids assuming a stereo microphone; decode normalizes for the project.
            _recording = _backend.StartRecording(new PcmFormat(Project.SampleRate, 1), _recordingPath);
            LiveRecording = _recording.Preview.Snapshot();
            _status = "Recording microphone. Press Stop to finish. Live monitoring is off.";
            Notify();
        }
        catch (Exception ex) { ReportError(ex); }
    }

    public async Task ImportAsync(string path)
    {
        if (!CanEdit) return;
        await RunAsync(async () =>
        {
            _transport.Pause();
            if (SelectedTrackId is null) _session.Apply("Add track", ProjectEdits.AddTrack);
            Directory.CreateDirectory(_workspace);
            var owned = Path.Combine(_workspace, $"{Guid.NewGuid():N}{Path.GetExtension(path)}");
            await Task.Run(() => File.Copy(path, owned));
            await ImportCoreAsync(owned, SelectedTrackId!.Value, Position, Path.GetFileNameWithoutExtension(path));
            _status = "Audio imported. Your source file is unchanged.";
        });
    }

    private async Task ImportCoreAsync(string path, Guid track, long at, string? name = null)
    {
        var id = Guid.NewGuid();
        var format = Format;
        var buffer = await Task.Run(() => new AudioBuffer(_backend.Decode(path, format)));
        _library.Add(id, buffer);
        _session.Apply("Import audio", p => ProjectEdits.Import(p,
            new AudioAsset(id, path, buffer.FrameCount), name ?? Path.GetFileNameWithoutExtension(path), track, at));
    }

    public void New()
    {
        if (!CanEdit) return;
        _transport.Stop();
        _projectPath = null;
        _session.Open(ProjectEdits.AddTrack(new AudioProject()));
        _library.Clear();
        _status = "New project.";
        Notify();
    }

    public async Task OpenAsync(string path)
    {
        if (!CanEdit) return;
        await RunAsync(async () =>
        {
            _transport.Pause();
            var project = await _store.LoadAsync(path);
            // Stage all decoding first. A failed open leaves the current project playable.
            var staged = new List<(AudioAsset Asset, AudioBuffer Buffer)>();
            foreach (var asset in project.Assets)
            {
                var buffer = await Task.Run(() => new AudioBuffer(_backend.Decode(asset.Path, new PcmFormat(project.SampleRate, 2))));
                if (buffer.FrameCount != asset.FrameCount) throw new InvalidDataException("Project media length has changed.");
                staged.Add((asset, buffer));
            }
            _transport.Stop();
            _library.Clear();
            foreach (var item in staged) _library.Add(item.Asset.Id, item.Buffer);
            _projectPath = path;
            _session.Open(project);
            _status = "Project opened.";
        });
    }

    public async Task SaveAsync(string? path = null)
    {
        if (!CanEdit) return;
        path ??= _projectPath;
        if (path is null) return;
        await RunAsync(async () =>
        {
            await _store.SaveAsync(Project, path);
            _projectPath = path;
            _session.MarkSaved();
            _status = "Project and media saved.";
        });
    }
    public string? ProjectPath => _projectPath;

    public async Task ExportAsync(string path, AudioFileFormat fileFormat = AudioFileFormat.Wav)
    {
        if (!CanEdit) return;
        await RunAsync(async () =>
        {
            var plan = new RenderPlan(Project, id => _library[id, Format], previous: _preparedPlan);
            await Task.Run(() => AudioExporter.Export(plan, path, fileFormat, _backend));
            _status = $"{AudioFileType.For(fileFormat).Name} exported.";
        });
    }

    private async Task RunAsync(Func<Task> action)
    {
        IsBusy = true;
        _status = "Working…";
        Notify();
        try { await action(); }
        catch (Exception ex) { ReportError(ex); }
        finally { IsBusy = false; Notify(); }
    }

    public void ReportError(Exception ex)
    {
        _status = $"Error: {ex.Message}";
        Notify();
    }

    private void SyncTrackHeaders()
    {
        for (var i = TrackHeaders.Count - 1; i >= 0; i--)
            if (!Project.Tracks.Any(t => t.Id == TrackHeaders[i].Id)) TrackHeaders.RemoveAt(i);
        for (var i = 0; i < Project.Tracks.Length; i++)
        {
            var track = Project.Tracks[i];
            var existing = TrackHeaders.FirstOrDefault(t => t.Id == track.Id);
            if (existing is null) TrackHeaders.Insert(i, new TrackHeaderViewModel(this, track.Id));
            else if (TrackHeaders.IndexOf(existing) != i) TrackHeaders.Move(TrackHeaders.IndexOf(existing), i);
        }
    }

    public void Dispose()
    {
        _session.Changed -= OnProjectChanged;
        _recording?.Dispose();
        _recording = null;
        _transport.Dispose();
    }
}
