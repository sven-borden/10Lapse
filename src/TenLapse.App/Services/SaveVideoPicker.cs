using Microsoft.UI;
using Microsoft.Windows.Storage.Pickers;
using TenLapse.Presentation.Export;

namespace TenLapse.Services;

/// <summary>
/// The Save dialog for the video (#24), on the Windows App SDK's <see cref="FileSavePicker"/>, which takes the window's id
/// (no <c>InitializeWithWindow</c>) and works packaged and unpackaged alike. It suggests the project's name with
/// <c>.mp4</c>, in the folder of the last export or in Videos.
/// </summary>
/// <param name="window">The id of the window the dialog belongs to.</param>
internal sealed class SaveVideoPicker(Func<WindowId> window) : ISaveVideoPicker
{
    // How long the remembered folder may take to answer before the dialog opens in Videos instead.
    private static readonly TimeSpan _startFolderLimit = TimeSpan.FromMilliseconds(500);

    /// <inheritdoc />
    public async Task<string?> PickVideoAsync(string suggestedName, string? startFolder)
    {
        var picker = new FileSavePicker(window())
        {
            SuggestedStartLocation = PickerLocationId.VideosLibrary,
            SuggestedFileName = Path.GetFileNameWithoutExtension(suggestedName),
            DefaultFileExtension = ".mp4",
        };
        picker.FileTypeChoices.Add(ExportText.FileTypeName, [".mp4"]);
        if (await StartFolderAsync(startFolder) is { } folder)
        {
            picker.SuggestedFolder = folder;
        }

        var result = await picker.PickSaveFileAsync();
        return string.IsNullOrEmpty(result?.Path) ? null : result.Path;
    }

    // A remembered folder that is gone (an unplugged drive) would make the dialog fail: Videos it is then. Checked off the UI
    // thread with a short limit, as the import pickers do.
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
