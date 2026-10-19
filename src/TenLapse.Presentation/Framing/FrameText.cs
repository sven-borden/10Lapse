using System.Globalization;
using TenLapse.Core.Projects;

namespace TenLapse.Presentation;

/// <summary>
/// Every user-visible word of the Frame panel (#18), as whole format strings for #39. The sizes are plain integers,
/// as in the Output panel.
/// </summary>
public static class FrameText
{
    /// <summary>The section's heading.</summary>
    public static string Heading => "Frame";

    /// <summary>Name of the segmented control that picks the shape (the prototype calls it Shape).</summary>
    public static string ShapeName => "Shape";

    /// <summary>Name of the segmented control that picks Fit or Fill.</summary>
    public static string FitName => "Fit";

    /// <summary>The first shape, as the segment shows it: the shape of the photos themselves.</summary>
    public static string ShapeMatch => "Match";

    /// <summary>The first shape, as a screen reader names it.</summary>
    public static string ShapeMatchName => "Match photos";

    /// <summary>The shape's label, as the segment shows it.</summary>
    public static string ShapeLabel(FrameAspect aspect) => aspect switch
    {
        FrameAspect.MatchPhotos => ShapeMatch,
        FrameAspect.Ratio16x9 => "16:9",
        FrameAspect.Ratio9x16 => "9:16",
        FrameAspect.Ratio1x1 => "1:1",
        FrameAspect.Ratio4x5 => "4:5",
        FrameAspect.Ratio4x3 => "4:3",
        _ => throw new ArgumentOutOfRangeException(nameof(aspect), aspect, "Unknown aspect."),
    };

    /// <summary>The shape's name for a screen reader and in sentences: the same as the label, but Match photos in full.</summary>
    public static string ShapeFullName(FrameAspect aspect) => aspect == FrameAspect.MatchPhotos ? ShapeMatchName : ShapeLabel(aspect);

    /// <summary>The first Fit or Fill choice: the whole photo, with bars where the shapes differ.</summary>
    public static string FitOption => "Fit";

    /// <summary>The second Fit or Fill choice: the frame is filled and the edges are cut.</summary>
    public static string FillOption => "Fill";

    /// <summary>The line under the controls when Fit is chosen.</summary>
    public static string FitHint => "Fit shows the whole photo with bars where shapes differ.";

    /// <summary>The line under the controls when Fill is chosen.</summary>
    public static string FillHint => "Fill crops the edges so the frame has no bars.";

    /// <summary>Match photos is chosen and the photos' sizes are known. {0}: the width, {1}: the height of the most common photo.</summary>
    public static string MatchingPhotos => "Matching your photos: {0} × {1}.";

    /// <summary>Match photos is chosen but no photo size is known yet (no photos, or none read), so the video is 16:9.</summary>
    public static string MatchingNoPhotos => "Matching your photos. Until their sizes are known the shape is 16:9.";

    /// <summary>The quiet line when most photos are portrait and the shape is landscape.</summary>
    public static string PortraitHint => "Most of your photos are portrait, so 9:16 suits them better.";

    /// <summary>The button that applies the suggestion.</summary>
    public static string PortraitApply => "Use 9:16";

    /// <summary>The button that closes the suggestion for the project.</summary>
    public static string PortraitDismiss => "Dismiss";

    /// <summary>Said to a screen reader after a change. {0}: the shape's name, {1}: Fit or Fill.</summary>
    public static string Announcement => "Frame: {0}, {1}.";

    /// <summary>Log context: a frame setting could not be written to the project.</summary>
    public static string ChangeFailed => "Changing the frame settings failed.";

    /// <summary>Log context: the panel could not follow a change of the project.</summary>
    public static string RefreshFailed => "The frame panel could not update.";

    /// <summary>Fills a format string in <paramref name="culture"/>.</summary>
    internal static string Format(CultureInfo culture, string format, params object[] values) =>
        string.Format(culture, format, values);
}
