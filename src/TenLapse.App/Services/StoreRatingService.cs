using TenLapse.Core.Diagnostics;
using TenLapse.Presentation.Rating;
using Windows.Services.Store;

namespace TenLapse.Services;

/// <summary>
/// <see cref="IRatingService"/> over the Store's own rating dialog (#31). It needs the package identity, so unpackaged (Debug)
/// it does nothing and says so once in the log. The Store context is bound to the main window's handle, as the Store asks
/// of a desktop app. The Store decides whether to show anything (it may stay quiet when the user has rated already);
/// the app sends nothing itself.
/// </summary>
/// <param name="windowHandle">The main window's HWND.</param>
/// <param name="log">Takes what the Store answered.</param>
internal sealed class StoreRatingService(Func<nint> windowHandle, ILog log) : IRatingService
{
    private bool _noted;

    /// <inheritdoc />
    public async Task<bool> RequestAsync()
    {
        if (!AppEnvironment.HasPackageIdentity)
        {
            if (!_noted)
            {
                _noted = true;
                log.Info(RatingText.UnpackagedNoOp);
            }

            return false;
        }

        var context = StoreContext.GetDefault();
        WinRT.Interop.InitializeWithWindow.Initialize(context, windowHandle());
        var result = await context.RequestRateAndReviewAppAsync();
        switch (result.Status)
        {
            case StoreRateAndReviewStatus.Succeeded:
            case StoreRateAndReviewStatus.CanceledByUser:
                return true;
            default:
                // NetworkError: the Store could not reach its service. Not shown, not recorded: the next success tries again.
                log.Warning($"The Store did not show its rating dialog: {result.Status}, {result.ExtendedError?.Message}");
                return false;
        }
    }
}
