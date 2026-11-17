using AudioNest.Application;
using AudioNest.Domain;
using Xunit;

namespace AudioNest.Tests;

public sealed class SnappingTests
{
    [Fact]
    public void StartAndEndSnapToPlayheadWithinTolerance()
    {
        var project = TestProjects.Create(100);
        var moving = project.Tracks[0].Clips[0];
        Assert.Equal(new ClipSnap(300, 300), ClipSnapping.Resolve(project, moving, 296, 300, 8));
        Assert.Equal(new ClipSnap(200, 300), ClipSnapping.Resolve(project, moving, 204, 300, 8));
        Assert.Equal(new ClipSnap(290, null), ClipSnapping.Resolve(project, moving, 290, 300, 8));
    }

    [Fact]
    public void SnapsBothEdgesToOtherClipEdgesAcrossTracksButNotItself()
    {
        var project = TestProjects.Create(100);
        var moving = project.Tracks[0].Clips[0];
        var other = moving with { Id = Guid.NewGuid(), Start = 400, Length = 100 };
        project = project with { Tracks = project.Tracks.Add(new AudioTrack { Clips = [other] }) };
        Assert.Equal(new ClipSnap(400, 400), ClipSnapping.Resolve(project, moving, 396, 900, 8));
        Assert.Equal(new ClipSnap(300, 400), ClipSnapping.Resolve(project, moving, 303, 900, 8));
        Assert.Equal(new ClipSnap(500, 500), ClipSnapping.Resolve(project, moving, 497, 900, 8));
        Assert.Equal(new ClipSnap(94, null), ClipSnapping.Resolve(project, moving, 94, 900, 8));
    }

    [Fact]
    public void ClosestSnapWinsAndNeverCreatesNegativeStart()
    {
        var project = TestProjects.Create(100);
        var moving = project.Tracks[0].Clips[0];
        project = project with
        {
            Tracks = [project.Tracks[0] with { Clips = [moving, moving with { Id = Guid.NewGuid(), Start = 305 }] }]
        };
        Assert.Equal(new ClipSnap(305, 305), ClipSnapping.Resolve(project, moving, 304, 300, 10));
        Assert.True(ClipSnapping.Resolve(project, moving, -10, 50, 100).Start >= 0);
    }
}
