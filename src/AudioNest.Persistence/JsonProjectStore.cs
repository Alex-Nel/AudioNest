using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using AudioNest.Application;
using AudioNest.Domain;

namespace AudioNest.Persistence;

/// <summary>
/// Versioned JSON plus a sibling content-addressed media directory. Media is copied
/// before atomically publishing JSON, so an interrupted save preserves the old project.
/// No media garbage collection: history and other projects may still reference it.
/// </summary>
public sealed class JsonProjectStore : IProjectStore
{
    public async Task SaveAsync(AudioProject project, string path, CancellationToken cancellationToken = default)
    {
        project.Validate();
        path = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var mediaDirectory = Path.Combine(directory, Path.GetFileName(path) + ".media");
        Directory.CreateDirectory(mediaDirectory);
        var assets = ImmutableArray.CreateBuilder<AudioAsset>();
        foreach (var asset in project.Assets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var source = File.OpenRead(asset.Path);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(source, cancellationToken)).ToLowerInvariant();
            var extension = Path.GetExtension(asset.Path).ToLowerInvariant();
            var destination = Path.Combine(mediaDirectory, hash + extension);
            if (!File.Exists(destination))
            {
                var copying = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    source.Position = 0;
                    await using (var target = new FileStream(copying, FileMode.CreateNew, FileAccess.Write))
                    {
                        await source.CopyToAsync(target, cancellationToken);
                        await target.FlushAsync(cancellationToken);
                        target.Flush(true);
                    }
                    File.Move(copying, destination, false);
                }
                finally { if (File.Exists(copying)) File.Delete(copying); }
            }
            assets.Add(asset with { Path = Path.GetRelativePath(directory, destination).Replace('\\', '/') });
        }
        var portable = project with { Assets = assets.ToImmutable() };
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write))
            {
                await JsonSerializer.SerializeAsync(stream, portable, ProjectJsonContext.Default.AudioProject, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(true);
            }
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    public async Task<AudioProject> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        path = Path.GetFullPath(path);
        await using var stream = File.OpenRead(path);
        // Avoid accidentally deserializing arbitrary-size files as project metadata.
        if (stream.Length > 16 * 1024 * 1024) throw new InvalidDataException("Project metadata exceeds 16 MB.");
        var project = await JsonSerializer.DeserializeAsync(stream, ProjectJsonContext.Default.AudioProject, cancellationToken)
            ?? throw new InvalidDataException("Empty project.");
        // V2 added track layout; V3 added clip mix; V4 adds track volume/dynamics.
        // Older sources
        // retain their sound via defaults, and remain unchanged on disk until Save.
        if (project.Version is 1 or 2 or 3) project = project with { Version = AudioProject.CurrentVersion };
        project.Validate();
        var directory = Path.GetDirectoryName(path)!;
        var assets = ImmutableArray.CreateBuilder<AudioAsset>();
        foreach (var asset in project.Assets)
        {
            if (Path.IsPathRooted(asset.Path))
                throw new InvalidDataException("Project media paths must be relative.");
            var resolved = Path.GetFullPath(Path.Combine(directory, asset.Path));
            var relative = Path.GetRelativePath(directory, resolved);
            if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new InvalidDataException("Project media path escapes the project directory.");
            if (!File.Exists(resolved)) throw new FileNotFoundException($"Missing project audio: {asset.Path}", resolved);
            assets.Add(asset with { Path = resolved });
        }
        return project with { Assets = assets.ToImmutable() };
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AudioProject))]
internal partial class ProjectJsonContext : JsonSerializerContext;
