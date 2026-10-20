using TenLapse.Presentation;

namespace TenLapse.Tests.Presentation;

/// <summary>#114 review: the wrapping row of the finished export and the Settings feedback buttons.</summary>
public sealed class WrapLayoutTests
{
    private static readonly (double Width, double Height)[] _twoButtons = [(120, 32), (150, 32)];

    [Fact]
    public void ChildrenSitSideBySideWhileTheyFit()
    {
        var layout = WrapLayout.Lay(_twoButtons, 8, 280);

        Assert.Equal([(0d, 0d), (128d, 0d)], layout.Positions);
        Assert.Equal(278, layout.Width);
        Assert.Equal(32, layout.Height);
    }

    [Fact]
    public void AChildThatWouldPassTheWidthStartsANewLine()
    {
        var layout = WrapLayout.Lay(_twoButtons, 8, 240);

        Assert.Equal([(0d, 0d), (0d, 40d)], layout.Positions);
        Assert.Equal(150, layout.Width);
        Assert.Equal(72, layout.Height);
    }

    [Fact]
    public void AnUnboundedWidthKeepsOneLine()
    {
        var layout = WrapLayout.Lay(_twoButtons, 8, double.PositiveInfinity);

        Assert.Equal(32, layout.Height);
        Assert.Equal(278, layout.Width);
    }

    [Fact]
    public void ArrangeAHairNarrowerThanMeasureKeepsTheMeasuredLines()
    {
        // An Auto column measures at an unbounded width, then arranges at the desired width after rounding.
        var measured = WrapLayout.Lay(_twoButtons, 8, double.PositiveInfinity);

        var arranged = WrapLayout.ForArrange(measured, _twoButtons, 8, 277.5);

        Assert.Same(measured, arranged);
        Assert.Equal((128d, 0d), arranged.Positions[1]);
    }

    [Fact]
    public void ArrangeTrulyNarrowerBreaksTheLinesAgain()
    {
        var measured = WrapLayout.Lay(_twoButtons, 8, double.PositiveInfinity);

        var arranged = WrapLayout.ForArrange(measured, _twoButtons, 8, 200);

        Assert.Equal((0d, 40d), arranged.Positions[1]);
    }

    [Fact]
    public void ArrangeWithOtherSizesLaysThemOutAgain()
    {
        var measured = WrapLayout.Lay(_twoButtons, 8, 280);

        var arranged = WrapLayout.ForArrange(measured, [(120, 32)], 8, 280);

        Assert.Single(arranged.Positions);
        Assert.Equal(120, arranged.Width);
    }

    [Fact]
    public void NoChildrenTakeNoPlace()
    {
        var layout = WrapLayout.Lay([], 8, 280);

        Assert.Empty(layout.Positions);
        Assert.Equal(0, layout.Width);
        Assert.Equal(0, layout.Height);
    }
}
