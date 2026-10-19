using TenLapse.Presentation;

namespace TenLapse.Tests.Presentation;

public sealed class StartupTraceTests
{
    [Fact]
    public void EachLineNamesItsPhaseAndTheTimeSinceTheProcessStarted()
    {
        var log = new FakeLog();
        var elapsed = TimeSpan.FromMilliseconds(1234);
        var trace = new StartupTrace(log, () => elapsed);

        trace.Phase("creating the window");

        var (level, message, _) = Assert.Single(log.Entries);
        Assert.Equal(TenLapse.Core.Diagnostics.LogLevel.Info, level);
        Assert.Equal("Start-up: creating the window (1234 ms).", message);
    }

    [Fact]
    public void TheTraceRunsUntilBothEndsOfStartUpAreReachedThenGoesQuiet()
    {
        var log = new FakeLog();
        var trace = new StartupTrace(log, () => TimeSpan.Zero);

        trace.FirstFrame("first frame rendering");
        trace.Phase("encoder probe answered");
        Assert.False(trace.IsDone);

        trace.ProbeApplied("encoder answer applied");
        trace.Phase("anything later");
        trace.FirstFrame("first frame rendering");

        Assert.True(trace.IsDone);
        Assert.Equal(3, log.Entries.Count);
        Assert.EndsWith("encoder answer applied (0 ms).", log.Entries[^1].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheProbeCanFinishBeforeTheFirstFrame()
    {
        var log = new FakeLog();
        var trace = new StartupTrace(log, () => TimeSpan.Zero);

        trace.ProbeApplied("encoder answer applied");
        trace.Phase("waiting for the first frame");
        trace.FirstFrame("first frame rendering");
        trace.Phase("anything later");

        Assert.True(trace.IsDone);
        Assert.Equal(3, log.Entries.Count);
    }

    [Fact]
    public void ATraceForThisProcessCountsFromBeforeItWasMade()
    {
        var log = new FakeLog();
        StartupTrace.ForThisProcess(log).Phase("now");

        var message = Assert.Single(log.Entries).Message;
        var milliseconds = double.Parse(message[(message.LastIndexOf('(') + 1)..message.LastIndexOf(" ms", StringComparison.Ordinal)], System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(milliseconds > 0, message);
    }
}
