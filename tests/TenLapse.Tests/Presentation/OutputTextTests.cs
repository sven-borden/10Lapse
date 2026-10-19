using System.Globalization;
using TenLapse.Presentation;

namespace TenLapse.Tests.Presentation;

/// <summary>The Output panel's words (#23): file sizes without false precision, in the reader's culture.</summary>
public sealed class OutputTextTests
{
    private const long Mb = 1L << 20;
    private const long Gb = 1L << 30;

    private static readonly CultureInfo _english = new("en-US");
    private static readonly CultureInfo _german = new("de-DE");
    private static readonly CultureInfo _french = new("fr-FR");

    [Theory]
    [InlineData(0L, "Less than 1 MB")]
    [InlineData(Mb - 1, "Less than 1 MB")]
    [InlineData(Mb, "1 MB")]
    [InlineData(Mb * 3 / 2, "1.5 MB")]
    [InlineData(4_430_000L, "4.2 MB")]
    [InlineData(39_200_000L, "37 MB")]
    [InlineData(192_000_000L, "180 MB")]
    [InlineData(Gb, "1 GB")]
    [InlineData(Gb * 19 / 10, "1.9 GB")]
    [InlineData(Gb * 250, "250 GB")]
    public void SizesKeepTwoSignificantFigures(long bytes, string expected)
    {
        Assert.Equal(expected, OutputText.Size(bytes, _english));
    }

    [Theory]
    [InlineData(994L, "990 MB")]
    [InlineData(996L, "1 GB")]
    [InlineData(999L, "1 GB")]
    [InlineData(1023L, "1 GB")]
    public void SizesBelowAGigabyteAreNeverWrittenAsAFractionOfOne(long megabytes, string expected) =>
        Assert.Equal(expected, OutputText.Size(Mb * megabytes, _english));

    [Fact]
    public void TheNumberIsWrittenInTheGivenCulture()
    {
        var bytes = Gb * 18 / 10;

        Assert.Equal("1.8 GB", OutputText.Size(bytes, _english));
        Assert.Equal("1,8 GB", OutputText.Size(bytes, _german));
        Assert.Equal("1,8 GB", OutputText.Size(bytes, _french));
        Assert.Equal("About 1,8 GB", OutputText.Estimate(bytes, _german));
        Assert.Equal("About 180 MB", OutputText.Estimate(180L * Mb, _german));
    }

    [Fact]
    public void TheCurrentCultureIsTheDefault()
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = _german;
            Assert.Equal("About 4,2 MB", OutputText.Estimate((long)(4.2 * Mb)));
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void AnEstimateUnderAMegabyteDoesNotSayAbout()
    {
        Assert.Equal("Less than 1 MB", OutputText.Estimate(300_000, _english));
        Assert.Equal("About 37 MB", OutputText.Estimate(39_200_000, _english));
    }

    [Fact]
    public void ANegativeSizeIsRefused() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => OutputText.Size(-1, _english));
}
