using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using TenLapse.Core.Look;
using TenLapse.Core.Projects;
using TenLapse.Core.Timing;
using TenLapse.Media.Decoding;
using Windows.Foundation;
using Windows.UI;

namespace TenLapse.Media.Rendering;

/// <summary>
/// The processing <see cref="FrameRenderer"/> applies to one frame (#45 onward): the exposure correction deflicker gives
/// the photo, in stops, and the part of the frame crop and pan shows (#46). The default is no change. Frame blending (#47)
/// draws several photos into one frame, each with its own stops (<see cref="BlendedPhoto"/>); #48 adds its step here.
/// </summary>
/// <param name="Stops">The exposure change in stops, applied in linear light: +1 doubles the light, 0 changes nothing.</param>
/// <param name="View">The part of the frame the video shows, enlarged to fill it (#46); <see cref="Framing.Whole"/> (the default) for all of it.</param>
public readonly record struct FrameLook(float Stops, Framing View = default)
{
    /// <summary>No processing: the photo exactly as decoded, the whole frame.</summary>
    public static FrameLook None => default;

    /// <summary>True when drawing with this look is drawing the photo as it is, the whole frame.</summary>
    public bool IsNeutral => Stops == 0 && View.IsWhole;

    /// <summary>
    /// The look of the photo <paramref name="frameId"/> in <paramref name="snapshot"/>: its deflicker correction
    /// (<see cref="ExposureCurve.For"/>, none while deflicker is off). Keyed by photo, so a photo shown twice looks the same.
    /// The whole frame: <see cref="For(ProjectSnapshot, Guid, int)"/> is a frame of the video.
    /// </summary>
    public static FrameLook For(ProjectSnapshot snapshot, Guid frameId) =>
        new((float)ExposureCurve.For(snapshot).Correction(frameId));

    /// <summary>
    /// The look of frame <paramref name="position"/> of <paramref name="snapshot"/>'s video, which shows the photo
    /// <paramref name="frameId"/>: the photo's deflicker correction, and the framing crop and pan gives that frame
    /// (<see cref="CropSettings.At(FrameSequence, int)"/> of <see cref="ProjectSnapshot.Playback"/>), which follows the
    /// video, not the photo.
    /// </summary>
    public static FrameLook For(ProjectSnapshot snapshot, Guid frameId, int position)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return For(snapshot, frameId) with { View = snapshot.Crop.At(snapshot.Playback, position) };
    }
}

/// <summary>One photo of a blended frame (#47) as <see cref="FrameRenderer"/> draws it.</summary>
/// <param name="Photo">The decoded photo, any size (it is placed as a frame places it).</param>
/// <param name="Stops">Its own exposure change (<see cref="FrameLook.Stops"/>, deflicker's correction of that photo).</param>
/// <param name="Weight">Its share of the frame's light (<see cref="BlendLayer.Weight"/>); a frame's shares add up to 1.</param>
public readonly record struct BlendedPhoto(CanvasBitmap Photo, float Stops, float Weight);

/// <summary>One photo of a blended frame (#47) placed by hand, for <see cref="FrameRenderer.DrawBlend"/>.</summary>
/// <param name="Photo">The decoded photo.</param>
/// <param name="Destination">Where it goes, in the session's DIPs.</param>
/// <param name="Source">The part of it drawn, in its own DIPs.</param>
/// <param name="Stops">Its exposure change.</param>
/// <param name="Weight">Its share of the light.</param>
public readonly record struct PlacedPhoto(CanvasBitmap Photo, Rect Destination, Rect Source, float Stops, float Weight);

