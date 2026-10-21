using Microsoft.Graphics.Canvas;
using TenLapse.Core.Projects;
using TenLapse.Media.Decoding;
using TenLapse.Media.Rendering;

namespace TenLapse.Media.Export;

/// <summary>
/// Frames from photo files on disk, decoded by <see cref="PhotoDecoder"/> (WIC) on the CPU at the size the frame
/// shows them (<see cref="FrameRenderer.DecodeOptions"/>: the cover size for Fill, the contain size for Fit), then
/// uploaded to the GPU. EXIF orientation is applied, colour converted to sRGB.
/// </summary>
/// <param name="paths">Absolute paths, one per frame, in frame order.</param>
/// <param name="fit">How the frames are drawn, which sets the decode size; the export's <see cref="VideoExportSettings.Fit"/> must agree.</param>
/// <remarks>
/// A photo that cannot be decoded throws a <see cref="PhotoDecodeException"/> naming the frame and the
/// <see cref="SkipReason"/>; the export fails with it (as the inner exception of a
/// <see cref="VideoExportFailure.FrameUnreadable"/> failure): it cannot leave a frame out without changing the clip's timing.
/// <para>
/// Frames in a row that show the same photo (a hold, #49) decode it once: the run's loads share one decode and each
/// uploads its own bitmap from it, so a 10 s hold at 60 fps is one decode, not 600. The shared pixels go when the run's
/// last frame has its bitmap, or, for an export cancelled or failed mid-run, when the source is disposed (review of PR
/// #134): a 4K photo's pixels are about 33 MB of native memory the collector does not feel.
/// </para>
/// </remarks>
public sealed class PhotoFileSource(IReadOnlyList<string> paths, FitMode fit = FitMode.Fill) : IFrameSource, IDisposable
{
    private readonly IReadOnlyList<string> _paths = paths ?? throw new ArgumentNullException(nameof(paths));

    // The decodes runs of the same photo share, by the run's first frame; each leaves once its last frame has its bitmap.
    private readonly Dictionary<int, SharedDecode> _shared = [];
    private readonly Lock _gate = new();
    private int _heldDecodes;
    private bool _disposed;

    /// <inheritdoc/>
    public int FrameCount => _paths.Count;

    /// <summary>How the frames are drawn.</summary>
    public FitMode Fit { get; } = Enum.IsDefined(fit) ? fit : throw new ArgumentOutOfRangeException(nameof(fit), fit, "Unknown fit mode.");

    /// <inheritdoc/>
    public string? SourcePath(int index) => _paths[index];

    /// <summary>Shared decoded pixels not yet let go, for the tests.</summary>
    internal int HeldDecodes => Volatile.Read(ref _heldDecodes);

    /// <summary>
    /// Lets go of every shared decode still held: a run the export stopped in (cancelled, or failed elsewhere) never
    /// reaches its last frame. A decode still running is let go when it ends. Call it once the pipeline has stopped.
    /// </summary>
    public void Dispose()
    {
        List<SharedDecode> held;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            held = [.. _shared.Values];
            _shared.Clear();
        }

