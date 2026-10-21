using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using TenLapse.Core.Look;
using TenLapse.Core.Projects;
using TenLapse.Media.Decoding;
using Windows.Foundation;
using Windows.UI;

namespace TenLapse.Media.Rendering;

/// <summary>
/// The per-photo processing <see cref="FrameRenderer"/> applies after placing a photo (#45 onward). Today the exposure
/// correction deflicker gives the photo, in stops; the default is no change. #46 to #48 add their steps here.
/// </summary>
/// <param name="Stops">The exposure change in stops, applied in linear light: +1 doubles the light, 0 changes nothing.</param>
public readonly record struct FrameLook(float Stops)
{
    /// <summary>No processing: the photo exactly as decoded.</summary>
    public static FrameLook None => default;

    /// <summary>True when drawing with this look is drawing the photo as it is.</summary>
    public bool IsNeutral => Stops == 0;

    /// <summary>
    /// The look of the photo <paramref name="frameId"/> in <paramref name="snapshot"/>: its deflicker correction
    /// (<see cref="ExposureCurve.For"/>, none while deflicker is off). Keyed by photo, so a photo shown twice looks the same.
    /// </summary>
    public static FrameLook For(ProjectSnapshot snapshot, Guid frameId) =>
        new((float)ExposureCurve.For(snapshot).Correction(frameId));
}

/// <summary>
/// The processing stage that preview (#19) and export (#22) share: given a decoded photo and the frame settings,
/// it draws one output frame. Fill is a centred cover crop; Fit shows the whole photo, centred, with black bars.
/// Deterministic, and with no shortcut for either caller, so a preview frame always matches the exported one. The
/// photo's <see cref="FrameLook"/> (deflicker's exposure, #45) is applied to it in the same call.
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
    public static void Draw(CanvasDrawingSession session, CanvasBitmap photo, FrameSettings settings, PixelSize output) =>
        Draw(session, photo, settings, output, FrameLook.None);

    /// <inheritdoc cref="Draw(CanvasDrawingSession, CanvasBitmap, FrameSettings, PixelSize)"/>
    /// <param name="look">The photo's processing (<see cref="FrameLook.For"/>).</param>
    public static void Draw(CanvasDrawingSession session, CanvasBitmap photo, FrameSettings settings, PixelSize output, FrameLook look)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Draw(session, photo, settings.Fit, output, look);
    }

    /// <inheritdoc cref="Draw(CanvasDrawingSession, CanvasBitmap, FrameSettings, PixelSize)"/>
    public static void Draw(CanvasDrawingSession session, CanvasBitmap photo, FitMode fit, PixelSize output) =>
        Draw(session, photo, fit, output, FrameLook.None);

    /// <inheritdoc cref="Draw(CanvasDrawingSession, CanvasBitmap, FrameSettings, PixelSize, FrameLook)"/>
    public static void Draw(CanvasDrawingSession session, CanvasBitmap photo, FitMode fit, PixelSize output, FrameLook look)
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
        DrawPhoto(session, photo, destination, source, look);
    }

    /// <summary>
    /// Draws the <paramref name="source"/> part of <paramref name="photo"/> (in its own DIPs) into
    /// <paramref name="destination"/> (the session's DIPs) with high-quality cubic filtering, then applies
    /// <paramref name="look"/> to what it drew, pixel by pixel: the one place a photo's processing happens. <see cref="Draw(CanvasDrawingSession, CanvasBitmap, FitMode, PixelSize, FrameLook)"/>
    /// calls it, and so does the preview's reframe, which places the photo by hand (#35), so the 400 ms of a reframe show
    /// the same correction.
    /// </summary>
    /// <remarks>
    /// The exposure is applied in linear light, exactly: each 8-bit sRGB code maps to sRGB(2^stops × linear(code)),
    /// clipped at white, through a 256-entry <see cref="TableTransferEffect"/> whose entries fall on the codes. The photo
    /// is placed first into a command list, so the effect works on the drawn pixels at the target's resolution and the
    /// placement (and its DPI handling) is the same call as without a look. A neutral look draws the photo directly, as
    /// before #45, so every existing frame is unchanged to the bit.
    /// </remarks>
    public static void DrawPhoto(CanvasDrawingSession session, CanvasBitmap photo, Rect destination, Rect source, FrameLook look)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(photo);
        if (look.IsNeutral)
        {
            session.DrawImage(photo, destination, source, 1f, CanvasImageInterpolation.HighQualityCubic);
            return;
        }

        using var placed = new CanvasCommandList(session);
        using (var inner = placed.CreateDrawingSession())
        {
            inner.DrawImage(photo, destination, source, 1f, CanvasImageInterpolation.HighQualityCubic);
        }

        var table = ExposureTable(look.Stops);
        using var exposed = new TableTransferEffect
        {
            Source = placed,
            RedTable = table,
            GreenTable = table,
            BlueTable = table,
            AlphaDisable = true,
            ClampOutput = true,
        };
        session.DrawImage(exposed);
    }

    /// <summary>
    /// The transfer table for an exposure change of <paramref name="stops"/>: entry n is the 8-bit code n / 255 decoded
    /// from sRGB, multiplied by 2^stops in linear light, clipped at 1 and encoded again.
    /// </summary>
    public static float[] ExposureTable(float stops)
    {
        var gain = Math.Pow(2, stops);
        var table = new float[256];
        for (var code = 0; code < table.Length; code++)
        {
            var linear = Math.Min(1, gain * LuminanceMeter.SrgbToLinear(code / 255.0));
            table[code] = (float)Math.Clamp(LuminanceMeter.LinearToSrgb(linear), 0, 1);
        }

        return table;
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
