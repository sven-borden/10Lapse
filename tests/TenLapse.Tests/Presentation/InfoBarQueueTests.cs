using TenLapse.Presentation;
using TenLapse.Presentation.Services;

namespace TenLapse.Tests.Presentation;

public sealed class InfoBarQueueTests
{
    private static readonly Func<Task> _noAction = () => Task.CompletedTask;

    private static InfoBarQueue NewQueue(FakeErrorSink? errors = null) => new(errors ?? new FakeErrorSink());

    [Fact]
    public void TheFirstInfoBarShowsAndTheNextOnesWaitInOrder()
    {
        var queue = NewQueue();
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
    public void AnInfoBarThatContinuesAnActionShowsAtOnceAheadOfTheWaitingOnes()
    {
        var queue = NewQueue();
        var visible = queue.Show(InfoBarSeverity.Caution, "One.");
        var waiting = queue.Show(InfoBarSeverity.Caution, "Two.");
        var changes = 0;
        queue.CurrentChanged += (_, _) => changes++;

        var next = queue.Show(new InfoBarRequest(InfoBarSeverity.Caution, "Next.", showsNext: true));

        Assert.Same(next, queue.Current);
        Assert.Equal(1, changes);
        queue.Dismiss(next);
        Assert.Same(visible, queue.Current);
        queue.Dismiss(visible);
        Assert.Same(waiting, queue.Current);
    }

    [Fact]
    public void DismissingAWaitingInfoBarDropsItWithoutTouchingTheVisibleOne()
    {
        var queue = NewQueue();
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
        var queue = NewQueue();
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
        var queue = NewQueue();
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

        var request = new InfoBarRequest(InfoBarSeverity.Caution, "The folder could not be read.", "Try again", _noAction);
        Assert.Equal("Try again", request.ActionLabel);
        Assert.Same(_noAction, request.Action);
    }

    [Fact]
    public async Task ShowAndDismissAreSafeFromManyThreads()
    {
        var queue = NewQueue();
        var tickets = await Task.WhenAll(Enumerable.Range(0, 64).Select(i =>
            Task.Run(() => queue.Show(InfoBarSeverity.Neutral, $"Message {i}."))));

        Assert.Equal(64, tickets.Distinct().Count());
        Assert.Equal(InfoBarQueue.MaxWaiting, queue.WaitingCount);

        await Task.WhenAll(tickets.Select(ticket => Task.Run(() => queue.Dismiss(ticket))));

        Assert.Null(queue.Current);
        Assert.Equal(0, queue.WaitingCount);
    }

    [Fact]
    public async Task AnActionDismissesItsInfoBarThenRunsOnceHoweverOftenItIsPressed()
    {
        var queue = NewQueue();
        var runs = 0;
        var ticket = queue.Show(InfoBarSeverity.Critical, "The export stopped.", "Try again", () =>
        {
            runs++;
            return Task.CompletedTask;
        });
        var next = queue.Show(InfoBarSeverity.Neutral, "Export cancelled.");

        await queue.RunActionAsync(ticket);
        await queue.RunActionAsync(ticket);

        Assert.Equal(1, runs);
        Assert.Same(next, queue.Current);
    }

    [Fact]
    public async Task AWaitingInfoBarsActionDoesNotRun()
    {
        var queue = NewQueue();
        queue.Show(InfoBarSeverity.Neutral, "Export cancelled.");
        var runs = 0;
        var waiting = queue.Show(InfoBarSeverity.Critical, "The export stopped.", "Try again", () =>
        {
            runs++;
            return Task.CompletedTask;
        });

        await queue.RunActionAsync(waiting);

        Assert.Equal(0, runs);
        Assert.Equal(1, queue.WaitingCount);
    }

    [Fact]
    public async Task AnActionThatKeepsTheInfoBarOpenCannotRunTwiceAtOnce()
    {
        var queue = NewQueue();
        var release = new TaskCompletionSource();
        var runs = 0;
        var ticket = queue.Show(new InfoBarRequest(
            InfoBarSeverity.Success,
            "Harbor sunset.mp4 is ready.",
            "Open folder",
            () =>
            {
                runs++;
                return release.Task;
            },
            actionKeepsOpen: true));

        var first = queue.RunActionAsync(ticket);
        await queue.RunActionAsync(ticket);
        Assert.Equal(1, runs);
        Assert.Same(ticket, queue.Current);

        release.SetResult();
        await first;
        await queue.RunActionAsync(ticket);
        Assert.Equal(2, runs);
        Assert.Same(ticket, queue.Current);
    }

    [Fact]
    public async Task AnActionThatThrowsIsReportedAndShownAsACriticalInfoBar()
    {
        var errors = new FakeErrorSink();
        var queue = NewQueue(errors);
        var failure = new InvalidOperationException("The folder is gone.");
        var faulted = queue.Show(InfoBarSeverity.Critical, "The folder could not be read.", "Try again", () => Task.FromException(failure));
        var throwing = queue.Show(InfoBarSeverity.Critical, "The export stopped.", "Try again", () => throw failure);

        await queue.RunActionAsync(faulted);
        await queue.RunActionAsync(throwing);

        Assert.Equal(2, errors.Reported.Count);
        Assert.All(errors.Reported, report =>
        {
            Assert.Same(failure, report.Exception);
            Assert.Equal(ProjectText.InfoBarActionFailedContext, report.Context);
        });

        // Both failures read the same, so one Critical InfoBar says it.
        Assert.Equal(InfoBarSeverity.Critical, queue.Current!.Request.Severity);
        Assert.Equal(ProjectText.InfoBarActionFailed, queue.Current.Request.Message);
        Assert.Equal(0, queue.WaitingCount);
    }

    [Fact]
    public void ADetailListIsPartOfWhatTheInfoBarSays()
    {
        var queue = NewQueue();
        InfoBarDetail[] skipped =
        [
            new("IMG_4402.HEIC", "Damaged file: it ends early"),
            new("IMG_4417.HEIC", "Empty file (0 bytes)"),
        ];

        var ticket = queue.ShowWithDetails(InfoBarSeverity.Caution, "2 photos couldn't be read and were skipped.", skipped);

        Assert.True(ticket.Request.HasDetails);
        Assert.Equal(skipped, ticket.Request.Details);
        Assert.Null(ticket.Request.ActionLabel);
        Assert.Same(ticket, queue.ShowWithDetails(
            InfoBarSeverity.Caution,
            "2 photos couldn't be read and were skipped.",
            [new("IMG_4402.HEIC", "Damaged file: it ends early"), new("IMG_4417.HEIC", "Empty file (0 bytes)")]));
        Assert.NotSame(ticket, queue.ShowWithDetails(
            InfoBarSeverity.Caution,
            "2 photos couldn't be read and were skipped.",
            [new("IMG_4402.HEIC", "Damaged file: it ends early"), new("IMG_4630.HEIC", "Empty file (0 bytes)")]));
        Assert.Equal(1, queue.WaitingCount);
    }

    [Fact]
    public void ARequestHasOneButtonBesidesDismissAndOnlyAnActionCanKeepItOpen()
    {
        InfoBarDetail[] lines = [new("IMG_4402.HEIC")];
        Assert.Throws<ArgumentException>(() => new InfoBarRequest(InfoBarSeverity.Caution, "Skipped.", "Try again", _noAction, details: lines));
        Assert.Throws<ArgumentException>(() => new InfoBarRequest(InfoBarSeverity.Caution, "Skipped.", actionKeepsOpen: true));
        Assert.Throws<ArgumentException>(() => new InfoBarRequest(InfoBarSeverity.Caution, "Skipped.", details: [null!]));
        Assert.ThrowsAny<ArgumentException>(() => new InfoBarDetail(" "));

        Assert.Null(new InfoBarDetail("IMG_4402.HEIC", " ").Note);
        Assert.False(new InfoBarRequest(InfoBarSeverity.Caution, "Skipped.", details: []).HasDetails);
        Assert.Empty(new InfoBarRequest(InfoBarSeverity.Caution, "Skipped.").Details);
    }

    [Fact]
    public void AFullQueueDropsTheLeastImportantWaitingInfoBar()
    {
        var queue = NewQueue();
        var visible = queue.Show(InfoBarSeverity.Neutral, "Visible.");
        queue.Show(InfoBarSeverity.Caution, "Caution 1.");
        queue.Show(InfoBarSeverity.Neutral, "Neutral 1.");
        queue.Show(InfoBarSeverity.Critical, "Critical 1.");
        queue.Show(InfoBarSeverity.Neutral, "Neutral 2.");
        queue.Show(InfoBarSeverity.Caution, "Caution 2.");
        Assert.Equal(InfoBarQueue.MaxWaiting, queue.WaitingCount);

        // A Caution one pushes out the oldest Neutral one.
        queue.Show(InfoBarSeverity.Caution, "Caution 3.");

        Assert.Equal(InfoBarQueue.MaxWaiting, queue.WaitingCount);
        Assert.Equal(
            ["Caution 1.", "Critical 1.", "Neutral 2.", "Caution 2.", "Caution 3."],
            Drain(queue, visible).Select(ticket => ticket.Request.Message));
    }

    [Fact]
    public void AFullQueueOfMoreImportantInfoBarsDropsTheNewOne()
    {
        var queue = NewQueue();
        var visible = queue.Show(InfoBarSeverity.Neutral, "Visible.");
        for (var i = 0; i < InfoBarQueue.MaxWaiting; i++)
        {
            queue.Show(InfoBarSeverity.Critical, $"Critical {i}.");
        }

        var changes = 0;
        queue.CurrentChanged += (_, _) => changes++;
        var dropped = queue.Show(InfoBarSeverity.Success, "Harbor sunset.mp4 is ready.");

        Assert.Equal(InfoBarQueue.MaxWaiting, queue.WaitingCount);
        Assert.Equal(0, changes);
        queue.Dismiss(dropped);
        Assert.Same(visible, queue.Current);
        Assert.DoesNotContain(Drain(queue, visible), ticket => ReferenceEquals(ticket, dropped));
    }

    // Dismisses the visible one and returns the ones that showed after it, in order.
    private static List<InfoBarTicket> Drain(InfoBarQueue queue, InfoBarTicket visible)
    {
        var shown = new List<InfoBarTicket>();
        queue.Dismiss(visible);
        while (queue.Current is { } current)
        {
            shown.Add(current);
            queue.Dismiss(current);
        }

        return shown;
    }
}
