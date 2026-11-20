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

    [Theory]
    [InlineData(true, 396, 400)]
    [InlineData(true, 496, 500)]
    [InlineData(false, 396, 400)]
    [InlineData(false, 496, 500)]
    public void TrimSnapsEitherEdgeToStartsAndEndsOnAnyTrack(bool start, long proposed, long expected)
    {
        var project = TestProjects.Create(1000);
        var resizing = project.Tracks[0].Clips[0] with { Start = 200, SourceOffset = 200, Length = 600 };
        var target = resizing with { Id = Guid.NewGuid(), Start = 400, SourceOffset = 0, Length = 100 };
        project = project with { Tracks = [project.Tracks[0] with { Clips = [resizing] }, new AudioTrack(), new AudioTrack { Clips = [target] }] };
        var snap = ClipSnapping.ResolveTrim(project, resizing, proposed, start, 900, 8);
        Assert.Equal(new ClipTrimSnap(expected, expected), snap);
        var trimmed = start ? ClipTrimming.Start(resizing, snap.Frame) : ClipTrimming.End(resizing, project.Assets[0], snap.Frame);
        Assert.Equal(start ? resizing.End : resizing.Start, start ? trimmed.End : trimmed.Start);
    }

    [Fact]
    public void TrimRejectsUnreachableTargetsIgnoresItselfAndChoosesNearestValidEdge()
    {
        var project = TestProjects.Create(1000);
        var clip = project.Tracks[0].Clips[0] with { Start = 200, SourceOffset = 100, Length = 600 };
        var other = clip with { Id = Guid.NewGuid(), Start = 99, SourceOffset = 0, Length = 6 };
        project = project with { Tracks = [project.Tracks[0] with { Clips = [clip, other] }] };
        Assert.Equal(new ClipTrimSnap(105, 105), ClipSnapping.ResolveTrim(project, clip, 101, true, 50, 8));
        Assert.Equal(new ClipTrimSnap(196, null), ClipSnapping.ResolveTrim(project, clip, 196, true, 50, 8));
        Assert.Equal(new ClipTrimSnap(796, null), ClipSnapping.ResolveTrim(project, clip, 796, false, 50, 8));
        Assert.Equal(new ClipTrimSnap(100, null), ClipSnapping.ResolveTrim(project, clip, -100, true, 50, 0));
        Assert.Equal(new ClipTrimSnap(799, null), ClipSnapping.ResolveTrim(project, clip, 1000, true, 50, 0));
        Assert.Equal(new ClipTrimSnap(201, null), ClipSnapping.ResolveTrim(project, clip, 0, false, 50, 0));
        Assert.Equal(new ClipTrimSnap(1100, null), ClipSnapping.ResolveTrim(project, clip, long.MaxValue, false, 50, 0));
        var locked = clip with { Locked = true };
        Assert.Equal(new ClipTrimSnap(200, null), ClipSnapping.ResolveTrim(project, locked, 105, true, 105, 8));
        Assert.Equal(new ClipTrimSnap(800, null), ClipSnapping.ResolveTrim(project, locked, 105, false, 105, 8));
    }

    [Fact]
    public void TrimCanSnapToPlayheadAndRemainsSafeAtExtremeTimelinePositions()
    {
        var project = TestProjects.Create(1000);
        var clip = project.Tracks[0].Clips[0];
        Assert.Equal(new ClipTrimSnap(300, 300), ClipSnapping.ResolveTrim(project, clip, 304, true, 300, 8));
        Assert.Equal(new ClipTrimSnap(700, 700), ClipSnapping.ResolveTrim(project, clip, 696, false, 700, 8));
        clip = clip with { Start = long.MaxValue - 2000 };
        project = project with { Tracks = [project.Tracks[0] with { Clips = [clip] }] };
        var result = ClipSnapping.ResolveTrim(project, clip, long.MaxValue, false, long.MaxValue, 8);
        Assert.Equal(new ClipTrimSnap(clip.End, null), result);
    }
}
