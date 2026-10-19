using System.Collections.Concurrent;
using System.Diagnostics;
using TenLapse.Presentation.Services;

namespace TenLapse.Tests.Import;

/// <summary>
/// A stand-in for the app's UI thread: one thread that runs queued work in order, with a synchronization context that
/// posts back to it (an <c>await</c> there resumes there, as on WinUI's dispatcher). Projects made on it are owned by
/// it, so a change from any other thread throws, as in the app. It measures how long each piece of work held it.
/// </summary>
internal sealed class UiThread : IUiDispatcher, IDisposable
{
    private readonly BlockingCollection<Action> _queue = [];
    private readonly Thread _thread;
    private readonly ConcurrentQueue<Exception> _failures = new();
    private long _longestTicks;

    public UiThread()
    {
        _thread = new Thread(Loop) { IsBackground = true, Name = "Test UI thread" };
        _thread.Start();
    }

    /// <summary>Exceptions thrown by work posted through the synchronization context (async continuations); should stay empty.</summary>
    public IReadOnlyCollection<Exception> Failures => _failures;

    /// <summary>The longest any one piece of work held the thread.</summary>
    public TimeSpan Longest => TimeSpan.FromTicks(Interlocked.Read(ref _longestTicks));

    public bool IsCurrent => Environment.CurrentManagedThreadId == _thread.ManagedThreadId;

    public Task<T> InvokeAsync<T>(Func<T> work)
    {
        var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() =>
        {
            try
            {
                result.TrySetResult(work());
            }
            catch (Exception exception)
            {
                result.TrySetException(exception);
            }
        });
        return result.Task;
    }

    /// <summary>Starts async work on this thread (it resumes here after each await) and completes when it does.</summary>
    public async Task RunAsync(Func<Task> work) => await (await InvokeAsync(work));

    /// <summary>Runs work here and waits for it (test setup).</summary>
    public T Run<T>(Func<T> work) => InvokeAsync(work).GetAwaiter().GetResult();

    public void Dispose() => _queue.CompleteAdding();

    private void Post(Action action)
    {
        if (!_queue.IsAddingCompleted)
        {
            _queue.Add(action);
        }
    }

    private void Loop()
    {
        SynchronizationContext.SetSynchronizationContext(new Context(this));
        foreach (var action in _queue.GetConsumingEnumerable())
        {
            var started = Stopwatch.GetTimestamp();
            try
            {
                action();
            }
            catch (Exception exception)
            {
                _failures.Enqueue(exception);
            }

            var ticks = Stopwatch.GetElapsedTime(started).Ticks;
            long seen;
            while (ticks > (seen = Interlocked.Read(ref _longestTicks)) && Interlocked.CompareExchange(ref _longestTicks, ticks, seen) != seen)
            {
            }
        }
    }

    private sealed class Context(UiThread owner) : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) => owner.Post(() => d(state));

        public override void Send(SendOrPostCallback d, object? state)
        {
            if (owner.IsCurrent)
            {
                d(state);
            }
            else
            {
                owner.InvokeAsync(() =>
                {
                    d(state);
                    return true;
                }).GetAwaiter().GetResult();
            }
        }

        public override SynchronizationContext CreateCopy() => this;
    }
}
