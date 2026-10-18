using System.Globalization;
using TenLapse.Core;
using TenLapse.Core.Projects;
using TenLapse.Core.Timing;
using TenLapse.Presentation.Services;

namespace TenLapse.Presentation;

/// <summary>
/// Every user-visible string the view models build, in one place so #39 can move the words to resource
/// files. English for now; numbers use the invariant culture, as <see cref="TimingFormat"/> does.
/// </summary>
public static class ProjectText
{
    /// <summary>The arrow between the photo count and the length.</summary>
    public static string Arrow => "→";

    /// <summary>The unsaved-changes marker before the name in the window title.</summary>
    public static string UnsavedMarker => "●";

    /// <summary>"1 photo", "742 photos", "5,000 photos".</summary>
    public static string Photos(int count) => count == 1
        ? "1 photo"
        : count.ToString("N0", CultureInfo.InvariantCulture) + " photos";

    /// <summary>
    /// The Timing readout, e.g. "742 photos → 24.7 s at 30 fps". In <see cref="SpeedMode.PerPhoto"/> the rate
    /// reads per photo ("40 photos → 20.0 s at 0.5 s per photo"); otherwise as <see cref="TimingFormat.FrameRate"/>.
    /// </summary>
    public static string TimingReadout(int frameCount, TimingSettings timing)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(frameCount);
        ArgumentNullException.ThrowIfNull(timing);

        var length = TimingFormat.Duration(FrameTiming.Duration(frameCount, timing.Rate));
        var rate = timing.Mode == SpeedMode.PerPhoto
            ? TimingFormat.SecondsPerPhoto(timing.Rate)
            : TimingFormat.FrameRate(timing.Rate);
        return $"{Photos(frameCount)} {Arrow} {length} at {rate}";
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

        var title = $"{projectName} – {Product.Name}";
        return hasUnsavedChanges ? $"{UnsavedMarker} {title}" : title;
    }

    /// <summary>The question <c>New project</c> asks when there is work to lose.</summary>
    public static Confirmation NewProjectConfirmation(int frameCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(frameCount);

        var message = frameCount switch
        {
            0 => "This project's settings will be cleared.",
            1 => "This project's photo and settings will be cleared. The photo file stays on your PC.",
            _ => $"This project's {Photos(frameCount)} and settings will be cleared. The photo files stay on your PC.",
        };
        return new Confirmation("Start a new project?", message, "Start new project", "Cancel");
    }
}
