using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Channels;
using Microsoft.Graphics.Canvas;
using TenLapse.Core.Timing;
using TenLapse.Media.Inspection;
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
/// <param name="FrameCount">Frames the encoder took.</param>
/// <param name="Duration">The clip duration in 100 ns ticks, <see cref="FrameTiming.Duration"/>.</param>
/// <param name="EncodeStage">The encode stage that wrote the file.</param>
/// <param name="Encoder">
/// The H.264 encoder that ran, as the sink writer reports it; null for
/// <see cref="VideoEncodeStage.MediaTranscoder"/>, which does not say.
/// </param>
public sealed record VideoExportResult(int FrameCount, long Duration, VideoEncodeStage EncodeStage, VideoEncoderInfo? Encoder);

/// <summary>Why an export failed in the encoder.</summary>
public enum VideoExportFailure
{
    /// <summary>No H.264 encoder could be loaded.</summary>
    EncoderUnavailable,

    /// <summary>The encoder refused the size, frame rate or bitrate.</summary>
    SettingsRejected,

    /// <summary>The encoder failed while writing.</summary>
    EncoderFailed,

    /// <summary>The encoder stopped taking frames.</summary>
    EncoderStalled,
}

/// <summary>The encoder could not be set up or failed while writing.</summary>
public sealed class VideoExportException : Exception
{
    /// <summary>Creates the exception.</summary>
    public VideoExportException(string message, VideoExportFailure failure, Exception? innerException = null)
        : base(message, innerException) => Failure = failure;

    /// <summary>What went wrong, for the message the app shows (#22).</summary>
    public VideoExportFailure Failure { get; }
}

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
    /// <remarks>Any exception a frame source throws is rethrown as it is. The file is left behind on failure and cancellation.</remarks>
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
        private readonly Channel<Task<CanvasBitmap>> _decoded;
        private readonly SemaphoreSlim _slots;
        private readonly ConcurrentDictionary<CanvasRenderTarget, byte> _live = new();
        private readonly Channel<RenderedFrame> _rendered;
        private readonly CancellationTokenSource _stop = new();
        private VideoDevice? _device;
        private Exception? _failure;
        private int _framesTaken;

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

                encoder = _settings.EncodeStage == VideoEncodeStage.SinkWriter
                    ? await EncodeWithSinkWriterAsync(outputPath, StartPipeline).ConfigureAwait(false)
                    : await EncodeWithTranscoderAsync(outputPath, StartPipeline).ConfigureAwait(false);
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

                if (exception is VideoExportException or OperationCanceledException || exception == failure)
                {
                    throw;
                }

                throw new VideoExportException($"The H.264 encoder failed: {exception.Message}", VideoExportFailure.EncoderFailed, exception);
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
            if (taken != _source.FrameCount)
            {
                throw new VideoExportException(
                    $"The encoder finished after {taken} of {_source.FrameCount} frames.",
                    VideoExportFailure.EncoderFailed);
            }

            return new VideoExportResult(taken, FrameTiming.Duration(taken, Rate), _settings.EncodeStage, encoder);
        }

        private async Task<VideoEncoderInfo?> EncodeWithSinkWriterAsync(string outputPath, Action startPipeline)
        {
            // The sink writer blocks in WriteSample and Finalize: keep it off the caller's thread.
            return await Task.Run(
                    async () =>
                    {
                        SinkWriterEncoder encoder;
                        try
                        {
                            encoder = new SinkWriterEncoder(outputPath, _settings, _device!);
                        }
                        catch (COMException exception)
                        {
                            throw SetupFailure(exception);
                        }

                        using (encoder)
                        {
                            startPipeline();
                            while (await _rendered.Reader.WaitToReadAsync(_stop.Token).ConfigureAwait(false))
                            {
                                while (_rendered.Reader.TryRead(out var frame))
                                {
                                    _stop.Token.ThrowIfCancellationRequested();
                                    encoder.Write(
                                        frame.Target,
                                        FrameTiming.FrameStart(frame.Index, Rate),
                                        FrameTiming.FrameDuration(frame.Index, Rate));
                                    FrameTaken(frame.Target);
                                }
                            }

                            encoder.Finish();
                            return encoder.Encoder;
                        }
                    },
                    _stop.Token)
                .ConfigureAwait(false);
        }

        private async Task<VideoEncoderInfo?> EncodeWithTranscoderAsync(string outputPath, Action startPipeline)
        {
            var source = CreateMediaStreamSource();
            source.SampleRequested += OnSampleRequested;
            try
            {
                var transcoder = new MediaTranscoder { HardwareAccelerationEnabled = _settings.HardwareAcceleration };
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
                        prepared.FailureReason == TranscodeFailureReason.CodecNotFound
                            ? VideoExportFailure.EncoderUnavailable
                            : VideoExportFailure.SettingsRejected);
                }

                startPipeline();
                await prepared.TranscodeAsync().AsTask(_stop.Token).ConfigureAwait(false);
                await output.FlushAsync().AsTask(_stop.Token).ConfigureAwait(false);
                return null;
            }
            finally
            {
                source.SampleRequested -= OnSampleRequested;
            }
        }

        private static VideoExportException SetupFailure(COMException exception) => (uint)exception.HResult switch
        {
            // MF_E_TOPO_CODEC_NOT_FOUND, MF_E_TRANSFORM_NOT_POSSIBLE_FOR_CURRENT_MEDIATYPE_COMBINATION
            0xC00D5212 or 0xC00D6D60 => new VideoExportException(
                $"No H.264 encoder could be loaded ({exception.Message}).", VideoExportFailure.EncoderUnavailable, exception),

            // MF_E_INVALIDMEDIATYPE, MF_E_UNSUPPORTED_RATE, E_INVALIDARG
            0xC00D36B4 or 0xC00D3E9D or 0x80070057 => new VideoExportException(
                $"The H.264 encoder refused these settings ({exception.Message}).", VideoExportFailure.SettingsRejected, exception),

            _ => new VideoExportException($"The H.264 encoder could not start: {exception.Message}", VideoExportFailure.EncoderFailed, exception),
        };

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
            var time = TimeSpan.FromTicks(FrameTiming.FrameStart(frame.Index, Rate));
            var sample = _settings.TranscoderInput == TranscoderInput.Direct3DSurface
                ? MediaStreamSample.CreateFromDirect3D11Surface(frame.Target, time)
                : MediaStreamSample.CreateFromBuffer(frame.Target.GetPixelBytes().AsBuffer(), time);
            sample.Duration = TimeSpan.FromTicks(FrameTiming.FrameDuration(frame.Index, Rate));
            sample.KeyFrame = true;

            // Processed means Media Foundation has taken the sample, not that it has read the surface.
            sample.Processed += (_, _) => FrameTaken(frame.Target);
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
