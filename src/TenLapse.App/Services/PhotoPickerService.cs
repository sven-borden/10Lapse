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
    /// <inheritdoc />
    public async Task<string?> PickFolderAsync(string? startFolder)
    {
        var picker = new FolderPicker(window())
        {
            SuggestedStartLocation = PickerLocationId.PicturesLibrary,
        };
        if (StartFolder(startFolder) is { } folder)
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
        foreach (var extension in PhotoCodecs.Extensions.Order(StringComparer.Ordinal))
        {
            picker.FileTypeFilter.Add(extension);
        }

        if (StartFolder(startFolder) is { } folder)
        {
            picker.SuggestedFolder = folder;
        }

        var results = await picker.PickMultipleFilesAsync();
        return results is null ? [] : [.. results.Select(result => result.Path).Where(path => !string.IsNullOrEmpty(path))];
    }

    // A remembered folder that is gone (an unplugged card) would make the picker fail; Pictures it is then.
    private static string? StartFolder(string? folder) =>
        !string.IsNullOrEmpty(folder) && Directory.Exists(folder) ? folder : null;
}
