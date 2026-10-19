using System.Collections.Concurrent;
using System.Collections.Immutable;
using TenLapse.Core.Preview;
using TenLapse.Core.Projects;
using TenLapse.Tests.Projects;

namespace TenLapse.Tests.Preview;

public sealed class ThumbnailCacheTests
{
    private static readonly ThumbnailSize _small = new(48, 32);

    [Theory]
    [InlineData(32, 1.0, 32)]
    [InlineData(32, 1.5, 48)]
    [InlineData(32, 1.25, 48)]
    [InlineData(48, 2.0, 96)]
    [InlineData(96, 1.75, 192)]
    [InlineData(96, 4.0, 256)]
    public void ASizeIsTheFirstStepThatCoversTheScreen(double heightDips, double scale, int height)
    {
        var size = ThumbnailSize.For(heightDips, scale);

        Assert.Equal(height, size.Height);
        Assert.Equal(height * 3 / 2, size.Width);
    }

    [Fact]
    public async Task ItLoadsTheWantedFramesInTheOrderGiven()
    {
        var source = new GatedSource();
        using var cache = new ThumbnailCache<FakeImage>(source, new ThumbnailCacheOptions { Workers = 1 });
        var frames = TestFrames.Folder("a", 4);
        cache.SetFrames([.. frames]);
        cache.Want([frames[2], frames[0], frames[3]], _small);

        for (var i = 0; i < 3; i++)
        {
            (await source.NextAsync()).Release();
        }

        Assert.Equal([frames[2].Id, frames[0].Id, frames[3].Id], source.Started);
        await WaitAsync(() => cache.Count == 3);
        using var lease = cache.Acquire(frames[0].Id);
        Assert.NotNull(lease);
        Assert.Equal(_small, lease.Size);
        Assert.Null(cache.Acquire(frames[1].Id));
    }

    [Fact]
    public async Task KeptFramesLoadAfterTheWantedOnesAndAWantDoesNotCancelThem()
    {
        var source = new GatedSource();
        using var cache = new ThumbnailCache<FakeImage>(source, new ThumbnailCacheOptions { Workers = 1 });
        var frames = TestFrames.Folder("a", 5);
        var large = new ThumbnailSize(72, 48);
        cache.SetFrames([.. frames]);
        cache.Want([frames[0]], _small);
        var first = await source.NextAsync();

        cache.Keep([frames[4], frames[3]], large);
        cache.Want([frames[0], frames[1]], _small);
        first.Release();
        (await source.NextAsync()).Release();
        var kept = await source.NextAsync();

        // The strip scrolls on: the kept frame's load goes on, and the newly wanted frame comes before the next kept one.
        cache.Want([frames[2]], _small);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.False(kept.Token.IsCancellationRequested);
        kept.Release();
        (await source.NextAsync()).Release();
        (await source.NextAsync()).Release();

        Assert.Equal([frames[0].Id, frames[1].Id, frames[4].Id, frames[2].Id, frames[3].Id], source.Started);
        await WaitAsync(() => cache.Count == 5);
        using var lease = cache.Acquire(frames[3].Id);
        Assert.Equal(large, lease!.Size);
    }

    [Fact]
    public async Task KeptFramesSurviveTheBudgetUntilTheyAreLetGo()
    {
        var source = new GatedSource { AutoRelease = true, BytesEach = 1000 };
        using var cache = new ThumbnailCache<FakeImage>(source, new ThumbnailCacheOptions { Workers = 1, MemoryBudgetBytes = 3000 });
        var frames = TestFrames.Folder("a", 40);
        cache.SetFrames([.. frames]);
        cache.Keep([frames[39]], _small);
        await WaitAsync(() => cache.Acquire(frames[39].Id) is { } lease && Disposed(lease));

        for (var start = 0; start < 30; start += 3)
        {
            var view = frames.Skip(start).Take(3).ToList();
            cache.Want(view, _small);
            await WaitAsync(() => view.All(frame => cache.Acquire(frame.Id) is { } lease && Disposed(lease)));
            Assert.True(cache.Acquire(frames[39].Id) is { } still && Disposed(still), "The kept frame was trimmed.");
        }

        cache.Keep([], _small);
        cache.Want(frames.Skip(30).Take(3).ToList(), _small);
        await WaitAsync(() => cache.Acquire(frames[39].Id) is null);
    }

