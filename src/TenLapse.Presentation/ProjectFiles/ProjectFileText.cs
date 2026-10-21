using System.Globalization;
using TenLapse.Core.Text;
using TenLapse.Presentation.Services;

namespace TenLapse.Presentation.ProjectFiles;

/// <summary>
/// Every word of saving and opening projects (#52): the File menu, the questions, the InfoBars, the recent list on the
/// first-launch screen and the recovery offer, from <c>Strings/ProjectFileText.resx</c>; the log lines and error contexts
/// stay here in English.
/// </summary>
public static class ProjectFileText
{
    private static readonly TextResources _strings = new("TenLapse.Presentation.Strings.ProjectFileText", typeof(ProjectFileText).Assembly);

    // ---- The File menu

    /// <summary>The command bar's File menu button, and its accessible name.</summary>
    public static string FileMenu => _strings.Get();

    /// <summary>The File menu's Open item: opens a project file.</summary>
    public static string Open => _strings.Get();

    /// <summary>The File menu's Save item.</summary>
    public static string Save => _strings.Get();

    /// <summary>The File menu's Save as item.</summary>
    public static string SaveAs => _strings.Get();

    /// <summary>The name of the project file type in the Open and Save dialogs.</summary>
    public static string FileTypeName => _strings.Get();

    // ---- Opening

    /// <summary>The Critical InfoBar when a file is not a project. {0} the file's name.</summary>
    public static string OpenNotAProjectFormat => _strings.Get();

    /// <summary>The Critical InfoBar when a file cannot be read. {0} the file's name.</summary>
    public static string OpenUnreadableFormat => _strings.Get();

    /// <summary>The Caution InfoBar after opening a file from a newer 10Lapse. {0} the file's name.</summary>
    public static string OpenNewerVersionFormat => _strings.Get();

    /// <summary>The Caution InfoBar after opening a file with settings this build could not read. {0} the file's name.</summary>
    public static string OpenSomeSettingsFormat => _strings.Get();

    /// <summary>The Caution InfoBar listing the photos an opened project could not find; they stay in its file. {0} the count.</summary>
    public static PluralText MissingPhotos => _strings.Plural();

    /// <summary>The Caution InfoBar when none of a project's photos were found, so it did not open. {0} the count, {1} the project's name.</summary>
    public static PluralText NoPhotosFound => _strings.Plural();

    /// <summary>The Caution InfoBar after opening a project whose photos are online-only in OneDrive. {0} the count, {1} their size.</summary>
    public static PluralText CloudOnlyPhotos => _strings.Plural();

    // ---- Saving

    /// <summary>The Critical InfoBar when a save fails. {0} the file's name.</summary>
    public static string SaveFailedFormat => _strings.Get();

    /// <summary>The Critical InfoBar when the project file's folder is gone. {0} the file's name.</summary>
    public static string SaveFolderGoneFormat => _strings.Get();

    /// <summary>The Caution InfoBar when Save asks where because a newer 10Lapse wrote the file. {0} the file's name.</summary>
    public static string NewerSaveAsFormat => _strings.Get();

    /// <summary>The question before unsaved changes would be lost (Open, Close). {0} the project's name.</summary>
    public static string UnsavedTitleFormat => _strings.Get();

    /// <summary>The question's explanation.</summary>
    public static string UnsavedMessage => _strings.Get();

    /// <summary>The question's button that saves first (the default: it loses nothing).</summary>
    public static string UnsavedSave => _strings.Get();

    /// <summary>The question's button that goes on without saving.</summary>
    public static string UnsavedDontSave => _strings.Get();

    // ---- Recovery

    /// <summary>The InfoBar that offers to restore a project 10Lapse could not save before it ended. {0} the project's name.</summary>
    public static string RecoveryOfferFormat => _strings.Get();

    /// <summary>The recovery InfoBar's action.</summary>
    public static string RecoveryRestore => _strings.Get();

    // ---- The recent list (first-launch screen)

    /// <summary>The heading over the recent projects.</summary>
    public static string RecentHeading => _strings.Get();

