using TenLapse.Presentation.Services;

namespace TenLapse.Tests.Presentation;

public sealed class InfoBarQueueTests
{
    private static readonly Func<Task> _noAction = () => Task.CompletedTask;

    [Fact]
    public void TheFirstInfoBarShowsAndTheNextOnesWaitInOrder()
    {
        var queue = new InfoBarQueue();
        var changes = 0;
        queue.CurrentChanged += (_, _) => changes++;

        var first = queue.Show(InfoBarSeverity.Caution, "3 photos couldn't be read and were skipped.");
        var second = queue.Show(InfoBarSeverity.Critical, "The export stopped.");
        var third = queue.Show(InfoBarSeverity.Neutral, "Export cancelled.");

        Assert.Same(first, queue.Current);
        Assert.Equal(2, queue.WaitingCount);
        Assert.Equal(1, changes);

        queue.Dismiss(first);
        Assert.Same(second, queue.Current);
        queue.Dismiss(second);
        Assert.Same(third, queue.Current);
        queue.Dismiss(third);
        Assert.Null(queue.Current);
        Assert.Equal(4, changes);
    }

    [Fact]
    public void DismissingAWaitingInfoBarDropsItWithoutTouchingTheVisibleOne()
    {
        var queue = new InfoBarQueue();
        var visible = queue.Show(InfoBarSeverity.Caution, "One.");
        var waiting = queue.Show(InfoBarSeverity.Caution, "Two.");
        var last = queue.Show(InfoBarSeverity.Caution, "Three.");
        var changes = 0;
        queue.CurrentChanged += (_, _) => changes++;

        queue.Dismiss(waiting);

        Assert.Equal(0, changes);
        Assert.Same(visible, queue.Current);
        queue.Dismiss(visible);
        Assert.Same(last, queue.Current);
    }

    [Fact]
    public void DismissingTwiceOrAfterTheQueueMovedOnChangesNothing()
    {
        var queue = new InfoBarQueue();
        var ticket = queue.Show(InfoBarSeverity.Success, "Harbor sunset.mp4 is ready.");
        queue.Dismiss(ticket);
        var changes = 0;
        queue.CurrentChanged += (_, _) => changes++;

        queue.Dismiss(ticket);

        Assert.Equal(0, changes);
        Assert.Null(queue.Current);
    }

    [Fact]
    public void AnInfoBarThatReadsTheSameIsNotShownTwice()
    {
        var queue = new InfoBarQueue();
        var first = queue.Show(InfoBarSeverity.Critical, "The folder could not be read.", "Try again", _noAction);
        queue.Show(InfoBarSeverity.Caution, "Something else.");

        var again = queue.Show(InfoBarSeverity.Critical, "The folder could not be read.", "Try again", () => Task.CompletedTask);
        Assert.Same(first, again);
        Assert.Equal(1, queue.WaitingCount);

        // A different severity, message or action label is a different InfoBar.
        Assert.NotSame(first, queue.Show(InfoBarSeverity.Caution, "The folder could not be read.", "Try again", _noAction));
        Assert.NotSame(first, queue.Show(InfoBarSeverity.Critical, "The folder could not be read."));
        Assert.Equal(3, queue.WaitingCount);

        // Once dismissed, the same text may show again.
        queue.Dismiss(first);
        Assert.NotSame(first, queue.Show(InfoBarSeverity.Critical, "The folder could not be read.", "Try again", _noAction));
    }

    [Fact]
    public void ARequestNeedsAMessageAndBothOrNeitherPartsOfItsAction()
    {
        Assert.ThrowsAny<ArgumentException>(() => new InfoBarRequest(InfoBarSeverity.Neutral, " "));
        Assert.Throws<ArgumentException>(() => new InfoBarRequest(InfoBarSeverity.Neutral, "Done.", "Open", null));
        Assert.Throws<ArgumentException>(() => new InfoBarRequest(InfoBarSeverity.Neutral, "Done.", null, _noAction));
        Assert.ThrowsAny<ArgumentException>(() => new InfoBarRequest(InfoBarSeverity.Neutral, "Done.", "", _noAction));
        Assert.Throws<ArgumentOutOfRangeException>(() => new InfoBarRequest((InfoBarSeverity)42, "Done."));

        var request = new InfoBarRequest(InfoBarSeverity.Caution, "3 photos couldn't be read and were skipped.", "Show list", _noAction);
        Assert.Equal("Show list", request.ActionLabel);
        Assert.Same(_noAction, request.Action);
    }

    [Fact]
    public async Task ShowAndDismissAreSafeFromManyThreads()
    {
        var queue = new InfoBarQueue();
        var tickets = await Task.WhenAll(Enumerable.Range(0, 64).Select(i =>
            Task.Run(() => queue.Show(InfoBarSeverity.Neutral, $"Message {i}."))));

        Assert.Equal(64, tickets.Distinct().Count());
        Assert.Equal(63, queue.WaitingCount);

        await Task.WhenAll(tickets.Select(ticket => Task.Run(() => queue.Dismiss(ticket))));

        Assert.Null(queue.Current);
        Assert.Equal(0, queue.WaitingCount);
    }
}
