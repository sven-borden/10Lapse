using System.Collections.Immutable;
using System.Diagnostics;
using TenLapse.Core.Projects;

namespace TenLapse.Core.Preview;

/// <summary>Where a thumbnail came from (#20).</summary>
public enum ThumbnailOrigin
{
    /// <summary>The small picture the camera embedded in the photo (EXIF), turned upright.</summary>
    Embedded,

    /// <summary>The preview's proxy on disk (#19), decoded small.</summary>
    Proxy,

    /// <summary>The photo itself, decoded small.</summary>
    Photo,
}

/// <summary>A thumbnail's size in pixels: the box the picture must cover, 3:2 like the filmstrip's thumbnails.</summary>
/// <param name="Width">The width in pixels.</param>
/// <param name="Height">The height in pixels.</param>
public readonly record struct ThumbnailSize(int Width, int Height)
{
    /// <summary>The heights thumbnails are loaded at; a thumbnail is loaded at the first that covers what is on screen.</summary>
    public static ImmutableArray<int> Heights { get; } = [32, 48, 64, 96, 128, 192, 256];

    /// <summary>
    /// The size to load for a thumbnail <paramref name="heightDips"/> tall (3:2) on a screen of <paramref name="scale"/>:
    /// the first of <see cref="Heights"/> at least that many pixels tall, or the largest.
    /// </summary>
    public static ThumbnailSize For(double heightDips, double scale)
    {
        var needed = Math.Ceiling(Math.Max(1, heightDips) * Math.Max(0.5, scale));
        var height = Heights.FirstOrDefault(step => step >= needed, Heights[^1]);
        return new ThumbnailSize((height * 3) / 2, height);
    }
}

/// <summary>What <see cref="IThumbnailSource{TImage}.LoadAsync"/> gave.</summary>
public sealed class ThumbnailResult<TImage>
    where TImage : class, IDisposable
{
    private ThumbnailResult(TImage? image, long bytes, ThumbnailOrigin origin, FrameLoadFailure? failure)
    {
        Image = image;
        Bytes = bytes;
        Origin = origin;
        Failure = failure;
    }

    /// <summary>The thumbnail; null when the photo cannot be read. The receiver owns it.</summary>
    public TImage? Image { get; }

    /// <summary>The memory it holds, for the cache's budget.</summary>
    public long Bytes { get; }

    /// <summary>Where it came from.</summary>
    public ThumbnailOrigin Origin { get; }

    /// <summary>Why the photo cannot be read; null when it loaded.</summary>
    public FrameLoadFailure? Failure { get; }

    /// <summary>A loaded thumbnail.</summary>
    public static ThumbnailResult<TImage> Loaded(TImage image, long bytes, ThumbnailOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        return new ThumbnailResult<TImage>(image, bytes, origin, null);
    }

    /// <summary>A photo that cannot be read.</summary>
    public static ThumbnailResult<TImage> Failed(FrameLoadFailure failure) =>
        new(null, 0, ThumbnailOrigin.Photo, failure ?? throw new ArgumentNullException(nameof(failure)));
}

/// <summary>
/// Where the filmstrip's thumbnails come from (#20). Media implements it (the preview's proxy, else the embedded EXIF
/// thumbnail until the proxy exists, else a small decode of the photo); the tests fake it. Every member may be called from
/// any thread, several at once.
/// </summary>
/// <typeparam name="TImage">The image (a <c>SoftwareBitmap</c> in the app).</typeparam>
public interface IThumbnailSource<TImage>
    where TImage : class, IDisposable
{
    /// <summary>
    /// Loads <paramref name="frame"/>'s thumbnail at the smallest size that covers <paramref name="size"/>, EXIF orientation
    /// applied. Never throws for a photo that cannot be read (that is <see cref="ThumbnailResult{TImage}.Failure"/>);
    /// cancellation throws <see cref="OperationCanceledException"/>.
    /// </summary>
    Task<ThumbnailResult<TImage>> LoadAsync(Frame frame, ThumbnailSize size, CancellationToken cancellationToken);

    /// <summary>
    /// True when <paramref name="frame"/>'s thumbnail, loaded from <paramref name="origin"/>, would now come from a better
    /// source (an embedded picture once the proxy exists). Cheap: <see cref="ThumbnailCache{TImage}.Recheck"/> asks it for
    /// the thumbnails in view. False by default.
    /// </summary>
    bool HasBetter(Frame frame, ThumbnailOrigin origin) => false;
}

