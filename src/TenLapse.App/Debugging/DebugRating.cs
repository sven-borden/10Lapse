using TenLapse.Core.Diagnostics;
using TenLapse.Presentation.Rating;

namespace TenLapse.Debugging;

/// <summary>
/// Debug-only switch for the rating prompt (#31; this folder is not built in Release): <c>--fake-rating</c> replaces the
/// Store with a stand-in that only writes to the log that it would have asked, so the flow (the second successful export,
/// once per version) can be checked unpackaged and without the Store. The count and the version asked in are
/// recorded as for a real ask, in the settings folder given by <c>--settings-folder</c>.
/// </summary>
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
