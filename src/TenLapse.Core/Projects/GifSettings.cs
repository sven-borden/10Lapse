using TenLapse.Core.Timing;

namespace TenLapse.Core.Projects;

/// <summary>
/// The size of an animated GIF (#51), named by its <b>long side</b>: GIFs go into chat messages and web pages, which
/// fit them in a box, so the long side is what decides how big one shows (a portrait GIF is as tall as a landscape
/// one is wide). The short side follows the frame shape (<see cref="GifOutput.Size(GifSize, FrameAspect, PixelSize?)"/>).
/// </summary>
public enum GifSize
{
    /// <summary>320 px on the long side: a reaction-sized GIF, smallest file.</summary>
    Small,

    /// <summary>480 px on the long side: the usual chat and web size (the default).</summary>
    Medium,

    /// <summary>640 px on the long side: the largest; GIF files grow fast past this.</summary>
    Large,
}

/// <summary>
/// The most frames per second an animated GIF (#51) plays. GIF times each frame in hundredths of a second, so the
/// presets are rates whose frame lasts a whole number of them (10, 5 and 4 cs). A video that plays slower keeps its own
/// rate (<see cref="GifOutput.EffectiveRate"/>).
/// </summary>
public enum GifFrameRate
{
    /// <summary>Up to 10 fps: every frame 10 cs. The smallest file; the default.</summary>
    Fps10,

    /// <summary>Up to 20 fps: every frame 5 cs.</summary>
    Fps20,

    /// <summary>Up to 25 fps: every frame 4 cs, the smoothest preset.</summary>
    Fps25,
}

/// <summary>The animated GIF's own settings (#51), used when <see cref="OutputSettings.Format"/> is <see cref="VideoFormat.Gif"/>. Immutable.</summary>
public sealed record GifSettings
{
    /// <summary>Medium (480 px) at up to 10 fps: small enough for chat.</summary>
    public static GifSettings Default { get; } = new(GifSize.Medium, GifFrameRate.Fps10);

    /// <summary>Creates GIF settings.</summary>
    public GifSettings(GifSize size, GifFrameRate rate)
    {
        Size = size;
        Rate = rate;
    }

    /// <summary>The long side.</summary>
    public GifSize Size
    {
        get;
        init => field = Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown GIF size.");
    }

    /// <summary>The highest frame rate.</summary>
    public GifFrameRate Rate
    {
        get;
        init => field = Enum.IsDefined(value) ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown GIF frame rate.");
    }
}

/// <summary>
/// What an animated GIF export (#51) writes for a project: its size, its frame rate, which photo each GIF frame shows,
/// and an estimate of the file size. Pure functions, shared by the Output panel, the export and the tests.
/// </summary>
/// <remarks>
/// <para>
/// <b>Frames.</b> The GIF plays as long as the video. When the video plays faster than the GIF's rate, the GIF shows,
/// at each of its own frame times, the photo the video shows at that moment (<see cref="SourceFrame"/>): a 30 fps video
/// as a 10 fps GIF shows every third photo. A slower video keeps its own rate and every photo. The GIF's last frame
/// ends on its own grid, so the GIF is at most one GIF frame longer than the video.
/// </para>
/// <para>
/// <b>Delays.</b> GIF times frames in hundredths of a second (cs). Each frame's delay is the difference of its own start
/// and the next frame's start, each rounded to the nearest cs from the exact rational time
/// (<see cref="GifTiming"/>), never a rounded delay added up, so the length never drifts.
/// </para>
/// </remarks>
public static class GifOutput
{
    /// <summary>The GIF never plays faster than 50 fps: browsers stretch a delay under 2 cs to 10 cs.</summary>
    public static Fraction MaxRate { get; } = new(50, 1);

    /// <summary>The long side in pixels: 320, 480 or 640.</summary>
    public static int LongSide(GifSize size) => size switch
    {
        GifSize.Small => 320,
        GifSize.Medium => 480,
        GifSize.Large => 640,
        _ => throw new ArgumentOutOfRangeException(nameof(size), size, "Unknown GIF size."),
    };