    [Fact]
    public async Task AFrameThatScrollsAwayHasItsLoadCancelled()
    {
        var source = new GatedSource();
        using var cache = new ThumbnailCache<FakeImage>(source, new ThumbnailCacheOptions { Workers = 1 });
        var frames = TestFrames.Folder("a", 3);
        cache.Want([frames[0]], _small);
        var first = await source.NextAsync();

        cache.Want([frames[1], frames[2]], _small);

        await WaitAsync(() => first.Token.IsCancellationRequested);
        first.Release();
        await WaitAsync(() => cache.Cancelled == 1);
        (await source.NextAsync()).Release();
        (await source.NextAsync()).Release();
        await WaitAsync(() => cache.Count == 2);
        Assert.Null(cache.Acquire(frames[0].Id));
    }

    [Fact]
    public async Task MemoryStaysWithinTheBudgetByDroppingWhatIsNotWanted()
    {
        var source = new GatedSource { AutoRelease = true, BytesEach = 1000 };
        using var cache = new ThumbnailCache<FakeImage>(source, new ThumbnailCacheOptions { Workers = 2, MemoryBudgetBytes = 5000 });
        var frames = TestFrames.Folder("a", 100);
        cache.SetFrames([.. frames]);

        for (var start = 0; start < 100; start += 4)
        {
            var view = frames.Skip(start).Take(4).ToList();
            cache.Want(view, _small);
            await WaitAsync(() => view.All(frame => cache.Acquire(frame.Id) is { } lease && Disposed(lease)));
            Assert.True(cache.Bytes <= 5000, $"{cache.Bytes} bytes held");
        }

        Assert.True(cache.Count <= 5);
        Assert.True(source.Disposed >= 95, $"{source.Disposed} disposed");
    }

    [Fact]
    public async Task ALeasedImageOutlivesItsEviction()
    {
        var source = new GatedSource { AutoRelease = true, BytesEach = 1000 };
        using var cache = new ThumbnailCache<FakeImage>(source, new ThumbnailCacheOptions { Workers = 1, MemoryBudgetBytes = 1000 });
        var frames = TestFrames.Folder("a", 2);
        cache.Want([frames[0]], _small);
        await WaitAsync(() => cache.Count == 1);
        var lease = cache.Acquire(frames[0].Id)!;

        cache.Want([frames[1]], _small);
        await WaitAsync(() => cache.Acquire(frames[1].Id) is { } other && Disposed(other) && cache.Count == 1);

        Assert.False(lease.Image.IsDisposed);
        lease.Dispose();
        Assert.True(lease.Image.IsDisposed);
    }

    [Fact]
    public async Task AnUnreadablePhotoIsReportedOnceAndKeepsItsPlaceholder()
    {
        var source = new GatedSource { AutoRelease = true, Unreadable = true };
        using var cache = new ThumbnailCache<FakeImage>(source, new ThumbnailCacheOptions { Workers = 1, RetryFailedAfter = TimeSpan.FromHours(1) });
        var failures = new ConcurrentQueue<Guid>();
        cache.Failed += (_, e) => failures.Enqueue(e.Frame.Id);
        var frame = TestFrames.At(@"C:\Photos\a\IMG_0001.jpg");

        cache.Want([frame], _small);
        await WaitAsync(() => cache.FailureOf(frame.Id) is not null);
        cache.Want([frame], _small);
        cache.Want([frame], ThumbnailSize.For(64, 1));
        await Task.Delay(100, TestContext.Current.CancellationToken);

        Assert.Single(failures);
        Assert.Single(source.Started);
        Assert.Equal(SkipReason.DecodeFailed, cache.FailureOf(frame.Id)!.File.Reason);
    }

    [Fact]
    public async Task AFrameThatLeftTheProjectIsFreedAndItsLoadDropped()
    {
        var source = new GatedSource();
        using var cache = new ThumbnailCache<FakeImage>(source, new ThumbnailCacheOptions { Workers = 1 });
        var frames = TestFrames.Folder("a", 2);
        cache.SetFrames([.. frames]);
        cache.Want([frames[0], frames[1]], _small);
        (await source.NextAsync()).Release();
        var second = await source.NextAsync();
        await WaitAsync(() => cache.Count == 1);
        var image = cache.Acquire(frames[0].Id)!;
        var held = image.Image;
        image.Dispose();

        cache.SetFrames([]);

        Assert.True(held.IsDisposed);
        Assert.Equal(0, cache.Count);
        await WaitAsync(() => second.Token.IsCancellationRequested);
    }

