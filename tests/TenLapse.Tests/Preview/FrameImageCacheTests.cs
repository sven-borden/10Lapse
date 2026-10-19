using System.Collections.Immutable;
using System.Diagnostics;
using TenLapse.Core.Preview;
using TenLapse.Core.Projects;
using TenLapse.Tests.Projects;

namespace TenLapse.Tests.Preview;

public sealed class FrameImageCacheTests
{
    private static readonly FrameImageCacheOptions _one = new() { Workers = 1, BuildProxiesInBackground = false, Neighbours = 0 };

    [Fact]
    public void LoadsTheFrameOnScreenFirstSmallThenItsProxyWhilePaused()
    {
        var source = new FakeFrameImageSource();
        using var cache = new FrameImageCache<FakeImage>(source, _one with { Neighbours = 1 });
        var frames = Frames("a", 100);

        cache.SetFocus(50, playing: false, lookahead: 0);
        cache.SetFrames(frames);

        var quick = source.Next();
        Assert.Equal((frames[50].Id, FrameImageQuality.Quick), (quick.Frame.Id, quick.Quality));
        var small = quick.Succeed();
        var proxy = source.Next();
        Assert.Equal((frames[50].Id, FrameImageQuality.Proxy), (proxy.Frame.Id, proxy.Quality));
        var large = proxy.Succeed();

        Eventually(() => Quality(cache, frames[50]) == FrameImageQuality.Proxy);
        Assert.True(small!.IsDisposed);
        Assert.False(large!.IsDisposed);

        // Then the neighbours, small first.
        var next = source.Next();
        Assert.Contains(next.Frame.Id, new[] { frames[49].Id, frames[51].Id });
        Assert.Equal(FrameImageQuality.Quick, next.Quality);
    }

    [Fact]
    public void AFrameWithAProxyOnDiskLoadsTheProxyStraightAway()
    {
        var source = new FakeFrameImageSource();
        using var cache = new FrameImageCache<FakeImage>(source, _one);
        var frames = Frames("a", 10);
        source.Proxies[frames[3].Id] = true;

        cache.SetFocus(3, playing: false, lookahead: 0);
        cache.SetFrames(frames);

        var call = source.Next();
        Assert.Equal((frames[3].Id, FrameImageQuality.Proxy), (call.Frame.Id, call.Quality));
    }

    [Fact]
    public void WhilePlayingItLoadsTheFramesAheadInOrderAcrossTheLoop()
    {
        var source = new FakeFrameImageSource();
        using var cache = new FrameImageCache<FakeImage>(source, _one);
        var frames = Frames("a", 10);

        cache.SetFocus(8, playing: true, lookahead: 4);
        cache.SetFrames(frames);

        var order = new List<int>();
        for (var i = 0; i < 5; i++)
        {
            var call = source.Next();
            Assert.Equal(FrameImageQuality.Quick, call.Quality);
            order.Add(frames.IndexOf(call.Frame));
            call.Succeed();
        }

        Assert.Equal([8, 9, 0, 1, 2], order);

        // While playing, a frame with a small image waits for the background pass: nothing more to do here.
        Assert.True(source.Quiet());
    }

    [Fact]
    public void ARemovedFramesResultIsFreedAndNeverShown()
    {
        var source = new FakeFrameImageSource();
        using var cache = new FrameImageCache<FakeImage>(source, _one);
        var frames = Frames("a", 10);
        cache.SetFocus(5, playing: false, lookahead: 0);
        cache.SetFrames(frames);
        var call = source.Next();

        cache.SetFrames(frames.RemoveAt(5));
        var image = call.Succeed();

        Eventually(() => image!.IsDisposed);
        Assert.Null(cache.Acquire(frames[5].Id));
        Assert.False(call.Cancellation.IsCancellationRequested);
    }

    [Fact]
    public void ANewProjectCancelsTheOldLoadsAndAResultThatComesAnywayIsFreed()
    {
        var source = new FakeFrameImageSource { IgnoreCancellation = true };
        using var cache = new FrameImageCache<FakeImage>(source, _one);
        var first = Frames("first", 10);
        var second = Frames("second", 10);
        cache.SetFrames(first);
        var old = source.Next();

        cache.SetFrames(second);
        Assert.True(old.Cancellation.IsCancellationRequested);
        var late = old.Succeed();

        var next = source.Next();
        Assert.Equal(second[0].Id, next.Frame.Id);
        Eventually(() => late!.IsDisposed);
        Assert.Null(cache.Acquire(first[0].Id));
        Assert.Equal(second, source.LastFrames);
    }

