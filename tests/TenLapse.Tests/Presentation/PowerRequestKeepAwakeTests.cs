using System.ComponentModel;
using TenLapse.Services;

namespace TenLapse.Tests.Presentation;

/// <summary>The power request behind <c>IKeepAwake</c> (#25): create, set, clear, close; one log line when Windows refuses.</summary>
public sealed class PowerRequestKeepAwakeTests
{
    [Fact]
    public void AHeldRequestIsSetAndALeaseDisposeClearsAndClosesItOnce()
    {
        var api = new FakePowerRequests();
        var awake = new PowerRequestKeepAwake(api, new FakeErrorSink());

        var lease = awake.Hold("Exporting Harbor.mp4");
        Assert.Equal(["create Exporting Harbor.mp4", "set 1"], api.Calls);

        lease.Dispose();
        lease.Dispose();

        Assert.Equal(["create Exporting Harbor.mp4", "set 1", "clear 1", "close 1"], api.Calls);
    }

    [Fact]
    public void ARefusedCreateIsLoggedOnceAndTheLeaseDoesNothing()
    {
        var api = new FakePowerRequests { FailCreate = true };
        var log = new FakeErrorSink();
        var awake = new PowerRequestKeepAwake(api, log);

        awake.Hold("one").Dispose();
        awake.Hold("two").Dispose();

        Assert.Single(log.Reported);
        Assert.Equal(["create one", "create two"], api.Calls);
    }

    [Fact]
    public void ARequestThatCannotBeSetIsClosedAtOnce()
    {
        var api = new FakePowerRequests { FailSet = true };
        var log = new FakeErrorSink();

        new PowerRequestKeepAwake(api, log).Hold("one").Dispose();

        Assert.Equal(["create one", "set 1", "close 1"], api.Calls);
        Assert.Single(log.Reported);
    }

    [Fact]
    public void AFailureToLetGoIsLoggedAndNeverThrown()
    {
        var api = new FakePowerRequests { FailClear = true };
        var log = new FakeErrorSink();

        new PowerRequestKeepAwake(api, log).Hold("one").Dispose();

        // The handle is closed even when clearing failed.
        Assert.Equal(["create one", "set 1", "clear 1", "close 1"], api.Calls);
        Assert.Single(log.Reported);
    }

    [Fact]
    public void WindowsAcceptsAndReleasesARealRequest()
    {
        // The real API with a reason string: nothing to see in the machine afterwards, but nothing may throw or be logged.
        var log = new FakeErrorSink();
        var awake = new PowerRequestKeepAwake(log);

        awake.Hold("10Lapse test: Exporting Harbor.mp4").Dispose();
        awake.Hold("10Lapse test: a second one").Dispose();

        Assert.Empty(log.Reported);
    }

    private sealed class FakePowerRequests : IPowerRequests
    {
        public List<string> Calls { get; } = [];

        public bool FailCreate { get; init; }

        public bool FailSet { get; init; }

        public bool FailClear { get; init; }

        public nint Create(string reason)
        {
            Calls.Add("create " + reason);
            return FailCreate ? throw new Win32Exception(5) : 1;
        }

        public void SetSystemRequired(nint handle)
        {
            Calls.Add("set " + handle);
            if (FailSet)
            {
                throw new Win32Exception(5);
            }
        }

        public void ClearSystemRequired(nint handle)
        {
            Calls.Add("clear " + handle);
            if (FailClear)
            {
                throw new Win32Exception(5);
            }
        }

        public void Close(nint handle) => Calls.Add("close " + handle);
    }
}