    [Fact]
    public async Task ANewSizeLoadsAgainButKeepsTheOldPictureUntilThen()
    {
        var source = new GatedSource();
        using var cache = new ThumbnailCache<FakeImage>(source, new ThumbnailCacheOptions { Workers = 1 });
        var frame = TestFrames.At(@"C:\Photos\a\IMG_0001.jpg");
        cache.Want([frame], _small);
        (await source.NextAsync()).Release();
        await WaitAsync(() => cache.Count == 1);

        var large = ThumbnailSize.For(96, 1);
        cache.Want([frame], large);
        var reload = await source.NextAsync();

        using (var old = cache.Acquire(frame.Id))
        {
            Assert.Equal(_small, old!.Size);
        }

        reload.Release();
        await WaitAsync(() => SizeOf(cache, frame) == large);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public async Task DisposingStopsTheWorkersAndFreesTheImages()
    {
        var source = new GatedSource { AutoRelease = true };
        var cache = new ThumbnailCache<FakeImage>(source, new ThumbnailCacheOptions { Workers = 2 });
        var frames = TestFrames.Folder("a", 3);
        cache.Want(frames, _small);
        await WaitAsync(() => cache.Count == 3);

        cache.Dispose();
        await cache.WhenStopped().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(3, source.Disposed);
        Assert.Null(cache.Acquire(frames[0].Id));
    }

    // #82 review, minor 4: an embedded picture is loaded again once the source has better (the proxy appeared).
    [Fact]
    public async Task AnEmbeddedPictureIsLoadedAgainOnceTheProxyExists()
    {
        var source = new GatedSource { AutoRelease = true, Origin = ThumbnailOrigin.Embedded };
        using var cache = new ThumbnailCache<FakeImage>(source, new ThumbnailCacheOptions { Workers = 1 });
        var frames = TestFrames.Folder("a", 2);
        cache.Want(frames, _small);
        await WaitAsync(() => cache.Count == 2);

        cache.Recheck();
        Assert.Equal(2, source.Started.Count);

        source.Origin = ThumbnailOrigin.Proxy;
        source.Better.Add(frames[1].Id);
        cache.Recheck();

        await WaitAsync(() => OriginOf(cache, frames[1]) == ThumbnailOrigin.Proxy);
        Assert.Equal(ThumbnailOrigin.Embedded, OriginOf(cache, frames[0]));
        Assert.Equal(3, source.Started.Count);
        Assert.Equal(1, source.Disposed);
    }

    // #82 review, M3: Want, SetFrames and Dispose cancel loads a worker may be finishing. A cancel that met a disposed
    // source returned a faulted task nobody observed, which the app shows as an unexpected error.
    [Fact]
    public async Task ScrollingNeverLeavesAnUnobservedFaultedCancel()
    {
        var unobserved = 0;
        void OnUnobserved(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            if (e.Exception.InnerExceptions.Any(exception => exception is ObjectDisposedException))
            {
                Interlocked.Increment(ref unobserved);
                e.SetObserved();
            }
        }

        TaskScheduler.UnobservedTaskException += OnUnobserved;
        try
        {
            var frames = TestFrames.Folder("a", 4000);
            var source = new QuickSource();
            using (var cache = new ThumbnailCache<FakeImage>(source, new ThumbnailCacheOptions { Workers = 3, MemoryBudgetBytes = 1 << 30 }))
            {
                cache.SetFrames([.. frames]);
                var clock = System.Diagnostics.Stopwatch.StartNew();
                for (var i = 0; clock.Elapsed < TimeSpan.FromSeconds(3); i++)
                {
                    // A scroll: a view of 40 frames that moves 3 frames a call, and now and then a new frame list.
                    var start = (i * 3) % (frames.Length - 40);
                    cache.Want(frames.Skip(start).Take(40).ToList(), _small);
                    if (i % 500 == 0)
                    {
                        cache.SetFrames([.. frames]);
                    }
                }

                Assert.True(source.Loads > 100, $"only {source.Loads} loads ran");
            }

            await Task.Delay(200, TestContext.Current.CancellationToken);
            for (var round = 0; round < 3; round++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }

            Assert.Equal(0, Volatile.Read(ref unobserved));
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= OnUnobserved;
        }
    }

    private static ThumbnailOrigin? OriginOf(ThumbnailCache<FakeImage> cache, Frame frame)
    {
        using var lease = cache.Acquire(frame.Id);
        return lease?.Origin;
    }

    private static ThumbnailSize SizeOf(ThumbnailCache<FakeImage> cache, Frame frame)
    {
        using var lease = cache.Acquire(frame.Id);
        return lease?.Size ?? default;
    }

    // Takes a lease and gives it back at once: true.
    private static bool Disposed(ThumbnailLease<FakeImage> lease)
    {
        lease.Dispose();
        return true;
    }

    private static async Task WaitAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out.");
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
    }

