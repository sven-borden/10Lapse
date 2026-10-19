using TenLapse.Presentation.Services;
using Windows.System;

namespace TenLapse.Services;

/// <summary><see cref="ILauncher"/> over <c>Launcher.LaunchUriAsync</c>: the Store or the browser opens, not 10Lapse.</summary>
internal sealed class LauncherService : ILauncher
{
    /// <inheritdoc />
    public async Task<bool> LaunchUriAsync(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return await Launcher.LaunchUriAsync(uri);
    }
}
