namespace TenLapse.Presentation;

/// <summary>
/// The shell's verdict on a drag over the window (#12, #29): whether a drop would be taken, which sets the cursor's
/// caption and the accepted operation, and whether the first-launch drop zone shows its drag-over look.
/// </summary>
/// <remarks>
/// The shell counts what a drag holds once, in the background, when it enters. Until that count is done the cursor
/// takes the drag (#12: "Add photos"), but the drop zone's look waits, so a drag that turns out to hold nothing to
/// import (text files, a zip, a video) never flashes amber before it is refused. A folder is known as soon as the
/// drag's items arrive, so a drag the window takes loses almost nothing.
/// </remarks>
/// <param name="Accepts">A drop would be taken: copy, with the count in the caption.</param>
/// <param name="ShowsLook">The drop zone shows its drag-over look: taken, and counted.</param>
/// <param name="SettingsOpen">Refused because Settings is open; the caption says how to add the photos.</param>
public readonly record struct DragVerdict(bool Accepts, bool ShowsLook, bool SettingsOpen)
{
    /// <summary>
    /// The verdict on a drag. <paramref name="holdsFiles"/>: it carries storage items. <paramref name="counted"/>:
    /// the background count has finished (or failed, which leaves the drop to find out). <paramref name="photos"/>:
    /// the photo files counted, null while counting or when folders are among them, 0 when nothing can be imported.
    /// </summary>
    public static DragVerdict Of(bool settingsOpen, bool holdsFiles, bool counted, int? photos)
    {
        if (settingsOpen)
        {
            return new DragVerdict(Accepts: false, ShowsLook: false, SettingsOpen: true);
        }

        return !holdsFiles || photos == 0
            ? new DragVerdict(Accepts: false, ShowsLook: false, SettingsOpen: false)
            : new DragVerdict(Accepts: true, ShowsLook: counted, SettingsOpen: false);
    }
}
