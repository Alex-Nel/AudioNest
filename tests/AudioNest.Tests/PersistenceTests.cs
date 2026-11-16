using System.Text.Json.Nodes;
using AudioNest.Domain;
using AudioNest.Persistence;
using Xunit;

namespace AudioNest.Tests;

public sealed class PersistenceTests
{
    [Fact]
    public async Task SaveIsPortableAndRoundTripsEffectsAndOffsets()
    {
        using var directory = new TempDirectory();
        var source = TestProjects.WriteWave(Path.Combine(directory.Path, "original"));
        var project = TestProjects.Create() with { Assets = [new AudioAsset(TestProjects.AssetId, source, 10)] };
        var store = new JsonProjectStore();
        var oldDir = Path.Combine(directory.Path, "old");
        var path = Path.Combine(oldDir, "session.audionest");
        await store.SaveAsync(project, path);
        var json = await File.ReadAllTextAsync(path);
        Assert.DoesNotContain(source.Replace("\\", "\\\\"), json);
        File.Delete(source);
        var moved = Path.Combine(directory.Path, "moved");
        Directory.Move(oldDir, moved);
        var loaded = await store.LoadAsync(Path.Combine(moved, "session.audionest"));
        Assert.True(File.Exists(loaded.Assets[0].Path));
        Assert.Equal(project.Tracks[0].Clips[0], loaded.Tracks[0].Clips[0]);
        Assert.Equal(project.SampleRate, loaded.SampleRate);
        await store.SaveAsync(loaded, Path.Combine(moved, "session.audionest"));
        Assert.Single(Directory.GetFiles(Path.Combine(moved, "session.audionest.media")));
    }

    [Fact]
    public async Task FailedSaveLeavesExistingProjectIntact()
    {
        using var directory = new TempDirectory();
        var store = new JsonProjectStore();
        var path = Path.Combine(directory.Path, "session.audionest");
        await store.SaveAsync(new AudioProject(), path);
        var before = await File.ReadAllTextAsync(path);
        await Assert.ThrowsAsync<FileNotFoundException>(() => store.SaveAsync(TestProjects.Create(), path));
        Assert.Equal(before, await File.ReadAllTextAsync(path));
    }

    [Theory]
    [InlineData("../outside.wav")]
    [InlineData("../../outside.wav")]
    public async Task LoadRejectsPathTraversal(string mediaPath)
    {
        using var directory = new TempDirectory();
        var source = TestProjects.WriteWave(directory.Path);
        var store = new JsonProjectStore();
        var path = Path.Combine(directory.Path, "session.audionest");
        await store.SaveAsync(TestProjects.Create() with { Assets = [new(TestProjects.AssetId, source, 10)] }, path);
        var root = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        root["assets"]![0]!["path"] = mediaPath;
        await File.WriteAllTextAsync(path, root.ToJsonString());
        await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync(path));
    }

    [Fact]
    public async Task MissingMediaHasActionableError()
    {
        using var directory = new TempDirectory();
        var source = TestProjects.WriteWave(directory.Path);
        var store = new JsonProjectStore();
        var path = Path.Combine(directory.Path, "session.audionest");
        await store.SaveAsync(TestProjects.Create() with { Assets = [new(TestProjects.AssetId, source, 10)] }, path);
        var loaded = await store.LoadAsync(path);
        File.Delete(loaded.Assets[0].Path);
        var error = await Assert.ThrowsAsync<FileNotFoundException>(() => store.LoadAsync(path));
        Assert.Contains("Missing project audio", error.Message);
    }
}
