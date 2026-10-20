using System.Globalization;
using TenLapse.Core.Projects;

namespace TenLapse.Presentation.Services;

/// <summary>
/// Turns the photos an import skipped into one InfoBar (#13, raised by #12): "3 photos couldn't be read and were
/// skipped." with one line per file behind <c>View list</c>, never one InfoBar per file.
/// </summary>
public static class SkippedFilesInfoBar
{
    /// <summary>
    /// The Caution InfoBar for <paramref name="skipped"/>, with a detail line per file in the order given: the file
    /// name, and <see cref="SkippedFileText.Reason"/> beside it. Null when nothing was skipped.
    /// </summary>
    public static InfoBarRequest? Create(IEnumerable<SkippedFile> skipped)
    {
        ArgumentNullException.ThrowIfNull(skipped);
        var details = skipped
            .Select(file => file is null
                ? throw new ArgumentException("A skipped file cannot be null.", nameof(skipped))
                : new InfoBarDetail(System.IO.Path.GetFileName(file.Path) is { Length: > 0 } name ? name : file.Path, SkippedFileText.Reason(file.Reason)))
            .ToArray();
        if (details.Length == 0)
        {
            return null;
        }

        var message = SkippedFileText.Skipped.Format(CultureInfo.CurrentCulture, details.Length, details.Length.ToString("N0", CultureInfo.CurrentCulture));
        return new InfoBarRequest(InfoBarSeverity.Caution, message, details: details);
    }
}
