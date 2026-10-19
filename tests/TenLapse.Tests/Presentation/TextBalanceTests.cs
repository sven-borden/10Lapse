using TenLapse.Presentation;
using TenLapse.Presentation.FirstLaunch;

namespace TenLapse.Tests.Presentation;

public sealed class TextBalanceTests
{
    // A stand-in for a TextBlock that wraps whole words: 14 DIPs a letter, 7 a space, 36 DIP lines.
    private const double Letter = 14;
    private const double Space = 7;
    private const double LineHeight = 36;

    [Fact]
    public void TheTitleBreaksAfterPhotosAsTheCanvasBalancesIt()
    {
        var title = ProjectText.FirstLaunchTitle;
        var greedy = Lines(title, 440);
        var width = TextBalance.Width(440, w => Height(title, w));

        Assert.Equal(["Drop a folder of photos to make a", "timelapse"], greedy); // what the review saw
        Assert.Equal(["Drop a folder of photos", "to make a timelapse"], Lines(title, width));
    }

    [Fact]
    public void AHeadingOnOneLineKeepsTheWholeWidth()
    {
        Assert.Equal(440, TextBalance.Width(440, w => Height("Drop to start", w)));
    }

    [Fact]
    public void ThreeLinesStayThreeLinesButEvenOut()
    {
        const string heading = "one two three four five six seven eight nine ten eleven";
        var width = TextBalance.Width(250, w => Height(heading, w));

        Assert.Equal(Lines(heading, 250).Count, Lines(heading, width).Count);
        Assert.True(width < 250);
        var lengths = Lines(heading, width).Select(Width).ToList();
        Assert.True(lengths.Max() - lengths.Min() < Lines(heading, 250).Select(Width).Max() - Lines(heading, 250).Select(Width).Min());
    }

    [Fact]
    public void ANarrowWidthNeverGrowsPastWhatIsAvailable()
    {
        Assert.True(TextBalance.Width(100, w => Height(ProjectText.FirstLaunchTitle, w)) <= 100);
        Assert.Equal(0, TextBalance.Width(0, _ => throw new InvalidOperationException("not measured")));
    }

    [Fact]
    public void TheTextColumnWidthFollowsTheLayout()
    {
        var wide = FirstLaunchLayout.Fit(1248, 700);

        Assert.True(wide.ShowsIllustration);
        Assert.Equal((1248 - 72 - 72 - 64) * 0.9 / 2.0, wide.TextColumnWidth(1248), 9);
        Assert.Equal(600 - 40 - 40, FirstLaunchLayout.TextsAlone.TextColumnWidth(600), 9);
        Assert.Equal(0, FirstLaunchLayout.TextsAlone.TextColumnWidth(50));
    }

    private static double Height(string text, double width) => Lines(text, width).Count * LineHeight;

    private static double Width(string line) => (line.Length * Letter) - (line.Count(c => c == ' ') * (Letter - Space));

    // Whole words, greedily, as WrapWholeWords does; a word wider than the line takes a line of its own.
    private static List<string> Lines(string text, double width)
    {
        var lines = new List<string>();
        var current = string.Empty;
        foreach (var word in text.Split(' '))
        {
            var candidate = current.Length == 0 ? word : current + " " + word;
            if (current.Length > 0 && Width(candidate) > width)
            {
                lines.Add(current);
                current = word;
            }
            else
            {
                current = candidate;
            }
        }

        lines.Add(current);
        return lines;
    }
}
