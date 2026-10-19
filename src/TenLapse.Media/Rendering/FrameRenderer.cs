using Microsoft.Graphics.Canvas;
using TenLapse.Core.Projects;
using TenLapse.Media.Decoding;
using Windows.Foundation;
using Windows.UI;

namespace TenLapse.Media.Rendering;

/// <summary>
/// The processing stage that preview (#19) and export (#22) share: given a decoded photo and the frame settings,
/// it draws one output frame. Fill is a centred cover crop; Fit shows the whole photo, centred, with black bars.
/// Deterministic, and with no shortcut for either caller, so a preview frame always matches the exported one.
/// </summary>
/// <remarks>
/// Decode with <see cref="DecodeOptions"/> for the same output size: the photo then arrives at the size the frame
/// shows it at (the cover size for Fill, the contain size for Fit), with sides whose margins split evenly, and the
/// drawing copies its pixels one to one instead of resampling them. A photo decoded at any other size (a 1280 px
/// proxy, a thumbnail) is scaled with high-quality cubic filtering into the same place.
/// </remarks>
public static class FrameRenderer
{
    /// <summary>The colour of the letterbox bars and of anything transparent in a photo.</summary>
    public static Color Background { get; } = Color.FromArgb(255, 0, 0, 0);

    /// <summary>How to decode a photo for an output of <paramref name="output"/> pixels drawn with <paramref name="fit"/>.</summary>
    public static PhotoDecodeOptions DecodeOptions(FitMode fit, PixelSize output) => fit switch
    {
        FitMode.Fill => PhotoDecodeOptions.Cover(output),
        FitMode.Fit => PhotoDecodeOptions.Contain(output),
        _ => throw new ArgumentOutOfRangeException(nameof(fit), fit, "Unknown fit mode."),
    };

    /// <summary>
    /// Draws <paramref name="photo"/> as frame settings <paramref name="settings"/> place it into the
    /// <paramref name="output"/>-pixel frame at the session's origin, bars included.
    /// </summary>
    /// <param name="session">
    /// Any Win2D session, on the device <paramref name="photo"/> was made on. Its transform applies (the preview can
    /// place and scale the frame with it). <paramref name="output"/> is in the target's pixels: on a 96 DPI target
    /// (the export's) that is DIPs too; on a display at 150 % the frame covers output × 96 / 144 DIPs.
    /// </param>
    public static void Draw(CanvasDrawingSession session, CanvasBitmap photo, FrameSettings settings, PixelSize output)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Draw(session, photo, settings.Fit, output);
    }

    /// <inheritdoc cref="Draw(CanvasDrawingSession, CanvasBitmap, FrameSettings, PixelSize)"/>
    public static void Draw(CanvasDrawingSession session, CanvasBitmap photo, FitMode fit, PixelSize output)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(photo);
        var pixels = photo.SizeInPixels;
        var (source, destination) = Layout(new PixelSize((int)pixels.Width, (int)pixels.Height), output, fit);

        // DrawImage takes the source rectangle in the bitmap's own DIPs: a photo that declares 95.9866 DPI (a PNG's
        // 3779 pixels a metre) would otherwise be resampled by 0.014 %. The destination is in the session's DIPs.
        // (Switching the session to CanvasUnits.Pixels resampled such a photo too, by up to 2 levels.)
        var dips = photo.Size;
        var (x, y) = (dips.Width / pixels.Width, dips.Height / pixels.Height);
        source = new Rect(source.X * x, source.Y * y, source.Width * x, source.Height * y);
        var scale = session.Units == CanvasUnits.Pixels ? 1.0 : session.ConvertPixelsToDips(1 << 20) / (double)(1 << 20);
        destination = new Rect(destination.X * scale, destination.Y * scale, destination.Width * scale, destination.Height * scale);

        // Filled, not cleared: the preview may draw the frame into part of a larger surface.
        session.FillRectangle(new Rect(0, 0, output.Width * scale, output.Height * scale), Background);
        session.DrawImage(photo, destination, source, 1f, CanvasImageInterpolation.HighQualityCubic);
    }

    /// <summary>
    /// Where a photo of <paramref name="photo"/> pixels goes in an <paramref name="output"/>-pixel frame: the part of
    /// the photo drawn (<paramref name="fit"/> Fill crops, Fit keeps all of it) and the rectangle it fills (the whole
    /// frame for Fill; for Fit, centred, touching two opposite edges, bars on the other two).
    /// </summary>
    public static (Rect Source, Rect Destination) Layout(PixelSize photo, PixelSize output, FitMode fit)
    {
        var source = new Size(photo.Width, photo.Height);
        var frame = new Size(output.Width, output.Height);
        return fit switch
        {
            FitMode.Fill => (CoverSourceRect(source, frame), new Rect(0, 0, output.Width, output.Height)),
            FitMode.Fit => (new Rect(0, 0, photo.Width, photo.Height), ContainRect(photo, output)),
            _ => throw new ArgumentOutOfRangeException(nameof(fit), fit, "Unknown fit mode."),
        };
    }

    /// <summary>The centred part of <paramref name="source"/> that, scaled, exactly covers <paramref name="output"/> (Fill).</summary>
    public static Rect CoverSourceRect(Size source, Size output)
    {
        if (source.Width <= 0 || source.Height <= 0 || output.Width <= 0 || output.Height <= 0)
        {
            throw new ArgumentException($"Sizes must be positive; got source {source}, output {output}.");
        }

        // Keep the whole of the side that limits the scale, so it is exact rather than off by rounding.
        if (output.Width * source.Height >= source.Width * output.Height)
        {
            var height = source.Width * output.Height / output.Width;
            return new Rect(0, (source.Height - height) / 2, source.Width, height);
        }

        var width = source.Height * output.Width / output.Height;
        return new Rect((source.Width - width) / 2, 0, width, source.Height);
    }

    /// <summary>
    /// The centred rectangle of <paramref name="output"/> a photo of <paramref name="photo"/> pixels fills when it
    /// fits inside (Fit). The side that limits the scale fills the output; the other is rounded to whole pixels
    /// with the parity of the output's side, as <see cref="PhotoDecoder.ScaledToContain"/> decodes it, so both bars
    /// are whole pixels and a photo decoded at the contain size lands on them one to one.
    /// </summary>
    public static Rect ContainRect(PixelSize photo, PixelSize output)
    {
        if (photo.Width <= 0 || photo.Height <= 0 || output.Width <= 0 || output.Height <= 0)
        {
            throw new ArgumentException($"Sizes must be positive; got photo {photo.Width}x{photo.Height}, output {output.Width}x{output.Height}.");
        }

        if ((long)output.Width * photo.Height >= (long)photo.Width * output.Height)
        {
            // The output is wider than the photo: full height, bars left and right.
            var width = ContainSide(photo.Width * ((double)output.Height / photo.Height), output.Width);
            return new Rect((output.Width - width) / 2.0, 0, width, output.Height);
        }

        var height = ContainSide(photo.Height * ((double)output.Width / photo.Width), output.Height);
        return new Rect(0, (output.Height - height) / 2.0, output.Width, height);
    }

    // The rounding of PhotoDecoder.ScaledToContain, for a photo scaled either way (the decoder never scales up).
    private static int ContainSide(double exact, int outputSide)
    {
        var floor = (int)Math.Floor(exact + 1e-6);
        var sameParity = (floor - outputSide) % 2 == 0;
        var result = Math.Abs(exact - floor) < 1e-6
            ? (sameParity ? floor : floor - 1)
            : (sameParity ? floor : floor + 1);
        return Math.Clamp(result, 1, outputSide);
    }
}
