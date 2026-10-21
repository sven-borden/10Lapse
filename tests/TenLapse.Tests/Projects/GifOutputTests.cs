using TenLapse.Core.Projects;
using TenLapse.Core.Timing;

namespace TenLapse.Tests.Projects;

/// <summary>The animated GIF's size, rate and frames (#51).</summary>
public sealed class GifOutputTests
{
    [Theory]
    [InlineData(GifSize.Small, FrameAspect.Ratio16x9, 320, 180)]
    [InlineData(GifSize.Medium, FrameAspect.Ratio16x9, 480, 270)]
    [InlineData(GifSize.Large, FrameAspect.Ratio16x9, 640, 360)]
    [InlineData(GifSize.Medium, FrameAspect.Ratio9x16, 270, 480)]
    [InlineData(GifSize.Medium, FrameAspect.Ratio1x1, 480, 480)]
    [InlineData(GifSize.Medium, FrameAspect.Ratio4x5, 384, 480)]
    [InlineData(GifSize.Medium, FrameAspect.Ratio4x3, 480, 360)]
    [InlineData(GifSize.Small, FrameAspect.Ratio4x5, 256, 320)]
    public void TheLongSideIsThePresetAndTheShortSideFollowsTheShape(GifSize size, FrameAspect aspect, int width, int height)
    {
        Assert.Equal(new PixelSize(width, height), GifOutput.Size(size, aspect));
    }

    [Fact]
    public void MatchPhotosTakesThePhotosShapeOnTheLongSide()
    {
        Assert.Equal(new PixelSize(480, 320), GifOutput.Size(GifSize.Medium, FrameAspect.MatchPhotos, new PixelSize(6000, 4000)));
        Assert.Equal(new PixelSize(360, 480), GifOutput.Size(GifSize.Medium, FrameAspect.MatchPhotos, new PixelSize(3024, 4032)));

        // A 30:1 strip still has two rows; every side is even.
        Assert.Equal(new PixelSize(640, 22), GifOutput.Size(GifSize.Large, FrameAspect.MatchPhotos, new PixelSize(30000, 1000)));
        Assert.Equal(new PixelSize(320, 2), GifOutput.Size(GifSize.Small, FrameAspect.MatchPhotos, new PixelSize(100000, 100)));
        Assert.Throws<ArgumentException>(() => GifOutput.Size(GifSize.Medium, FrameAspect.MatchPhotos));
    }

    [Fact]
    public void EverySizeIsEvenAtEveryShape()
    {
        foreach (var size in Enum.GetValues<GifSize>())
        {
            foreach (var aspect in Enum.GetValues<FrameAspect>().Where(aspect => aspect != FrameAspect.MatchPhotos))
            {
                var pixels = GifOutput.Size(size, aspect);
                Assert.True(pixels.Width % 2 == 0 && pixels.Height % 2 == 0, $"{size} {aspect}: {pixels.Width}x{pixels.Height}");
                Assert.Equal(GifOutput.LongSide(size), Math.Max(pixels.Width, pixels.Height));
            }
        }
    }

    [Fact]
    public void TheSnapshotsVideoSizeIsTheGifsWhenGifIsChosen()
    {
        var project = new Project();
        project.AddFrames(TestFrames.Folder(@"C:\Photos", 1));
        Assert.Equal(new PixelSize(1920, 1080), project.Snapshot.VideoSize);

        project.Output = project.Output with { Format = VideoFormat.Gif, Gif = new GifSettings(GifSize.Large, GifFrameRate.Fps20) };
        Assert.Equal(new PixelSize(640, 360), project.Snapshot.VideoSize);

        // The resolution and the GIF settings are kept apart: back to MP4, back to 1080p.
        project.Output = project.Output with { Format = VideoFormat.Mp4H264 };
        Assert.Equal(new PixelSize(1920, 1080), project.Snapshot.VideoSize);
        Assert.Equal(new GifSettings(GifSize.Large, GifFrameRate.Fps20), project.Output.Gif);
    }

    [Fact]
    public void ThePresetsAreRatesGifTimesExactly()
    {
        // 10, 5 and 4 cs a frame.
        Assert.Equal([new Fraction(10, 1), new Fraction(20, 1), new Fraction(25, 1)], Enum.GetValues<GifFrameRate>().Select(GifOutput.Rate));
        Assert.All(Enum.GetValues<GifFrameRate>(), preset => Assert.Equal(0, 100 * GifOutput.Rate(preset).Denominator % GifOutput.Rate(preset).Numerator));
        Assert.Equal(GifSettings.Default, new GifSettings(GifSize.Medium, GifFrameRate.Fps10));
        Assert.Equal(GifSettings.Default, OutputSettings.Default.Gif);
    }

