using TenLapse.Presentation.Export;

namespace TenLapse.Tests.Presentation;

// The gate's continuations run inline here: with no synchronization context, an await of a completed task or of a
// task completed by SetResult goes on at once, so every test is synchronous.
public sealed class WindowCloseGateTests
{
    private readonly FakeErrorSink _errors = new();
    private int _asks;
    private int _cancels;
    private int _closes;

    [Fact]
    public void WithNothingRunningTheWindowClosesAtOnce()
    {
        var gate = Gate(mayCloseNow: () => true, prepare: () => throw new InvalidOperationException("not asked"));

        Assert.True(gate.Closing());
        Assert.Equal(0, _asks);
        Assert.Empty(_errors.Reported);
    }

    [Fact]
    public void DuringAnExportTheCloseIsHeldAskedOnceAndDoneOnYes()
    {
        var answer = new TaskCompletionSource<bool>();
        var gate = Gate(mayCloseNow: () => false, prepare: () => answer.Task);

        Assert.False(gate.Closing());
        Assert.False(gate.Closing());
        Assert.Equal(1, _asks);

        answer.SetResult(true);

        Assert.Equal(1, _closes);
        Assert.True(gate.Closing());
    }

    [Fact]
    public void OnNoTheWindowStaysAndTheNextCloseAsksAgain()
    {
        var gate = Gate(mayCloseNow: () => false, prepare: () => Task.FromResult(false));

        Assert.False(gate.Closing());
        Assert.False(gate.Closing());

        Assert.Equal(2, _asks);
        Assert.Equal(0, _closes);
    }

    [Fact]
    public void AFailedCheckIsLoggedStopsTheExportAndLetsTheWindowClose()
    {
        var failure = new InvalidOperationException("check failed");
        var gate = Gate(mayCloseNow: () => throw failure, prepare: () => Task.FromResult(true));

        Assert.True(gate.Closing());

        Assert.Equal(1, _cancels);
        var (exception, context) = Assert.Single(_errors.Reported);
        Assert.Same(failure, exception);
        Assert.Equal(ExportText.CloseCheckFailed, context);
        Assert.Empty(_errors.Shown);
    }

    [Fact]
    public void AFailedStopIsLoggedTooAndTheWindowStillCloses()
    {
        SynchronizationContext.SetSynchronizationContext(null);
        var gate = new WindowCloseGate(
            () => throw new InvalidOperationException("check failed"),
            () => Task.FromResult(true),
            () => throw new InvalidOperationException("stop failed"),
            () => _closes++,
            _errors);

        Assert.True(gate.Closing());

        Assert.Equal([ExportText.CloseCheckFailed, ExportText.CloseStopFailed], _errors.Reported.Select(entry => entry.Context));
    }

    [Fact]
    public void AFailedAskIsShownAndTheNextCloseAsksAgain()
    {
        var gate = Gate(mayCloseNow: () => false, prepare: () => Task.FromException<bool>(new InvalidOperationException("dialog failed")));

        Assert.False(gate.Closing());

        var (_, context) = Assert.Single(_errors.Shown);
        Assert.Equal(ExportText.CloseFailed, context);
        Assert.False(gate.Closing());
        Assert.Equal(2, _asks);
    }

    private WindowCloseGate Gate(Func<bool> mayCloseNow, Func<Task<bool>> prepare)
    {
        SynchronizationContext.SetSynchronizationContext(null);
        return new(
            mayCloseNow,
            () =>
            {
                _asks++;
                return prepare();
            },
            () => _cancels++,
            () => _closes++,
            _errors);
    }
}
