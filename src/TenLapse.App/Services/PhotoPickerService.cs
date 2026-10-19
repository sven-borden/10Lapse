using Microsoft.UI;
using Microsoft.Windows.Storage.Pickers;
using TenLapse.Media.Decoding;
using TenLapse.Presentation.Services;

namespace TenLapse.Services;

/// <summary>
/// <see cref="IPhotoPicker"/> on the Windows App SDK pickers (<c>Microsoft.Windows.Storage.Pickers</c>), which take the
/// window's id instead of <c>InitializeWithWindow</c> and work packaged and unpackaged alike. They open in the Pictures
/// library, or in the folder the import remembered (<see cref="IImportFolderMemory"/>).
/// </summary>
/// <param name="window">The id of the window the pickers belong to.</param>
internal sealed class PhotoPickerService(Func<WindowId> window) : IPhotoPicker
{
    // How long the remembered folder may take to answer before the picker opens in Pictures instead.
    private static readonly TimeSpan _startFolderLimit = TimeSpan.FromMilliseconds(500);

    /// <inheritdoc />
    public async Task<string?> PickFolderAsync(string? startFolder)
    {
        var picker = new FolderPicker(window())
        {
            SuggestedStartLocation = PickerLocationId.PicturesLibrary,
        };
        if (await StartFolderAsync(startFolder) is { } folder)
        {
            picker.SuggestedFolder = folder;
        }

        var result = await picker.PickSingleFolderAsync();
        return string.IsNullOrEmpty(result?.Path) ? null : result.Path;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> PickPhotosAsync(string? startFolder)
    {
        var picker = new FileOpenPicker(window())
        {
            SuggestedStartLocation = PickerLocationId.PicturesLibrary,
            ViewMode = PickerViewMode.Thumbnail,
        };
        // The formats this PC reads and the known ones it needs a Store extension for (they import as "needs an
        // extension", which #14 turns into a one-click fix).
        foreach (var extension in PhotoCodecs.Extensions.Concat(KnownPhotoFormats.All.SelectMany(format => format.Extensions)).Distinct().Order(StringComparer.Ordinal))
        {
            picker.FileTypeFilter.Add(extension);
        }

        if (await StartFolderAsync(startFolder) is { } folder)
        {
            picker.SuggestedFolder = folder;
        }

        var results = await picker.PickMultipleFilesAsync();
        return results is null ? [] : [.. results.Select(result => result.Path).Where(path => !string.IsNullOrEmpty(path))];
    }

    // A remembered folder that is gone (an unplugged card) would make the picker fail; Pictures it is then. Checked off
    // the UI thread with a short limit: a folder on a sleeping network drive or a disconnected share can take seconds to
    // answer, and the picker must open at once. Back on the caller's (UI) thread afterwards, where the picker lives.
    private static async Task<string?> StartFolderAsync(string? folder)
    {
        if (string.IsNullOrEmpty(folder))
        {
            return null;
        }

        try
        {
            return await Task.Run(() => Directory.Exists(folder)).WaitAsync(_startFolderLimit) ? folder : null;
        }
        catch (TimeoutException)
        {
            return null;
        }
    }
}