    internal sealed class FakeImage : IDisposable
    {
        private readonly Action _disposed;

        public FakeImage(Action disposed) => _disposed = disposed;

        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            if (!IsDisposed)
            {
                IsDisposed = true;
                _disposed();
            }
        }
    }

    // A source whose loads wait until the test releases them (or not, with AutoRelease).
    private sealed class GatedSource : IThumbnailSource<FakeImage>
    {
        private readonly Channel _pending = new();
        private int _disposed;

        public bool AutoRelease { get; init; }

        public bool Unreadable { get; init; }

        public long BytesEach { get; init; } = 100;

        public ThumbnailOrigin Origin { get; set; } = ThumbnailOrigin.Photo;

        public ConcurrentBag<Guid> Better { get; } = [];

        public ConcurrentQueue<Guid> Started { get; } = new();

        public bool HasBetter(Frame frame, ThumbnailOrigin origin) => origin == ThumbnailOrigin.Embedded && Better.Contains(frame.Id);

        public int Disposed => Volatile.Read(ref _disposed);

        public Task<Load> NextAsync() => _pending.ReadAsync();

        public async Task<ThumbnailResult<FakeImage>> LoadAsync(Frame frame, ThumbnailSize size, CancellationToken cancellationToken)
        {
            Started.Enqueue(frame.Id);
            if (!AutoRelease)
            {
                var load = new Load(cancellationToken);
                _pending.Write(load);
                await load.Released.Task.WaitAsync(cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return Unreadable
                ? ThumbnailResult<FakeImage>.Failed(new FrameLoadFailure(new SkippedFile(frame.SourcePath, SkipReason.DecodeFailed, "Damaged.")))
                : ThumbnailResult<FakeImage>.Loaded(new FakeImage(() => Interlocked.Increment(ref _disposed)), BytesEach, Origin);
        }
    }

    // A source that answers at once, after one hop to the thread pool.
    private sealed class QuickSource : IThumbnailSource<FakeImage>
    {
        private int _loads;

        public int Loads => Volatile.Read(ref _loads);

        public async Task<ThumbnailResult<FakeImage>> LoadAsync(Frame frame, ThumbnailSize size, CancellationToken cancellationToken)
        {
            await Task.Yield();
            Interlocked.Increment(ref _loads);
            return ThumbnailResult<FakeImage>.Loaded(new FakeImage(() => { }), 1, ThumbnailOrigin.Proxy);
        }
    }

    internal sealed class Load(CancellationToken token)
    {
        public CancellationToken Token { get; } = token;

        public TaskCompletionSource Released { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => Released.TrySetResult();
    }

    // A tiny unbounded channel: loads in the order they started.
    private sealed class Channel
    {
        private readonly Lock _gate = new();
        private readonly Queue<Load> _items = new();
        private readonly Queue<TaskCompletionSource<Load>> _readers = new();

        public void Write(Load load)
        {
            TaskCompletionSource<Load>? reader = null;
            lock (_gate)
            {
                if (!_readers.TryDequeue(out reader))
                {
                    _items.Enqueue(load);
                }
            }

            reader?.TrySetResult(load);
        }

        public Task<Load> ReadAsync()
        {
            lock (_gate)
            {
                if (_items.TryDequeue(out var load))
                {
                    return Task.FromResult(load);
                }

                var reader = new TaskCompletionSource<Load>(TaskCreationOptions.RunContinuationsAsynchronously);
                _readers.Enqueue(reader);
                return reader.Task.WaitAsync(TimeSpan.FromSeconds(10));
            }
        }
    }
}