    /// <summary>The text button that empties the recent list.</summary>
    public static string RecentClear => _strings.Get();

    /// <summary>The screen reader name of <see cref="RecentClear"/>.</summary>
    public static string RecentClearName => _strings.Get();

    /// <summary>The note beside a recent project whose file is not there.</summary>
    public static string RecentNotFound => _strings.Get();

    /// <summary>A recent project's screen reader name. {0} the project's name, {1} its folder.</summary>
    public static string RecentItemNameFormat => _strings.Get();

    /// <summary>A missing recent project's screen reader name. {0} the project's name, {1} <see cref="RecentNotFound"/>.</summary>
    public static string RecentItemMissingNameFormat => _strings.Get();

    /// <summary>The Caution InfoBar when a recent project's file is gone. {0} the project's name.</summary>
    public static string RecentGoneFormat => _strings.Get();

    // ---- Filled sentences

    /// <summary>"“Harbor.10lapse” isn't a 10Lapse project, or it's damaged…"</summary>
    public static string OpenNotAProject(string path) => Format(OpenNotAProjectFormat, FileName(path));

    /// <summary>"“Harbor.10lapse” couldn't be opened…"</summary>
    public static string OpenUnreadable(string path) => Format(OpenUnreadableFormat, FileName(path));

    /// <summary>"“Harbor.10lapse” was saved by a newer version of 10Lapse…"</summary>
    public static string OpenNewerVersion(string path) => Format(OpenNewerVersionFormat, FileName(path));

    /// <summary>"Some settings in “Harbor.10lapse” couldn't be read…"</summary>
    public static string OpenSomeSettings(string path) => Format(OpenSomeSettingsFormat, FileName(path));

    /// <summary>"3 photos of this project weren't found…"</summary>
    public static string Missing(int count) =>
        MissingPhotos.Format(CultureInfo.CurrentCulture, count, count.ToString("N0", CultureInfo.CurrentCulture));

    /// <summary>"None of the 1,240 photos of “Alps” were found, so the project wasn't opened…"</summary>
    public static string NoneFound(int count, string name) =>
        NoPhotosFound.Format(CultureInfo.CurrentCulture, count, count.ToString("N0", CultureInfo.CurrentCulture), name);

    /// <summary>"40 photos of this project are only in OneDrive, about 2.4 GB in all…" (at least 1 MB, as the import's question says).</summary>
    public static string CloudOnly(int count, long bytes) =>
        CloudOnlyPhotos.Format(CultureInfo.CurrentCulture, count, count.ToString("N0", CultureInfo.CurrentCulture), OutputText.Size(Math.Max(bytes, 1L << 20)));

    /// <summary>"“Harbor.10lapse” couldn't be saved…"</summary>
    public static string SaveFailed(string path) => Format(SaveFailedFormat, FileName(path));

    /// <summary>"“Harbor.10lapse” couldn't be saved because its folder isn't there any more…"</summary>
    public static string SaveFolderGone(string path) => Format(SaveFolderGoneFormat, FileName(path));

    /// <summary>"“Harbor.10lapse” was saved by a newer version of 10Lapse. Saving over it would drop…"</summary>
    public static string NewerSaveAs(string path) => Format(NewerSaveAsFormat, FileName(path));

    /// <summary>The save-first question for the project named <paramref name="name"/> (Primary saves, Secondary does not).</summary>
    public static Choice UnsavedQuestion(string name) =>
        new(Format(UnsavedTitleFormat, name), UnsavedMessage, UnsavedSave, UnsavedDontSave, ProjectText.Cancel);

    /// <summary>"10Lapse closed before your changes to “Harbor sunset” were saved…"</summary>
    public static string RecoveryOffer(string name) => Format(RecoveryOfferFormat, name);

    /// <summary>A recent project's screen reader name.</summary>
    public static string RecentItemName(string name, string folder, bool missing) =>
        missing ? Format(RecentItemMissingNameFormat, name, RecentNotFound) : Format(RecentItemNameFormat, name, folder);

