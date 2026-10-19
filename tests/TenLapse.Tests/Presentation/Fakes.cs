using TenLapse.Core.Projects;
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

    /// <summary>The answer to every <see cref="Choice"/>.</summary>
    public ChoiceResult Choice { get; set; } = ChoiceResult.Primary;

    public List<Choice> Chosen { get; } = [];

    public Task<ChoiceResult> ChooseAsync(Choice choice)
    {
        Chosen.Add(choice);
        return Task.FromResult(Choice);
    }
}

/// <summary>
/// Records every reported (log only) and shown (log and InfoBar) failure, and can run a check at the moment of the
/// report.
/// </summary>
internal sealed class FakeErrorSink : IErrorPresenter
{
    private readonly Lock _gate = new();

    public List<(Exception Exception, string Context)> Reported { get; } = [];

    public List<(Exception Exception, string Context)> Shown { get; } = [];

    public Action? OnReport { get; set; }

    public void Report(Exception exception, string context)
    {
        lock (_gate)
        {
            Reported.Add((exception, context));
        }

        OnReport?.Invoke();
    }

    public void Show(Exception exception, string context)
    {
        lock (_gate)
        {
            Shown.Add((exception, context));
        }

        OnReport?.Invoke();
    }
}

/// <summary>Records every InfoBar request.</summary>
internal sealed class FakeInfoBarService : IInfoBarService
{
    private readonly Lock _gate = new();

    public List<InfoBarRequest> Shown { get; } = [];

    public InfoBarTicket Show(InfoBarRequest request)
    {
        lock (_gate)
        {
            Shown.Add(request);
        }

        // A real queue makes the ticket; this one only needs to hand one back.
        return new InfoBarQueue(new FakeErrorSink()).Show(request);
    }

    public void Dismiss(InfoBarTicket ticket)
    {
    }
}

/// <summary>The PC's photo extensions, as the test says: which are missing, what opening the Store does.</summary>
internal sealed class FakeCodecExtensions : ICodecExtensions
{
    private readonly Lock _gate = new();

    /// <summary>The extensions this PC does not have.</summary>
    public HashSet<PhotoCodecExtension> Missing { get; } = [];

    /// <summary>What opening the Store page answers: true, false (nothing to open it with), or a throw.</summary>
    public Func<PhotoCodecExtension, bool> OpenResult { get; set; } = _ => true;

    /// <summary>The extensions whose Store page was asked for, in order.</summary>
    public List<PhotoCodecExtension> Opened { get; } = [];

    /// <summary>How many times the verdicts were forgotten.</summary>
    public int Refreshed { get; private set; }

    /// <summary>The extensions IsInstalled was asked about.</summary>
    public List<PhotoCodecExtension> Asked { get; } = [];

    public void Refresh()
    {
        lock (_gate)
        {
            Refreshed++;
        }
    }

    public bool IsInstalled(PhotoCodecExtension extension)
    {
        lock (_gate)
        {
            Asked.Add(extension);
            return !Missing.Contains(extension);
        }
    }

    public Task<bool> OpenStorePageAsync(PhotoCodecExtension extension)
    {
        lock (_gate)
        {
            Opened.Add(extension);
        }

        return Task.FromResult(OpenResult(extension));
    }
}

/// <summary>Records every log entry in memory.</summary>
internal sealed class FakeLog : TenLapse.Core.Diagnostics.ILog
{
    private readonly Lock _gate = new();

    public List<(TenLapse.Core.Diagnostics.LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

    public void Write(TenLapse.Core.Diagnostics.LogLevel level, string message, Exception? exception = null)
    {
        lock (_gate)
        {
            Entries.Add((level, message, exception));
        }
    }

    public IReadOnlyList<string> RecentLines(int count)
    {
        lock (_gate)
        {
            return Entries.Select(entry => $"{entry.Level} {entry.Message}").TakeLast(count).ToArray();
        }
    }
}

/// <summary>Remembers the text it was given, or throws like a busy clipboard.</summary>
internal sealed class FakeClipboard : IClipboardService
{
    public string? Text { get; private set; }

    public Exception? Failure { get; set; }

    public void SetText(string text)
    {
        if (Failure is not null)
        {
            throw Failure;
        }

        Text = text;
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

    public Task<ChoiceResult> ChooseAsync(Choice choice) => throw new NotSupportedException("Only yes-or-no questions wait here.");
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

/// <summary>Runs a callback when disposed.</summary>
internal sealed class CallbackResource(Action onDispose) : IDisposable
{
    public void Dispose() => onDispose();
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

/// <summary>Records the links it was asked to open, and answers or throws as the test says (the Store and the browser stay shut).</summary>
internal sealed class FakeLauncher : ILauncher
{
    public List<Uri> Launched { get; } = [];

    public bool Result { get; set; } = true;

    public Exception? Failure { get; set; }

    public Task<bool> LaunchUriAsync(Uri uri)
    {
        Launched.Add(uri);
        return Failure is { } failure ? Task.FromException<bool>(failure) : Task.FromResult(Result);
    }
}
