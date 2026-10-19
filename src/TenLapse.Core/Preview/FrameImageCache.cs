using System.Collections.Immutable;
using TenLapse.Core.Projects;

namespace TenLapse.Core.Preview;

/// <summary>How <see cref="FrameImageCache{TImage}"/> works.</summary>
public sealed record FrameImageCacheOptions
{
    /// <summary>Loads at once. Worker 0 only ever loads what is about to be shown; the others also build proxies in the background.</summary>
    public int Workers { get; init; } = Math.Clamp(Environment.ProcessorCount / 2, 2, 4);

    /// <summary>
    /// The memory the images may hold together (192 MB is about 50 proxies of a 3:2 photo). The frames about to be
    /// shown stay even past it; the least recently used others go first.
    /// </summary>
    public long MemoryBudgetBytes { get; init; } = 192L << 20;

    /// <summary>Frames kept ready on each side of the playhead while paused, so stepping is instant.</summary>
    public int Neighbours { get; init; } = 6;

    /// <summary>Whether idle workers build the proxies of the whole project, from the playhead on.</summary>
    public bool BuildProxiesInBackground { get; init; } = true;

    /// <summary>
    /// How long a frame that could not be read keeps its placeholder before it is tried again (when it is on or near
    /// the playhead), so a photo put back where it was shows again.
    /// </summary>
    public TimeSpan RetryFailedAfter { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// After this many proxies in a row could not be saved (a full disk, an unwritable cache), the background pass stops
    /// for the life of the cache: it would only decode every photo for nothing. The frame on screen still gets its
    /// proxy-sized image while paused.
    /// </summary>
    public int StopPassAfterCacheFailures { get; init; } = 3;

    /// <summary>
    /// While paused, how often the photo of the frame on screen is checked (one stat), so a photo edited or replaced
    /// meanwhile is shown as the export would read it. Zero turns the check off.
    /// </summary>
    public TimeSpan RecheckPausedEvery { get; init; } = TimeSpan.FromSeconds(1);
}

/// <summary>
/// The preview's frame images (#19): keeps the frames around the playhead loaded, in memory, best first, and builds
/// the proxies of the whole project in the background. The render thread takes images with <see cref="Acquire"/>
/// without ever waiting; the player says where it is with <see cref="SetFocus"/>; the project's frames arrive with
/// <see cref="SetFrames"/>.
/// </summary>
/// <remarks>
/// <para>
/// Scheduling is pulled, not queued: a free worker asks what matters most right now (the frame on screen, then the
/// frames about to be, then the next proxy to build), so a seek, a scrub or a new project changes the order at once
/// and stale work is never started. While playing, a frame without a proxy is loaded small
/// (<see cref="FrameImageQuality.Quick"/>) so playback can start before the proxies exist; paused, the frame on screen
/// is upgraded to its proxy.
/// </para>
/// <para>
/// Images are kept by <see cref="Frame.Id"/>, so a reorder keeps them, and ids are never reused, so a result that
/// comes back for a frame the project no longer has (removed, or a replaced project) is disposed and never shown. An
/// image the render thread holds (<see cref="Acquire"/>) is disposed only when its lease is released, even if it was
/// evicted meanwhile.
/// </para>
/// <para>
/// <see cref="Clear"/> (a lost graphics device) starts a new generation: a load still in flight when it is called comes
/// back with an image made on the old device, and is freed instead of kept. A source that throws (a lost device, a
/// bug) is reported as <see cref="UnexpectedFailure"/> and retried later, but never marks the frame unreadable: the
/// render thread keeps the picture it had.
/// </para>
/// <para>Thread-safe. Events are raised on worker threads, outside the cache's lock.</para>
/// </remarks>
public sealed class FrameImageCache<TImage> : IDisposable
    where TImage : class, IDisposable
{
    private readonly Lock _gate = new();
    private readonly IFrameImageSource<TImage> _source;
    private readonly FrameImageCacheOptions _options;
    private readonly CancellationTokenSource _disposing = new();
    private readonly Dictionary<Guid, Entry> _entries = [];
    private readonly Dictionary<Guid, (FrameLoadFailure Failure, long At, bool Unexpected)> _failed = [];
    private readonly HashSet<Guid> _reported = [];
    private readonly HashSet<Guid> _loading = [];
    private readonly HashSet<Guid> _building = [];
    private readonly HashSet<Guid> _proxyReady = [];
    private readonly HashSet<Guid> _proxyFailed = [];
    private readonly Task[] _workers;
    private TaskCompletionSource _wake = NewWake();
    private ImmutableArray<Frame> _frames = [];
    private Dictionary<Guid, int> _indexOf = [];
    private CancellationTokenSource _projectCancel = new();
    private int _focus;
    private bool _playing;
    private int _lookahead;
    private long _bytes;
    private long _useClock;
    private bool _disposed;

    // Bumped by Clear (a new graphics device): results of loads started before belong to the old one.
    private int _generation;

    // Proxies that could not be saved, in a row; past the option the background pass stops.
    private int _cacheFailuresInARow;
    private bool _passStopped;

    // The paused frame's stat check: when it last ran (Environment.TickCount64), and whether one is running.
    private long _lastCheck;
    private bool _checking;

    /// <summary>Starts the workers; they wait until there are frames.</summary>
    public FrameImageCache(IFrameImageSource<TImage> source, FrameImageCacheOptions? options = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _options = options ?? new FrameImageCacheOptions();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(_options.Workers);
        ArgumentOutOfRangeException.ThrowIfNegative(_options.MemoryBudgetBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(_options.Neighbours);
        _workers = [.. Enumerable.Range(0, _options.Workers).Select(worker => Task.Run(() => WorkAsync(worker)))];
    }

    /// <summary>A frame's image arrived or got better; the render thread redraws if it shows that frame.</summary>
    public event EventHandler<FrameImageReadyEventArgs>? ImageReady;

    /// <summary>
    /// A frame of the current project cannot be shown. Raised once per frame, until the frame loads again or
    /// <see cref="SetFrames"/> drops it.
    /// </summary>
    public event EventHandler<FrameLoadFailedEventArgs>? FrameFailed;

    /// <summary>A proxy could not be written or read; the preview goes on from the photos, slower.</summary>
    public event EventHandler<PreviewFailureEventArgs>? CacheFailed;

    /// <summary>The cache's own code, or a listener, failed (a bug); the preview goes on.</summary>
    public event EventHandler<PreviewFailureEventArgs>? UnexpectedFailure;

    /// <summary>The bytes the images in memory hold.</summary>
    public long Bytes
    {
        get
        {
            lock (_gate)
            {
                return _bytes;
            }
        }
    }

    /// <summary>The images in memory.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    /// <summary>
    /// How many of the current frames the background pass is done with (proxy built or found in this session, or the
    /// photo or its proxy failed), and how many frames there are.
    /// </summary>
    public (int Done, int Total) ProxyProgress
    {
        get
        {
            lock (_gate)
            {
                return (_frames.Count(frame => _proxyReady.Contains(frame.Id) || _proxyFailed.Contains(frame.Id) || _failed.ContainsKey(frame.Id)), _frames.Length);
            }
        }
    }

    /// <summary>
    /// The project's frames, in order: from a <see cref="ProjectSnapshot"/>. The same array again changes nothing.
    /// Images of frames no longer there are freed; when the frames are a different project (no frame in common),
    /// the loads still in flight for the old one are cancelled.
    /// </summary>
    public void SetFrames(ImmutableArray<Frame> frames)
    {
        if (frames.IsDefault)
        {
            frames = [];
        }

        var dispose = new List<TImage>();
        CancellationTokenSource? cancelled = null;
        lock (_gate)
        {
            if (_disposed || frames == _frames)
            {
                return;
            }

            var indexOf = new Dictionary<Guid, int>(frames.Length);
            for (var i = 0; i < frames.Length; i++)
            {
                indexOf[frames[i].Id] = i;
            }

            if (!_indexOf.Keys.Any(indexOf.ContainsKey))
            {
                cancelled = _projectCancel;
                _projectCancel = new CancellationTokenSource();
            }

            _frames = frames;
            _indexOf = indexOf;
            _focus = Math.Clamp(_focus, 0, Math.Max(0, frames.Length - 1));
            foreach (var entry in _entries.Values.Where(entry => !indexOf.ContainsKey(entry.Id)).ToList())
            {
                Evict(entry, dispose);
            }

            foreach (var id in _failed.Keys.Where(id => !indexOf.ContainsKey(id)).ToList())
            {
                _failed.Remove(id);
                _reported.Remove(id);
            }
        }

        CancelQuietly(cancelled);
        DisposeAll(dispose);
        Guard(() => _source.FramesChanged(frames));
        Wake();
    }

    /// <summary>Where the player is: the frame on screen, whether it plays, and how many frames ahead to keep ready while it does.</summary>
    public void SetFocus(int index, bool playing, int lookahead)
    {
        lock (_gate)
        {
            // Before the frames arrive the index is kept as given; SetFrames clamps it.
            index = _frames.IsEmpty ? Math.Max(0, index) : Math.Clamp(index, 0, _frames.Length - 1);
            lookahead = Math.Max(0, lookahead);
            if (_disposed || (index == _focus && playing == _playing && lookahead == _lookahead))
            {
                return;
            }

            _focus = index;
            _playing = playing;
            _lookahead = lookahead;
        }

        Wake();
    }

    /// <summary>
    /// The best image of frame <paramref name="id"/> in memory, leased: it stays valid until the lease is disposed.
    /// Null when there is none yet. Never waits.
    /// </summary>
    public FrameImageLease<TImage>? Acquire(Guid id)
    {
        lock (_gate)
        {
            if (_disposed || !_entries.TryGetValue(id, out var entry))
            {
                return null;
            }

            entry.Leases++;
            entry.LastUsed = ++_useClock;
            return new FrameImageLease<TImage>(this, entry.Image, entry.Quality, entry);
        }
    }

    /// <summary>Why frame <paramref name="id"/> cannot be shown, or null.</summary>
    public FrameLoadFailure? FailureOf(Guid id)
    {
        lock (_gate)
        {
            // A source that threw says nothing about the photo: no placeholder for it.
            return _failed.TryGetValue(id, out var failed) && !failed.Unexpected ? failed.Failure : null;
        }
    }

    /// <summary>True once the background pass has stopped because proxies could not be saved (<see cref="FrameImageCacheOptions.StopPassAfterCacheFailures"/>).</summary>
    public bool IsPassStopped
    {
        get
        {
            lock (_gate)
            {
                return _passStopped;
            }
        }
    }

    /// <summary>
    /// Frees every image in memory (the graphics device was lost: they belong to the old one) and starts a new
    /// generation: loads still in flight are freed when they come back, and frames whose loads threw are tried again at
    /// once. The proxies stay.
    /// </summary>
    public void Clear()
    {
        var dispose = new List<TImage>();
        lock (_gate)
        {
            _generation++;
            foreach (var entry in _entries.Values.ToList())
            {
                Evict(entry, dispose);
            }

            foreach (var id in _failed.Where(pair => pair.Value.Unexpected).Select(pair => pair.Key).ToList())
            {
                _failed.Remove(id);
            }
        }

        DisposeAll(dispose);
        Wake();
    }

    /// <summary>Stops the workers (without waiting for loads in flight, whose results are freed) and frees the images.</summary>
    public void Dispose()
    {
        var dispose = new List<TImage>();
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (var entry in _entries.Values.ToList())
            {
                Evict(entry, dispose);
            }
        }

        CancelQuietly(_projectCancel);
        _ = _disposing.CancelAsync();
        DisposeAll(dispose);
        Wake();
    }

    // Cancels on the thread pool, never inline: a cancellation's continuations would otherwise run the workers' code on
    // the caller's thread (the UI or render thread). The source is not disposed: loads in flight still hold its token,
    // and it has no timer to release.
    private static void CancelQuietly(CancellationTokenSource? source)
    {
        if (source is not null)
        {
            _ = source.CancelAsync();
        }
    }

    /// <summary>For the tests: completes when every worker has stopped after <see cref="Dispose"/>.</summary>
    internal Task WhenStopped() => Task.WhenAll(_workers);

    internal void Release(object handle)
    {
        var entry = (Entry)handle;
        bool dispose;
        lock (_gate)
        {
            entry.Leases--;
            dispose = entry.Evicted && entry.Leases == 0;
        }

        if (dispose)
        {
            DisposeAll([entry.Image]);
        }
    }

    private async Task WorkAsync(int worker)
    {
        var stop = _disposing.Token;
        var problems = new List<Exception>();
        while (!stop.IsCancellationRequested)
        {
            // Taken before looking for work, so a change in between still wakes this worker.
            var wake = Volatile.Read(ref _wake).Task;
            Job? job = null;
            Check? check = null;
            var due = long.MaxValue;
            try
            {
                lock (_gate)
                {
                    if (!_disposed)
                    {
                        job = NextJob(worker, ref due, problems);
                        check = job is null ? NextCheck(worker, ref due) : null;
                    }
                }
            }
            catch (Exception exception)
            {
                problems.Add(exception);
            }

            // Reported outside the lock: a listener may show an InfoBar.
            foreach (var problem in problems)
            {
                ReportUnexpected(problem);
            }

            problems.Clear();
            if (check is not null)
            {
                RunCheck(check);
                continue;
            }

            if (job is null)
            {
                try
                {
                    if (due == long.MaxValue)
                    {
                        await wake.WaitAsync(stop).ConfigureAwait(false);
                    }
                    else
                    {
                        // A failed frame waits for its retry, the paused frame for its next check: nothing else may
                        // wake this worker meanwhile (review m1).
                        var wait = Math.Clamp(due - Environment.TickCount64, 1, int.MaxValue);
                        await wake.WaitAsync(TimeSpan.FromMilliseconds(wait), stop).ConfigureAwait(false);
                    }
                }
                catch (TimeoutException)
                {
                    // Time to retry or check.
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                continue;
            }

            try
            {
                await RunAsync(job).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                // RunAsync handles the source's failures; this is the cache's own.
                ReportUnexpected(exception);
                lock (_gate)
                {
                    _loading.Remove(job.Frame.Id);
                    _building.Remove(job.Frame.Id);
                }
            }
        }
    }

    // Under the lock. The most useful thing to do now, marked as in flight; null when there is nothing. A failed frame
    // still waiting for its retry lowers due to the moment it may be tried; failures of the source's hint go to problems.
    private Job? NextJob(int worker, ref long due, List<Exception> problems)
    {
        if (_frames.IsEmpty)
        {
            return null;
        }

        foreach (var index in Wanted())
        {
            var frame = _frames[index];
            if (_loading.Contains(frame.Id))
            {
                continue;
            }

            if (_failed.TryGetValue(frame.Id, out var failed))
            {
                // A photo put back where it was shows again: a frame on or near the playhead is retried after a while.
                var retryAt = failed.At + (long)_options.RetryFailedAfter.TotalMilliseconds;
                if (Environment.TickCount64 < retryAt)
                {
                    due = Math.Min(due, retryAt);
                    continue;
                }

                _failed.Remove(frame.Id);
            }

            // A stale image (the photo changed while paused on it) stays on screen until its replacement comes.
            var have = _entries.TryGetValue(frame.Id, out var entry) && !entry.Stale ? entry.Quality : FrameImageQuality.None;
            if (have == FrameImageQuality.Proxy)
            {
                continue;
            }

            FrameImageQuality want;
            if (_proxyReady.Contains(frame.Id) || Hint(frame, problems))
            {
                want = FrameImageQuality.Proxy;
            }
            else if (have == FrameImageQuality.None)
            {
                // Something to show soon: a small decode, ready long before a proxy would be.
                want = FrameImageQuality.Quick;
            }
            else if (index == _focus && !_playing)
            {
                // Paused on it: worth its proxy now. Even when its proxy cannot be saved, the source decodes the photo
                // at proxy size, so the frame being judged is sharp (review M2).
                want = FrameImageQuality.Proxy;
            }
            else
            {
                continue;
            }

            // The background pass is building this proxy right now: load it once it is there, not a second build.
            if (want == FrameImageQuality.Proxy && _building.Contains(frame.Id))
            {
                continue;
            }

            _loading.Add(frame.Id);
            return new Job(frame, want, Build: false, _projectCancel.Token, _generation);
        }

        if (!_options.BuildProxiesInBackground || _passStopped || (worker == 0 && _options.Workers > 1))
        {
            return null;
        }

        // The background pass: the next frame without a proxy, from the playhead on, wrapping.
        for (var step = 0; step < _frames.Length; step++)
        {
            var frame = _frames[(_focus + step) % _frames.Length];
            if (_proxyReady.Contains(frame.Id) || _proxyFailed.Contains(frame.Id) || _failed.ContainsKey(frame.Id)
                || _building.Contains(frame.Id) || _loading.Contains(frame.Id))
            {
                continue;
            }

            _building.Add(frame.Id);
            return new Job(frame, FrameImageQuality.Proxy, Build: true, _projectCancel.Token, _generation);
        }

        return null;
    }

    // Under the lock: the paused frame's photo is due for its stat check (worker 0 only, one at a time); otherwise due
    // is lowered to when it will be.
    private Check? NextCheck(int worker, ref long due)
    {
        var every = (long)_options.RecheckPausedEvery.TotalMilliseconds;
        if (worker != 0 || every <= 0 || _playing || _checking || _frames.IsEmpty
            || !_entries.TryGetValue(_frames[_focus].Id, out var entry) || entry.Stale || entry.Version is not { } version)
        {
            return null;
        }

        var now = Environment.TickCount64;
        if (now < _lastCheck + every)
        {
            due = Math.Min(due, _lastCheck + every);
            return null;
        }

        _lastCheck = now;
        _checking = true;
        return new Check(_frames[_focus], entry, version);
    }

    // Outside the lock: one stat of the photo. A photo changed since its image was loaded marks the image stale, so it
    // is loaded again (and stays on screen until then).
    private void RunCheck(Check check)
    {
        var stale = false;
        try
        {
            stale = _source.IsStale(check.Frame, check.Version);
        }
        catch (Exception exception)
        {
            ReportUnexpected(exception);
        }

        lock (_gate)
        {
            _checking = false;
            if (stale && !check.Entry.Evicted)
            {
                check.Entry.Stale = true;
            }
        }

        if (stale)
        {
            Wake();
        }
    }

    // Under the lock: the frames to keep in memory, most wanted first: the playhead, then the frames ahead while playing
    // (looping), or the neighbours on both sides while paused.
    private IEnumerable<int> Wanted()
    {
        var count = _frames.Length;
        if (count == 0)
        {
            yield break;
        }

        yield return _focus;
        if (_playing)
        {
            for (var ahead = 1; ahead <= _lookahead && ahead < count; ahead++)
            {
                yield return (_focus + ahead) % count;
            }

            yield break;
        }

        for (var step = 1; step <= _options.Neighbours; step++)
        {
            if (_focus + step < count)
            {
                yield return _focus + step;
            }

            if (_focus - step >= 0)
            {
                yield return _focus - step;
            }
        }
    }

    // Under the lock: the failure is collected and reported once the lock is left (review m12).
    private bool Hint(Frame frame, List<Exception> problems)
    {
        try
        {
            return _source.HasProxy(frame);
        }
        catch (Exception exception)
        {
            problems.Add(exception);
            return false;
        }
    }

    private async Task RunAsync(Job job)
    {
        FrameImageResult<TImage>? loaded = null;
        FrameProxyResult? built = null;
        try
        {
            if (job.Build)
            {
                built = await _source.EnsureProxyAsync(job.Frame, job.Cancel).ConfigureAwait(false);
            }
            else
            {
                loaded = await _source.LoadAsync(job.Frame, job.Quality, job.Cancel).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (job.Cancel.IsCancellationRequested)
        {
            // A new project or the cache going away: nothing to keep.
        }
        catch (Exception exception)
        {
            // The source says a bad photo with a result; a throw is something else (a lost graphics device, a bug). It is
            // reported as such, not as an unreadable photo: the frame keeps the picture it had (no placeholder) and is
            // tried again later.
            ReportUnexpected(exception);
            var failure = new FrameLoadFailure(new SkippedFile(job.Frame.SourcePath, SkipReason.Other, exception.Message), exception);
            loaded = job.Build ? null : FrameImageResult<TImage>.Failed(failure);
            built = job.Build ? new FrameProxyResult(failure) : null;
            Complete(job, loaded, built, unexpected: true);
            return;
        }

        Complete(job, loaded, built, unexpected: false);
    }

    private void Complete(Job job, FrameImageResult<TImage>? loaded, FrameProxyResult? built, bool unexpected)
    {
        var id = job.Frame.Id;
        var dispose = new List<TImage>();
        FrameImageReadyEventArgs? ready = null;
        FrameLoadFailedEventArgs? failed = null;
        var cacheFailure = loaded?.CacheFailure ?? built?.CacheFailure;
        var failure = loaded?.Failure ?? built?.Failure;
        lock (_gate)
        {
            _loading.Remove(id);
            _building.Remove(id);

            // Ids are never reused, so a frame of a replaced project is simply not there any more. A load started
            // before Clear made its image (or failed) on the old graphics device: neither is kept (review M3).
            var stillThere = !_disposed && _indexOf.ContainsKey(id);
            var current = job.Generation == _generation;
            if (failure is not null && stillThere && current)
            {
                _failed[id] = (failure, Environment.TickCount64, unexpected);
                if (!unexpected && _reported.Add(id))
                {
                    failed = new FrameLoadFailedEventArgs(job.Frame, failure);
                }
            }
            else if (failure is null && loaded?.Image is not null)
            {
                _reported.Remove(id);
            }

            if (cacheFailure is not null)
            {
                _proxyFailed.Add(id);
                if (++_cacheFailuresInARow >= _options.StopPassAfterCacheFailures)
                {
                    _passStopped = true;
                }
            }
            else if ((built?.Succeeded ?? false) || loaded is { Image: not null, Quality: FrameImageQuality.Proxy })
            {
                _proxyReady.Add(id);
                _proxyFailed.Remove(id);
                if (built?.Succeeded ?? false)
                {
                    _cacheFailuresInARow = 0;
                }
            }

            if (loaded?.Image is { } image)
            {
                if (stillThere && current)
                {
                    ready = Store(job.Frame, image, loaded.Quality, loaded.Bytes, loaded.Version, dispose);
                }
                else
                {
                    dispose.Add(image);
                }
            }
        }

        DisposeAll(dispose);
        if (failed is not null)
        {
            Raise(FrameFailed, failed);
        }

        if (cacheFailure is not null)
        {
            Raise(CacheFailed, new PreviewFailureEventArgs(cacheFailure));
        }

        if (ready is not null)
        {
            Raise(ImageReady, ready);
        }

        Wake();
    }

    // Under the lock.
    private FrameImageReadyEventArgs? Store(Frame frame, TImage image, FrameImageQuality quality, long bytes, string? version, List<TImage> dispose)
    {
        if (_entries.TryGetValue(frame.Id, out var existing))
        {
            // A stale image (the photo changed) gives way to any new one.
            if (existing.Quality >= quality && !existing.Stale)
            {
                dispose.Add(image);
                return null;
            }

            Evict(existing, dispose);
        }

        var entry = new Entry(frame.Id, image, quality, bytes, version) { LastUsed = ++_useClock };
        _entries[frame.Id] = entry;
        _bytes += bytes;
        TrimToBudget(dispose);
        return new FrameImageReadyEventArgs(frame, quality);
    }

    // Under the lock: least recently used first, never a frame about to be shown.
    private void TrimToBudget(List<TImage> dispose)
    {
        if (_bytes <= _options.MemoryBudgetBytes)
        {
            return;
        }

        var keep = Wanted().Select(index => _frames[index].Id).ToHashSet();
        foreach (var entry in _entries.Values.Where(entry => !keep.Contains(entry.Id)).OrderBy(entry => entry.LastUsed).ToList())
        {
            if (_bytes <= _options.MemoryBudgetBytes)
            {
                break;
            }

            Evict(entry, dispose);
        }
    }

    // Under the lock.
    private void Evict(Entry entry, List<TImage> dispose)
    {
        if (entry.Evicted)
        {
            return;
        }

        if (_entries.TryGetValue(entry.Id, out var mapped) && ReferenceEquals(mapped, entry))
        {
            _entries.Remove(entry.Id);
        }

        _bytes -= entry.Bytes;
        entry.Evicted = true;
        if (entry.Leases == 0)
        {
            dispose.Add(entry.Image);
        }
    }

    private void DisposeAll(List<TImage> images)
    {
        foreach (var image in images)
        {
            Guard(image.Dispose);
        }
    }

    private void Guard(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            ReportUnexpected(exception);
        }
    }

    private void Wake() => Interlocked.Exchange(ref _wake, NewWake()).TrySetResult();

    private void Raise<T>(EventHandler<T>? handler, T args)
    {
        try
        {
            handler?.Invoke(this, args);
        }
        catch (Exception exception)
        {
            // A listener that throws must not stop a worker; it is reported like the cache's own failures.
            ReportUnexpected(exception);
        }
    }

    private void ReportUnexpected(Exception exception)
    {
        try
        {
            UnexpectedFailure?.Invoke(this, new PreviewFailureEventArgs(exception));
        }
        catch (Exception)
        {
            // The failure listener itself failed: nothing is left to tell, and the worker must go on.
        }
    }

    private static TaskCompletionSource NewWake() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed record Job(Frame Frame, FrameImageQuality Quality, bool Build, CancellationToken Cancel, int Generation);

    private sealed record Check(Frame Frame, Entry Entry, string Version);

    private sealed class Entry(Guid id, TImage image, FrameImageQuality quality, long bytes, string? version)
    {
        public Guid Id { get; } = id;

        public TImage Image { get; } = image;

        public FrameImageQuality Quality { get; } = quality;

        public long Bytes { get; } = bytes;

        // The photo's state the image shows (FrameImageResult.Version), for the paused stat check.
        public string? Version { get; } = version;

        // The photo changed since: loaded again, and shown until then. Under the lock.
        public bool Stale { get; set; }

        public long LastUsed { get; set; }

        public int Leases { get; set; }

        public bool Evicted { get; set; }
    }
}

/// <summary>A frame image the render thread may draw until it disposes the lease (<see cref="FrameImageCache{TImage}.Acquire"/>).</summary>
public sealed class FrameImageLease<TImage> : IDisposable
    where TImage : class, IDisposable
{
    private readonly FrameImageCache<TImage> _cache;
    private readonly object _handle;
    private int _released;

    internal FrameImageLease(FrameImageCache<TImage> cache, TImage image, FrameImageQuality quality, object handle)
    {
        _cache = cache;
        Image = image;
        Quality = quality;
        _handle = handle;
    }

    /// <summary>The image; valid until <see cref="Dispose"/>.</summary>
    public TImage Image { get; }

    /// <summary>How good it is.</summary>
    public FrameImageQuality Quality { get; }

    /// <summary>Lets the cache free the image once it no longer keeps it. Safe to call twice.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _released, 1) == 0)
        {
            _cache.Release(_handle);
        }
    }
}

/// <summary>A frame's image arrived or improved.</summary>
public sealed class FrameImageReadyEventArgs(Frame frame, FrameImageQuality quality) : EventArgs
{
    /// <summary>The frame.</summary>
    public Frame Frame { get; } = frame;

    /// <summary>The image's quality now.</summary>
    public FrameImageQuality Quality { get; } = quality;
}

/// <summary>A frame cannot be shown.</summary>
public sealed class FrameLoadFailedEventArgs(Frame frame, FrameLoadFailure failure) : EventArgs
{
    /// <summary>The frame.</summary>
    public Frame Frame { get; } = frame;

    /// <summary>Why.</summary>
    public FrameLoadFailure Failure { get; } = failure;
}

/// <summary>A failure of the proxy cache or of the preview's own code.</summary>
public sealed class PreviewFailureEventArgs(Exception exception) : EventArgs
{
    /// <summary>What went wrong.</summary>
    public Exception Exception { get; } = exception;
}
