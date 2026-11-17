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
    private bool _isDarkMode;

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
        _session.Changed += OnProjectChanged;
        _session.Open(ProjectEdits.AddTrack(new AudioProject()));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? TimelineChanged;
    public event Action? ClockChanged;
    public ObservableCollection<TrackHeaderViewModel> TrackHeaders { get; } = [];
    public AudioProject Project => _session.Project;
    public AudioClip? SelectedClip => Project.Tracks.SelectMany(t => t.Clips).FirstOrDefault(c => c.Id == SelectedClipId);
    public AudioTrack? SelectedTrack => Project.Tracks.FirstOrDefault(t => t.Id == SelectedTrackId);
    public Guid? SelectedClipId { get; private set; }
    public Guid? SelectedTrackId { get; private set; }
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
    public bool HasSelectedTrack => CanEdit && SelectedTrack is not null;
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
    public double TimelineWidth => Math.Max(_timelineViewportWidth, ((double)Project.Duration / Project.SampleRate + 5) * PixelsPerSecond);
    public double TimelineHeight => Math.Max(_timelineViewportHeight, Math.Max(1, Project.Tracks.Length) * TimelineMetrics.TrackHeight);
    public AudioBuffer Buffer(Guid asset) => _library[asset];
    private PcmFormat Format => new(Project.SampleRate, 2);

    private void Notify()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        TimelineChanged?.Invoke();
        ClockChanged?.Invoke();
        foreach (var track in TrackHeaders) track.Refresh();
    }

    private void OnProjectChanged()
    {
        if (SelectedTrack is null) SelectedTrackId = Project.Tracks.FirstOrDefault()?.Id;
        if (SelectedClip is null) SelectedClipId = null;
        SyncTrackHeaders();
        _transport.SetPlan(new RenderPlan(Project, id => _library[id]));
        Notify();
    }

    public void Tick()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PositionText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PlayLabel)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PositionFramesText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanPlay)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanPause)));
        ClockChanged?.Invoke();
    }

    public void Select(Guid? track, Guid? clip)
    {
        if (!CanEdit) return;
        SelectedTrackId = track;
        SelectedClipId = clip;
        Notify();
    }

    public void Seek(long frame) { if (CanEdit) { _transport.Seek(frame); Notify(); } }
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
            ((double)Project.Duration / Project.SampleRate + 5), 0.1, 800);
        Notify();
    }
    public void Zoom(double multiplier)
    {
        PixelsPerSecond = Math.Clamp(PixelsPerSecond * multiplier, 0.1, 800);
        Notify();
    }

    public void Edit(string name, Func<AudioProject, AudioProject> edit)
    {
        if (!CanEdit) return;
        try
        {
            _transport.Pause();
            _session.Apply(name, edit);
            _status = name;
            Notify();
        }
        catch (Exception ex) { ReportError(ex); }
    }

    public void AddTrack() => Edit("Add track", ProjectEdits.AddTrack);
    public void DeleteTrack(Guid id) => Edit("Delete track", p => ProjectEdits.DeleteTrack(p, id));
    public void DeleteSelectedTrack() { if (SelectedTrackId is { } id) DeleteTrack(id); }
    public void Split() { if (SelectedClipId is { } id) Edit("Split clip", p => ProjectEdits.Split(p, id, Position)); }
    public void Delete() { if (SelectedClipId is { } id) Edit("Delete clip", p => ProjectEdits.Delete(p, id)); }
    public void Move(Guid clip, Guid track, long frame) => Edit("Move clip", p => ProjectEdits.Move(p, clip, track, frame));
    public ClipSnap ResolveClipMove(AudioClip clip, long start, bool bypassSnapping = false) =>
        IsSnappingEnabled && !bypassSnapping
            ? ClipSnapping.Resolve(Project, clip, start, Position, (long)(TimelineMetrics.SnapPixels / PixelsPerSecond * Project.SampleRate))
            : new ClipSnap(Math.Clamp(start, 0, long.MaxValue - clip.Length), null);

    public void ChangeTrackGain(Guid id, float decibels) => Edit("Track gain",
        p => ProjectEdits.UpdateTrack(p, id, t =>
        {
            var gain = t.Effects.FirstOrDefault(e => e.Kind == EffectKind.Gain)?.Value ?? 1;
            return t with
            {
                Effects = t.Effects.RemoveAll(e => e.Kind == EffectKind.Gain).Insert(0,
                    new EffectSpec(EffectKind.Gain, Math.Clamp(gain * MathF.Pow(10, decibels / 20), 0, 16)))
            };
        }));
    public void SetTrackPan(Guid id, float pan) => Edit("Track stereo balance",
        p => ProjectEdits.UpdateTrack(p, id, t => t with { Pan = Math.Clamp(pan, -1, 1) }));
    public void ToggleTrackMute(Guid id) => Edit("Toggle track mute",
        p => ProjectEdits.UpdateTrack(p, id, t => t with { Muted = !t.Muted }));
    public void ToggleTrackSolo(Guid id) => Edit("Toggle track solo",
        p => ProjectEdits.UpdateTrack(p, id, t => t with { Solo = !t.Solo }));
    public void Nudge(double seconds)
    {
        if (SelectedClip is { } c && SelectedTrackId is { } track)
            Move(c.Id, track, Math.Max(0, c.Start + (long)(seconds * Project.SampleRate)));
    }
    public void ToggleMute()
    {
        if (SelectedTrackId is { } id) Edit("Toggle track mute",
            p => ProjectEdits.UpdateTrack(p, id, t => t with { Muted = !t.Muted }));
    }
    public void ToggleSolo()
    {
        if (SelectedTrackId is { } id) Edit("Toggle track solo",
            p => ProjectEdits.UpdateTrack(p, id, t => t with { Solo = !t.Solo }));
    }
    public void ApplyGain(float decibels, bool track)
    {
        ImmutableArray<EffectSpec> Change(ImmutableArray<EffectSpec> effects)
        {
            var gain = effects.FirstOrDefault(e => e.Kind == EffectKind.Gain)?.Value ?? 1;
            return effects.RemoveAll(e => e.Kind == EffectKind.Gain)
                .Insert(0, new EffectSpec(EffectKind.Gain, Math.Clamp(gain * MathF.Pow(10, decibels / 20), 0, 16)));
        }
        if (track && SelectedTrackId is { } tid)
            Edit("Track gain", p => ProjectEdits.UpdateTrack(p, tid, t => t with { Effects = Change(t.Effects) }));
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
            Edit("Toggle track hard limiter", p => ProjectEdits.UpdateTrack(p, tid, t => t with { Effects = Change(t.Effects) }));
        else if (!track && SelectedClipId is { } cid)
            Edit("Toggle clip hard limiter", p => ProjectEdits.UpdateClip(p, cid, c => c with { Effects = Change(c.Effects) }));
    }

    public void Undo() { if (CanUndo) { _transport.Pause(); _session.Undo(); } }
    public void Redo() { if (CanRedo) { _transport.Pause(); _session.Redo(); } }
    public void Play()
    {
        if (!CanEdit) return;
        try { if (_transport.IsPlaying) _transport.Pause(); else _transport.Play(); Notify(); }
        catch (Exception ex) { ReportError(ex); }
    }
    public void StartPlayback() { if (CanPlay) Play(); }
    public void Pause() { if (CanEdit) { _transport.Pause(); Notify(); } }
    public void SkipToStart() => Seek(0);
    public void SkipToEnd() => Seek(Project.Duration);

    public async Task StopAsync()
    {
        if (IsBusy) return;
        if (_recording is null) { _transport.Stop(); Notify(); return; }
        var recording = _recording;
        var path = _recordingPath!;
        var track = _recordingTrack;
        var at = _recordingStart;
        await RunAsync(async () =>
        {
            try { await Task.Run(recording.Stop); }
            finally { _recording = null; }
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
            if (SelectedTrackId is null) AddTrack();
            Directory.CreateDirectory(_workspace);
            _recordingPath = Path.Combine(_workspace, $"Take-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.wav");
            _recordingStart = Position;
            _recordingTrack = SelectedTrackId!.Value;
            // Mono capture avoids assuming a stereo microphone; decode normalizes for the project.
            _recording = _backend.StartRecording(new PcmFormat(Project.SampleRate, 1), _recordingPath);
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

    public async Task ExportAsync(string path)
    {
        if (!CanEdit) return;
        await RunAsync(async () =>
        {
            var plan = new RenderPlan(Project, id => _library[id]);
            await Task.Run(() => WaveExporter.Export(plan, path));
            _status = "Stereo PCM16 WAV exported.";
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
