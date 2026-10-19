using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using Microsoft.Graphics.Canvas;
using TenLapse.Core.Projects;
using TenLapse.Core.Timing;
using TenLapse.Media.Decoding;
using TenLapse.Media.Inspection;
using TenLapse.Media.Rendering;
using Windows.Graphics.DirectX;

namespace TenLapse.Media.Export;

/// <summary>What an export wrote.</summary>
/// <param name="FrameCount">Frames the encoder took.</param>
/// <param name="Duration">The clip duration in 100 ns ticks, <see cref="FrameTiming.Duration"/>.</param>
/// <param name="Encoder">The encoder that ran, as the sink writer reports it; null if it could not tell.</param>
public sealed record VideoExportResult(int FrameCount, long Duration, VideoEncoderInfo? Encoder);

/// <summary>
/// The streaming export: decode, process (<see cref="FrameRenderer"/>, Fit or Fill) and encode (H.264 or HEVC
/// MP4) run at once, connected by bounded queues, so memory stays flat whatever the frame count.
/// </summary>
/// <remarks>
/// <para>
/// At most <see cref="VideoExportSettings.DecodeThreads"/> loads run at once and at most
/// <see cref="VideoExportSettings.DecodeAhead"/> frames are loading or loaded and not yet drawn (by default
/// fewer on an integrated GPU or a PC with 8 GB of RAM or less, <see cref="VideoExportSettings.DecodeFor"/>). Each frame is drawn
/// into a new output-sized <c>CanvasRenderTarget</c>, and at most <see cref="VideoExportSettings.RenderTargets"/>
/// of them wait for the encoder at once. A target is never drawn twice: Media Foundation may still read a surface
/// after it has taken the sample, so the pipeline drops its own reference once the encoder has the frame and Media
/// Foundation's reference keeps the texture alive until it is done.
/// </para>
/// <para>
/// Sample time and duration of frame n are <see cref="FrameTiming.FrameStart"/> and
/// <see cref="FrameTiming.FrameDuration"/>, never accumulated. Every export runs on its own
/// <see cref="VideoDevice"/>, created and disposed by the export; frame sources get that device.
/// </para>
/// <para>
/// Every encoder call (opening, each frame, finishing) runs on its own thread pool thread and is timed: one that
/// takes longer than <see cref="VideoExportSettings.StallTimeout"/> fails the export as
/// <see cref="VideoExportFailure.EncoderStalled"/> and is abandoned. A cancellation or failure returns as soon as
/// the pipeline has stopped and the loads in flight have finished; releasing the encoder (an unfinished sink writer
/// can take about 5 s), deleting the unfinished file and disposing the device then finish in the background
/// (<see cref="ExportCleanup"/>).
/// </para>
/// <para>
/// <see cref="ExportAsync(IFrameSource, string, VideoExportSettings, IProgress{int}?, CancellationToken)"/>
/// may be called from any thread, the UI thread included: the whole run, device and encoder too, lives on thread
/// pool (MTA) threads, and frame sources are called there. A frame source must not hand the export COM objects
/// created on an STA.
/// </para>
/// </remarks>
public static class VideoExporter
{
    /// <summary>How long one encoder call (opening, one frame, finishing) may take before the export fails instead of hanging.</summary>
    public static readonly TimeSpan EncoderStallTimeout = TimeSpan.FromSeconds(60);

    /// <summary>How long a failed or cancelled export waits for its clean-up before it returns and leaves the rest to the background.</summary>
    internal static readonly TimeSpan CleanupGrace = TimeSpan.FromMilliseconds(250);

    /// <summary>How long the background clean-up waits for an abandoned encoder call before it gives the encoder and the device up.</summary>
    internal static readonly TimeSpan AbandonAfter = TimeSpan.FromMinutes(5);

    // Encoder calls, in this process, that stalled (GuardAsync) and have not returned yet.
    private static int _stuckCalls;

