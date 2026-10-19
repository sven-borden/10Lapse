using TenLapse.Core.Projects;
using TenLapse.Core.Timing;
using TenLapse.Media.Decoding;
using TenLapse.Media.Export;
using TenLapse.Media.Inspection;
using TenLapse.Tests.Media;
using static TenLapse.Tests.Media.MediaSkips;

namespace TenLapse.Tests.Fixtures;

/// <summary>The fixture folder through the export: the good photos play, a broken one fails the export with its frame and reason.</summary>
[Collection(nameof(Media.RealEncoderCollection))]
public sealed class PhotoFixtureExportTests : IDisposable
{
    private static readonly VideoExportSettings _small = new(320, 180, new Fraction(30, 1));

    private readonly PhotoFixtures _scratch = new();

    public void Dispose() => _scratch.Dispose();

    [Fact]
    public async Task EveryPhotoThatDecodesExportsAsOneFrame()
    {
        SkipWithoutH264Encoder();
        var good = PhotoFixtureCatalogue.All.Where(fixture => fixture.Decodes).Select(fixture => fixture.Path).ToList();
        var output = _scratch.PathOf("good.mp4");

        var result = await ExportOrSkipAsync(new PhotoFileSource(good), output, _small);

        Assert.Equal(good.Count, result.FrameCount);
        Assert.Equal(good.Count, VideoFileReader.Read(output).FrameCount);
    }

    [Fact]
    public async Task ABrokenPhotoFailsTheExportAtItsFrameWithItsReasonAndLeavesNoFile()
    {
        SkipWithoutH264Encoder();
        var good = PhotoFixtureCatalogue.Get("orientation-6.jpg").Path;
        foreach (var fixture in PhotoFixtureCatalogue.All.Where(fixture => !fixture.Decodes))
        {
            var output = _scratch.PathOf(Path.ChangeExtension(fixture.Name, ".mp4"));

            var exception = await Assert.ThrowsAsync<VideoExportException>(
                () => ExportOrSkipAsync(new PhotoFileSource([good, good, fixture.Path, good]), output, _small));
            await ExportCleanup.WhenDone(output);

            Assert.Equal(VideoExportFailure.FrameUnreadable, exception.Failure);
            Assert.Equal(2, exception.FrameIndex);
            Assert.Equal(fixture.Path, exception.SourcePath);
            var failure = Assert.IsType<PhotoDecodeException>(exception.InnerException);
            Assert.Equal(2, failure.FrameIndex);
            Assert.Equal(fixture.Reason, failure.Failure.Reason);
            Assert.Equal(fixture.Path, failure.Failure.Path);
            Assert.False(File.Exists(output), fixture.Name);
        }
    }
}
