using TenLapse.Core.Projects;
using TenLapse.Media.Decoding;
using TenLapse.Presentation.Services;

namespace TenLapse.Services;

/// <summary>
/// Windows' answers about the photo extensions from the Microsoft Store (#14): what is installed comes from
/// Media's <see cref="PhotoCodecs"/> (asked of Windows each time, never remembered), and the Store page opens with
/// <c>Launcher.LaunchUriAsync</c>. The address is <c>ms-windows-store://pdp/?ProductId=…</c>: it only hands the product
/// to the Store app, so 10Lapse makes no network request and opens no browser.
/// </summary>
internal sealed class WindowsCodecExtensions : ICodecExtensions
{
    /// <inheritdoc />
    public void Refresh() => PhotoCodecs.Refresh();

    /// <inheritdoc />
    public bool IsInstalled(PhotoCodecExtension extension) => PhotoCodecs.IsInstalled(extension);

    /// <inheritdoc />
    public async Task<bool> OpenStorePageAsync(PhotoCodecExtension extension)
    {
        ArgumentNullException.ThrowIfNull(extension);
        return await Windows.System.Launcher.LaunchUriAsync(StorePage(extension));
    }

    /// <summary>The address that opens the extension's page in the Store app.</summary>
    internal static Uri StorePage(PhotoCodecExtension extension) =>
        new("ms-windows-store://pdp/?ProductId=" + Uri.EscapeDataString(extension.StoreProductId));
}