    /// <summary>The preset's rate: 10, 20 or 25 fps.</summary>
    public static Fraction Rate(GifFrameRate rate) => rate switch
    {
        GifFrameRate.Fps10 => new Fraction(10, 1),
        GifFrameRate.Fps20 => new Fraction(20, 1),
        GifFrameRate.Fps25 => new Fraction(25, 1),
        _ => throw new ArgumentOutOfRangeException(nameof(rate), rate, "Unknown GIF frame rate."),
    };

    /// <summary>
    /// The GIF's width and height: <see cref="LongSide"/> on the long side, the short side from the shape, rounded to the
    /// nearest even number (the export's frames have even sides) and at least 2. 480 gives 480 × 270 at 16:9,
    /// 270 × 480 at 9:16, 480 × 480 at 1:1, 384 × 480 at 4:5 and 480 × 360 at 4:3.
    /// </summary>
    /// <param name="size">The preset.</param>
    /// <param name="aspect">The frame shape.</param>
    /// <param name="photoSize">For <see cref="FrameAspect.MatchPhotos"/>: the most common photo size.</param>
    /// <exception cref="ArgumentException"><see cref="FrameAspect.MatchPhotos"/> without a photo size.</exception>
    public static PixelSize Size(GifSize size, FrameAspect aspect, PixelSize? photoSize = null)
    {
        var (width, height) = aspect switch
        {
            FrameAspect.Ratio16x9 => (16, 9),
            FrameAspect.Ratio9x16 => (9, 16),
            FrameAspect.Ratio1x1 => (1, 1),
            FrameAspect.Ratio4x5 => (4, 5),
            FrameAspect.Ratio4x3 => (4, 3),
            FrameAspect.MatchPhotos => photoSize is { } photo
                ? (photo.Width, photo.Height)
                : throw new ArgumentException("Match photos needs the photos' size.", nameof(photoSize)),
            _ => throw new ArgumentOutOfRangeException(nameof(aspect), aspect, "Unknown aspect."),
        };

        long longSide = LongSide(size);
        long longRatio = Math.Max(width, height);
        long shortRatio = Math.Min(width, height);

        // longSide × shortRatio / longRatio, rounded half up to an even number, at least 2.
        var shortSide = (int)Math.Max(2, ((longSide * shortRatio) + longRatio) / (2 * longRatio) * 2);
        return width >= height ? new PixelSize((int)longSide, shortSide) : new PixelSize(shortSide, (int)longSide);
    }

    /// <summary>
    /// A project's GIF size: <see cref="Size(GifSize, FrameAspect, PixelSize?)"/> with Match photos taking the most common
    /// photo size (<see cref="OutputSize.MostCommonPhotoSize"/>), or 16:9 when no frame has a size yet.
    /// </summary>
    public static PixelSize Size(GifSize size, FrameAspect aspect, IEnumerable<Frame> frames)
    {
        ArgumentNullException.ThrowIfNull(frames);
        if (aspect != FrameAspect.MatchPhotos)
        {
            return Size(size, aspect);
        }

        return OutputSize.MostCommonPhotoSize(frames) is { } photo
            ? Size(size, aspect, photo)
            : Size(size, FrameAspect.Ratio16x9);
    }

    /// <summary>The rate the GIF plays at: the preset's, or the video's own when it is slower.</summary>
    public static Fraction EffectiveRate(Fraction videoRate, GifFrameRate preset)
    {
        CheckRate(videoRate, nameof(videoRate));
        var most = Rate(preset);
        return videoRate < most ? videoRate : most;
    }

    /// <summary>
    /// How many GIF frames cover a video of <paramref name="videoFrames"/> frames: ⌈frames × gif rate / video rate⌉, so
    /// the last one starts before the video ends. Equal to <paramref name="videoFrames"/> when the rates are equal.
    /// </summary>
    public static int FrameCount(int videoFrames, Fraction videoRate, Fraction gifRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(videoFrames);
        CheckRates(videoRate, gifRate);

        // frames × (g.num / g.den) / (v.num / v.den), exact.
        var numerator = (Int128)videoFrames * gifRate.Numerator * videoRate.Denominator;
        var denominator = (Int128)gifRate.Denominator * videoRate.Numerator;
        return (int)((numerator + denominator - 1) / denominator);
    }

