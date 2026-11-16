using AudioNest.Application;
using AudioNest.Domain;
using Xunit;

namespace AudioNest.Tests;

public sealed class EditingTests
{
    [Fact]
    public void SplitSharesSourceAndPreservesOffsetsAndEffects()
    {
        var project = TestProjects.Create(100, 20);
        var original = project.Tracks[0].Clips[0] with { SourceOffset = 10, Length = 90, Effects = [new(EffectKind.Gain, 0.5f)] };
        project = ProjectEdits.UpdateClip(project, original.Id, _ => original);
        var edited = ProjectEdits.Split(project, original.Id, 50);
        edited.Validate();
        var clips = edited.Tracks[0].Clips;
        Assert.Equal(2, clips.Length);
        Assert.Equal(30, clips[0].Length);
        Assert.Equal(40, clips[1].SourceOffset);
        Assert.Equal(60, clips[1].Length);
        Assert.Equal(original.AssetId, clips[1].AssetId);
        Assert.Equal(original.Effects, clips[1].Effects);
        Assert.Equal(90, project.Tracks[0].Clips[0].Length);
        Assert.Same(project.Assets[0], edited.Assets[0]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(20)]
    public void SplitOutsideInteriorIsNoOp(long at)
    {
        var project = TestProjects.Create();
        Assert.Same(project, ProjectEdits.Split(project, project.Tracks[0].Clips[0].Id, at));
    }

    [Fact]
    public void MoveBetweenTracksAndDeleteNeverRemoveSource()
    {
        var project = ProjectEdits.AddTrack(TestProjects.Create());
        var clip = project.Tracks[0].Clips[0];
        var moved = ProjectEdits.Move(project, clip.Id, project.Tracks[1].Id, 42);
        moved.Validate();
        Assert.Empty(moved.Tracks[0].Clips);
        Assert.Equal(42, moved.Tracks[1].Clips[0].Start);
        Assert.Single(ProjectEdits.Delete(moved, clip.Id).Assets);
    }

    [Fact]
    public void UndoRedoDirtyStateAndFailedEditsAreAtomic()
    {
        var session = new ProjectSession();
        session.Open(TestProjects.Create());
        var original = session.Project;
        session.Apply("add", ProjectEdits.AddTrack);
        var edited = session.Project;
        Assert.True(session.IsDirty);
        session.MarkSaved();
        Assert.False(session.IsDirty);
        session.Undo();
        Assert.Same(original, session.Project);
        Assert.True(session.IsDirty);
        session.Redo();
        Assert.Same(edited, session.Project);
        Assert.False(session.IsDirty);
        Assert.Throws<InvalidDataException>(() => session.Apply("invalid", p => p with { SampleRate = 0 }));
        Assert.Same(edited, session.Project);
        session.Undo();
        session.Apply("branch", ProjectEdits.AddTrack);
        Assert.False(session.CanRedo);
    }

    [Fact]
    public void ValidationRejectsOutOfBoundsUnknownEffectsAndDuplicateIds()
    {
        var project = TestProjects.Create();
        var clip = project.Tracks[0].Clips[0];
        Assert.Throws<InvalidDataException>(() => ProjectEdits.UpdateClip(project, clip.Id, c => c with { Length = 11 }).Validate());
        Assert.Throws<InvalidDataException>(() => ProjectEdits.UpdateClip(project, clip.Id, c => c with { Start = long.MaxValue }).Validate());
        Assert.Throws<InvalidDataException>(() => ProjectEdits.UpdateClip(project, clip.Id, c => c with { Effects = [new((EffectKind)99, 1)] }).Validate());
        Assert.Throws<InvalidDataException>(() => (project with { Tracks = project.Tracks.Add(project.Tracks[0]) }).Validate());
        Assert.Throws<InvalidDataException>(() => (project with { Version = 999 }).Validate());
        Assert.Throws<InvalidDataException>(() => (project with { Assets = [null!] }).Validate());
        Assert.Throws<InvalidDataException>(() => (project with { Tracks = [null!] }).Validate());
        Assert.Throws<InvalidDataException>(() => ProjectEdits.UpdateClip(project, clip.Id, c => c with { Effects = [null!] }).Validate());
    }
}
