using AudioNest.Domain;

namespace AudioNest.Application;

/// <summary>UI-thread-owned editing session. History stores metadata, never sample buffers.</summary>
public sealed class ProjectSession
{
    private const int HistoryLimit = 200;
    private readonly List<(string Label, AudioProject Project)> _undo = [];
    private readonly Stack<(string Label, AudioProject Project)> _redo = [];
    private AudioProject _saved;

    public ProjectSession()
    {
        Project = new AudioProject();
        _saved = Project;
    }

    public AudioProject Project { get; private set; }
    public bool IsDirty => !ReferenceEquals(Project, _saved);
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public event Action? Changed;

    public void Apply(string label, Func<AudioProject, AudioProject> edit)
    {
        var next = edit(Project);
        if (next == Project) return;
        next.Validate();
        _undo.Add((label, Project));
        if (_undo.Count > HistoryLimit) _undo.RemoveAt(0);
        _redo.Clear();
        Project = next;
        Changed?.Invoke();
    }

    public void Undo()
    {
        if (!CanUndo) return;
        var item = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Push((item.Label, Project));
        Project = item.Project;
        Changed?.Invoke();
    }

    public void Redo()
    {
        if (!_redo.TryPop(out var item)) return;
        _undo.Add((item.Label, Project));
        Project = item.Project;
        Changed?.Invoke();
    }

    public void Open(AudioProject project)
    {
        project.Validate();
        Project = _saved = project;
        _undo.Clear();
        _redo.Clear();
        Changed?.Invoke();
    }

    public void MarkSaved()
    {
        _saved = Project;
        Changed?.Invoke();
    }
}
