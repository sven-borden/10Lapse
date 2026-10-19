using System.Collections.Concurrent;
using System.Collections.Immutable;
using TenLapse.Core.Preview;
using TenLapse.Core.Projects;

namespace TenLapse.Tests.Preview;

/// <summary>An image that knows whether it was disposed, and how often.</summary>
internal sealed class FakeImage(Guid frame, FrameImageQuality quality) : IDisposable
{
    private int _disposals;

    public Guid Frame { get; } = frame;

    public FrameImageQuality Quality { get; } = quality;

    public int Disposals => Volatile.Read(ref _disposals);

    public bool IsDisposed => Disposals > 0;

    public void Dispose() => Interlocked.Increment(ref _disposals);
}

/// <summary>One call the cache made to the source, which the test completes when it likes.</summary>
internal sealed class FakeCall(Frame frame, FrameImageQuality quality, bool build, CancellationToken cancellation)
{
    private readonly TaskCompletionSource<object> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Frame Frame { get; } = frame;

    public FrameImageQuality Quality { get; } = quality;

    public bool Build { get; } = build;

    public CancellationToken Cancellation { get; } = cancellation;

    public Task<object> Result => _result.Task;

    public FakeImage? Image { get; private set; }

    /// <summary>Gives the image (a load) or says the proxy is there (a build).</summary>
    public FakeImage? Succeed(long bytes = 100, Exception? cacheFailure = null, string? version = null)
    {
        if (Build)
        {
            _result.TrySetResult(cacheFailure is null ? FrameProxyResult.Ready : new FrameProxyResult(CacheFailure: cacheFailure));
            return null;
        }

        Image = new FakeImage(Frame.Id, Quality);
        _result.TrySetResult(FrameImageResult<FakeImage>.Loaded(Image, Quality, bytes, cacheFailure, version));
        return Image;
    }

    public void Fail(SkipReason reason = SkipReason.FileNotFound)
    {
        var failure = new FrameLoadFailure(new SkippedFile(Frame.SourcePath, reason, "test"));
        _result.TrySetResult(Build ? new FrameProxyResult(failure) : FrameImageResult<FakeImage>.Failed(failure));
    }

    public void Throw(Exception exception) => _result.TrySetException(exception);

    public void Cancel() => _result.TrySetCanceled(Cancellation);
}

/// <summary>
/// A source the test drives: every call waits until the test completes it (or answers at once with
/// <see cref="AutoComplete"/>). Calls are recorded in order.
/// </summary>
internal sealed class FakeFrameImageSource : IFrameImageSource<FakeImage>
{
    private readonly BlockingCollection<FakeCall> _pending = [];
    private readonly ConcurrentQueue<FakeCall> _all = new();
    private int _staleChecks;

    /// <summary>Frames whose proxy is "on disk".</summary>
    public ConcurrentDictionary<Guid, bool> Proxies { get; } = new();

    /// <summary>When set, every call succeeds with this many bytes, after <see cref="AutoDelay"/>.</summary>
    public long? AutoComplete { get; set; }

    /// <summary>With <see cref="AutoComplete"/>: up to this many milliseconds before each call completes (random).</summary>
    public int AutoDelay { get; set; }

    /// <summary>When set, a call ignores its cancellation: its result arrives even after the cache moved on.</summary>
    public bool IgnoreCancellation { get; set; }

    /// <summary>
    /// With <see cref="AutoComplete"/>: the cache cannot be written. Builds fail with this, and proxy-size loads give the
    /// image with it as their cache failure (as the app's source does when it cannot save).
    /// </summary>
    public Exception? AutoCacheFailure { get; set; }

    /// <summary>The photos' versions on disk: a load gives the frame's, and <see cref="IsStale"/> compares against it.</summary>
    public ConcurrentDictionary<Guid, string> Versions { get; } = new();

    /// <summary>How many stat checks the cache made.</summary>
    public int StaleChecks => Volatile.Read(ref _staleChecks);

    public IReadOnlyCollection<FakeCall> All => _all;

    public ImmutableArray<Frame> LastFrames { get; private set; }

    /// <summary>When set, <see cref="HasProxy"/> throws it (a bug in the source).</summary>
    public Exception? HasProxyThrows { get; set; }

    public bool HasProxy(Frame frame) => HasProxyThrows is { } exception ? throw exception : Proxies.ContainsKey(frame.Id);

    public async Task<FrameImageResult<FakeImage>> LoadAsync(Frame frame, FrameImageQuality quality, CancellationToken cancellationToken) =>
        (FrameImageResult<FakeImage>)await Record(new FakeCall(frame, quality, build: false, cancellationToken));

    public async Task<FrameProxyResult> EnsureProxyAsync(Frame frame, CancellationToken cancellationToken)
    {
        var result = (FrameProxyResult)await Record(new FakeCall(frame, FrameImageQuality.Proxy, build: true, cancellationToken));
        if (result.Succeeded)
        {
            Proxies[frame.Id] = true;
        }

        return result;
    }

    public void FramesChanged(ImmutableArray<Frame> frames) => LastFrames = frames;

    public bool IsStale(Frame frame, string version)
    {
        Interlocked.Increment(ref _staleChecks);
        return Versions.TryGetValue(frame.Id, out var now) && now != version;
    }

    /// <summary>The next call the cache makes, within five seconds.</summary>
    public FakeCall Next()
    {
        if (!_pending.TryTake(out var call, TimeSpan.FromSeconds(5)))
        {
            throw new TimeoutException("The cache made no call.");
        }

        return call;
    }

    /// <summary>True when no call comes within <paramref name="milliseconds"/>.</summary>
    public bool Quiet(int milliseconds = 150) => !_pending.TryTake(out _, milliseconds);

    private async Task<object> Record(FakeCall call)
    {
        _all.Enqueue(call);
        if (AutoComplete is { } bytes)
        {
            if (AutoDelay > 0)
            {
                await Task.Delay(Random.Shared.Next(AutoDelay + 1));
            }

            var failure = call.Quality == FrameImageQuality.Proxy ? AutoCacheFailure : null;
            if (call.Build && failure is null)
            {
                Proxies[call.Frame.Id] = true;
            }

            call.Succeed(bytes, failure, Versions.GetValueOrDefault(call.Frame.Id));
            return await call.Result;
        }

        _pending.Add(call);
        return await (IgnoreCancellation ? call.Result : call.Result.WaitAsync(call.Cancellation));
    }
}
