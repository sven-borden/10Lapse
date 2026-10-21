using TenLapse.Core.Projects;

namespace TenLapse.Presentation.Export;

/// <summary>How the finished state shows the file it just wrote (#51).</summary>
public enum PremiereKind
{
    /// <summary>An MP4, played by Media Foundation.</summary>
    Video,

    /// <summary>An animated GIF, shown by XAML's own GIF decoder (Media Foundation does not play GIF).</summary>
    AnimatedGif,

    /// <summary>A GIF too large to hold in memory: its first frame alone, and a Neutral InfoBar that says why.</summary>
    GifFirstFrame,
}

/// <summary>
/// Picks the premiere for an exported file by the format that was written, never by the file's extension: a GIF the
/// user saved as <c>Harbor.mp4</c> is still a GIF, and an MP4 saved as <c>x.gif</c> still plays as a video.
/// </summary>
public static class PremierePlan
{
    /// <summary>
    /// The largest GIF the premiere plays: 256 MiB. XAML keeps the whole encoded GIF in memory while it plays (it decodes
    /// the frames again on every loop), on top of an app that peaks around 700 MB after a long export (#34's 1 GB
    /// target, test matrix 5.18.10); 256 MiB is about 4,300 frames at Medium or 2,200 at Large, minutes of GIF. A larger
    /// one shows its first frame, decoded alone from the file.
    /// </summary>
    public const long GifPlayLimit = 256L << 20;

    /// <summary>How the finished state shows <paramref name="video"/>.</summary>
    public static PremiereKind For(ExportedVideo video)
    {
        ArgumentNullException.ThrowIfNull(video);
        return video.Format != VideoFormat.Gif ? PremiereKind.Video
            : video.FileBytes > GifPlayLimit ? PremiereKind.GifFirstFrame
            : PremiereKind.AnimatedGif;
    }
}