    /// <summary>
    /// Encoder calls in this process that stalled and have not returned yet. Each holds a thread, its encoder (an
    /// NVENC session among the few a consumer GPU allows) and its device; while any is outstanding, an encoder that
    /// fails to open fails as <see cref="VideoExportFailure.EncoderStuck"/>, whose sentence says to restart the app.
    /// </summary>
    internal static int StuckEncoderCalls => Volatile.Read(ref _stuckCalls);

    /// <summary>Writes every frame of <paramref name="source"/> to an MP4 at <paramref name="outputPath"/>, replacing it.</summary>
    /// <param name="progress">Receives the number of frames the encoder has taken, from worker threads.</param>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <exception cref="VideoExportException">
    /// The export failed for a reason <see cref="VideoExportException.Failure"/> names: no usable graphics device,
    /// the output folder, the disk, the driver, memory, the encoder, or a frame that could not be loaded or drawn
    /// (<see cref="VideoExportException.FrameIndex"/> and <see cref="VideoExportException.SourcePath"/> say which).
    /// Bugs in our own code surface as themselves.
    /// </exception>
    /// <remarks>
    /// Once the encoder has opened the output file, a failure or a cancellation deletes it (in the background,
    /// <see cref="ExportCleanup.WhenDone"/>); a file that was there before and could not be opened is left alone.
    /// </remarks>
    public static Task<VideoExportResult> ExportAsync(
        IFrameSource source,
        string outputPath,
        VideoExportSettings settings,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default) =>
        ExportAsync(source, outputPath, settings, progress, cancellationToken, static (path, s, device) => new SinkWriterEncoder(path, s, device));

    /// <summary>The export with the encode stage supplied, for tests that need it to fail on cue.</summary>
    internal static async Task<VideoExportResult> ExportAsync(
        IFrameSource source,
        string outputPath,
        VideoExportSettings settings,
        IProgress<int>? progress,
        CancellationToken cancellationToken,
        VideoEncoderFactory createEncoder)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrEmpty(outputPath);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(createEncoder);
        if (source.FrameCount <= 0)
        {
            throw new ArgumentException("The source has no frames.", nameof(source));
        }

