using System.Runtime.InteropServices.WindowsRuntime;
using TenLapse.Core.Look;
using TenLapse.Core.Ordering;
using TenLapse.Core.Projects;
using TenLapse.Media.Decoding;

namespace TenLapse.Media.Look;

/// <summary>
/// Measures photos for deflicker (#45) with the Windows decoders: a decode at most <see cref="MeasureSide"/> pixels on
/// its long side (JPEG decodes at an eighth of its size directly; about 30 ms for a 24 MP photo), the brightness of its
/// pixels (<see cref="LuminanceMeter"/>), and the shutter time, f-number and ISO from its Exif block
/// (<see cref="ExposureReader"/>). Stateless; safe on any thread.
/// </summary>
public sealed class ExposureMeter : IExposureMeter
{
    /// <summary>
    /// The longest side measured. 256 px is about 44,000 pixels for a 3:2 photo: the brightness of a scene, not of its
    /// noise, and a twentieth of a millisecond to histogram.
    /// </summary>
    public const int MeasureSide = 256;

    /// <inheritdoc/>
    public async Task<ExposureMeasurement> MeasureAsync(Frame frame, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(frame);
        try
        {
            var decoded = await PhotoDecoder.DecodeAsync(frame.SourcePath, new PhotoDecodeOptions { MaxSide = MeasureSide }, cancellationToken).ConfigureAwait(false);
            if (!decoded.Succeeded)
            {
                return ExposureMeasurement.Failed($"{decoded.Failure.Reason}: {decoded.Failure.Detail}");
            }

            double luminance;
            using (var photo = decoded.Value)
            {
                var size = photo.Size;
                var pixels = new byte[size.Width * size.Height * 4];
                photo.Pixels.CopyToBuffer(pixels.AsBuffer());
                luminance = LuminanceMeter.Measure(pixels, size.Width, size.Height, size.Width * 4);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var settings = ExposureReader.ReadFile(frame.SourcePath);
            return ExposureMeasurement.Measured(new PhotoExposure(luminance)
            {
                ExposureTime = settings?.ExposureTime,
                FNumber = settings?.FNumber,
                Iso = settings?.Iso,
            });
        }
        catch (Exception exception) when (!PhotoDecoder.IsCancellation(exception, cancellationToken))
        {
            // The decoder classifies every file problem itself; anything that still escapes costs this photo only.
            return ExposureMeasurement.Failed(exception.Message);
        }
    }
}
