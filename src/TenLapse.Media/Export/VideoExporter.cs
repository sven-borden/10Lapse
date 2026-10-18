using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using Microsoft.Graphics.Canvas;
using TenLapse.Core.Timing;
using TenLapse.Media.Inspection;
using Windows.Foundation;
using Windows.Graphics.DirectX;
using Windows.UI;

namespace TenLapse.Media.Export;

/// <summary>What an export wrote.</summary>
/// <param name="FrameCount">Frames the encoder took.</param>
/// <param name="Duration">The clip duration in 100 ns ticks, <see cref="FrameTiming.Duration"/>.</param>
/// <param name="Encoder">The H.264 encoder that ran, as the sink writer reports it; null if it could not tell.</param>
public sealed record VideoExportResult(int FrameCount, long Duration, VideoEncoderInfo? Encoder);

/// <summary>
/// The streaming export: decode, process (Win2D fill) and encode (H.264 MP4)
/// run at once, connected by bounded queues, so memory stays flat whatever the
/// frame count.
/// </summary>
/// <remarks>
/// <para>
/// Decode runs up to <see cref="VideoExportSettings.DecodeAhead"/> frames ahead.
/// Each frame is drawn into a new output-sized <c>CanvasRenderTarget</c>, and at
/// most <see cref="VideoExportSettings.RenderTargets"/> of them wait for the
/// encoder at once. A target is never drawn twice: Media Foundation may still
/// read a surface after it has taken the sample, so the pipeline drops its own
/// reference once the encoder has the frame and Media Foundation's reference
/// keeps the texture alive until it is done.
/// </para>
/// <para>
/// Sample time and duration of frame n are <see cref="FrameTiming.FrameStart"/>
/// and <see cref="FrameTiming.FrameDuration"/>, never accumulated. Every export
/// runs on its own <see cref="VideoDevice"/>; frame sources get that device.
/// </para>
/// </remarks>
public static class VideoExporter
{
    /// <summary>How long drawing waits for the encoder to take a frame before the export fails instead of hanging.</summary>
    public static readonly TimeSpan EncoderStallTimeout = TimeSpan.FromSeconds(60);

    /// <summary>Writes every frame of <paramref name="source"/> to an H.264 MP4 at <paramref name="outputPath"/>, replacing it.</summary>
    /// <param name="progress">Receives the number of frames the encoder has taken, from worker threads.</param>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <exception cref="VideoExportException">The encoder refused the settings or failed.</exception>
    /// <remarks>
    /// Any exception a frame source throws is rethrown as it is. Once the encoder has opened the output file, a
    /// failure or a cancellation deletes it; a file that was there before and could not be opened is left alone.
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
        private readonly Channel<Task<CanvasBitmap>> _decoded;
        private readonly SemaphoreSlim _slots;
        private readonly ConcurrentDictionary<CanvasRenderTarget, byte> _live = new();
        private readonly Channel<RenderedFrame> _rendered;
        private readonly CancellationTokenSource _stop = new();
        private VideoDevice? _device;
        private Exception? _failure;
        private int _framesTaken;

        public ExportRun(IFrameSource source, VideoExportSettings settings, IProgress<int>? progress, VideoEncoderFactory createEncoder)
        {
            _source = source;
            _settings = settings;
            _progress = progress;
            _createEncoder = createEncoder;
            _decoded = Channel.CreateBounded<Task<CanvasBitmap>>(new BoundedChannelOptions(Math.Max(1, settings.DecodeAhead))
            {
                SingleReader = true,
                SingleWriter = true,
            });
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
            using var registration = cancellationToken.Register(_stop.Cancel);
            var producing = Task.CompletedTask;
            var processing = Task.CompletedTask;
            VideoEncoderInfo? encoder;
            try
            {
                _device = VideoDevice.Create();
                void StartPipeline()
                {
                    producing = Task.Run(ProduceAsync);
                    processing = Task.Run(ProcessAsync);
                }

                encoder = await EncodeAsync(outputPath, StartPipeline).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _stop.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
                var failure = Volatile.Read(ref _failure);
                if (failure is not null && failure != exception)
                {
                    // The source or the drawing failed first; the encoder's error is only the consequence.
                    ExceptionDispatchInfo.Throw(failure);
                }

                // A VideoExportException from a native boundary (device, encoder, drawing), the source's own
                // exception, or a bug in our code, which must not pass for an encoder failure.
                throw;
            }
            finally
            {
                _stop.Cancel();
                await Task.WhenAll(producing, processing).ConfigureAwait(false);
                while (_decoded.Reader.TryRead(out var load))
                {
                    DisposeWhenLoaded(load);
                }

                // Frames drawn but never taken, or taken but not reported processed when the run stopped.
                foreach (var target in _live.Keys)
                {
                    target.Dispose();
                }

                _device?.Dispose();
                _stop.Dispose();
                _slots.Dispose();
            }

            var taken = Volatile.Read(ref _framesTaken);
            return new VideoExportResult(taken, FrameTiming.Duration(taken, Rate), encoder);
        }

