using Microsoft.UI;
using Microsoft.Windows.Storage.Pickers;
using TenLapse.Core.Projects;
using TenLapse.Presentation.ProjectFiles;

namespace TenLapse.Services;

/// <summary>
/// The Open and Save dialogs for <c>.10lapse</c> project files (#52), on the Windows App SDK's pickers, which take the
/// window's id and work packaged and unpackaged alike. They open in the project's folder, or the last project's, else in
/// Documents.
/// </summary>
/// <param name="window">The id of the window the dialogs belong to.</param>
internal sealed class ProjectFilePicker(Func<WindowId> window) : IProjectFilePicker
{
    // How long the start folder may take to answer before the dialog opens in Documents instead (an unplugged drive).
    private static readonly TimeSpan _startFolderLimit = TimeSpan.FromMilliseconds(500);

    /// <inheritdoc />
    public async Task<string?> PickOpenAsync(string? startFolder)
    {
        var picker = new FileOpenPicker(window())
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            ViewMode = PickerViewMode.List,
        };
        picker.FileTypeFilter.Add(ProjectFileJson.Extension);
        if (await StartFolderAsync(startFolder) is { } folder)
        {
            picker.SuggestedFolder = folder;
        }

        var result = await picker.PickSingleFileAsync();
        return string.IsNullOrEmpty(result?.Path) ? null : result.Path;
    }

    /// <inheritdoc />
    public async Task<string?> PickSaveAsync(string suggestedName, string? startFolder)
    {
        var picker = new FileSavePicker(window())
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = suggestedName,
            DefaultFileExtension = ProjectFileJson.Extension,
        };
        picker.FileTypeChoices.Add(ProjectFileText.FileTypeName, [ProjectFileJson.Extension]);
        if (await StartFolderAsync(startFolder) is { } folder)
        {
            picker.SuggestedFolder = folder;
        }

        var result = await picker.PickSaveFileAsync();
        return string.IsNullOrEmpty(result?.Path) ? null : result.Path;
    }

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