    [Fact]
    public void AReorderKeepsTheImages()
    {
        var source = new FakeFrameImageSource { AutoComplete = 100 };
        using var cache = new FrameImageCache<FakeImage>(source, _one);
        var frames = Frames("a", 4);
        cache.SetFrames(frames);
        Eventually(() => Quality(cache, frames[0]) == FrameImageQuality.Proxy);
        var calls = source.All.Count;

        cache.SetFrames([frames[3], frames[2], frames[1], frames[0]]);

        // Frame 0 is now last; the focus (index 0) is frame 3, which loads; frame 0 keeps its image.
        Eventually(() => Quality(cache, frames[3]) == FrameImageQuality.Proxy);
        Assert.Equal(FrameImageQuality.Proxy, Quality(cache, frames[0]));
        Assert.DoesNotContain(source.All.Skip(calls), call => call.Frame.Id == frames[0].Id);
    }

    [Fact]
    public void ALeasedImageIsFreedOnlyWhenTheLeaseGoes()
    {
        var source = new FakeFrameImageSource();
        using var cache = new FrameImageCache<FakeImage>(source, _one);
        var frames = Frames("a", 3);
        cache.SetFrames(frames);
        var image = source.Next().Succeed()!;
        Eventually(() => cache.Count == 1);

        var lease = cache.Acquire(frames[0].Id)!;
        Assert.Same(image, lease.Image);
        cache.SetFrames(Frames("b", 3));

        Assert.False(image.IsDisposed);
        lease.Dispose();
        lease.Dispose();
        Assert.Equal(1, image.Disposals);
    }

    [Fact]
    public void PastTheBudgetTheLeastRecentlyUsedGoButNeverTheFramesAboutToShow()
    {
        var source = new FakeFrameImageSource { AutoComplete = 100 };
        using var cache = new FrameImageCache<FakeImage>(source, _one with { MemoryBudgetBytes = 250 });
        var frames = Frames("a", 10);
        cache.SetFrames(frames);
        for (var i = 0; i < 4; i++)
        {
            cache.SetFocus(i, playing: false, lookahead: 0);
            var index = i;
            Eventually(() => Quality(cache, frames[index]) == FrameImageQuality.Proxy);
        }

        Assert.True(cache.Bytes <= 250);
        Assert.Null(cache.Acquire(frames[0].Id));
        Assert.Null(cache.Acquire(frames[1].Id));
        using (var kept = cache.Acquire(frames[3].Id))
        {
            Assert.NotNull(kept);
        }

        // Five frames about to show, 100 bytes each, on a 50-byte budget: all of them stay.
        using var tight = new FrameImageCache<FakeImage>(new FakeFrameImageSource { AutoComplete = 100 }, _one with { MemoryBudgetBytes = 50, Neighbours = 2 });
        tight.SetFocus(5, playing: false, lookahead: 0);
        tight.SetFrames(frames);
        Eventually(() => Enumerable.Range(3, 5).All(index => Quality(tight, frames[index]) != FrameImageQuality.None));
        Assert.Equal(5, tight.Count);
    }

    [Fact]
    public void AnUnreadableFrameIsToldOnceAndTriedAgainLater()
    {
        var source = new FakeFrameImageSource();
        using var cache = new FrameImageCache<FakeImage>(source, _one with { RetryFailedAfter = TimeSpan.Zero });
        var told = new List<FrameLoadFailedEventArgs>();
        cache.FrameFailed += (_, e) => told.Add(e);
        var frames = Frames("a", 3);
        cache.SetFrames(frames);

        source.Next().Fail(SkipReason.FileNotFound);
        Eventually(() => told.Count == 1);
        Assert.Equal(SkipReason.FileNotFound, told[0].Failure.File.Reason);

        // Tried again (the photo may be back); failing again tells nothing new.
        source.Next().Fail(SkipReason.FileNotFound);
        var retry = source.Next();
        Assert.Equal(frames[0].Id, retry.Frame.Id);
        Assert.Single(told);
        retry.Succeed();
        Eventually(() => Quality(cache, frames[0]) != FrameImageQuality.None);
        Assert.Null(cache.FailureOf(frames[0].Id));
    }

    [Fact]
    public void AnUnreadableFrameKeepsItsPlaceholderUntilItIsTimeToRetry()
    {
        var source = new FakeFrameImageSource();
        using var cache = new FrameImageCache<FakeImage>(source, _one);
        var frames = Frames("a", 3);
        cache.SetFrames(frames);

        source.Next().Fail(SkipReason.DecodeFailed);

        Assert.True(source.Quiet());
        Assert.Equal(SkipReason.DecodeFailed, cache.FailureOf(frames[0].Id)!.File.Reason);
    }