    [Theory]
    [InlineData(30, 1, GifFrameRate.Fps10, 10, 1)]
    [InlineData(30, 1, GifFrameRate.Fps25, 25, 1)]
    [InlineData(24, 1, GifFrameRate.Fps25, 24, 1)]
    [InlineData(30000, 1001, GifFrameRate.Fps20, 20, 1)]
    [InlineData(1, 2, GifFrameRate.Fps10, 1, 2)]
    [InlineData(25, 2, GifFrameRate.Fps20, 25, 2)]
    public void TheGifPlaysAtThePresetOrTheVideosSlowerRate(long videoNumerator, long videoDenominator, GifFrameRate preset, long numerator, long denominator)
    {
        Assert.Equal(new Fraction(numerator, denominator), GifOutput.EffectiveRate(new Fraction(videoNumerator, videoDenominator), preset));
    }

    [Fact]
    public void A30FpsVideoAsA10FpsGifShowsEveryThirdPhoto()
    {
        var frames = GifOutput.SourceFrames(300, new Fraction(30, 1), new Fraction(10, 1));
        Assert.Equal(100, frames.Length);
        Assert.Equal(Enumerable.Range(0, 100).Select(i => i * 3), frames);
    }

    [Fact]
    public void AGifAsFastAsTheVideoShowsEveryPhoto()
    {
        var rate = new Fraction(24, 1);
        Assert.Equal(Enumerable.Range(0, 77), GifOutput.SourceFrames(77, rate, rate));
    }

    [Theory]
    [InlineData(30, 1, 25, 1)]
    [InlineData(30000, 1001, 20, 1)]
    [InlineData(30000, 1001, 25, 1)]
    [InlineData(60, 1, 25, 1)]
    [InlineData(120, 1, 10, 1)]
    [InlineData(7, 3, 2, 1)]
    public void EachGifFrameShowsThePhotoTheVideoShowsAtItsStart(long videoNumerator, long videoDenominator, long gifNumerator, long gifDenominator)
    {
        var video = new Fraction(videoNumerator, videoDenominator);
        var gif = new Fraction(gifNumerator, gifDenominator);
        foreach (var count in new[] { 1, 2, 7, 301, 5000 })
        {
            var frames = GifOutput.SourceFrames(count, video, gif);

            // The first is the first photo, every one is a photo, strictly in order.
            Assert.Equal(0, frames[0]);
            Assert.All(frames, frame => Assert.InRange(frame, 0, count - 1));
            Assert.True(frames.Zip(frames.Skip(1)).All(pair => pair.First < pair.Second), "strictly increasing");

            for (var k = 0; k < frames.Length; k++)
            {
                // The video frame on screen at the GIF frame's start: FrameTiming says which.
                var start = FrameTiming.FrameStart(k, gif);
                Assert.True(FrameTiming.FrameStart(frames[k], video) <= start, $"{count} frames, GIF frame {k}");
                Assert.True(frames[k] + 1 >= count || FrameTiming.FrameStart(frames[k] + 1, video) > start - 1, $"{count} frames, GIF frame {k}");
            }

            // The GIF covers the video: its last frame starts before the video ends, and it ends at or after it, by
            // less than one GIF frame.
            var videoLength = FrameTiming.Duration(count, video);
            Assert.True(FrameTiming.FrameStart(frames.Length - 1, gif) < videoLength);
            var gifLength = FrameTiming.Duration(frames.Length, gif);
            Assert.InRange(gifLength - videoLength, -1, FrameTiming.FrameDuration(frames.Length - 1, gif));
        }
    }

    [Fact]
    public void TheEstimateGrowsWithFramesAndPixels()
    {
        var medium = GifOutput.Size(GifSize.Medium, FrameAspect.Ratio16x9);
        var large = GifOutput.Size(GifSize.Large, FrameAspect.Ratio16x9);
        Assert.Equal(0, GifOutput.EstimateBytes(0, medium));
        Assert.True(GifOutput.EstimateBytes(100, medium) < GifOutput.EstimateBytes(200, medium));
        Assert.True(GifOutput.EstimateBytes(100, medium) < GifOutput.EstimateBytes(100, large));

        // A 10 s, 10 fps GIF at 480 x 270: a few megabytes, as chat apps take.
        Assert.InRange(GifOutput.EstimateBytes(100, medium), 2_000_000, 12_000_000);
    }
}
