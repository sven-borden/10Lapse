using Microsoft.Graphics.Canvas;
using TenLapse.Core.Projects;
using TenLapse.Media.Decoding;
using TenLapse.Media.Rendering;
using Windows.Graphics.DirectX;

namespace TenLapse.Media.Export;

/// <summary>
/// The export's frames when frame blending is on (#47): each exported frame is drawn from the photos around the one it
/// shows (<see cref="BlendSettings.Layers"/>), each with its own deflicker exposure, through the crop and pan view of
/// that frame, by <see cref="FrameRenderer.Draw(CanvasDrawingSession, IReadOnlyList{BlendedPhoto}, FitMode, PixelSize, Framing)"/>,
/// the call the preview makes. The source hands the pipeline that frame ready drawn, at the output size, and
/// <see cref="LookOf"/> is none, so the pipeline's own drawing copies it one to one.
/// </summary>
/// <remarks>
/// <para>
/// Each photo is decoded once (<see cref="PhotoDecoder"/>, at the size <see cref="FrameRenderer.DecodeOptions(FitMode, PixelSize, double)"/>
/// asks for) and uploaded once, and its bitmap is shared by every exported frame in a row whose blend takes it in: a
/// window of 16 photos slides on by one photo a frame, so a plain export decodes each photo once, as without blending.
/// The bitmap goes when the last of those frames has been drawn, or, for an export stopped in the middle, when the source
/// is disposed. A photo the blend comes back to later (a boomerang's way back) is decoded again then, rather than held
/// in between.
/// </para>
/// <para>
/// At most <see cref="DecodeThreads"/> photos are decoded at once, however many frames load: the first frame of a 16-photo
/// blend needs 8 photos at once.
/// </para>
/// <para>
/// A photo that cannot be decoded fails the export with a <see cref="PhotoDecodeException"/> naming the first exported
/// frame that shows it (so the message names that photo), or the frame being drawn when no exported frame shows it (a
/// GIF that skips it); its <see cref="PhotoFailure.Path"/> is the photo's own.
/// </para>
/// </remarks>
public sealed class BlendedPhotoSource : IFrameSource, IDisposable
{
    private readonly IReadOnlyList<string> _photos;
    private readonly FitMode _fit;
    private readonly IReadOnlyList<Framing> _views;
    private readonly IReadOnlyList<float> _stops;

    // Per exported frame: the shared decodes it is drawn from and their weights. Per photo: the first exported frame showing it.
    private readonly (Shared Decode, float Weight)[][] _layers;
    private readonly int[] _firstFrameOf;
    private readonly int[] _frames;
    private readonly HashSet<Shared> _live = [];
    private readonly SemaphoreSlim _decoding;
    private readonly Lock _gate = new();
    private int _held;
    private bool _disposed;

