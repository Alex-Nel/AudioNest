using AudioNest.Domain;

namespace AudioNest.Application;

public interface IProjectStore
{
    Task<AudioProject> LoadAsync(string path, CancellationToken cancellationToken = default);
    Task SaveAsync(AudioProject project, string path, CancellationToken cancellationToken = default);
}
