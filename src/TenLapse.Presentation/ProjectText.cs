using System.Globalization;
using TenLapse.Core;
using TenLapse.Core.Projects;
using TenLapse.Core.Timing;
using TenLapse.Presentation.Services;

namespace TenLapse.Presentation;

/// <summary>
/// Every user-visible string the view models build, in one place so #39 can move them to resource files.
/// Each sentence is one whole format string with numbered placeholders, never pieces glued together, so a
/// translation can reorder it; the singular and the plural are separate strings, chosen by count. The
/// timing units ("s", "fps", "per photo") stay in Core's <see cref="TimingWords"/>. English for now; numbers
/// use the invariant culture, as <see cref="TimingFormat"/> does.
/// </summary>
public static class ProjectText
{
    /// <summary>The readout for one photo. {0} the length ("0.5 s"), {1} the rate ("30 fps").</summary>
    public static string TimingReadoutOne => "1 photo → {0} at {1}";

    /// <summary>The readout for 0 or several photos. {0} the count ("742"), {1} the length, {2} the rate.</summary>
    public static string TimingReadoutMany => "{0} photos → {1} at {2}";

    /// <summary>The window title of a project with nothing unsaved. {0} the project name, {1} the product name.</summary>
    public static string WindowTitleSaved => "{0} – {1}";

    /// <summary>The window title while there are unsaved changes. {0} the project name, {1} the product name.</summary>
    public static string WindowTitleUnsaved => "● {0} – {1}";

    /// <summary>The title of the <c>New project</c> question.</summary>
    public static string NewProjectTitle => "Start a new project?";

    /// <summary>The <c>New project</c> question for a project with settings but no photos.</summary>
    public static string NewProjectMessageNoPhotos => "This project's settings will be cleared.";

    /// <summary>The <c>New project</c> question for one photo.</summary>
    public static string NewProjectMessageOne => "This project's photo and settings will be cleared. The photo file stays on your PC.";

    /// <summary>The <c>New project</c> question for several photos. {0} the count ("742").</summary>
    public static string NewProjectMessageMany => "This project's {0} photos and settings will be cleared. The photo files stay on your PC.";

    /// <summary>The button that goes ahead with <c>New project</c>.</summary>
    public static string NewProjectConfirm => "Start new project";

    /// <summary>The button that keeps everything as it is.</summary>
    public static string Cancel => "Cancel";

    /// <summary>Error context: a resource of the replaced project threw while it was disposed.</summary>
    public static string ClosingProjectFailed => "Part of the previous project could not be closed.";

    /// <summary>Error context: a view's <c>ProjectReplaced</c> handler threw.</summary>
    public static string SwitchingViewFailed => "A view could not switch to the new project.";

    /// <summary>
    /// The Critical InfoBar shown when an InfoBar's action failed, and the error context it is reported with.
    /// </summary>
    public static string InfoBarActionFailed => "That couldn't be done because of an unexpected problem. Try again, and restart 10Lapse if it keeps happening.";

    /// <summary>The InfoBar button that shows its detail list (the prototype's "View list").</summary>
    public static string InfoBarShowDetails => "View list";

    /// <summary>The same button while the list shows.</summary>
    public static string InfoBarHideDetails => "Hide list";

    /// <summary>The accessible name of an InfoBar's close button.</summary>
    public static string InfoBarDismiss => "Dismiss";

    /// <summary>
    /// The Timing readout, e.g. "742 photos → 24.7 s at 30 fps", at the effective rate for this count
    /// (<see cref="TimingSettings.EffectiveRate"/>: in Length mode a clamped rate gives the length the clip
    /// really has). In <see cref="SpeedMode.PerPhoto"/> the rate reads per photo ("40 photos → 20.0 s at
    /// 0.5 s per photo"); otherwise as <see cref="TimingFormat.FrameRate"/>.
    /// </summary>
    public static string TimingReadout(int frameCount, TimingSettings timing)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(frameCount);
        ArgumentNullException.ThrowIfNull(timing);

        var effective = timing.EffectiveRate(frameCount).Rate;
        var length = TimingFormat.Duration(FrameTiming.Duration(frameCount, effective));
        var rate = timing.Mode == SpeedMode.PerPhoto
            ? TimingFormat.SecondsPerPhoto(effective)
            : TimingFormat.FrameRate(effective);
        return frameCount == 1
            ? Format(TimingReadoutOne, length, rate)
            : Format(TimingReadoutMany, Count(frameCount), length, rate);
    }

    /// <summary>
    /// The window title: "10Lapse" for a fresh project; "Harbor sunset – 10Lapse" once it has photos;
    /// "● Harbor sunset – 10Lapse" while it has unsaved changes.
    /// </summary>
    public static string WindowTitle(string projectName, bool hasFrames, bool hasUnsavedChanges)
    {
        if (!hasFrames && !hasUnsavedChanges)
        {
            return Product.Name;
        }

        return Format(hasUnsavedChanges ? WindowTitleUnsaved : WindowTitleSaved, projectName, Product.Name);
    }

    /// <summary>The question <c>New project</c> asks when there is work to lose.</summary>
    public static Confirmation NewProjectConfirmation(int frameCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(frameCount);

        var message = frameCount switch
        {
            0 => NewProjectMessageNoPhotos,
            1 => NewProjectMessageOne,
            _ => Format(NewProjectMessageMany, Count(frameCount)),
        };
        return new Confirmation(NewProjectTitle, message, NewProjectConfirm, Cancel);
    }

    // "5,000": grouped, invariant until #39 localizes numbers with the words.
    private static string Count(int count) => count.ToString("N0", CultureInfo.InvariantCulture);

    private static string Format(string format, params object[] values) =>
        string.Format(CultureInfo.InvariantCulture, format, values);
}