/// <summary>
/// The processing stage that preview (#19) and export (#22) share: given a decoded photo and the frame settings,
/// it draws one output frame. Fill is a centred cover crop; Fit shows the whole photo, centred, with black bars.
/// Deterministic, and with no shortcut for either caller, so a preview frame always matches the exported one. The
/// photo's <see cref="FrameLook"/> (deflicker's exposure, #45; crop and pan's view of the frame, #46) is applied to it in
/// the same call.
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
    /// How to decode a photo for a frame that enlarges the picture by <paramref name="zoom"/> (#46: a crop and pan view,
    /// <see cref="Framing.Zoom"/>): for a frame <paramref name="zoom"/> times as large, so the part in view keeps its detail
    /// (the decoder never enlarges, so a photo smaller than that is decoded whole). A zoom of 1 is
    /// <see cref="DecodeOptions(FitMode, PixelSize)"/>.
    /// </summary>
    public static PhotoDecodeOptions DecodeOptions(FitMode fit, PixelSize output, double zoom)
    {
        if (!(zoom > 1))
        {
            return DecodeOptions(fit, output);
        }

        var enlarged = new PixelSize((int)Math.Ceiling(output.Width * zoom), (int)Math.Ceiling(output.Height * zoom));
        return DecodeOptions(fit, enlarged);
    }

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
        var (destination, source, frame) = Placed(session, photo, fit, output, look.View);

        // Filled, not cleared: the preview may draw the frame into part of a larger surface.
        session.FillRectangle(frame, Background);
        if (destination.Width > 0 && destination.Height > 0)
        {
            // Empty only when a Fit frame's view shows nothing but a bar (#46).
            DrawPhoto(session, photo, destination, source, look);
        }
    }

    /// <summary>
    /// Draws a blended frame (#47): each of <paramref name="photos"/> placed as <paramref name="fit"/> and
    /// <paramref name="view"/> place a photo into the <paramref name="output"/>-pixel frame (each by its own size, bars
    /// included), with its own exposure, then all of them averaged by their weights in linear light
    /// (<see cref="DrawBlend"/>). One photo is <see cref="Draw(CanvasDrawingSession, CanvasBitmap, FitMode, PixelSize, FrameLook)"/>
    /// with its look, to the bit.
    /// </summary>
    /// <param name="session">As for <see cref="Draw(CanvasDrawingSession, CanvasBitmap, FrameSettings, PixelSize)"/>.</param>
    /// <param name="photos">The frame's photos (<see cref="BlendSettings.Layers"/>), at least one, on the session's device.</param>
    /// <param name="fit">How each photo is placed.</param>
    /// <param name="output">The frame's size in the target's pixels.</param>
    /// <param name="view">The part of the frame the video shows (#46), the same for every photo.</param>
    public static void Draw(CanvasDrawingSession session, IReadOnlyList<BlendedPhoto> photos, FitMode fit, PixelSize output, Framing view)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(photos);
        ArgumentOutOfRangeException.ThrowIfZero(photos.Count);
        if (photos.Count == 1)
        {
            Draw(session, photos[0].Photo, fit, output, new FrameLook(photos[0].Stops, view));
            return;
        }

        var placed = new PlacedPhoto[photos.Count];
        var frame = default(Rect);
        for (var i = 0; i < photos.Count; i++)
        {
            var photo = photos[i];
            ArgumentNullException.ThrowIfNull(photo.Photo, nameof(photos));
            (var destination, var source, frame) = Placed(session, photo.Photo, fit, output, view);
            placed[i] = new PlacedPhoto(photo.Photo, destination, source, photo.Stops, photo.Weight);
        }

        DrawBlend(session, frame, placed);
    }

    /// <summary>
    /// Draws photos blended (#47): each one over <see cref="Background"/> filling <paramref name="area"/>, placed where it
    /// says, with its exposure (<see cref="ExposureTable"/>), taken to linear light, weighted, summed and encoded back to
    /// sRGB, so a light in one photo of eight leaves an eighth of its light, as in a long exposure. The images between are
    /// 16-bit floating point, so the shadows do not band. <see cref="Draw(CanvasDrawingSession, IReadOnlyList{BlendedPhoto}, FitMode, PixelSize, Framing)"/>
    /// calls it, and so does the preview's reframe, which places the photos by hand (#35). One photo is the area filled and
    /// <see cref="DrawPhoto"/>, to the bit.
    /// </summary>
    /// <remarks>
    /// sRGB to linear and back go through 256-entry <see cref="TableTransferEffect"/> tables (Win2D 1.4 has no sRGB gamma
    /// effect). To linear the entries fall on the 8-bit codes, so it is exact for a photo's pixels, its exposure table
    /// folded in (<see cref="LinearTable"/>); back to sRGB the curve between entries is interpolated, within a fifth of a
    /// level (<see cref="EncodeTable"/>).
    /// </remarks>
    public static void DrawBlend(CanvasDrawingSession session, Rect area, IReadOnlyList<PlacedPhoto> photos)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(photos);
        ArgumentOutOfRangeException.ThrowIfZero(photos.Count);
        if (photos.Count == 1)
        {
            var only = photos[0];
            session.FillRectangle(area, Background);
            if (only.Destination.Width > 0 && only.Destination.Height > 0)
            {
                DrawPhoto(session, only.Photo, only.Destination, only.Source, new FrameLook(only.Stops));
            }

            return;
        }

        var made = new List<IDisposable>(photos.Count * 3);
        try
        {
            ICanvasImage? sum = null;
            foreach (var photo in photos)
            {
                ArgumentNullException.ThrowIfNull(photo.Photo, nameof(photos));
                var placed = new CanvasCommandList(session);
                made.Add(placed);
                using (var inner = placed.CreateDrawingSession())
                {
                    inner.FillRectangle(area, Background);
                    if (photo.Destination.Width > 0 && photo.Destination.Height > 0)
                    {
                        inner.DrawImage(photo.Photo, photo.Destination, photo.Source, 1f, CanvasImageInterpolation.HighQualityCubic);
                    }
                }

                var table = LinearTable(photo.Stops);
                var linear = new TableTransferEffect
                {
                    Source = placed,
                    RedTable = table,
                    GreenTable = table,
                    BlueTable = table,
                    AlphaDisable = true,
                    BufferPrecision = CanvasBufferPrecision.Precision16Float,
                };
                made.Add(linear);

                // The first photo weighted; each next one added to the sum with its weight. On premultiplied pixels, so the
                // alpha of the area adds up to 1 as the weights do.
                var weighted = new ArithmeticCompositeEffect
                {
                    Source1 = sum ?? linear,
                    Source2 = linear,
                    MultiplyAmount = 0,
                    Source1Amount = sum is null ? 0 : 1,
                    Source2Amount = photo.Weight,
                    Offset = 0,
                    BufferPrecision = CanvasBufferPrecision.Precision16Float,
                };
                made.Add(weighted);
                sum = weighted;
            }

            var encode = EncodeTable;
            var encoded = new TableTransferEffect
            {
                Source = sum,
                RedTable = encode,
                GreenTable = encode,
                BlueTable = encode,
                AlphaDisable = true,
                ClampOutput = true,
                BufferPrecision = CanvasBufferPrecision.Precision16Float,
            };
            made.Add(encoded);
            session.DrawImage(encoded);
        }
        finally
        {
            for (var i = made.Count - 1; i >= 0; i--)
            {
                made[i].Dispose();
            }
        }
    }

    /// <summary>
    /// The table that takes a photo's 8-bit code to linear light with an exposure change of <paramref name="stops"/> (#47):
    /// entry n is <see cref="ExposureTable"/>'s entry n decoded from sRGB, so a blended photo is corrected exactly as it is
    /// when it is drawn alone.
    /// </summary>
    public static float[] LinearTable(float stops)
    {
        var exposed = ExposureTable(stops);
        var table = new float[exposed.Length];
        for (var code = 0; code < table.Length; code++)
        {
            table[code] = (float)LuminanceMeter.SrgbToLinear(exposed[code]);
        }

        return table;
    }

    /// <summary>The table that takes linear light back to sRGB (#47): entry n is the sRGB encoding of n / 255. A copy.</summary>
    public static float[] EncodeTable => (float[])_encodeTable.Clone();

    private static readonly float[] _encodeTable = [.. Enumerable.Range(0, 256).Select(level => (float)LuminanceMeter.LinearToSrgb(level / 255.0))];

    // Where a photo goes in the frame, in DIPs: the destination in the session's, the source in the photo's own, and the
    // whole frame. DrawImage takes the source rectangle in the bitmap's own DIPs: a photo that declares 95.9866 DPI (a
    // PNG's 3779 pixels a metre) would otherwise be resampled by 0.014 %. (Switching the session to CanvasUnits.Pixels
    // resampled such a photo too, by up to 2 levels.)
    private static (Rect Destination, Rect Source, Rect Frame) Placed(CanvasDrawingSession session, CanvasBitmap photo, FitMode fit, PixelSize output, Framing view)
    {
        var pixels = photo.SizeInPixels;
        var (source, destination) = Layout(new PixelSize((int)pixels.Width, (int)pixels.Height), output, fit, view);
        var dips = photo.Size;
        var (x, y) = (dips.Width / pixels.Width, dips.Height / pixels.Height);
        source = new Rect(source.X * x, source.Y * y, source.Width * x, source.Height * y);
        var scale = session.Units == CanvasUnits.Pixels ? 1.0 : session.ConvertPixelsToDips(1 << 20) / (double)(1 << 20);
        destination = new Rect(destination.X * scale, destination.Y * scale, destination.Width * scale, destination.Height * scale);
        return (destination, source, new Rect(0, 0, output.Width * scale, output.Height * scale));
    }

    /// <summary>
    /// Draws the <paramref name="source"/> part of <paramref name="photo"/> (in its own DIPs) into
    /// <paramref name="destination"/> (the session's DIPs) with high-quality cubic filtering, then applies
    /// <paramref name="look"/> to what it drew, pixel by pixel: the one place a photo's processing happens. <see cref="Draw(CanvasDrawingSession, CanvasBitmap, FitMode, PixelSize, FrameLook)"/>
    /// calls it, and so does the preview's reframe, which places the photo by hand (#35), so the 400 ms of a reframe show
    /// the same correction.
    /// </summary>
    /// <remarks>
    /// Only the exposure is applied here: <see cref="FrameLook.View"/> is geometry, which the caller has placed already
    /// (<see cref="Layout(PixelSize, PixelSize, FitMode, Framing)"/>, <see cref="Placement"/>).
    /// The exposure is applied in linear light: each 8-bit sRGB code maps to sRGB(2^stops × linear(code)) through a
    /// 256-entry <see cref="TableTransferEffect"/> whose entries fall on the codes (<see cref="ExposureTable"/>, which also
    /// rolls brightened highlights off into white and limits how far apart brightened shadows spread). The photo is placed
    /// first into a command list, so the effect works on the drawn pixels at the target's resolution and the placement (and
    /// its DPI handling) is the same call as without a look. A neutral look draws the photo directly, as before #45, so
    /// every existing frame is unchanged to the bit.
    /// </remarks>
    public static void DrawPhoto(CanvasDrawingSession session, CanvasBitmap photo, Rect destination, Rect source, FrameLook look)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(photo);
        if (look.Stops == 0)
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
    /// The linear level from which a brightened photo rolls off into white instead of clipping (sRGB code 231).
    /// </summary>
    public const double ShoulderKnee = 0.8;

    /// <summary>
    /// The steepest a brightened table rises, in output levels per input level. An 8-bit photo has one level per step;
    /// +2 stops would spread its darkest levels 4 apart (bands in a night sky). Binds only past +1.58 stops.
    /// </summary>
    public const double MaxShadowSlope = 3;

    /// <summary>
    /// The transfer table for an exposure change of <paramref name="stops"/>: entry n is the 8-bit code n / 255 decoded
    /// from sRGB, multiplied by 2^stops in linear light and encoded again. Darkening is exact. Brightening keeps every
    /// level below <see cref="ShoulderKnee"/> exact and rolls the levels above it off into white along a soft shoulder
    /// (they stay apart instead of clipping into a flat patch), and rises at most <see cref="MaxShadowSlope"/> levels per
    /// level in the shadows. Both fade in with the gain: at 0 stops the table is the identity.
    /// </summary>
    public static float[] ExposureTable(float stops)
    {
        var gain = Math.Pow(2, stops);
        var table = new float[256];
        for (var code = 0; code < table.Length; code++)
        {
            var linear = gain * LuminanceMeter.SrgbToLinear(code / 255.0);
            var encoded = gain > 1
                ? Math.Min(LuminanceMeter.LinearToSrgb(Shoulder(linear, gain)), MaxShadowSlope * code / 255.0)
                : LuminanceMeter.LinearToSrgb(linear);
            table[code] = (float)Math.Clamp(encoded, 0, 1);
        }

        return table;
    }

    // The shoulder of a gain above 1: the identity up to the knee, then a rational curve from (knee, knee) with slope 1 to
    // (gain, 1), so white stays white and nothing above the knee clips. At a gain of 1 it is the identity.
    private static double Shoulder(double linear, double gain)
    {
        if (linear <= ShoulderKnee)
        {
            return linear;
        }

        var slope = (gain - ShoulderKnee) / (1 - ShoulderKnee);
        var t = Math.Min(1, (linear - ShoulderKnee) / (gain - ShoulderKnee));
        return ShoulderKnee + ((1 - ShoulderKnee) * (slope * t / (1 + ((slope - 1) * t))));
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

    /// <summary>
    /// <see cref="Layout(PixelSize, PixelSize, FitMode)"/> through crop and pan's <paramref name="view"/> (#46): the part of
    /// the photo that shows in that part of the frame, and where it lands once the view is enlarged to fill the frame. For
    /// Fill that is always the whole frame; for Fit a view that takes in a bar shows the bar, enlarged too, and a view inside
    /// a bar shows no photo at all (both rectangles empty). <see cref="Framing.Whole"/> is the layout without a view.
    /// </summary>
    public static (Rect Source, Rect Destination) Layout(PixelSize photo, PixelSize output, FitMode fit, Framing view)
    {
        var (source, destination) = Layout(photo, output, fit);
        if (view.IsWhole)
        {
            return (source, destination);
        }

        var (shown, visible) = Visible(destination, output, view);
        if (visible.IsEmpty || visible.Width <= 0 || visible.Height <= 0)
        {
            return (new Rect(0, 0, 0, 0), new Rect(0, 0, 0, 0));
        }

        // The part of the photo behind the visible part of the frame: the same share of the source as of the destination.
        var (perX, perY) = (source.Width / destination.Width, source.Height / destination.Height);
        var cropped = new Rect(
            source.X + ((visible.X - destination.X) * perX),
            source.Y + ((visible.Y - destination.Y) * perY),
            visible.Width * perX,
            visible.Height * perY);
        return (cropped, Enlarged(visible, shown, view));
    }

    /// <summary>
    /// Where the whole photo lies in the <paramref name="output"/>-pixel frame when it is drawn through
    /// <paramref name="view"/> (#46): <see cref="Layout(PixelSize, PixelSize, FitMode)"/>'s placement of all of it, the
    /// part a cover crop leaves out included, enlarged with the view. Larger than the frame where it is cropped. The
    /// preview's reframe moves the photo between two of these.
    /// </summary>
    public static Rect Placement(PixelSize photo, PixelSize output, FitMode fit, Framing view)
    {
        var (source, destination) = Layout(photo, output, fit);
        var (perX, perY) = (destination.Width / source.Width, destination.Height / source.Height);
        var whole = new Rect(destination.X - (source.X * perX), destination.Y - (source.Y * perY), photo.Width * perX, photo.Height * perY);
        return view.IsWhole ? whole : Enlarged(whole, Visible(destination, output, view).Shown, view);
    }

    // The view as a rectangle of the frame's pixels, and the part of the destination inside it.
    private static (Rect Shown, Rect Visible) Visible(Rect destination, PixelSize output, Framing view)
    {
        var shown = new Rect(view.Left * output.Width, view.Top * output.Height, view.Size * output.Width, view.Size * output.Height);
        var visible = destination;
        visible.Intersect(shown);
        return (shown, visible);
    }

    // A rectangle of the frame, where it lands once the view (shown, in the frame's pixels) is enlarged to fill the frame.
    private static Rect Enlarged(Rect rect, Rect shown, Framing view) => new(
        (rect.X - shown.X) / view.Size,
        (rect.Y - shown.Y) / view.Size,
        rect.Width / view.Size,
        rect.Height / view.Size);

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
