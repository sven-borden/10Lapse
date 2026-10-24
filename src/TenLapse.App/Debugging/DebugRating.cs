using TenLapse.Core.Diagnostics;
using TenLapse.Presentation.Rating;

namespace TenLapse.Debugging;

/// <summary>
/// Debug-only switch for the rating question (#31, #139; this folder is not built in Release): <c>--fake-rating</c>
/// replaces the Store's rating dialog with a stand-in that only writes to the log that it would show it now, so the
/// flow (the card in the second successful export's finished state, once per version, Yes) can be checked unpackaged
/// without the Store. Without it, Yes in an unpackaged run opens the Store's review page (the dialog needs package
/// identity), so a check by UI Automation passes this switch. The count and the version asked in are recorded as for a
/// real run, in the settings folder given by <c>--settings-folder</c>.
/// </summary>
/// <remarks>
/// To see the card after one export instead of two, start with a fresh settings folder whose <c>settings.json</c> is
/// <c>{"version": 1, "successfulExports": 1}</c>, and export once with <c>--export-to=PATH --sample-frames=N</c> (DebugExport).
/// A Debug build's version is <c>1.0.0+&lt;commit&gt;</c>, so each new commit may ask once more.
/// </remarks>
internal sealed class DebugRating(ILog log) : IRatingService
{
    private static readonly string[] _args = Environment.GetCommandLineArgs();

    /// <summary>True with <c>--fake-rating</c>.</summary>
    public static bool Requested => _args.Contains("--fake-rating", StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public Task<bool> RequestAsync()
    {
        log.Info(RatingText.FakeAsked);
        return Task.FromResult(true);
    }
}