/// <summary>How <see cref="ThumbnailCache{TImage}"/> works.</summary>
public sealed record ThumbnailCacheOptions
{
    /// <summary>Loads at once. The preview's own workers decode beside these, so a few are enough.</summary>
    public int Workers { get; init; } = Math.Clamp(Environment.ProcessorCount / 4, 2, 3);

    /// <summary>
    /// The memory the thumbnails may hold together. The thumbnails the strip has realized stay even past it (a few
    /// screens' worth, bounded by the strip's width, never by the project's length); the least recently used others go.
    /// </summary>
    public long MemoryBudgetBytes { get; init; } = 48L << 20;

    /// <summary>How long a photo that could not be read keeps its placeholder before it is tried again (when still wanted).</summary>
    public TimeSpan RetryFailedAfter { get; init; } = TimeSpan.FromSeconds(10);
}

/// <summary>A thumbnail arrived (on a worker thread).</summary>
public sealed class ThumbnailReadyEventArgs(Guid id, ThumbnailOrigin origin, ThumbnailSize size, TimeSpan elapsed) : EventArgs
{
    /// <summary>The frame.</summary>
    public Guid Id { get; } = id;

    /// <summary>Where it came from.</summary>
    public ThumbnailOrigin Origin { get; } = origin;

    /// <summary>The size it was loaded for.</summary>
    public ThumbnailSize Size { get; } = size;

    /// <summary>How long the load took.</summary>
    public TimeSpan Elapsed { get; } = elapsed;
}

