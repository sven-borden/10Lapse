using System.Diagnostics;
using TenLapse.Presentation.Export;

namespace TenLapse.Services;

/// <summary>Show in folder (#24): File Explorer opens the video's folder with the video selected (<c>explorer.exe /select,</c>).</summary>
internal sealed class ExplorerFileReveal : IFileReveal
{
    /// <inheritdoc />
    public void ShowInFolder(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        using var process = Process.Start(new ProcessStartInfo(explorer, $"/select,\"{Path.GetFullPath(path)}\"") { UseShellExecute = false });
    }
}