    /// <summary>Creates the frames of a blended export.</summary>
    /// <param name="photos">The project's photos, as absolute paths, in the project's order.</param>
    /// <param name="frames">The photo (an index into <paramref name="photos"/>) each exported frame shows, in order.</param>
    /// <param name="blend">The blend; the frames are blends of <see cref="BlendSettings.Layers"/> of their photo.</param>
    /// <param name="fit">How the photos are placed; the export's <see cref="VideoExportSettings.Fit"/> must agree.</param>
    /// <param name="views">Each exported frame's crop and pan view (#46), in order; null for the whole frame.</param>
    /// <param name="stops">Each photo's exposure change (#45, deflicker), in the photos' order; null for none.</param>
    /// <param name="maxDecodeZoom">
    /// The most a photo is decoded enlarged for a zoomed view (#46), 1 or more: the views ask for their zoom, up to this.
    /// <see cref="ProjectExporter"/> keeps the photos a blend holds at once within a memory budget with it.
    /// </param>
    public BlendedPhotoSource(
        IReadOnlyList<string> photos,
        IReadOnlyList<int> frames,
        BlendSettings blend,
        FitMode fit = FitMode.Fill,
        IReadOnlyList<Framing>? views = null,
        IReadOnlyList<float>? stops = null,
        double maxDecodeZoom = double.PositiveInfinity)
    {
        ArgumentNullException.ThrowIfNull(photos);
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentNullException.ThrowIfNull(blend);
        if (views is not null && views.Count != frames.Count)
        {
            throw new ArgumentException("One view per exported frame.", nameof(views));
        }

        if (stops is not null && stops.Count != photos.Count)
        {
            throw new ArgumentException("One exposure per photo.", nameof(stops));
        }

        if (!(maxDecodeZoom >= 1))
        {
            throw new ArgumentOutOfRangeException(nameof(maxDecodeZoom), maxDecodeZoom, "The decode zoom must be 1 or more.");
        }

        _photos = photos;
        _fit = Enum.IsDefined(fit) ? fit : throw new ArgumentOutOfRangeException(nameof(fit), fit, "Unknown fit mode.");
        _views = views ?? [.. Enumerable.Repeat(Framing.Whole, frames.Count)];
        _stops = stops ?? new float[photos.Count];
        _frames = [.. frames];
        _firstFrameOf = [.. Enumerable.Repeat(-1, photos.Count)];
        _layers = new (Shared, float)[frames.Count][];
        DecodeThreads = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);
        _decoding = new SemaphoreSlim(DecodeThreads);