/// <summary>
/// The filmstrip's thumbnails in memory (#20): loads the thumbnails of the frames the strip shows, nearest first, off the UI
/// thread; cancels the loads of frames that scrolled away; and keeps memory bounded, so 5,000 frames cost what 50 do.
/// The strip says what it shows with <see cref="Want"/>, takes images with <see cref="Acquire"/> without ever waiting, and
/// hears of new ones through <see cref="Ready"/>.
/// </summary>
/// <remarks>
/// <para>
/// Scheduling is pulled, as in <see cref="FrameImageCache{TImage}"/>: a free worker takes the first wanted frame without a
/// thumbnail of the wanted size, so a scroll changes the order at once and work for frames out of view is never started; a
/// load already running for a frame that is no longer wanted is cancelled. A frame keeps the image it has (of another size)
/// until the new one arrives, so a size change never blanks the strip.
/// </para>
/// <para>
/// Images are kept by <see cref="Frame.Id"/> and leased like the preview's, so one is disposed only when no lease holds it.
/// A photo that cannot be read is reported once through <see cref="Failed"/> and tried again after
/// <see cref="ThumbnailCacheOptions.RetryFailedAfter"/> while still wanted; a source that throws is reported as
/// <see cref="UnexpectedFailure"/> and retried the same way, without a placeholder.
/// </para>
/// <para>Thread-safe. Events are raised on worker threads, outside the cache's lock.</para>
/// </remarks>
public sealed class ThumbnailCache<TImage> : IDisposable
    where TImage : class, IDisposable
{
    private readonly Lock _gate = new();
    private readonly IThumbnailSource<TImage> _source;
    private readonly ThumbnailCacheOptions _options;
    private readonly CancellationTokenSource _disposing = new();
    private readonly Dictionary<Guid, Entry> _entries = [];
    private readonly Dictionary<Guid, (FrameLoadFailure? Failure, long At)> _failed = [];
    private readonly HashSet<Guid> _reported = [];
    private readonly Dictionary<Guid, CancellationTokenSource> _loading = [];
    private readonly Task[] _workers;
    private TaskCompletionSource _wake = NewWake();
    private IReadOnlyList<Frame> _wanted = [];
    private HashSet<Guid> _wantedIds = [];
    private IReadOnlyList<Frame> _kept = [];
    private HashSet<Guid> _keptIds = [];
    private ThumbnailSize _keptSize = new(48, 32);
    private HashSet<Guid>? _known;
    private ThumbnailSize _size = new(48, 32);
    private long _bytes;
    private long _useClock;
    private int _cancelled;
    private bool _disposed;

    /// <summary>Starts the workers; they wait until something is wanted.</summary>
    public ThumbnailCache(IThumbnailSource<TImage> source, ThumbnailCacheOptions? options = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _options = options ?? new ThumbnailCacheOptions();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(_options.Workers);
        ArgumentOutOfRangeException.ThrowIfNegative(_options.MemoryBudgetBytes);
        _workers = [.. Enumerable.Range(0, _options.Workers).Select(_ => Task.Run(WorkAsync))];
    }

    /// <summary>A thumbnail arrived; the strip shows it if the frame is on screen.</summary>
    public event EventHandler<ThumbnailReadyEventArgs>? Ready;

    /// <summary>A frame's photo cannot be read. Raised once per frame, until it loads again or leaves the project.</summary>
    public event EventHandler<FrameLoadFailedEventArgs>? Failed;

    /// <summary>The source or a listener threw (a bug); the strip goes on.</summary>
    public event EventHandler<PreviewFailureEventArgs>? UnexpectedFailure;

    /// <summary>The bytes the thumbnails in memory hold.</summary>
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

    /// <summary>The thumbnails in memory.</summary>
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

    /// <summary>Loads that were cancelled because their frame scrolled out of view or left the project.</summary>
    public int Cancelled => Volatile.Read(ref _cancelled);

    /// <summary>
    /// The project's frames: thumbnails and failures of frames no longer there are freed, and their loads cancelled.
    /// </summary>
    public void SetFrames(ImmutableArray<Frame> frames)
    {
        var dispose = new List<TImage>();
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            var known = (frames.IsDefault ? [] : frames).Select(frame => frame.Id).ToHashSet();
            _known = known;
            foreach (var entry in _entries.Values.Where(entry => !known.Contains(entry.Id)).ToList())
            {
                Evict(entry, dispose);
            }

            foreach (var id in _failed.Keys.Where(id => !known.Contains(id)).ToList())
            {
                _failed.Remove(id);
                _reported.Remove(id);
            }

            CancelLoads(id => !known.Contains(id));
        }

        DisposeAll(dispose);
    }

    /// <summary>
    /// The frames the strip shows or is about to, most important first, and the size to load them at. Loads of frames
    /// not in the list are cancelled.
    /// </summary>
    public void Want(IReadOnlyList<Frame> frames, ThumbnailSize size)
    {
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size.Width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size.Height);
        var dispose = new List<TImage>();
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _wanted = [.. frames];
            var wanted = frames.Select(frame => frame.Id).ToHashSet();
            _wantedIds = wanted;
            _size = size;
            var kept = _keptIds;
            CancelLoads(id => !wanted.Contains(id) && !kept.Contains(id));
            Trim(dispose);
        }

        DisposeAll(dispose);
        Wake();
    }

    /// <summary>
    /// A few frames to have in memory besides the wanted ones (the export's film gate, #24): loaded at
    /// <paramref name="size"/> only when no wanted frame needs a load, never cancelled by <see cref="Want"/>, and kept
    /// past the memory budget's trimming until the next call (an empty list lets them go). A frame with a picture of any
    /// size already counts as there.
    /// </summary>
    public void Keep(IReadOnlyList<Frame> frames, ThumbnailSize size)
    {
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size.Width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size.Height);
        var dispose = new List<TImage>();
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _kept = [.. frames];
            var kept = frames.Select(frame => frame.Id).ToHashSet();
            _keptIds = kept;
            _keptSize = size;
            var wanted = _wantedIds;
            CancelLoads(id => !wanted.Contains(id) && !kept.Contains(id));
            Trim(dispose);
        }

        DisposeAll(dispose);
        Wake();
    }

    /// <summary>
    /// The thumbnail of frame <paramref name="id"/> in memory, of any size, leased: valid until the lease is disposed.
    /// Null when there is none yet. Never waits.
    /// </summary>
    public ThumbnailLease<TImage>? Acquire(Guid id)
    {
        lock (_gate)
        {
            if (_disposed || !_entries.TryGetValue(id, out var entry))
            {
                return null;
            }

            entry.Leases++;
            entry.LastUsed = ++_useClock;
            return new ThumbnailLease<TImage>(this, entry.Image, entry.Size, entry.Origin, entry);
        }
    }

    /// <summary>
    /// Asks the source whether the wanted thumbnails that came from an embedded picture have a better source now (the
    /// proxy appeared); those are loaded again, the old picture staying until the new one arrives. The strip calls it
    /// every few seconds. Never throws: a source that does is reported as <see cref="UnexpectedFailure"/>.
    /// </summary>
    public void Recheck()
    {
        List<(Frame Frame, Entry Entry)> embedded;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            embedded = [.. _wanted
                .Select(frame => (Frame: frame, Entry: _entries.GetValueOrDefault(frame.Id)))
                .Where(pair => pair.Entry is { Origin: ThumbnailOrigin.Embedded, Stale: false })
                .Select(pair => (pair.Frame, pair.Entry!))];
        }

        if (embedded.Count == 0)
        {
            return;
        }

        List<Entry> better;
        try
        {
            better = [.. embedded.Where(pair => _source.HasBetter(pair.Frame, ThumbnailOrigin.Embedded)).Select(pair => pair.Entry)];
        }
        catch (Exception exception)
        {
            Raise(() => UnexpectedFailure?.Invoke(this, new PreviewFailureEventArgs(exception)));
            return;
        }

        if (better.Count == 0)
        {
            return;
        }

        lock (_gate)
        {
            foreach (var entry in better)
            {
                entry.Stale = true;
            }
        }

        Wake();
    }

    /// <summary>Why frame <paramref name="id"/>'s photo cannot be read, or null.</summary>
    public FrameLoadFailure? FailureOf(Guid id)
    {
        lock (_gate)
        {
            return _failed.TryGetValue(id, out var failed) ? failed.Failure : null;
        }
    }

    /// <summary>Stops the workers (loads in flight are cancelled and their results freed) and frees the thumbnails.</summary>
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

            CancelLoads(_ => true);
        }

        _ = _disposing.CancelAsync();
        DisposeAll(dispose);
        Wake();
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
            DisposeQuietly(entry.Image);
        }
    }

    private static TaskCompletionSource NewWake() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    // Under the lock: cancels the loads of the frames that match. The cancellation runs on the thread pool, never inline
    // (the load's continuations would otherwise run on this thread, holding the lock). It must be asked for under the
    // lock: a worker takes its source out of _loading under the lock and only then disposes it, so a source still in
    // _loading is never disposed yet. Outside the lock, CancelAsync could meet a disposed source and return a faulted
    // task that nobody observes, which the app reports as an unexpected error (#82 review, M3).
    private void CancelLoads(Func<Guid, bool> which)
    {
        foreach (var (id, source) in _loading)
        {
            if (which(id))
            {
                _ = source.CancelAsync();
            }
        }
    }

    private static void DisposeQuietly(TImage image)
    {
        try
        {
            image.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // Already gone.
        }
    }

    private static void DisposeAll(List<TImage> images) => images.ForEach(DisposeQuietly);

    private void Wake()
    {
        TaskCompletionSource wake;
        lock (_gate)
        {
            wake = _wake;
            _wake = NewWake();
        }

        wake.TrySetResult();
    }

    private async Task WorkAsync()
    {
        while (true)
        {
            Frame? frame;
            ThumbnailSize size;
            CancellationTokenSource load;
            Task wake;
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                frame = Next(out size);
                wake = _wake.Task;
                load = frame is null ? null! : CancellationTokenSource.CreateLinkedTokenSource(_disposing.Token);
                if (frame is not null)
                {
                    _loading[frame.Id] = load;
                }
            }

            if (frame is null)
            {
                await wake.ConfigureAwait(false);
                continue;
            }

            await LoadAsync(frame, size, load).ConfigureAwait(false);
        }
    }

    // Under the lock: the first wanted frame that needs a load (none in memory of the wanted size from its best source, not
    // loading, not failed lately), then the first kept frame without any picture, and the size to load it at.
    private Frame? Next(out ThumbnailSize size)
    {
        var now = Environment.TickCount64;
        var retry = (long)_options.RetryFailedAfter.TotalMilliseconds;
        bool Waiting(Frame frame) =>
            _loading.ContainsKey(frame.Id)
            || (_known is { } known && !known.Contains(frame.Id))
            || (_failed.TryGetValue(frame.Id, out var failed) && now - failed.At < retry);

        size = _size;
        foreach (var frame in _wanted)
        {
            if (Waiting(frame) || (_entries.TryGetValue(frame.Id, out var entry) && entry.Size == _size && !entry.Stale))
            {
                continue;
            }

            return frame;
        }

        size = _keptSize;
        foreach (var frame in _kept)
        {
            if (Waiting(frame) || _entries.ContainsKey(frame.Id))
            {
                continue;
            }

            return frame;
        }

        return null;
    }

    private async Task LoadAsync(Frame frame, ThumbnailSize size, CancellationTokenSource load)
    {
        var started = Stopwatch.GetTimestamp();
        ThumbnailResult<TImage>? result = null;
        Exception? unexpected = null;
        var cancelled = false;
        try
        {
            result = await _source.LoadAsync(frame, size, load.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (load.IsCancellationRequested)
        {
            cancelled = true;
        }
        catch (Exception exception)
        {
            unexpected = exception;
        }

        var dispose = new List<TImage>();
        var ready = false;
        var reportFailure = false;
        lock (_gate)
        {
            _loading.Remove(frame.Id);
            if (cancelled)
            {
                _cancelled++;
            }
            else if (_disposed || (_known is { } known && !known.Contains(frame.Id)))
            {
                if (result?.Image is { } stale)
                {
                    dispose.Add(stale);
                }
            }
            else if (unexpected is not null)
            {
                // Says nothing about the photo: no placeholder, tried again later.
                _failed[frame.Id] = (null, Environment.TickCount64);
            }
            else if (result!.Image is { } image)
            {
                _failed.Remove(frame.Id);
                _reported.Remove(frame.Id);
                if (_entries.TryGetValue(frame.Id, out var old))
                {
                    Evict(old, dispose);
                }

                _entries[frame.Id] = new Entry(frame.Id, image, size, result.Origin, result.Bytes) { LastUsed = ++_useClock };
                _bytes += result.Bytes;
                Trim(dispose);
                ready = true;
            }
            else
            {
                _failed[frame.Id] = (result.Failure, Environment.TickCount64);
                reportFailure = _reported.Add(frame.Id);
            }
        }

        load.Dispose();
        DisposeAll(dispose);
        if (unexpected is not null)
        {
            Raise(() => UnexpectedFailure?.Invoke(this, new PreviewFailureEventArgs(unexpected)));
        }
        else if (ready)
        {
            var elapsed = Stopwatch.GetElapsedTime(started);
            Raise(() => Ready?.Invoke(this, new ThumbnailReadyEventArgs(frame.Id, result!.Origin, size, elapsed)));
        }
        else if (reportFailure)
        {
            Raise(() => Failed?.Invoke(this, new FrameLoadFailedEventArgs(frame, result!.Failure!)));
        }
    }

    // A listener that throws is a bug: reported, and the worker goes on.
    private void Raise(Action raise)
    {
        try
        {
            raise();
        }
        catch (Exception exception)
        {
            try
            {
                UnexpectedFailure?.Invoke(this, new PreviewFailureEventArgs(exception));
            }
            catch (Exception)
            {
                // Nothing left to tell.
            }
        }
    }

    // Under the lock: past the budget, the least recently used thumbnails that are neither wanted nor kept go.
    private void Trim(List<TImage> dispose)
    {
        if (_bytes <= _options.MemoryBudgetBytes)
        {
            return;
        }

        foreach (var entry in _entries.Values.Where(entry => !_wantedIds.Contains(entry.Id) && !_keptIds.Contains(entry.Id)).OrderBy(entry => entry.LastUsed).ToList())
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

        _entries.Remove(entry.Id);
        _bytes -= entry.Bytes;
        entry.Evicted = true;
        if (entry.Leases == 0)
        {
            dispose.Add(entry.Image);
        }
    }

    private sealed class Entry(Guid id, TImage image, ThumbnailSize size, ThumbnailOrigin origin, long bytes)
    {
        public Guid Id { get; } = id;

        public TImage Image { get; } = image;

        public ThumbnailSize Size { get; } = size;

        public ThumbnailOrigin Origin { get; } = origin;

        public long Bytes { get; } = bytes;

        public long LastUsed { get; set; }

        public int Leases { get; set; }

        public bool Evicted { get; set; }

        // An embedded picture whose proxy has appeared since (Recheck): loaded again.
        public bool Stale { get; set; }
    }
}

/// <summary>A thumbnail on loan from <see cref="ThumbnailCache{TImage}"/>: valid until disposed.</summary>
public sealed class ThumbnailLease<TImage> : IDisposable
    where TImage : class, IDisposable
{
    private readonly ThumbnailCache<TImage> _cache;
    private readonly object _handle;
    private int _released;

    internal ThumbnailLease(ThumbnailCache<TImage> cache, TImage image, ThumbnailSize size, ThumbnailOrigin origin, object handle)
    {
        _cache = cache;
        Image = image;
        Size = size;
        Origin = origin;
        _handle = handle;
    }

    /// <summary>The thumbnail.</summary>
    public TImage Image { get; }

    /// <summary>The size it was loaded for.</summary>
    public ThumbnailSize Size { get; }

    /// <summary>Where it came from.</summary>
    public ThumbnailOrigin Origin { get; }

    /// <summary>Gives the thumbnail back.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _released, 1) == 0)
        {
            _cache.Release(_handle);
        }
    }
}
