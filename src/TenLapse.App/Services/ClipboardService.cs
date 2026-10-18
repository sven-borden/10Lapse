using TenLapse.Presentation.Services;
using Windows.ApplicationModel.DataTransfer;

namespace TenLapse.Services;

/// <summary>Puts text on the Windows clipboard. Call it on the UI thread.</summary>
internal sealed class ClipboardService : IClipboardService
{
    /// <inheritdoc />
    public void SetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        package.SetText(text);
        Clipboard.SetContent(package);
    }
}
