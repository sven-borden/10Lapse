using System.Runtime.InteropServices;
using Microsoft.Graphics.Canvas;
using TenLapse.Media.Inspection;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Direct3D11;
using Windows.Win32.Media.MediaFoundation;

namespace TenLapse.Media.Export;

/// <summary>
/// The encode stage on <c>IMFSinkWriter</c>: Direct3D 11 surface samples in,
/// H.264 MP4 out, with the export's own device handed to Media Foundation
/// through an <c>IMFDXGIDeviceManager</c>, so the colour converter and the
/// hardware encoder work on the textures Win2D drew, on the same device.
/// </summary>
/// <remarks>
/// <see cref="Write"/> blocks while the encoder is behind (the sink writer
/// throttles), which is the pipeline's backpressure. The container is MP4
/// whatever the file extension, so a <c>.mp4.partial</c> path works (#22).
/// </remarks>
internal sealed unsafe class SinkWriterEncoder : IDisposable
{
    private readonly MediaFoundationSession _session = new();
    private readonly IMFDXGIDeviceManager _manager;
    private readonly IMFSinkWriter _writer;
    private readonly uint _stream;
    private readonly int _width;
    private readonly int _height;
    private bool _disposed;

    public SinkWriterEncoder(string path, VideoExportSettings settings, VideoDevice device)
    {
        _width = settings.Width;
        _height = settings.Height;
        try
        {
            PInvoke.MFCreateDXGIDeviceManager(out var token, out _manager).ThrowOnFailure();
            _manager.ResetDevice(device.Direct3D, token);

            PInvoke.MFCreateAttributes(out var attributes, 4).ThrowOnFailure();
            try
            {
                attributes.SetUnknown(PInvoke.MF_SINK_WRITER_D3D_MANAGER, _manager);
                attributes.SetUINT32(PInvoke.MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS, settings.HardwareAcceleration ? 1u : 0u);
                attributes.SetGUID(PInvoke.MF_TRANSCODE_CONTAINERTYPE, PInvoke.MFTranscodeContainerType_MPEG4);

                // Without it the encoders hand out decode times behind the presentation times and the MP4 sink
                // writes a one-frame composition offset (no edit list): the clip then starts one frame late,
                // always with the Microsoft encoder, below 1 fps with NVENC (10 s late at 0.1 fps).
                attributes.SetUINT32(PInvoke.MF_LOW_LATENCY, 1);
                PInvoke.MFCreateSinkWriterFromURL(path, null, attributes, out _writer).ThrowOnFailure();
            }
            finally
            {
                Marshal.ReleaseComObject(attributes);
            }

            var output = VideoType(settings, PInvoke.MFVideoFormat_H264);
            try
            {
                output.SetUINT32(PInvoke.MF_MT_AVG_BITRATE, settings.EffectiveBitrate);
                output.SetUINT32(PInvoke.MF_MT_MPEG2_PROFILE, (uint)eAVEncH264VProfile.eAVEncH264VProfile_High);
                _writer.AddStream(output, out _stream);
            }
            finally
            {
                Marshal.ReleaseComObject(output);
            }

            // ARGB32 is BGRA in memory, what a Win2D B8G8R8A8 target holds.
            var input = VideoType(settings, PInvoke.MFVideoFormat_ARGB32);
            try
            {
                input.SetUINT32(PInvoke.MF_MT_ALL_SAMPLES_INDEPENDENT, 1);
                _writer.SetInputMediaType(_stream, input, null);
            }
            finally
            {
                Marshal.ReleaseComObject(input);
            }

            _writer.BeginWriting();
            Encoder = DescribeEncoder();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>The encoder transform the sink writer loaded, read from its attributes.</summary>
    public VideoEncoderInfo? Encoder { get; }

    /// <summary>Queues one frame. The sink writer keeps its own reference to the texture until it has encoded it.</summary>
    public void Write(CanvasRenderTarget target, long time, long duration)
    {
        var texture = VideoDevice.TextureOf(target);
        IMFMediaBuffer? buffer = null;
        IMFSample? sample = null;
        try
        {
            PInvoke.MFCreateDXGISurfaceBuffer(typeof(ID3D11Texture2D).GUID, texture, 0, false, out buffer).ThrowOnFailure();
            ((IMF2DBuffer)buffer).GetContiguousLength(out var length);
            buffer.SetCurrentLength(length);
            PInvoke.MFCreateSample(out sample).ThrowOnFailure();
            sample.AddBuffer(buffer);
            sample.SetSampleTime(time);
            sample.SetSampleDuration(duration);
            _writer.WriteSample(_stream, sample);
        }
        finally
        {
            Release(sample);
            Release(buffer);
            Release(texture);
        }
    }

    /// <summary>Drains the encoder and writes the MP4 index. The file is complete only after this.</summary>
    public void Finish() => _writer.Finalize();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Release(_writer);
        Release(_manager);
        _session.Dispose();
    }

    private IMFMediaType VideoType(VideoExportSettings settings, Guid subtype)
    {
        PInvoke.MFCreateMediaType(out var type).ThrowOnFailure();
        type.SetGUID(PInvoke.MF_MT_MAJOR_TYPE, PInvoke.MFMediaType_Video);
        type.SetGUID(PInvoke.MF_MT_SUBTYPE, subtype);
        type.SetUINT64(PInvoke.MF_MT_FRAME_SIZE, Pack((uint)_width, (uint)_height));
        type.SetUINT64(PInvoke.MF_MT_FRAME_RATE, Pack((uint)settings.FrameRate.Numerator, (uint)settings.FrameRate.Denominator));
        type.SetUINT64(PInvoke.MF_MT_PIXEL_ASPECT_RATIO, Pack(1, 1));
        type.SetUINT32(PInvoke.MF_MT_INTERLACE_MODE, (uint)MFVideoInterlaceMode.MFVideoInterlace_Progressive);
        return type;
    }

    private VideoEncoderInfo? DescribeEncoder()
    {
        if (_writer is not IMFSinkWriterEx writer)
        {
            return null;
        }

        for (uint index = 0; ; index++)
        {
            IMFTransform transform;
            Guid category;
            try
            {
                writer.GetTransformForStream(_stream, index, &category, out transform);
            }
            catch (COMException)
            {
                // MF_E_INVALIDINDEX: no more transforms on this stream.
                return null;
            }

            try
            {
                if (category != PInvoke.MFT_CATEGORY_VIDEO_ENCODER)
                {
                    continue;
                }

                transform.GetAttributes(out var attributes);
                try
                {
                    var isHardware = HasString(attributes, PInvoke.MFT_ENUM_HARDWARE_URL_Attribute);
                    var name = ReadString(attributes, PInvoke.MFT_FRIENDLY_NAME_Attribute)
                        ?? (isHardware ? "(unnamed hardware encoder)" : "Microsoft H.264 software encoder");
                    return new VideoEncoderInfo(name, isHardware);
                }
                finally
                {
                    Release(attributes);
                }
            }
            catch (COMException)
            {
                // Software MFTs may have no attribute store at all.
                return new VideoEncoderInfo("Microsoft H.264 software encoder", false);
            }
            finally
            {
                Release(transform);
            }
        }
    }

    private static bool HasString(IMFAttributes attributes, Guid key)
    {
        try
        {
            attributes.GetStringLength(key, out _);
            return true;
        }
        catch (COMException)
        {
            return false;
        }
    }

    private static string? ReadString(IMFAttributes attributes, Guid key)
    {
        try
        {
            attributes.GetAllocatedString(key, out var value, out _);
            try
            {
                return value.ToString();
            }
            finally
            {
                PInvoke.CoTaskMemFree(value.Value);
            }
        }
        catch (COMException)
        {
            return null;
        }
    }

    private static ulong Pack(uint high, uint low) => ((ulong)high << 32) | low;

    private static void Release(object? comObject)
    {
        if (comObject is not null && Marshal.IsComObject(comObject))
        {
            Marshal.ReleaseComObject(comObject);
        }
    }
}
