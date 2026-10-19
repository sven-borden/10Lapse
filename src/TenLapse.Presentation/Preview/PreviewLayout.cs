using TenLapse.Core.Projects;

namespace TenLapse.Presentation.Preview;

/// <summary>A rectangle on the stage, in DIPs.</summary>
/// <param name="X">Left edge.</param>
/// <param name="Y">Top edge.</param>
/// <param name="Width">Width.</param>
/// <param name="Height">Height.</param>
public readonly record struct StageRect(double X, double Y, double Width, double Height)
{
    /// <summary>True when there is nothing to draw in (narrower or lower than one DIP).</summary>
    public bool IsEmpty => Width < 1 || Height < 1;
}

/// <summary>Where the preview's gate sits on the stage (#19).</summary>
public static class PreviewLayout
{
    /// <summary>
    /// The frame's place in an area of <paramref name="width"/> by <paramref name="height"/> DIPs: as large as fits at
    /// the video's shape, centred, on whole DIPs so its edges stay crisp. Empty when the area or the video has no size.
    /// </summary>
    public static StageRect Gate(double width, double height, PixelSize video)
    {
        if (!(width > 0) || !(height > 0) || video.Width <= 0 || video.Height <= 0)
        {
            return default;
        }

        var scale = Math.Min(width / video.Width, height / video.Height);
        var gateWidth = Math.Round(video.Width * scale);
        var gateHeight = Math.Round(video.Height * scale);
        return new StageRect(Math.Round((width - gateWidth) / 2), Math.Round((height - gateHeight) / 2), gateWidth, gateHeight);
    }
}
