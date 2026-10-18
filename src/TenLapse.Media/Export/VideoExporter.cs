using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using Microsoft.Graphics.Canvas;
using TenLapse.Core.Timing;
using Windows.Foundation;
using Windows.Graphics.DirectX;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI;

namespace TenLapse.Media.Export;

/// <summary>What an export wrote.</summary>
/// <param name="FrameCount">Frames handed to the encoder and released by it.</param>
/// <param name="Duration">The clip duration in 100 ns ticks, <see cref="FrameTiming.Duration"/>.</param>
/// <param name="HardwareAccelerationEnabled">Whether <c>MediaTranscoder</c> was allowed a GPU encoder.</param>
public sealed record VideoExportResult(int FrameCount, long Duration, bool HardwareAccelerationEnabled);

/// <summary>The encoder could not be set up or failed while writing.</summary>
public sealed class VideoExportException : Exception
{
    /// <summary>Creates the exception.</summary>
    public VideoExportException(string message, TranscodeFailureReason reason, Exception? innerException = null)
        : base(message, innerException) => Reason = reason;

    /// <summary>Why <c>MediaTranscoder</c> refused, or <see cref="TranscodeFailureReason.Unknown"/> for a failure while encoding.</summary>
    public TranscodeFailureReason Reason { get; }
}

/// <summary>
/// The streaming export: decode, process (Win2D fill) and encode (H.264 MP4
/// through <c>MediaTranscoder</c>) run at once, connected by bounded queues, so
/// memory stays flat whatever the frame count.
/// </summary>
/// <remarks>
/// Decode runs up to <see cref="VideoExportSettings.DecodeAhead"/> frames ahead.
/// Drawing takes one of <see cref="VideoExportSettings.RenderTargets"/>
/// output-sized <c>CanvasRenderTarget</c>s and blocks when none is free; the
/// <c>MediaStreamSource</c> hands each target to Media Foundation as a Direct3D
/// surface sample and gets it back when the sample's <c>Processed</c> event fires.
/// Sample time and duration of frame n are <see cref="FrameTiming.FrameStart"/>
/// and <see cref="FrameTiming.FrameDuration"/>, never accumulated.
/// </remarks>
public static class VideoExporter
{
    /// <summary>
    /// How long drawing waits for the encoder to release a render target before
    /// the export fails instead of hanging.
    /// </summary>
    public static readonly TimeSpan EncoderStallTimeout = TimeSpan.FromSeconds(60);

    /// <summary>Writes every frame of <paramref name="source"/> to an H.264 MP4 at <paramref name="outputPath"/>, replacing it.</summary>
    /// <param name="progress">Receives the number of frames the encoder has taken, from Media Foundation threads.</param>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <exception cref="VideoExportException">The encoder refused the settings or failed.</exception>
    /// <remarks>Any exception a frame source throws is rethrown as it is. The file is left behind on failure.</remarks>
    public static async Task<VideoExportResult> ExportAsync(
        IFrameSource source,
        string outputPath,
        VideoExportSettings settings,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrEmpty(outputPath);
        ArgumentNullException.ThrowIfNull(settings);
        if (source.FrameCount <= 0)
        {
            throw new ArgumentException("The source has no frames.", nameof(source));
        }

        var run = new ExportRun(source, settings, progress);
        return await run.RunAsync(Path.GetFullPath(outputPath), cancellationToken).ConfigureAwait(false);
    }

    private sealed class ExportRun
    {
        private readonly IFrameSource _source;
        private readonly VideoExportSettings _settings;
        private readonly IProgress<int>? _progress;
        private readonly CanvasDevice _device = GraphicsDevice.Shared;
        private readonly Channel<Task<CanvasBitmap>> _decoded;
        private readonly Channel<CanvasRenderTarget> _free = Channel.CreateUnbounded<CanvasRenderTarget>();
        private readonly Channel<RenderedFrame> _rendered;
        private readonly CancellationTokenSource _stop = new();
        private Exception? _failure;
        private int _framesSupplied;
        private int _framesEncoded;

