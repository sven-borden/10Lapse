namespace TenLapse.Presentation.Services;

/// <summary>
/// Hands a link to the app Windows picks for it (the Store, the browser). The App implements it with
/// <c>Launcher.LaunchUriAsync</c>; tests use a fake. 10Lapse makes no network request itself: the other app does.
/// </summary>
public interface ILauncher
{
    /// <summary>Opens <paramref name="uri"/>. Returns false when Windows found no app to open it or the launch was refused; throws when the launch itself fails.</summary>
    Task<bool> LaunchUriAsync(Uri uri);
}