        foreach (var shared in held)
        {
            shared.Release();
        }
    }

    /// <inheritdoc/>
    public Task<CanvasBitmap> LoadAsync(int index, CanvasDevice device, PixelSize outputSize, CancellationToken cancellationToken)
    {
        var (first, last) = RunOf(index);

        // Task.Run: the first awaits complete synchronously often enough that the caller would decode inline.
        return first == last
            ? Task.Run(() => DecodeAsync(index, device, outputSize, cancellationToken), cancellationToken)
            : Task.Run(() => LoadSharedAsync(index, first, last, device, outputSize, cancellationToken), cancellationToken);
    }

    // The frames around index that show the same file, first and last included.
    private (int First, int Last) RunOf(int index)
    {
        var path = _paths[index];
        var first = index;
        while (first > 0 && string.Equals(_paths[first - 1], path, StringComparison.Ordinal))
        {
            first--;
        }

        var last = index;
        while (last + 1 < _paths.Count && string.Equals(_paths[last + 1], path, StringComparison.Ordinal))
        {
            last++;
        }

        return (first, last);
    }

    private async Task<CanvasBitmap> LoadSharedAsync(int index, int first, int last, CanvasDevice device, PixelSize outputSize, CancellationToken cancellationToken)
    {
        SharedDecode shared;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_shared.TryGetValue(first, out shared!))
            {
                // Started on the pool: a decode's first steps run synchronously, and must not hold the gate.
                shared = new SharedDecode(last - first + 1, Task.Run(() => DecodeSharedAsync(first, device, outputSize, cancellationToken), cancellationToken), () => Interlocked.Decrement(ref _heldDecodes));
                _shared[first] = shared;
            }
        }

        try
        {
            var result = await shared.Decode.ConfigureAwait(false);
            if (!result.Succeeded)
            {
                throw new PhotoDecodeException(result.Failure, index);
            }

            // One upload at a time from the shared pixels: each frame gets a bitmap of its own (the pipeline disposes it).
            // Under the decode's own lock, which its release takes too, so the pixels never go mid-upload.
            lock (shared)
            {
                ObjectDisposedException.ThrowIf(shared.Released, this);
                return CanvasBitmap.CreateFromSoftwareBitmap(device, result.Value.Pixels);
            }
        }
        finally
        {
            Release(first, shared);
        }
    }

    // The last frame of the run that took its bitmap (or failed) lets the decoded pixels go.
    private void Release(int first, SharedDecode shared)
    {
        bool done;
        lock (_gate)
        {
            done = --shared.Remaining == 0;
            if (done)
            {
                _shared.Remove(first);
            }
        }

        if (done)
        {
            shared.Release();
        }
    }

    // A decode a run shares; counted while its pixels are held.
    private async Task<PhotoResult<DecodedPhoto>> DecodeSharedAsync(int index, CanvasDevice device, PixelSize outputSize, CancellationToken cancellationToken)
    {
        var options = FrameRenderer.DecodeOptions(Fit, outputSize) with { MaxSide = device.MaximumBitmapSizeInPixels };
        var result = await PhotoDecoder.DecodeAsync(_paths[index], options, cancellationToken).ConfigureAwait(false);
        if (result.Succeeded)
        {
            Interlocked.Increment(ref _heldDecodes);
        }

        return result;
    }

    private async Task<CanvasBitmap> DecodeAsync(int index, CanvasDevice device, PixelSize outputSize, CancellationToken cancellationToken)
    {
        // The device's largest bitmap bounds a photo far narrower than the output's aspect (a 30,000 px panorama).
        var options = FrameRenderer.DecodeOptions(Fit, outputSize) with { MaxSide = device.MaximumBitmapSizeInPixels };
        var result = await PhotoDecoder.DecodeAsync(_paths[index], options, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new PhotoDecodeException(result.Failure, index);
        }

        using var photo = result.Value;
        return CanvasBitmap.CreateFromSoftwareBitmap(device, photo.Pixels);
    }

    private sealed class SharedDecode(int frames, Task<PhotoResult<DecodedPhoto>> decode, Action released)
    {
        public Task<PhotoResult<DecodedPhoto>> Decode { get; } = decode;

        // Frames of the run that have not taken their bitmap yet; under the source's gate.
        public int Remaining { get; set; } = frames;

        // True once the pixels went (or will go when the decode ends); under this object's lock.
        public bool Released { get; private set; }

        // Lets the pixels go: now when the decode has ended, else when it ends. Once, whoever asks first.
        public void Release()
        {
            lock (this)
            {
                if (Released)
                {
                    return;
                }

                Released = true;
            }

            Decode.ContinueWith(
                static (task, state) => ((SharedDecode)state!).DisposePixels(task),
                this,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private void DisposePixels(Task<PhotoResult<DecodedPhoto>> task)
        {
            if (!task.IsCompletedSuccessfully || !task.Result.Succeeded)
            {
                return;
            }

            // Under the lock an upload from these pixels holds.
            lock (this)
            {
                task.Result.Value.Dispose();
            }

            released();
        }
    }
}