        public ExportRun(IFrameSource source, VideoExportSettings settings, IProgress<int>? progress)
        {
            _source = source;
            _settings = settings;
            _progress = progress;
            _decoded = Channel.CreateBounded<Task<CanvasBitmap>>(new BoundedChannelOptions(Math.Max(1, settings.DecodeAhead))
            {
                SingleReader = true,
                SingleWriter = true,
            });
            _rendered = Channel.CreateBounded<RenderedFrame>(new BoundedChannelOptions(Math.Max(1, settings.RenderTargets))
            {
                SingleReader = true,
                SingleWriter = true,
            });
        }

        private Fraction Rate => _settings.FrameRate;

        public async Task<VideoExportResult> RunAsync(string outputPath, CancellationToken cancellationToken)
        {
            using var registration = cancellationToken.Register(_stop.Cancel);
            var targets = new List<CanvasRenderTarget>();
            var source = CreateMediaStreamSource();
            source.SampleRequested += OnSampleRequested;
            var transcoder = new MediaTranscoder { HardwareAccelerationEnabled = _settings.HardwareAcceleration };
            var producing = Task.CompletedTask;
            var processing = Task.CompletedTask;
            try
            {
                for (var i = 0; i < Math.Max(1, _settings.RenderTargets); i++)
                {
                    var target = new CanvasRenderTarget(
                        _device,
                        _settings.Width,
                        _settings.Height,
                        96f,
                        DirectXPixelFormat.B8G8R8A8UIntNormalized,
                        CanvasAlphaMode.Premultiplied);
                    targets.Add(target);
                    _free.Writer.TryWrite(target);
                }

                using var output = await FileRandomAccessStream.OpenAsync(
                        outputPath,
                        FileAccessMode.ReadWrite,
                        StorageOpenOptions.None,
                        FileOpenDisposition.CreateAlways)
                    .AsTask(_stop.Token)
                    .ConfigureAwait(false);
                var prepared = await transcoder.PrepareMediaStreamSourceTranscodeAsync(source, output, CreateProfile())
                    .AsTask(_stop.Token)
                    .ConfigureAwait(false);
                if (!prepared.CanTranscode)
                {
                    throw new VideoExportException(
                        $"The H.264 encoder cannot take this export ({prepared.FailureReason}).",
                        prepared.FailureReason);
                }

                producing = Task.Run(ProduceAsync);
                processing = Task.Run(ProcessAsync);
                await prepared.TranscodeAsync().AsTask(_stop.Token).ConfigureAwait(false);
                await output.FlushAsync().AsTask(_stop.Token).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _stop.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
                if (Volatile.Read(ref _failure) is { } failure)
                {
                    // The source or the drawing failed first; the transcoder's error is only the consequence.
                    ExceptionDispatchInfo.Throw(failure);
                }

                if (exception is VideoExportException or OperationCanceledException)
                {
                    throw;
                }

                throw new VideoExportException($"The H.264 encoder failed: {exception.Message}", TranscodeFailureReason.Unknown, exception);
            }
            finally
            {
                _stop.Cancel();
                await Task.WhenAll(producing, processing).ConfigureAwait(false);
                while (_decoded.Reader.TryRead(out var load))
                {
                    DisposeWhenLoaded(load);
                }

                source.SampleRequested -= OnSampleRequested;
                foreach (var target in targets)
                {
                    target.Dispose();
                }

                _stop.Dispose();
            }

            var supplied = Volatile.Read(ref _framesSupplied);
            if (supplied != _source.FrameCount)
            {
                throw new VideoExportException(
                    $"The encoder finished after {supplied} of {_source.FrameCount} frames.",
                    TranscodeFailureReason.Unknown);
            }

            return new VideoExportResult(supplied, FrameTiming.Duration(supplied, Rate), transcoder.HardwareAccelerationEnabled);
        }