    /// <summary>
    /// The video frame GIF frame <paramref name="gifFrame"/> shows: the one on screen at that GIF frame's start,
    /// ⌊index × video rate / gif rate⌋. Strictly increasing in the index while the GIF is not faster than the video.
    /// </summary>
    public static int SourceFrame(int gifFrame, Fraction videoRate, Fraction gifRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(gifFrame);
        CheckRates(videoRate, gifRate);
        var numerator = (Int128)gifFrame * videoRate.Numerator * gifRate.Denominator;
        var denominator = (Int128)videoRate.Denominator * gifRate.Numerator;
        return (int)(numerator / denominator);
    }

    /// <summary>
    /// The video frames a GIF of <paramref name="videoFrames"/> shows, in order: <see cref="SourceFrame"/> for each of
    /// its <see cref="FrameCount"/> frames.
    /// </summary>
    public static int[] SourceFrames(int videoFrames, Fraction videoRate, Fraction gifRate)
    {
        var count = FrameCount(videoFrames, videoRate, gifRate);
        var frames = new int[count];
        for (var i = 0; i < count; i++)
        {
            frames[i] = SourceFrame(i, videoRate, gifRate);
        }

        return frames;
    }

    /// <summary>
    /// The expected size in bytes of a GIF of <paramref name="gifFrames"/> frames at <paramref name="size"/>: a measured
    /// cost per pixel of each frame plus the file's own header and each frame's palette and headers. An estimate: GIF
    /// compresses smooth sky far better than foliage or water, so a real file lands within
    /// <see cref="EstimateLowShare"/> to <see cref="EstimateHighShare"/> of it.
    /// </summary>
    /// <remarks>
    /// Measured (#51, <c>GifExportTests.MeasureBytesPerPixel</c>) by writing nine landscape photographs (the ones
    /// Windows ships as lock screen and wallpaper pictures) filled into 16:9 at each size, as one GIF frame each with the
    /// export's per-frame palette and error diffusion: 0.39 to 0.59 bytes per pixel, 0.49 on average, the same at every
    /// size within 0.02. Every frame is written whole (no frame reuses the one before), so the size is linear in frames.
    /// </remarks>
    public static long EstimateBytes(int gifFrames, PixelSize size)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(gifFrames);
        if (gifFrames == 0)
        {
            return 0;
        }

        var pixels = (double)size.Width * size.Height;
        return HeaderBytes + (gifFrames * (FrameHeaderBytes + (long)Math.Round(pixels * BytesPerPixel)));
    }

    /// <summary>The bytes per pixel of each frame of a photo GIF with a per-frame palette and error diffusion, as measured.</summary>
    public const double BytesPerPixel = 0.5;

    /// <summary>
    /// The share of <see cref="EstimateBytes"/> the Output panel gives as the low end: the quietest photos measured
    /// (0.39 bytes per pixel) and below, as fog or a clear sky goes.
    /// </summary>
    public const double EstimateLowShare = 0.75;

    /// <summary>
    /// The share of <see cref="EstimateBytes"/> the Output panel gives as the high end: the busiest photos measured (0.59)
    /// with room for what was not measured here, a camera's noise at night and foliage in wind.
    /// </summary>
    public const double EstimateHighShare = 1.5;

    // The GIF header, logical screen descriptor and the looping extension.
    private const long HeaderBytes = 64;

    // Per frame: the graphic control extension, the image descriptor and a 256-colour palette (768 bytes).
    private const long FrameHeaderBytes = 800;

    private static void CheckRates(Fraction videoRate, Fraction gifRate)
    {
        CheckRate(videoRate, nameof(videoRate));
        CheckRate(gifRate, nameof(gifRate));
    }

    private static void CheckRate(Fraction rate, string name)
    {
        if (rate.Numerator <= 0 || rate.Denominator <= 0)
        {
            throw new ArgumentOutOfRangeException(name, rate, "The frame rate must be positive.");
        }
    }
}
