using TenLapse.Presentation.Services;

namespace TenLapse.Tests.Presentation;

/// <summary>Answers every question with <see cref="Answer"/> and records what was asked.</summary>
internal sealed class FakeDialogService(bool answer = true) : IDialogService
{
    public bool Answer { get; set; } = answer;

    public List<Confirmation> Asked { get; } = [];

    public Task<bool> ConfirmAsync(Confirmation confirmation)
    {
        Asked.Add(confirmation);
        return Task.FromResult(Answer);
    }
}

/// <summary>Records every reported failure, and can run a check at the moment of the report.</summary>
internal sealed class FakeErrorSink : IErrorSink
{
    private readonly Lock _gate = new();

    public List<(Exception Exception, string Context)> Reported { get; } = [];

    public Action? OnReport { get; set; }

    public void Report(Exception exception, string context)
    {
        lock (_gate)
        {
            Reported.Add((exception, context));
        }

        OnReport?.Invoke();
    }
}

/// <summary>Holds the question open until the test answers it.</summary>
internal sealed class PendingDialogService : IDialogService
{
    private TaskCompletionSource<bool>? _pending;

    public int Asked { get; private set; }

    public Task<bool> ConfirmAsync(Confirmation confirmation)
    {
        Asked++;
        _pending = new TaskCompletionSource<bool>();
        return _pending.Task;
    }

    public void Answer(bool confirmed) => _pending!.SetResult(confirmed);
}

/// <summary>Stands in for #19's proxy cache: holds proxies until disposed.</summary>
internal sealed class FakeProxyCache : IDisposable
{
    public int Count { get; set; }

    public bool IsDisposed { get; private set; }

    public void Dispose()
    {
        Count = 0;
        IsDisposed = true;
    }
}

/// <summary>Stands in for #19's preview player source: a source until disposed.</summary>
internal sealed class FakePreviewSource : IDisposable
{
    public object? Source { get; set; } = new();

    public bool IsDisposed { get; private set; }

    public void Dispose()
    {
        Source = null;
        IsDisposed = true;
    }
}

/// <summary>Records the order it was disposed in, and can throw.</summary>
internal sealed class RecordingResource(string name, List<string> log, bool throws = false) : IDisposable
{
    public void Dispose()
    {
        log.Add(name);
        if (throws)
        {
            throw new InvalidOperationException(name);
        }
    }
}