    [Fact]
    public void AProxyThatCannotBeSavedIsReportedAndNotBuiltAgain()
    {
        var source = new FakeFrameImageSource();
        using var cache = new FrameImageCache<FakeImage>(source, _one with { BuildProxiesInBackground = true });
        var failures = new List<Exception>();
        cache.CacheFailed += (_, e) => failures.Add(e.Exception);
        var frames = Frames("a", 3);
        cache.SetFrames(frames);
        var first = source.Next();
        Assert.Equal(FrameImageQuality.Quick, first.Quality);
        first.Succeed();
        var builds = new List<FakeCall>();
        for (var i = 0; i < 3; i++)
        {
            var call = source.Next();
            Assert.True(call.Build || call.Frame.Id == frames[0].Id);
            if (call.Frame.Id == frames[1].Id)
            {
                call.Succeed(cacheFailure: new IOException("The disk is full."));
            }
            else
            {
                call.Succeed();
            }

            builds.Add(call);
        }

        Eventually(() => cache.ProxyProgress == (3, 3));
        Assert.True(source.Quiet());
        Assert.Single(failures);
        Assert.Single(builds, call => call.Frame.Id == frames[1].Id);
    }

    [Fact]
    public void ASourceThatThrowsIsAnUnexpectedFailureNotAnUnreadablePhoto()
    {
        var source = new FakeFrameImageSource();
        using var cache = new FrameImageCache<FakeImage>(source, _one);
        var unexpected = new List<Exception>();
        var unreadable = 0;
        cache.UnexpectedFailure += (_, e) => unexpected.Add(e.Exception);
        cache.FrameFailed += (_, _) => unreadable++;
        var frames = Frames("a", 2);
        cache.SetFrames(frames);

        source.Next().Throw(new InvalidOperationException("The device is gone."));

        Eventually(() => unexpected.Count == 1);
        Assert.Equal(0, unreadable);
        Assert.NotNull(cache.FailureOf(frames[0].Id));
    }

    [Fact]
    public void TheBackgroundPassBuildsEveryProxyOnce()
    {
        var source = new FakeFrameImageSource { AutoComplete = 10, AutoDelay = 1 };
        using var cache = new FrameImageCache<FakeImage>(source, new FrameImageCacheOptions { Workers = 3, Neighbours = 1 });
        var frames = Frames("a", 60);
        cache.SetFocus(30, playing: false, lookahead: 0);
        cache.SetFrames(frames);

        Eventually(() => cache.ProxyProgress == (60, 60));

        var builds = source.All.Where(call => call.Build).Select(call => call.Frame.Id).ToList();
        Assert.Equal(builds.Count, builds.Distinct().Count());
        Assert.All(frames, frame => Assert.True(source.Proxies.ContainsKey(frame.Id) || Quality(cache, frame) == FrameImageQuality.Proxy));
    }

    [Fact]
    public async Task SnapshotChangesRacingTheLoadsNeverShowAFreedImageOrLeakOne()
    {
        var source = new FakeFrameImageSource { AutoComplete = 1_000, AutoDelay = 2 };
        var cache = new FrameImageCache<FakeImage>(source, new FrameImageCacheOptions { Workers = 4, MemoryBudgetBytes = 20_000, Neighbours = 3 });
        var projects = new[] { Frames("a", 200), Frames("b", 50), Frames("c", 400) };
        var random = new Random(7);
        var leases = new List<FrameImageLease<FakeImage>>();
        var clock = Stopwatch.StartNew();
        var frames = projects[0];
        while (clock.Elapsed < TimeSpan.FromSeconds(2))
        {
            switch (random.Next(6))
            {
                case 0:
                    // A New project, or frames removed, or a reorder.
                    var project = projects[random.Next(projects.Length)];
                    frames = random.Next(3) switch
                    {
                        0 => project,
                        1 => [.. project.Where((_, index) => index % 3 != 0)],
                        _ => [.. project.Reverse()],
                    };
                    cache.SetFrames(frames);
                    break;
                case 1:
                    cache.SetFocus(random.Next(frames.Length), playing: random.Next(2) == 0, lookahead: 8);
                    break;
                default:
                    if (cache.Acquire(frames[random.Next(frames.Length)].Id) is { } lease)
                    {
                        Assert.False(lease.Image.IsDisposed);
                        leases.Add(lease);
                    }

                    if (leases.Count > 5)
                    {
                        leases[0].Dispose();
                        leases.RemoveAt(0);
                    }

                    break;
            }

            Thread.Sleep(random.Next(2));
        }

        cache.Dispose();
        await cache.WhenStopped().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        foreach (var lease in leases)
        {
            Assert.False(lease.Image.IsDisposed);
            lease.Dispose();
        }

        // Every image a load made was freed exactly once.
        var images = source.All.Select(call => call.Image).OfType<FakeImage>().ToList();
        Assert.NotEmpty(images);
        Eventually(() => images.All(image => image.Disposals == 1));
    }

    private static ImmutableArray<Frame> Frames(string folder, int count) => [.. TestFrames.Folder(folder, count)];

    private static FrameImageQuality Quality(FrameImageCache<FakeImage> cache, Frame frame)
    {
        using var lease = cache.Acquire(frame.Id);
        return lease?.Quality ?? FrameImageQuality.None;
    }

    private static void Eventually(Func<bool> condition)
    {
        var waited = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(waited.Elapsed < TimeSpan.FromSeconds(5), "The cache did not get there within 5 s.");
            Thread.Sleep(5);
        }
    }
}