        // Runs on the thread pool (see ExportAsync): WriteSample and Finalize block.
        private async Task<VideoEncoderInfo?> EncodeAsync(string outputPath, Action startPipeline)
        {
            IVideoEncoder encoder;
            try
            {
                encoder = _createEncoder(outputPath, _settings, _device!);
            }
            catch (Exception exception) when (ExportFailures.ShouldClassify(exception))
            {
                throw ExportFailures.Classify(exception, VideoExportFailure.EncoderFailed, setup: true);
            }

            var complete = false;
            try
            {
                startPipeline();
                while (await _rendered.Reader.WaitToReadAsync(_stop.Token).ConfigureAwait(false))
                {
                    while (_rendered.Reader.TryRead(out var frame))
                    {
                        _stop.Token.ThrowIfCancellationRequested();
                        try
                        {
                            encoder.Write(
                                frame.Target,
                                FrameTiming.FrameStart(frame.Index, Rate),
                                FrameTiming.FrameDuration(frame.Index, Rate));
                        }
                        catch (Exception exception) when (ExportFailures.ShouldClassify(exception))
                        {
                            throw ExportFailures.Classify(exception, VideoExportFailure.EncoderFailed);
                        }

                        FrameTaken(frame.Target);
                    }
                }

                var taken = Volatile.Read(ref _framesTaken);
                if (taken != _source.FrameCount)
                {
                    throw new InvalidOperationException($"The pipeline ended after {taken} of {_source.FrameCount} frames without a failure.");
                }

                try
                {
                    encoder.Finish();
                }
                catch (Exception exception) when (ExportFailures.ShouldClassify(exception))
                {
                    throw ExportFailures.Classify(exception, VideoExportFailure.EncoderFailed);
                }

                complete = true;
                return encoder.Encoder;
            }
            finally
            {
                encoder.Dispose();
                if (!complete)
                {
                    DeleteIncompleteFile(outputPath);
                }
            }
        }

        // The encoder opened (and so truncated) the file: what is there now is not a playable video.
        private static void DeleteIncompleteFile(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The export's own failure is what the caller needs; a file that cannot be deleted is #22's .partial cleanup.
            }
        }

        private async Task ProduceAsync()
        {
            try
            {
                for (var i = 0; i < _source.FrameCount; i++)
                {
                    _stop.Token.ThrowIfCancellationRequested();
                    var load = _source.LoadAsync(i, Canvas, _stop.Token);
                    try
                    {
                        await _decoded.Writer.WriteAsync(load, _stop.Token).ConfigureAwait(false);
                    }
                    catch
                    {
                        DisposeWhenLoaded(load);
                        throw;
                    }
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
            var output = new Size(_settings.Width, _settings.Height);
            var black = Color.FromArgb(255, 0, 0, 0);
            var index = 0;
            try
            {
                while (await _decoded.Reader.WaitToReadAsync(_stop.Token).ConfigureAwait(false))
                {
                    while (_decoded.Reader.TryRead(out var load))
                    {
                        CanvasBitmap bitmap;
                        try
                        {
                            bitmap = await load.WaitAsync(_stop.Token).ConfigureAwait(false);
                        }
                        catch
                        {
                            DisposeWhenLoaded(load);
                            throw;
                        }

                        using (bitmap)
                        {
                            if (!await _slots.WaitAsync(EncoderStallTimeout, _stop.Token).ConfigureAwait(false))
                            {
                                throw new VideoExportException(
                                    $"The encoder has not taken a frame for {EncoderStallTimeout.TotalSeconds:0} s.",
                                    VideoExportFailure.EncoderStalled);
                            }

                            var target = Draw(bitmap, output, black);
                            await _rendered.Writer.WriteAsync(new RenderedFrame(index, target), _stop.Token).ConfigureAwait(false);
                        }

                        index++;
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
        private CanvasRenderTarget Draw(CanvasBitmap bitmap, Size output, Color background)
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
                    session.Clear(background);
                    FrameFill.Draw(session, bitmap, output);
                }

                return target;
            }
            catch (Exception exception) when (ExportFailures.ShouldClassify(exception) && ExportFailures.FromHResult(exception.HResult) is { } failure)
            {
                // A driver reset or out of video memory while drawing; anything else surfaces as it is (#22 adds frame context).
                throw ExportFailures.Classify(exception, failure);
            }
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

        private static void DisposeWhenLoaded(Task<CanvasBitmap> load) =>
            _ = load.ContinueWith(
                static completed => completed.Result.Dispose(),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnRanToCompletion | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
    }

    private readonly record struct RenderedFrame(int Index, CanvasRenderTarget Target);
}
