using Microsoft.Graphics.Canvas;
using Windows.Foundation;

namespace TenLapse.Media.Export;

/// <summary>
/// The process stage for now: fill the output frame with the photo, scaled to
/// cover it and centred, cropping what sticks out. Preview (#19) draws through
/// the same code so it always matches the export.
/// </summary>
public static class FrameFill
{
    /// <summary>The centred part of <paramref name="source"/> that, scaled, exactly covers <paramref name="output"/>.</summary>
    public static Rect SourceRect(Size source, Size output)
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

    /// <summary>Draws <paramref name="source"/> filled into a session whose target is <paramref name="output"/> DIPs.</summary>
    public static void Draw(CanvasDrawingSession session, CanvasBitmap source, Size output)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(source);

        // Size is in DIPs, which is what DrawImage's source rectangle uses, whatever DPI the photo declares.
        var sourceRect = SourceRect(source.Size, output);
        session.DrawImage(
            source,
            new Rect(0, 0, output.Width, output.Height),
            sourceRect,
            1f,
            CanvasImageInterpolation.HighQualityCubic);
    }
}
