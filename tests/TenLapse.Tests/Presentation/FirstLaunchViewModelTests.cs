using TenLapse.Core.Timing;
using TenLapse.Presentation;
using TenLapse.Presentation.FirstLaunch;

namespace TenLapse.Tests.Presentation;

public sealed class FirstLaunchViewModelTests
{
    [Fact]
    public void TheExplanationIsTheCanvasSentence()
    {
        var viewModel = new FirstLaunchViewModel();

        Assert.Equal("10Lapse plays your photos back as a video. Shoot one photo every few seconds, and 300 of them become a 10-second clip at 30 frames per second.", viewModel.Explanation);
        Assert.Equal("Your photos never leave this PC.", ProjectText.FirstLaunchPrivacy);
    }

    [Fact]
    public void TheExampleTakesItsLengthFromFrameTiming()
    {
        Assert.Equal(10 * FrameTiming.TicksPerSecond, FrameTiming.Duration(FirstLaunchViewModel.ExamplePhotos, FirstLaunchViewModel.ExampleRate));
        Assert.Contains("600 of them become a 20-second clip at 30 frames per second.", ProjectText.FirstLaunchExplanation(600, new Fraction(30, 1)), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(301, 30, 1)]  // 10.033 s: not a whole number of seconds
    [InlineData(300, 30000, 1001)] // not a whole frame rate
    [InlineData(0, 30, 1)]
    public void TheExampleMustBeWholeNumbers(int photos, long numerator, long denominator)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ProjectText.FirstLaunchExplanation(photos, new Fraction(numerator, denominator)));
    }

    [Fact]
    public void ADragTheWindowTakesShowsTheDropTitle()
    {
        var viewModel = new FirstLaunchViewModel();
        var raised = new List<string?>();
        viewModel.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        Assert.Equal("Drop a folder of photos to make a timelapse", viewModel.Title);
        viewModel.DragOver(acceptable: true);

        Assert.True(viewModel.IsDragOver);
        Assert.Equal("Drop to start your timelapse", viewModel.Title);
        Assert.Contains(nameof(FirstLaunchViewModel.Title), raised);
    }

    [Fact]
    public void ADragTheWindowRefusesShowsNothing()
    {
        var viewModel = new FirstLaunchViewModel();

        viewModel.DragOver(acceptable: true);
        viewModel.DragOver(acceptable: false);

        Assert.False(viewModel.IsDragOver);
        Assert.Equal(ProjectText.FirstLaunchTitle, viewModel.Title);
    }

    [Fact]
    public void ALeaveIntoAnotherElementKeepsTheLook()
    {
        var viewModel = new FirstLaunchViewModel();
        viewModel.DragOver(acceptable: true);

        var ticket = viewModel.DragLeft();
        viewModel.DragOver(acceptable: true); // the element the pointer moved into
        viewModel.ConfirmLeave(ticket);

        Assert.True(viewModel.IsDragOver);
    }

    [Fact]
    public void ADragEnterAloneVoidsAWaitingLeave()
    {
        // The pointer crosses from one element into another and no DragOver arrives within the grace.
        var viewModel = new FirstLaunchViewModel();
        viewModel.DragOver(acceptable: true);

        var ticket = viewModel.DragLeft();
        viewModel.DragEntered();
        viewModel.ConfirmLeave(ticket);

        Assert.True(viewModel.IsDragOver);
    }

    [Fact]
    public void ALeaveNothingFollowsEndsTheLook()
    {
        // Escape cancels a drag over the window: DragLeave fires inside it, and nothing after.
        var viewModel = new FirstLaunchViewModel();
        viewModel.DragOver(acceptable: true);

        viewModel.ConfirmLeave(viewModel.DragLeft());

        Assert.False(viewModel.IsDragOver);
    }

    [Fact]
    public void OnlyTheLatestLeaveCounts()
    {
        var viewModel = new FirstLaunchViewModel();
        viewModel.DragOver(acceptable: true);
        var first = viewModel.DragLeft();
        var second = viewModel.DragLeft();

        viewModel.ConfirmLeave(first);
        Assert.True(viewModel.IsDragOver);

        viewModel.ConfirmLeave(second);
        Assert.False(viewModel.IsDragOver);
    }

    [Fact]
    public void ADropOrALeaveOutsideEndsTheLookAndVoidsAWaitingLeave()
    {
        var viewModel = new FirstLaunchViewModel();
        viewModel.DragOver(acceptable: true);
        var ticket = viewModel.DragLeft();

        viewModel.DragEnded();
        viewModel.DragOver(acceptable: true); // the next drag
        viewModel.ConfirmLeave(ticket);

        Assert.True(viewModel.IsDragOver);
    }

    [Fact]
    public void ANewDropZoneStartsInAnActiveWindowWithoutTheDragLook()
    {
        var viewModel = new FirstLaunchViewModel();

        Assert.True(viewModel.IsWindowActive);
        Assert.False(viewModel.IsDragOver);
    }

    [Theory]
    [InlineData(1, 600, "Frame 0001 of 0600")]
    [InlineData(312, 742, "Frame 0312 of 0742")]
    [InlineData(5000, 5000, "Frame 5000 of 5000")]
    [InlineData(7, 12345, "Frame 00007 of 12345")]
    public void FrameCaptionsArePaddedToFourDigitsOrTheTotal(int number, int total, string expected)
    {
        Assert.Equal(expected, ProjectText.FrameCaption(number, total));
    }

    [Theory]
    [InlineData(0, 600)]
    [InlineData(601, 600)]
    [InlineData(1, 0)]
    public void FrameCaptionsRejectAFrameOutsideTheSequence(int number, int total)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ProjectText.FrameCaption(number, total));
    }
}
