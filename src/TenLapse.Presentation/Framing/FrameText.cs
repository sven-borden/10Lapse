using System.Globalization;
using TenLapse.Core.Projects;
using TenLapse.Core.Text;

namespace TenLapse.Presentation;

/// <summary>
/// Every user-visible word of the Frame panel (#18), as whole format strings read from <c>Strings/FrameText.resx</c>
/// (#39); the log contexts stay here in English. The sizes are plain integers,
/// as in the Output panel.
/// </summary>
public static class FrameText
{
    private static readonly TextResources _strings = new("TenLapse.Presentation.Strings.FrameText", typeof(FrameText).Assembly);

    /// <summary>The section's heading.</summary>
    public static string Heading => _strings.Get();

    /// <summary>Name of the segmented control that picks the shape (the prototype calls it Shape).</summary>
    public static string ShapeName => _strings.Get();

    /// <summary>Name of the segmented control that picks Fit or Fill.</summary>
    public static string FitName => _strings.Get();

    /// <summary>The first shape, as the segment shows it: the shape of the photos themselves.</summary>
    public static string ShapeMatch => _strings.Get();

    /// <summary>The first shape, as a screen reader names it.</summary>
    public static string ShapeMatchName => _strings.Get();

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
    public static string FitOption => _strings.Get();

    /// <summary>The second Fit or Fill choice: the frame is filled and the edges are cut.</summary>
    public static string FillOption => _strings.Get();

    /// <summary>The line under the controls when Fit is chosen. {0}: <see cref="FitOption"/>.</summary>
    public static string FitHint => _strings.Get();

    /// <summary>The line under the controls when Fill is chosen. {0}: <see cref="FillOption"/>.</summary>
    public static string FillHint => _strings.Get();

    /// <summary>The Fit or Fill option's label, as its segment shows it.</summary>
    public static string FitLabel(FitMode fit) => fit == FitMode.Fill ? FillOption : FitOption;

    /// <summary>The line under the controls for <paramref name="fit"/>, with its option's label: "Fit shows the whole photo…".</summary>
    public static string Hint(FitMode fit) =>
        Format(CultureInfo.CurrentCulture, fit == FitMode.Fill ? FillHint : FitHint, FitLabel(fit));

    /// <summary>What a screen reader says after a change: "Frame: 16:9, Fit." (the heading, the shape, the option).</summary>
    public static string Announce(FrameAspect aspect, FitMode fit) =>
        Format(CultureInfo.CurrentCulture, Announcement, Heading, ShapeFullName(aspect), FitLabel(fit));

    /// <summary>Match photos is chosen and the photos' sizes are known. {0}: the width, {1}: the height of the most common photo.</summary>
    public static string MatchingPhotos => _strings.Get();

    /// <summary>Match photos is chosen but no photo size is known yet (no photos, or none read), so the video is 16:9.</summary>
    public static string MatchingNoPhotos => _strings.Get();

    /// <summary>Match photos gives a shape so wide or tall that no size can be exported on this PC; says what to do instead.</summary>
    public static string NoSize => _strings.Get();

    /// <summary>The quiet line when most photos are portrait and the shape is landscape.</summary>
    public static string PortraitHint => _strings.Get();

    /// <summary>The button that applies the suggestion.</summary>
    public static string PortraitApply => _strings.Get();

    /// <summary>The button that closes the suggestion for the project.</summary>
    public static string PortraitDismiss => _strings.Get();

    /// <summary>Said to a screen reader after a change. {0}: <see cref="Heading"/>, {1}: the shape's name, {2}: Fit or Fill.</summary>
    public static string Announcement => _strings.Get();

    /// <summary>Log context: a frame setting could not be written to the project.</summary>
    public static string ChangeFailed => "Changing the frame settings failed.";

    /// <summary>Log context: the panel could not follow a change of the project.</summary>
    public static string RefreshFailed => "The frame panel could not update.";

    /// <summary>Fills a format string in <paramref name="culture"/>.</summary>
    internal static string Format(CultureInfo culture, string format, params object[] values) =>
        string.Format(culture, format, values);
}