        // Each photo's decode is shared by the exported frames in a row that take it in; a frame that does not breaks the run.
        var open = new Dictionary<int, (Shared Decode, int Last)>();
        for (var i = 0; i < frames.Count; i++)
        {
            var photo = frames[i];
            ArgumentOutOfRangeException.ThrowIfNegative(photo, nameof(frames));
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(photo, photos.Count, nameof(frames));
            if (_firstFrameOf[photo] < 0)
            {
                _firstFrameOf[photo] = i;
            }

            var layers = blend.Layers(photo, photos.Count);
            var row = new (Shared, float)[layers.Length];
            var zoom = Math.Min(maxDecodeZoom, Math.Max(1, _views[i].Zoom));
            for (var n = 0; n < layers.Length; n++)
            {
                var layer = layers[n];
                if (!open.TryGetValue(layer.Photo, out var run) || run.Last != i - 1)
                {
                    run = (new Shared(layer.Photo), i);
                }

                run.Decode.Frames++;
                run.Decode.Zoom = Math.Max(run.Decode.Zoom, zoom);
                open[layer.Photo] = (run.Decode, i);
                row[n] = (run.Decode, (float)layer.Weight);
            }

            _layers[i] = row;
        }
    }

    /// <inheritdoc/>
    public int FrameCount => _frames.Length;

    /// <summary>How the photos are placed.</summary>
    public FitMode Fit => _fit;

    /// <summary>How many photos are decoded at once at most.</summary>
    public int DecodeThreads { get; }

    /// <summary>Uploaded photos not yet let go, for the tests.</summary>
    internal int HeldPhotos => Volatile.Read(ref _held);

    /// <summary>The file of the photo exported frame <paramref name="index"/> shows.</summary>
    public string? SourcePath(int index) => _photos[_frames[index]];

    /// <summary>None: the frame is drawn already, its exposure, blend and view applied.</summary>
    public FrameLook LookOf(int index) => FrameLook.None;

    /// <inheritdoc/>
    public Task<CanvasBitmap> LoadAsync(int index, CanvasDevice device, PixelSize outputSize, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(device);
        var row = _layers[index];
        var loads = new Task<CanvasBitmap>[row.Length];
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            for (var n = 0; n < row.Length; n++)
            {
                var shared = row[n].Decode;
                if (shared.Load is null)
                {
                    _live.Add(shared);

                    // Started on the pool: a decode's first steps run synchronously, and must not hold the gate.
                    shared.Load = Task.Run(() => UploadAsync(shared, index, device, outputSize, cancellationToken), cancellationToken);
                }

                loads[n] = shared.Load;
            }
        }

        return Task.Run(() => DrawAsync(index, row, loads, device, outputSize, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// Lets go of every uploaded photo still held: an export stopped in the middle never draws the frames that would.
    /// A decode still running is let go when it ends. Call it once the pipeline has stopped.
    /// </summary>
    public void Dispose()
    {
        List<Shared> held;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            held = [.. _live];
            _live.Clear();
        }

        foreach (var shared in held)
        {
            Free(shared);
        }
    }

    private async Task<CanvasBitmap> DrawAsync(int index, (Shared Decode, float Weight)[] row, Task<CanvasBitmap>[] loads, CanvasDevice device, PixelSize outputSize, CancellationToken cancellationToken)
    {
        try
        {
            var photos = new BlendedPhoto[row.Length];
            for (var n = 0; n < row.Length; n++)
            {
                var bitmap = await loads[n].WaitAsync(cancellationToken).ConfigureAwait(false);
                photos[n] = new BlendedPhoto(bitmap, _stops[row[n].Decode.Photo], row[n].Weight);
            }

            var target = new CanvasRenderTarget(device, outputSize.Width, outputSize.Height, 96f, DirectXPixelFormat.B8G8R8A8UIntNormalized, CanvasAlphaMode.Premultiplied);
            try
            {
                using (var session = target.CreateDrawingSession())
                {
                    FrameRenderer.Draw(session, photos, _fit, outputSize, _views[index]);
                }

                return target;
            }
            catch
            {
                target.Dispose();
                throw;
            }
        }
        finally
        {
            foreach (var (shared, _) in row)
            {
                Release(shared);
            }
        }
    }

    // The photo decoded at the size its frames ask for, uploaded once; counted while it is held.
    private async Task<CanvasBitmap> UploadAsync(Shared shared, int index, CanvasDevice device, PixelSize outputSize, CancellationToken cancellationToken)
    {
        await _decoding.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // The device's largest bitmap bounds a photo far narrower than the output's aspect (a 30,000 px panorama).
            var options = FrameRenderer.DecodeOptions(_fit, outputSize, shared.Zoom) with { MaxSide = device.MaximumBitmapSizeInPixels };
            var result = await PhotoDecoder.DecodeAsync(_photos[shared.Photo], options, cancellationToken).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                var shown = _firstFrameOf[shared.Photo];
                throw new PhotoDecodeException(result.Failure, shown >= 0 ? shown : index);
            }

            using var photo = result.Value;
            var bitmap = CanvasBitmap.CreateFromSoftwareBitmap(device, photo.Pixels);
            Interlocked.Increment(ref _held);
            return bitmap;
        }
        finally
        {
            _decoding.Release();
        }
    }

    // A frame drawn from this photo is done; the last one lets the bitmap go.
    private void Release(Shared shared)
    {
        lock (_gate)
        {
            if (--shared.Frames > 0 || !_live.Remove(shared))
            {
                return;
            }
        }

        Free(shared);
    }

    // Disposes the bitmap once the upload has ended; nothing when it failed.
    private void Free(Shared shared) =>
        shared.Load?.ContinueWith(
            static (task, state) =>
            {
                if (task.IsCompletedSuccessfully)
                {
                    task.Result.Dispose();
                    Interlocked.Decrement(ref ((BlendedPhotoSource)state!)._held);
                }
            },
            this,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    // One photo's decode, shared by a run of exported frames: how many of them have not been drawn yet (under the gate)
    // and the largest zoom they ask for.
    private sealed class Shared(int photo)
    {
        public int Photo { get; } = photo;

        public int Frames { get; set; }

        public double Zoom { get; set; } = 1;

        public Task<CanvasBitmap>? Load { get; set; }
    }
}