        // The whole run lives on the thread pool (MTA). Called from the UI thread (an STA), the Direct3D device's
        // runtime callable wrapper would otherwise belong to that apartment, and Media Foundation's threads could
        // not use it ("Specified cast is not valid" in IMFDXGIDeviceManager.ResetDevice).
        var run = new ExportRun(source, settings, progress, createEncoder);
        var fullPath = Path.GetFullPath(outputPath);
        return await Task.Run(() => run.RunAsync(fullPath, cancellationToken), CancellationToken.None).ConfigureAwait(false);
    }

    private sealed class ExportRun
    {
        private readonly IFrameSource _source;
        private readonly VideoExportSettings _settings;
        private readonly IProgress<int>? _progress;
        private readonly VideoEncoderFactory _createEncoder;
        private readonly PixelSize _output;
        // Sized once the device says how much memory there is (DecodeFor): set in RunAsync before the stages start.
        private Channel<PendingFrame> _decoded = null!;

        // Loads started and not yet owned by the drawing stage: awaited and disposed before the export returns.
        private readonly ConcurrentDictionary<Task<CanvasBitmap>, byte> _loads = new();

        // Never disposed: a load's continuation may release _decoding after the run (SemaphoreSlim needs no disposal
        // unless its wait handle was used).
        private SemaphoreSlim _ahead = null!;
        private SemaphoreSlim _decoding = null!;
        private readonly SemaphoreSlim _slots;
        private readonly ConcurrentDictionary<CanvasRenderTarget, byte> _live = new();
        private readonly Channel<RenderedFrame> _rendered;
        private readonly CancellationTokenSource _stop = new();
        private VideoDevice? _device;
        private Exception? _failure;
        private int _framesTaken;

        // The encoder once open, the opening call, and the last encoder call started: the clean-up waits for the
        // calls before it releases the encoder (releasing it under a running WriteSample would free it in use).
        private IVideoEncoder? _encoder;
        private Task<IVideoEncoder>? _opening;
        private Task? _call;
        private bool _complete;

        public ExportRun(IFrameSource source, VideoExportSettings settings, IProgress<int>? progress, VideoEncoderFactory createEncoder)
        {
            _source = source;
            _settings = settings;
            _progress = progress;
            _createEncoder = createEncoder;
            _output = new PixelSize(settings.Width, settings.Height);
            var slots = Math.Max(1, settings.RenderTargets);
            _slots = new SemaphoreSlim(slots, slots);
            _rendered = Channel.CreateBounded<RenderedFrame>(new BoundedChannelOptions(slots)
            {
                SingleReader = true,
                SingleWriter = true,
            });
        }

        private Fraction Rate => _settings.FrameRate;

        private CanvasDevice Canvas => _device!.Canvas;

        public async Task<VideoExportResult> RunAsync(string outputPath, CancellationToken cancellationToken)
        {
            var registration = cancellationToken.Register(_stop.Cancel);
            var producing = Task.CompletedTask;
            var processing = Task.CompletedTask;
            VideoEncoderInfo? encoder = null;
            ExceptionDispatchInfo? error = null;
            try
            {
                _device = VideoDevice.Create();
                SizeDecoding(VideoExportSettings.IsTightMemory(_device.HasUnifiedMemory, GC.GetGCMemoryInfo().TotalAvailableMemoryBytes));
                void StartPipeline()
                {
                    producing = Task.Run(ProduceAsync);
                    processing = Task.Run(ProcessAsync);
                }

                encoder = await EncodeAsync(outputPath, StartPipeline).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                error = ExceptionDispatchInfo.Capture(exception);
            }

            // Both stages catch their own exceptions and stop at once on _stop.
            _stop.Cancel();
            await Task.WhenAll(producing, processing).ConfigureAwait(false);

            // A WIC decode cannot be interrupted; the loads in flight finish before the export returns, so no frame
            // source runs on after it.
            await ((Task)Task.WhenAll(_loads.Keys)).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            foreach (var load in _loads.Keys)
            {
                if (load.IsCompletedSuccessfully)
                {
                    load.Result.Dispose();
                }
            }

            // Waits for a callback already running, so nothing calls _stop.Cancel() after this.
            registration.Dispose();
            _stop.Dispose();

            var cleanup = Task.Run(() => CleanUpAsync(outputPath));
            ExportCleanup.Track(outputPath, cleanup);
            if (error is null)
            {
                await cleanup.ConfigureAwait(false);
            }
            else
            {
                // Usually done at once; an unfinished sink writer that takes seconds to let go finishes in the background.
                await cleanup.WaitAsync(CleanupGrace).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                cancellationToken.ThrowIfCancellationRequested();
                var failure = Volatile.Read(ref _failure);
                if (failure is not null && failure != error.SourceException)
                {
                    // The source or the drawing failed first; the encoder's error is only the consequence.
                    ExceptionDispatchInfo.Throw(failure);
                }

                // A VideoExportException from a native boundary (device, encoder, drawing, a frame), or a bug in our
                // code, which must not pass for an export failure.
                error.Throw();
            }

            var taken = Volatile.Read(ref _framesTaken);
            return new VideoExportResult(taken, FrameTiming.Duration(taken, Rate), encoder);
        }

        // How many frames may be decoding and decoded at once: fewer where the decoded textures share system memory.
        private void SizeDecoding(bool tightMemory)
        {
            var (ahead, threads) = _settings.DecodeFor(tightMemory);
            _ahead = new SemaphoreSlim(ahead, ahead);
            _decoding = new SemaphoreSlim(threads, threads);
            _decoded = Channel.CreateBounded<PendingFrame>(new BoundedChannelOptions(ahead)
            {
                SingleReader = true,
                SingleWriter = true,
            });
        }

        // Runs on the thread pool (see ExportAsync); every encoder call runs on its own pool thread, timed (GuardAsync).
        private async Task<VideoEncoderInfo?> EncodeAsync(string outputPath, Action startPipeline)
        {
            var opening = Task.Run(() => _createEncoder(outputPath, _settings, _device!));
            _opening = opening;
            IVideoEncoder encoder;
            try
            {
                encoder = await GuardAsync(opening, "opening the encoder", frame: null).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException && !ExportFailures.IsBug(exception) && StuckElsewhere(exception))
            {
                throw new VideoExportException(
                    $"{ExportFailures.Summary(VideoExportFailure.EncoderStuck, _settings.Format)} ({StuckEncoderCalls} call(s)): {exception.Message}",
                    VideoExportFailure.EncoderStuck,
                    exception);
            }
            catch (Exception exception) when (ExportFailures.ShouldClassify(exception))
            {
                throw Classify(exception, VideoExportFailure.EncoderFailed, frame: null, setup: true);
            }

            _encoder = encoder;
            startPipeline();
            while (await _rendered.Reader.WaitToReadAsync(_stop.Token).ConfigureAwait(false))
            {
                while (_rendered.Reader.TryRead(out var frame))
                {
                    _stop.Token.ThrowIfCancellationRequested();
                    var target = frame.Target;
                    var time = FrameTiming.FrameStart(frame.Index, Rate);
                    var duration = FrameTiming.FrameDuration(frame.Index, Rate);
                    var write = Task.Run(() => encoder.Write(target, time, duration));
                    _call = write;
                    try
                    {
                        await GuardAsync(write, $"writing frame {frame.Index}", frame.Index).ConfigureAwait(false);
                    }
                    catch (Exception exception) when (ExportFailures.ShouldClassify(exception))
                    {
                        throw Classify(exception, VideoExportFailure.EncoderFailed, frame.Index);
                    }

                    FrameTaken(target);
                }
            }

            var taken = Volatile.Read(ref _framesTaken);
            if (taken != _source.FrameCount)
            {
                throw new InvalidOperationException($"The pipeline ended after {taken} of {_source.FrameCount} frames without a failure.");
            }

            var finish = Task.Run(encoder.Finish);
            _call = finish;
            try
            {
                await GuardAsync(finish, "finishing the file", frame: null).ConfigureAwait(false);
            }
            catch (Exception exception) when (ExportFailures.ShouldClassify(exception))
            {
                throw Classify(exception, VideoExportFailure.EncoderFailed, frame: null);
            }

            _complete = true;
            return encoder.Encoder;
        }

        // True when an encoder call of another export is stuck: this opening's own stall does not count.
        private static bool StuckElsewhere(Exception exception) =>
            StuckEncoderCalls - (exception is VideoExportException { Failure: VideoExportFailure.EncoderStalled } ? 1 : 0) > 0;

        // Waits for one encoder call, but no longer than the stall limit and not past a cancellation: a call that
        // does not return is abandoned (the clean-up waits for it before releasing the encoder).
        private async Task<T> GuardAsync<T>(Task<T> call, string what, int? frame)
        {
            await GuardAsync((Task)call, what, frame).ConfigureAwait(false);
            return call.Result;
        }

        private async Task GuardAsync(Task call, string what, int? frame)
        {
            try
            {
                await call.WaitAsync(_settings.StallTimeout, _stop.Token).ConfigureAwait(false);
            }
            catch (TimeoutException) when (!call.IsCompleted)
            {
                // Counted until it returns, if it ever does.
                Interlocked.Increment(ref _stuckCalls);
                _ = call.ContinueWith(
                    static _ => Interlocked.Decrement(ref _stuckCalls),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
                throw new VideoExportException(
                    $"{ExportFailures.Summary(VideoExportFailure.EncoderStalled, _settings.Format)}: {what} did not return within {_settings.StallTimeout.TotalSeconds:0.#} s.",
                    VideoExportFailure.EncoderStalled)
                {
                    FrameIndex = frame,
                    SourcePath = frame is int index ? _source.SourcePath(index) : null,
                };
            }
        }

        private async Task CleanUpAsync(string outputPath)
        {
            foreach (var call in new[] { (Task?)_opening, _call })
            {
                if (call is null)
                {
                    continue;
                }

                await call.WaitAsync(AbandonAfter).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                if (!call.IsCompleted)
                {
                    // A stalled driver call: releasing the encoder or the device under it could crash the process.
                    // Both are given up; the process keeps them until it exits.
                    ExportCleanup.Report(new ExportCleanupFailure(outputPath, "release the encoder and the graphics device after a stalled encoder call", null));
                    return;
                }
            }

            var encoder = _encoder ?? (_opening is { IsCompletedSuccessfully: true } opened ? opened.Result : null);
            if (encoder is not null)
            {
                ExportCleanup.Try(outputPath, "release the encoder", encoder.Dispose);

                // The encoder opened (and so truncated) the file: what is there now is not a playable video.
                if (!_complete)
                {
                    ExportCleanup.Try(outputPath, "delete the unfinished file", () => ExportCleanup.DeleteIfThere(outputPath));
                }
            }

            // Frames drawn but never taken, or whose write was cut short.
            foreach (var target in _live.Keys)
            {
                ExportCleanup.Try(outputPath, "release a frame", target.Dispose);
            }

            if (_device is { } device)
            {
                ExportCleanup.Try(outputPath, "release the graphics device", device.Dispose);
            }
        }

        private async Task ProduceAsync()
        {
            try
            {
                for (var i = 0; i < _source.FrameCount; i++)
                {
                    await _ahead.WaitAsync(_stop.Token).ConfigureAwait(false);
                    await _decoding.WaitAsync(_stop.Token).ConfigureAwait(false);
                    Task<CanvasBitmap> load;
                    try
                    {
                        load = _source.LoadAsync(i, Canvas, _output, _stop.Token);
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException && !ExportFailures.IsBug(exception))
                    {
                        throw LoadFailure(exception, i);
                    }

                    _loads.TryAdd(load, 0);
                    _ = load.ContinueWith(
                        static (_, decoding) => ((SemaphoreSlim)decoding!).Release(),
                        _decoding,
                        CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
                    await _decoded.Writer.WriteAsync(new PendingFrame(i, load), _stop.Token).ConfigureAwait(false);
                }

                _decoded.Writer.TryComplete();
            }
            catch (Exception exception)
            {
                RecordFailure(exception);
                _decoded.Writer.TryComplete(exception);
            }
        }

        private async Task ProcessAsync()
        {
            try
            {
                while (await _decoded.Reader.WaitToReadAsync(_stop.Token).ConfigureAwait(false))
                {
                    while (_decoded.Reader.TryRead(out var pending))
                    {
                        // Until here the bitmap belongs to _loads (RunAsync disposes it); from here, to this stage.
                        CanvasBitmap bitmap;
                        try
                        {
                            bitmap = await pending.Load.WaitAsync(_stop.Token).ConfigureAwait(false);
                        }
                        catch (Exception exception) when (exception is not OperationCanceledException && !ExportFailures.IsBug(exception))
                        {
                            throw LoadFailure(exception, pending.Index);
                        }

                        _loads.TryRemove(pending.Load, out _);
                        CanvasRenderTarget target;
                        using (bitmap)
                        {
                            // No time limit of its own: a slot is held only by a frame the encode stage has not
                            // written yet, and every write is timed (GuardAsync). A second timer here raced the
                            // write's own and could report the stall without its frame.
                            await _slots.WaitAsync(_stop.Token).ConfigureAwait(false);

                            target = Draw(bitmap, pending.Index);
                        }

                        // Drawn: the photo is gone, so the next one may load. The target is in _live until the encoder has it.
                        _ahead.Release();
                        await _rendered.Writer.WriteAsync(new RenderedFrame(pending.Index, target), _stop.Token).ConfigureAwait(false);
                    }
                }

                _rendered.Writer.TryComplete();
            }
            catch (Exception exception)
            {
                RecordFailure(exception);
                _rendered.Writer.TryComplete(exception);
            }
        }

        // A new target per frame (never drawn twice, see the class remarks), tracked in _live until the encoder has it.
        private CanvasRenderTarget Draw(CanvasBitmap bitmap, int index)
        {
            try
            {
                var target = new CanvasRenderTarget(
                    Canvas,
                    _settings.Width,
                    _settings.Height,
                    96f,
                    DirectXPixelFormat.B8G8R8A8UIntNormalized,
                    CanvasAlphaMode.Premultiplied);
                _live.TryAdd(target, 0);
                using (var session = target.CreateDrawingSession())
                {
                    FrameRenderer.Draw(session, bitmap, _settings.Fit, _output);
                }

                return target;
            }
            catch (Exception exception) when (ExportFailures.ShouldClassify(exception))
            {
                throw Classify(exception, VideoExportFailure.DrawingFailed, index);
            }
        }

        // A frame that could not be loaded: the photo's own failure, or the device, memory, or the source.
        private Exception LoadFailure(Exception exception, int index)
        {
            if (exception is VideoExportException)
            {
                return exception;
            }

            if (exception is PhotoDecodeException photo)
            {
                return new VideoExportException(
                    $"{ExportFailures.Summary(VideoExportFailure.FrameUnreadable, _settings.Format)}: {photo.Message}",
                    VideoExportFailure.FrameUnreadable,
                    photo)
                {
                    FrameIndex = photo.FrameIndex,
                    SourcePath = photo.Failure.Path,
                };
            }

            // Uploading to a removed device, or out of memory: not the photo's fault.
            var classified = Classify(exception, VideoExportFailure.FrameUnreadable, index);
            return classified.Failure is VideoExportFailure.GraphicsDeviceLost or VideoExportFailure.OutOfMemory
                ? classified
                : new VideoExportException(classified.Message, VideoExportFailure.FrameUnreadable, exception)
                {
                    FrameIndex = index,
                    SourcePath = classified.SourcePath,
                };
        }

        // A native failure with the frame it happened at. A removed device names the failure whatever the HRESULT
        // the call returned (a WriteSample after a driver reset often says only E_FAIL).
        private VideoExportException Classify(Exception exception, VideoExportFailure fallback, int? frame, bool setup = false)
        {
            var path = frame is int index ? _source.SourcePath(index) : null;
            var where = frame is int at ? $" (frame {at}{(path is null ? string.Empty : $", {path}")})" : string.Empty;
            var removed = _device?.RemovedReason() ?? 0;
            if (removed != 0)
            {
                return new VideoExportException(
                    $"{ExportFailures.Summary(VideoExportFailure.GraphicsDeviceLost, _settings.Format)} ({ExportFailures.DeviceRemovedName(removed)}): {exception.Message}{where}",
                    VideoExportFailure.GraphicsDeviceLost,
                    exception)
                {
                    FrameIndex = frame,
                    SourcePath = path,
                    DeviceRemovedReason = removed,
                };
            }

            var failure = ExportFailures.FromHResult(exception.HResult, setup) ?? fallback;
            return new VideoExportException($"{ExportFailures.Summary(failure, _settings.Format)}: {exception.Message}{where}", failure, exception)
            {
                FrameIndex = frame,
                SourcePath = path,
            };
        }

        // The encoder has the frame: drop our reference to the target (Media Foundation holds its own) and free a slot.
        private void FrameTaken(CanvasRenderTarget target)
        {
            if (_live.TryRemove(target, out _))
            {
                target.Dispose();
            }

            _slots.Release();
            // Not inside ?.Report(...): with no progress the count would never move.
            var taken = Interlocked.Increment(ref _framesTaken);
            _progress?.Report(taken);
        }

        private void RecordFailure(Exception exception)
        {
            if (exception is OperationCanceledException)
            {
                return;
            }

            if (exception is ChannelClosedException { InnerException: { } inner })
            {
                exception = inner;
            }

            Interlocked.CompareExchange(ref _failure, exception, null);
        }
    }

    private readonly record struct PendingFrame(int Index, Task<CanvasBitmap> Load);

    private readonly record struct RenderedFrame(int Index, CanvasRenderTarget Target);
}
