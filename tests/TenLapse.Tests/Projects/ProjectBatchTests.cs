using TenLapse.Core.Projects;

namespace TenLapse.Tests.Projects;

public sealed class ProjectBatchTests
{
    [Fact]
    public void SevenSpellingsOfOnePhotoAreOneFrameOrRejected()
    {
        var project = new Project();
        string[] spellings =
        [
            @"C:\Photos\a\IMG_0001.jpg",
            @"C:/Photos/a/IMG_0001.jpg",
            @"\\?\C:\Photos\a\IMG_0001.jpg",
            @"C:\Photos\.\a\IMG_0001.jpg",
            @"C:\Photos\b\..\a\IMG_0001.jpg",
            @"C:\Photos\a\IMG_0001.jpg ",
        ];

        var added = project.AddFrames(spellings.Select(TestFrames.At));

        Assert.Equal(1, added);
        Assert.Equal(@"C:\Photos\a\IMG_0001.jpg", Assert.Single(project.Frames).SourcePath);
        Assert.Throws<ArgumentException>(() => TestFrames.At(@"Photos\a\IMG_0001.jpg"));
        Assert.Throws<ArgumentException>(() => TestFrames.At(@"C:Photos\a\IMG_0001.jpg"));
        Assert.Throws<ArgumentException>(() => TestFrames.At(@"\Photos\a\IMG_0001.jpg"));
    }

    [Theory]
    [InlineData(@"C:/Photos/a/IMG_0001.jpg", @"C:\Photos\a\IMG_0001.jpg")]
    [InlineData(@"\\?\C:\Photos\a\IMG_0001.jpg", @"C:\Photos\a\IMG_0001.jpg")]
    [InlineData(@"\\?\UNC\nas\share\IMG_0001.jpg", @"\\nas\share\IMG_0001.jpg")]
    [InlineData(@"C:\Photos\a\IMG_0001.jpg. ", @"C:\Photos\a\IMG_0001.jpg")]
    [InlineData(@"C:\Photos\a \IMG_0001.jpg", @"C:\Photos\a \IMG_0001.jpg")]
    [InlineData(@"\\?\C:\Photos\a\IMG_0001.jpg ", @"\\?\C:\Photos\a\IMG_0001.jpg ")]
    public void APathIsStoredInItsCanonicalSpelling(string given, string stored)
    {
        Assert.Equal(stored, TestFrames.At(given).SourcePath);
    }

    [Fact]
    public void ABadBatchChangesNothing()
    {
        var project = new Project();
        var frames = TestFrames.Folder("a", 3);
        var changed = 0;
        project.Changed += (_, _) => changed++;

        Assert.Throws<ArgumentNullException>(() => project.AddFrames([frames[0], null!, frames[1]]));
        Assert.Throws<InvalidOperationException>(() => project.AddFrames(Failing(frames[0])));
        Assert.Throws<ArgumentNullException>(() => project.AddSkippedFiles([new SkippedFile(@"C:\x.jpg", SkipReason.Other, ""), null!]));

        Assert.Empty(project.Frames);
        Assert.Empty(project.SkippedFiles);
        Assert.Equal(0, changed);
        Assert.False(project.IsDirty);
        Assert.Equal(3, project.AddFrames(frames));
    }

    [Fact]
    public void DuplicatesInsideOneBatchAreLeftOutToo()
    {
        var project = new Project();
        var frame = TestFrames.At(@"C:\Photos\a\IMG_0001.jpg");

        var added = project.AddFrames([frame, frame, TestFrames.At(@"C:\PHOTOS\A\img_0001.jpg")]);

        Assert.Equal(1, added);
        Assert.Equal([frame], project.Frames);
    }

    private static IEnumerable<Frame> Failing(Frame first)
    {
        yield return first;
        throw new InvalidOperationException("The folder went away.");
    }
}
