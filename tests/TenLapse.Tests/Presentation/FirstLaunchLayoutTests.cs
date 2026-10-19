using TenLapse.Presentation.FirstLaunch;

namespace TenLapse.Tests.Presentation;

public sealed class FirstLaunchLayoutTests
{
    [Fact]
    public void TheDefaultWindowGetsThePrototypesTwoColumns()
    {
        // 1280 x 800: the body under the 88 px bars, 16 px in at the sides and the bottom.
        var layout = FirstLaunchLayout.Fit(1248, 696);

        Assert.True(layout.ShowsIllustration);
        Assert.Equal(72, layout.SidePadding);
        Assert.Equal(64, layout.ColumnGap);
        Assert.Equal(1.1, layout.IllustrationShare);
        Assert.Equal(572, layout.FrameWidth); // 1.1 / 2 of 1248 - 144 - 64
        Assert.Equal(321.75, layout.FrameHeight);
        Assert.Equal((572 - 33) / 12.0, layout.ThumbWidth, 9);
        Assert.Equal(layout.ThumbWidth * 2 / 3, layout.ThumbHeight, 9);
    }

    [Fact]
    public void ANarrowWindowShowsTheTextsAlone()
    {
        // The smallest window, 640 x 480.
        var layout = FirstLaunchLayout.Fit(608, 376);

        Assert.False(layout.ShowsIllustration);
        Assert.Equal(40, layout.SidePadding);
        Assert.Equal(0, layout.ColumnGap);
        Assert.Equal(0, layout.IllustrationShare);
        Assert.Equal(0, layout.IllustrationHeight);
    }

    [Fact]
    public void AShortWindowNarrowsTheFrameToFit()
    {
        var layout = FirstLaunchLayout.Fit(1248, 360);

        Assert.True(layout.ShowsIllustration);
        Assert.True(layout.FrameWidth < 572);
        Assert.True(layout.IllustrationHeight <= 360 - (2 * FirstLaunchLayout.VerticalMargin));
    }

    [Fact]
    public void AWindowTooShortForALegibleFrameDropsIt()
    {
        Assert.False(FirstLaunchLayout.Fit(1248, 220).ShowsIllustration);
    }

    [Fact]
    public void DaybreakAlwaysFitsWhereItShows()
    {
        for (var width = 300; width <= 3000; width += 37)
        {
            for (var height = 200; height <= 1600; height += 29)
            {
                var layout = FirstLaunchLayout.Fit(width, height);
                if (!layout.ShowsIllustration)
                {
                    continue;
                }

                var text = (width - 144 - 64) * 0.9 / 2;
                Assert.True(layout.FrameWidth >= FirstLaunchLayout.MinFrameWidth, $"{width} x {height}");
                Assert.True(text >= FirstLaunchLayout.MinTextWidth, $"{width} x {height}");
                Assert.True(layout.FrameWidth <= (width - 144 - 64) * 1.1 / 2, $"{width} x {height}");
                Assert.True(layout.IllustrationHeight <= height - (2 * FirstLaunchLayout.VerticalMargin) + 1e-9, $"{width} x {height}");
            }
        }
    }
}