        private MediaStreamSource CreateMediaStreamSource()
        {
            var properties = VideoEncodingProperties.CreateUncompressed(
                MediaEncodingSubtypes.Bgra8,
                (uint)_settings.Width,
                (uint)_settings.Height);
            properties.FrameRate.Numerator = (uint)Rate.Numerator;
            properties.FrameRate.Denominator = (uint)Rate.Denominator;
            properties.PixelAspectRatio.Numerator = 1;
            properties.PixelAspectRatio.Denominator = 1;
            return new MediaStreamSource(new VideoStreamDescriptor(properties))
            {
                // A file source: no network buffering, no seeking, a known end.
                BufferTime = TimeSpan.Zero,
                CanSeek = false,
                Duration = TimeSpan.FromTicks(FrameTiming.Duration(_source.FrameCount, Rate)),
            };
        }

        private MediaEncodingProfile CreateProfile()
        {
            var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p);
            profile.Audio = null;
            var video = profile.Video;
            video.Width = (uint)_settings.Width;
            video.Height = (uint)_settings.Height;
            video.Bitrate = _settings.EffectiveBitrate;
            video.FrameRate.Numerator = (uint)Rate.Numerator;
            video.FrameRate.Denominator = (uint)Rate.Denominator;
            video.PixelAspectRatio.Numerator = 1;
            video.PixelAspectRatio.Denominator = 1;
            video.ProfileId = H264ProfileIds.High;
            return profile;
        }

        private async Task ProduceAsync()
        {
            try
            {
                for (var i = 0; i < _source.FrameCount; i++)
                {
                    _stop.Token.ThrowIfCancellationRequested();
                    var load = _source.LoadAsync(i, _device, _stop.Token);
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
                            var target = await TakeFreeTargetAsync().ConfigureAwait(false);
                            using (var session = target.CreateDrawingSession())
                            {
                                session.Clear(black);
                                FrameFill.Draw(session, bitmap, output);
                            }

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

        private async Task<CanvasRenderTarget> TakeFreeTargetAsync()
        {
            try
            {
                return await _free.Reader.ReadAsync(_stop.Token).AsTask().WaitAsync(EncoderStallTimeout, _stop.Token).ConfigureAwait(false);
            }
            catch (TimeoutException exception)
            {
                throw new VideoExportException(
                    $"The encoder has not released a frame for {EncoderStallTimeout.TotalSeconds:0} s.",
                    TranscodeFailureReason.Unknown,
                    exception);
            }
        }

        // Media Foundation raises this on its own threads, one request at a time per stream.
        private void OnSampleRequested(MediaStreamSource sender, MediaStreamSourceSampleRequestedEventArgs args)
        {
            var request = args.Request;
            var deferral = request.GetDeferral();
            _ = SupplySampleAsync(sender, request, deferral);
        }

        private async Task SupplySampleAsync(
            MediaStreamSource sender,
            MediaStreamSourceSampleRequest request,
            MediaStreamSourceSampleRequestDeferral deferral)
        {
            try
            {
                // False once every frame is out: a null sample ends the stream and the transcoder finishes the file.
                if (await _rendered.Reader.WaitToReadAsync(_stop.Token).ConfigureAwait(false)
                    && _rendered.Reader.TryRead(out var frame))
                {
                    request.Sample = CreateSample(frame);
                }
            }
            catch (OperationCanceledException)
            {
                // Cancelled: end the stream; RunAsync reports the cancellation.
            }
            catch (Exception exception)
            {
                RecordFailure(exception);
                sender.NotifyError(MediaStreamSourceErrorStatus.Other);
            }
            finally
            {
                deferral.Complete();
            }
        }

        private MediaStreamSample CreateSample(RenderedFrame frame)
        {
            var sample = MediaStreamSample.CreateFromDirect3D11Surface(
                frame.Target,
                TimeSpan.FromTicks(FrameTiming.FrameStart(frame.Index, Rate)));
            sample.Duration = TimeSpan.FromTicks(FrameTiming.FrameDuration(frame.Index, Rate));
            sample.KeyFrame = true;
            sample.Processed += (_, _) =>
            {
                // The encoder no longer reads the surface: the target can be drawn again.
                _free.Writer.TryWrite(frame.Target);
                _progress?.Report(Interlocked.Increment(ref _framesEncoded));
            };
            Interlocked.Increment(ref _framesSupplied);
            return sample;
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
