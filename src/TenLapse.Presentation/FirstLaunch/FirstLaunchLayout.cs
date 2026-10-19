namespace TenLapse.Presentation.FirstLaunch;

/// <summary>
/// How the drop zone lays out at a given size (DIPs). Wide, it is the prototype's two columns: the texts and
/// actions left (0.9 parts), Daybreak right (1.1 parts), 64 apart inside 72 of side padding, the frame as wide as
/// its column unless the height runs out first. Narrow (or too short for a legible frame), the texts stand alone,
/// centered, and the timelapse is hidden and stopped.
/// </summary>
/// <param name="ShowsIllustration">Whether Daybreak is on screen.</param>
/// <param name="SidePadding">The drop zone's padding on the left and right.</param>
/// <param name="ColumnGap">The space between the two columns (0 when narrow).</param>
/// <param name="IllustrationShare">The right column's share of the width, in star units (0 when narrow).</param>
/// <param name="FrameWidth">The Daybreak frame's width (0 when narrow).</param>
public readonly record struct FirstLaunchLayout(
    bool ShowsIllustration,
    double SidePadding,
    double ColumnGap,
    double IllustrationShare,
    double FrameWidth)
{
    /// <summary>The text column's share of the width, in star units.</summary>
    public const double TextShare = 0.9;

    /// <summary>The Daybreak column's share of the width, in star units.</summary>
    public const double WideIllustrationShare = 1.1;

    /// <summary>The drop zone's side padding with two columns.</summary>
    public const double WidePadding = 72;

    /// <summary>The drop zone's side padding with the texts alone.</summary>
    public const double NarrowPadding = 40;

    /// <summary>The space between the columns.</summary>
    public const double WideGap = 64;

    /// <summary>The narrowest text column that keeps the title on three lines or fewer.</summary>
    public const double MinTextWidth = 300;

    /// <summary>The narrowest frame that still reads as a picture.</summary>
    public const double MinFrameWidth = 240;

    /// <summary>The space kept above and below Daybreak inside the drop zone.</summary>
    public const double VerticalMargin = 24;

    /// <summary>Space between the frame, the strip and the caption.</summary>
    public const double RowGap = 10;

    /// <summary>Space between thumbnails.</summary>
    public const double ThumbGap = 3;

    /// <summary>The caption row's height (Caption, 16 px lines).</summary>
    public const double CaptionHeight = 16;

    /// <summary>The texts alone, centered, without Daybreak: a narrow or short drop zone, or Daybreak not shown.</summary>
    public static FirstLaunchLayout TextsAlone { get; } = new(false, NarrowPadding, 0, 0, 0);

    /// <summary>The frame's height: 16:9.</summary>
    public double FrameHeight => FrameWidth * 9 / 16;

    /// <summary>A thumbnail's width: twelve across the frame's width with 3 px gaps.</summary>
    public double ThumbWidth => ThumbWidthFor(FrameWidth);

    /// <summary>A thumbnail's height: 3:2.</summary>
    public double ThumbHeight => ThumbWidth * 2 / 3;

    /// <summary>The layout for a drop zone of <paramref name="width"/> by <paramref name="height"/>.</summary>
    public static FirstLaunchLayout Fit(double width, double height)
    {
        var columns = Math.Max(0, width - (2 * WidePadding) - WideGap);
        var text = columns * TextShare / (TextShare + WideIllustrationShare);
        var column = columns - text;

        // The illustration's height is linear in its width: frame + gap + strip + gap + caption.
        var perWidth = (9.0 / 16) + (2.0 / (3 * Daybreak.ThumbCount));
        var fixedHeight = (2 * RowGap) + CaptionHeight - (ThumbGap * (Daybreak.ThumbCount - 1) * 2 / (3 * Daybreak.ThumbCount));
        var byHeight = (height - (2 * VerticalMargin) - fixedHeight) / perWidth;
        var frame = Math.Floor(Math.Min(column, byHeight));

        return text >= MinTextWidth && frame >= MinFrameWidth
            ? new FirstLaunchLayout(true, WidePadding, WideGap, WideIllustrationShare, frame)
            : TextsAlone;
    }

    /// <summary>The text column's width in a drop zone <paramref name="width"/> wide (before the column's own cap).</summary>
    public double TextColumnWidth(double width) =>
        Math.Max(0, width - (2 * SidePadding) - ColumnGap) * TextShare / (TextShare + IllustrationShare);

    /// <summary>The height Daybreak takes at this layout: frame, strip and caption with their gaps.</summary>
    public double IllustrationHeight => ShowsIllustration ? FrameHeight + RowGap + ThumbHeight + RowGap + CaptionHeight : 0;

    private static double ThumbWidthFor(double frameWidth) =>
        Math.Max(0, (frameWidth - (ThumbGap * (Daybreak.ThumbCount - 1))) / Daybreak.ThumbCount);
}
