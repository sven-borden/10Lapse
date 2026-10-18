using TenLapse.Media.Export;
using Windows.Foundation;

namespace TenLapse.Tests.Media;

public sealed class FrameFillTests
{
    [Fact]
    public void A3By2PhotoLosesItsTopAndBottomIn16By9()
    {
        var rect = FrameFill.SourceRect(new Size(6000, 4000), new Size(3840, 2160));

        Assert.Equal(new Rect(0, 312.5, 6000, 3375), rect);
    }

    [Fact]
    public void APortraitPhotoKeepsItsMiddleBandIn16By9()
    {
        var rect = FrameFill.SourceRect(new Size(1000, 2000), new Size(1920, 1080));

        Assert.Equal(new Rect(0, 718.75, 1000, 562.5), rect);
    }

    [Fact]
    public void AWidePhotoLosesItsSidesIn4By3()
    {
        var rect = FrameFill.SourceRect(new Size(4000, 2000), new Size(1440, 1080));

        Assert.Equal(new Rect(666.6666666666666, 0, 2666.6666666666665, 2000), rect);
    }

    [Fact]
    public void TheSameAspectKeepsTheWholePhoto()
    {
        var rect = FrameFill.SourceRect(new Size(7680, 4320), new Size(1920, 1080));

        Assert.Equal(new Rect(0, 0, 7680, 4320), rect);
    }

    [Fact]
    public void AnEmptySizeIsRejected()
    {
        Assert.Throws<ArgumentException>(() => FrameFill.SourceRect(new Size(0, 10), new Size(10, 10)));
    }
}
