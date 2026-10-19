using TenLapse.Core.Projects;

namespace TenLapse.Presentation.Services;

/// <summary>
/// What the import needs to know about, and do for, the Windows photo extensions from the Microsoft Store (#14):
/// whether this PC has one, and opening its Store page. The App implements it with Media's <c>PhotoCodecs</c> and
/// <c>Launcher.LaunchUriAsync</c>; tests use a fake. The app makes no network request: opening the page only hands
/// a <c>ms-windows-store://</c> address to the Store app.
/// </summary>
public interface ICodecExtensions
{
    /// <summary>
    /// Forgets what was learned about the installed decoders, so the next answer comes from Windows again. Called at
    /// the start of every import: a "not installed" answer is never kept, so an extension installed meanwhile counts.
    /// </summary>
    void Refresh();

    /// <summary>True when this PC has what <paramref name="extension"/> adds, asked of Windows now; true when that cannot be told.</summary>
    bool IsInstalled(PhotoCodecExtension extension);

    /// <summary>
    /// Opens the Store page of <paramref name="extension"/> (<c>ms-windows-store://pdp/?ProductId=…</c>). False when
    /// Windows had nothing to open it with; a failure may also throw.
    /// </summary>
    Task<bool> OpenStorePageAsync(PhotoCodecExtension extension);
}
