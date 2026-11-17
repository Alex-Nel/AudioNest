using AudioNest.Application;
using AudioNest.Audio;
using AudioNest.Domain;
using AudioNest.Hardware;
using AudioNest.Persistence;
using Xunit;

namespace AudioNest.Tests;

public sealed class TimelineEditingTests
{
    [Fact]
    public void TrimmingRevealsSourceAndPreservesOppositeEdgeWithoutStretching()
    {
        var project = TestProjects.Create(100, 20);
        var clip = project.Tracks[0].Clips[0];
        var trimmed = ProjectEdits.TrimStart(project, clip.Id, 50);
        var left = trimmed.Tracks[0].Clips[0];
        Assert.Equal(50, left.Start);
        Assert.Equal(30, left.SourceOffset);
        Assert.Equal(70, left.Length);
        Assert.Equal(clip.End, left.End);
        var expanded = ProjectEdits.TrimStart(trimmed, clip.Id, -100);
        Assert.Equal(20, expanded.Tracks[0].Clips[0].Start); // Original source, not padding.
        Assert.Equal(0, expanded.Tracks[0].Clips[0].SourceOffset);
        var shortened = ProjectEdits.TrimEnd(trimmed, clip.Id, 80);
        Assert.Equal(30, shortened.Tracks[0].Clips[0].Length);
        var restored = ProjectEdits.TrimEnd(shortened, clip.Id, 10000);
        Assert.Equal(left, restored.Tracks[0].Clips[0]);
        var minimum = ProjectEdits.TrimEnd(trimmed, clip.Id, 0);
        Assert.Equal(1, minimum.Tracks[0].Clips[0].Length);
        restored.Validate();
        Assert.Same(project.Assets[0], restored.Assets[0]);
    }

    [Fact]
    public void TrimmedRenderingUsesExactlyTheCorrectSourceFramesAndUndoRestoresMetadata()
    {
        var session = new ProjectSession();
        var project = TestProjects.Create(10);
        session.Open(project);
        var clip = project.Tracks[0].Clips[0];
        session.Apply("trim", p => ProjectEdits.TrimStart(p, clip.Id, 4));
        var samples = Enumerable.Range(0, 20).Select(i => i / 40f).ToArray();
        var buffer = new AudioBuffer(new DecodedAudio(new PcmFormat(48000, 2), samples));
        var result = new float[4];
        new RenderPlan(session.Project, _ => buffer).Render(4, result);
        Assert.Equal(samples[8..12], result);
        session.Undo();
        Assert.Same(project, session.Project);
        session.Redo();
        Assert.Equal(4, session.Project.Tracks[0].Clips[0].SourceOffset);
    }

    [Theory]
    [InlineData(TrackChannelLayout.Stereo, 0.8f, -0.2f)]
    [InlineData(TrackChannelLayout.Mono, 0.3f, 0.3f)]
    public void StereoAndMonoTracksHaveRealChannelSemantics(TrackChannelLayout layout, float left, float right)
    {
        var project = TestProjects.Create(1);
        project = project with { Tracks = [project.Tracks[0] with { Layout = layout }] };
        var buffer = new AudioBuffer(new DecodedAudio(new PcmFormat(48000, 2), [0.8f, -0.2f]));
        var output = new float[2];
        new RenderPlan(project, _ => buffer).Render(0, output);
        Assert.Equal(left, output[0], 5);
        Assert.Equal(right, output[1], 5);
        Assert.Throws<InvalidDataException>(() =>
            (project with { Tracks = [project.Tracks[0] with { Layout = (TrackChannelLayout)42 }] }).Validate());
    }

    [Fact]
    public void MonoPeakEnvelopeAgreesWithAntiPhaseDownmix()
    {
        var samples = Enumerable.Range(0, 1200).Select(i => i % 2 == 0 ? 0.5f : -0.5f).ToArray();
        var buffer = new AudioBuffer(new DecodedAudio(new PcmFormat(48000, 2), samples));
        Assert.Equal(new WavePeak(-0.5f, 0.5f), buffer.Peak(0, 600));
        Assert.Equal(new WavePeak(0, 0), buffer.Peak(0, 600, true));
        Assert.Equal(new WavePeak(0, 0), buffer.Peak(17, 534, true));
    }

    [Fact]
    public async Task MonoLayoutAndTrimBoundsRoundTripAndOldProjectsDefaultToStereo()
    {
        using var directory = new TempDirectory();
        var source = TestProjects.WriteWave(directory.Path, 100);
        var project = TestProjects.Create(100) with { Assets = [new(TestProjects.AssetId, source, 100)] };
        project = project with { Tracks = [project.Tracks[0] with { Layout = TrackChannelLayout.Mono }] };
        project = ProjectEdits.TrimStart(project, project.Tracks[0].Clips[0].Id, 20);
        var store = new JsonProjectStore();
        var path = Path.Combine(directory.Path, "mono.audionest");
        await store.SaveAsync(project, path);
        var loaded = await store.LoadAsync(path);
        Assert.Equal(TrackChannelLayout.Mono, loaded.Tracks[0].Layout);
        Assert.Equal(20, loaded.Tracks[0].Clips[0].SourceOffset);
        var json = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        json["tracks"]![0]!.AsObject().Remove("layout");
        json["version"] = 1;
        await File.WriteAllTextAsync(path, json.ToJsonString());
        var upgraded = await store.LoadAsync(path);
        Assert.Equal(TrackChannelLayout.Stereo, upgraded.Tracks[0].Layout);
        Assert.Equal(2, upgraded.Version);
        Assert.Equal(1, System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(path))!["version"]!.GetValue<int>());
    }
}