    /// <summary>"“Harbor” isn't where it was saved any more…"</summary>
    public static string RecentGone(string name) => Format(RecentGoneFormat, name);

    // ---- Log lines and error contexts (English, for the log)

    /// <summary>Error context: Open (the command, the picker, or reading the file) failed.</summary>
    public static string OpenFailedContext => "Opening a project failed.";

    /// <summary>Error context: Save or Save as failed outside the write itself.</summary>
    public static string SaveFailedContext => "Saving the project failed.";

    /// <summary>Error context: writing the project file failed (the user sees <see cref="SaveFailed"/>).</summary>
    public static string SaveWriteFailedContext => "Writing the project file failed.";

    /// <summary>Error context: reading a project file failed (the user sees <see cref="OpenUnreadable"/>).</summary>
    public static string OpenReadFailedContext => "Reading the project file failed.";

    /// <summary>Error context: opening the file 10Lapse was started with (a double click, the command line) failed.</summary>
    public static string OpenAtLaunchFailedContext => "Opening the project 10Lapse was started with failed.";

    /// <summary>Error context: a recent project failed to open.</summary>
    public static string RecentOpenFailedContext => "Opening a recent project failed.";

    /// <summary>Error context: clearing the recent list failed.</summary>
    public static string RecentClearFailedContext => "Clearing the recent projects failed.";

    /// <summary>Error context: checking which recent projects are still there failed.</summary>
    public static string RecentRefreshFailedContext => "Checking the recent projects failed.";

    /// <summary>Error context: the question before closing with unsaved changes failed.</summary>
    public static string CloseQuestionFailedContext => "Asking about unsaved changes before closing failed.";

    /// <summary>Error context: reading the size and capture time of an opened project's photos again failed.</summary>
    public static string DetailsReadFailedContext => "Reading the details of an opened project's photos failed.";

    /// <summary>Error context: writing the recovery copy failed (the first time it is shown, then logged).</summary>
    public static string AutosaveFailedContext => "Writing the recovery copy of the project failed.";

    /// <summary>Error context: looking for a recovery copy at launch failed.</summary>
    public static string RecoveryLookFailedContext => "Looking for a recovery copy failed.";

    /// <summary>Error context: restoring a recovery copy failed.</summary>
    public static string RecoveryRestoreFailedContext => "Restoring the recovery copy failed.";

    /// <summary>Log line: a recovery copy was found but is not a project; it was deleted.</summary>
    public static string RecoveryUnreadable(IReadOnlyList<string> problems) => "A recovery copy was left behind but could not be read, so it is not offered: " + string.Join(" ", problems);

    /// <summary>Log line: a recovery copy was found and offered.</summary>
    public static string RecoveryOffered(int frames, DateTime savedUtc) =>
        string.Create(CultureInfo.InvariantCulture, $"Offering to restore a recovery copy with {frames} frames, written {savedUtc:u}.");

    /// <summary>Log line: a project was opened, with what was found and what was not.</summary>
    public static string Opened(int frames, int missing, int changed, int besideProject, int version, IReadOnlyList<string> problems) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"Opened a version {version} project file: {frames} frames, {missing} missing, {changed} changed since the save, {besideProject} found beside the project file.")
        + (problems.Count > 0 ? " Problems: " + string.Join(" | ", problems) : string.Empty);

    /// <summary>Log line: a project file could not be read as a project.</summary>
    public static string NotAProjectLog(IReadOnlyList<string> problems) => "The file is not a usable project: " + string.Join(" ", problems);

    /// <summary>Log line: a project was saved.</summary>
    public static string Saved(int frames, double milliseconds) =>
        string.Create(CultureInfo.InvariantCulture, $"Saved the project file: {frames} frames in {milliseconds:0} ms.");

    private static string FileName(string path) => Path.GetFileName(path) is { Length: > 0 } name ? name : path;

    private static string Format(string format, params object[] values) => string.Format(CultureInfo.CurrentCulture, format, values);
}
