using TenLapse.Core.Projects;
using TenLapse.Presentation.Preview;
using TenLapse.Tests.Projects;

namespace TenLapse.Tests.Preview;

/// <summary>
/// The preview forgets what it showed when the photos are not the same set any more (#35, review M2): otherwise a second
/// arrival reframes from the previous project's shape, and its develop starts on the previous project's picture.
/// </summary>
public sealed class PreviewContinuityTests
{
    [Fact]
    public void TheFirstDrawStartsAfresh()
    {
        var project = new Project();
        project.AddFrames(TestFrames.Folder("a", 3));
        Assert.True(PreviewContinuity.StartsAfresh(null, project.Snapshot));
    }

    [Fact]
    public void ChangesWithinOneProjectContinue()
    {
        var project = new Project();
        project.AddFrames(TestFrames.Folder("a", 3));
        var before = project.Snapshot;
        project.FrameSettings = new FrameSettings(FrameAspect.Ratio9x16, FitMode.Fit);
        project.AddFrames(TestFrames.Folder("b", 2));

        Assert.False(PreviewContinuity.StartsAfresh(before, project.Snapshot));
        Assert.False(PreviewContinuity.StartsAfresh(project.Snapshot, project.Snapshot));
    }

    [Fact]
    public void AProjectWithNoPhotosStartsAfresh()
    {
        var project = new Project();
        project.AddFrames(TestFrames.Folder("a", 3));
        var before = project.Snapshot;
        project.RemoveFrames(before.Frames.Select(frame => frame.Id));

        Assert.True(PreviewContinuity.StartsAfresh(before, project.Snapshot));
        Assert.True(PreviewContinuity.StartsAfresh(project.Snapshot, project.Snapshot));
    }

    [Fact]
    public void AnotherProjectStartsAfreshEvenWhenTheEmptyMomentWasMissed()
    {
        // Portrait photos, a few changes; then New project and a landscape import the render thread first sees with photos.
        var first = new Project();
        first.AddFrames(TestFrames.Folder("portrait", 3));
        first.FrameSettings = new FrameSettings(FrameAspect.Ratio9x16, FitMode.Fit);
        first.AddFrames(TestFrames.Folder("more", 2));
        var second = new Project();
        second.AddFrames(TestFrames.Folder("landscape", 4));

        Assert.True(second.Snapshot.Version < first.Snapshot.Version);
        Assert.True(PreviewContinuity.StartsAfresh(first.Snapshot, second.Snapshot));
    }
}
