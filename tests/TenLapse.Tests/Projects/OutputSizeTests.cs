using TenLapse.Core.Projects;

namespace TenLapse.Tests.Projects;

public sealed class OutputSizeTests
{
    [Theory]
    [InlineData(OutputResolution.Hd1080, FrameAspect.Ratio16x9, 1920, 1080)]
    [InlineData(OutputResolution.Hd1080, FrameAspect.Ratio9x16, 1080, 1920)]
    [InlineData(OutputResolution.Hd1080, FrameAspect.Ratio1x1, 1080, 1080)]
    [InlineData(OutputResolution.Hd1080, FrameAspect.Ratio4x5, 1080, 1350)]
    [InlineData(OutputResolution.Hd1080, FrameAspect.Ratio4x3, 1440, 1080)]
    [InlineData(OutputResolution.Hd720, FrameAspect.Ratio16x9, 1280, 720)]
    [InlineData(OutputResolution.Qhd1440, FrameAspect.Ratio9x16, 1440, 2560)]
    [InlineData(OutputResolution.Uhd2160, FrameAspect.Ratio16x9, 3840, 2160)]
    [InlineData(OutputResolution.Uhd2160, FrameAspect.Ratio9x16, 2160, 3840)]
    [InlineData(OutputResolution.Uhd2160, FrameAspect.Ratio4x5, 2160, 2700)]
    public void TheResolutionIsTheShortSide(OutputResolution resolution, FrameAspect aspect, int width, int height)
    {
        Assert.Equal(new PixelSize(width, height), OutputSize.For(resolution, aspect));
    }

    [Theory]
    [InlineData(6000, 4000, 1620, 1080)]
    [InlineData(4000, 6000, 1080, 1620)]
    [InlineData(4032, 3024, 1440, 1080)]
    [InlineData(3000, 3000, 1080, 1080)]
    [InlineData(4001, 3000, 1440, 1080)]
    [InlineData(1000, 777, 1390, 1080)]
    public void MatchPhotosTakesThePhotosShapeWithEvenSides(int photoWidth, int photoHeight, int width, int height)
    {
        var size = OutputSize.For(OutputResolution.Hd1080, FrameAspect.MatchPhotos, new PixelSize(photoWidth, photoHeight));

        Assert.Equal(new PixelSize(width, height), size);
        Assert.Equal(0, size.Width % 2);
        Assert.Equal(0, size.Height % 2);
    }

    [Fact]
    public void MatchPhotosNeedsThePhotoSize()
    {
        Assert.Throws<ArgumentException>(() => OutputSize.For(OutputResolution.Hd1080, FrameAspect.MatchPhotos));
        Assert.Throws<ArgumentOutOfRangeException>(() => OutputSize.For((OutputResolution)9, FrameAspect.Ratio1x1));
        Assert.Throws<ArgumentOutOfRangeException>(() => OutputSize.For(OutputResolution.Hd720, (FrameAspect)9));
    }
}
