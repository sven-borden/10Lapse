using Microsoft.UI;
using Microsoft.Windows.Storage.Pickers;
using TenLapse.Core.Projects;
using TenLapse.Media.Audio;
using TenLapse.Presentation.Music;

namespace TenLapse.Services;

/// <summary>
/// The Music section's files (#50): the Open dialog for a song, on the Windows App SDK's picker (packaged and unpackaged
/// alike), and Media's reading of the chosen file, off the UI thread.
/// </summary>
/// <param name="window">The id of the window the dialog belongs to.</param>
internal sealed class MusicFiles(Func<WindowId> window) : IMusicFiles
{
    // How long the start folder may take to answer before the dialog opens in Music instead (an unplugged drive).
    private static readonly TimeSpan _startFolderLimit = TimeSpan.FromMilliseconds(500);

    /// <inheritdoc />
    public async Task<string?> PickAsync(string? startFolder)
    {
        var picker = new FileOpenPicker(window())
        {
            SuggestedStartLocation = PickerLocationId.MusicLibrary,
            ViewMode = PickerViewMode.List,
        };
        foreach (var extension in MusicFile.Extensions)
        {
            picker.FileTypeFilter.Add(extension);
        }

        if (await StartFolderAsync(startFolder) is { } folder)
        {
            picker.SuggestedFolder = folder;
        }

        var result = await picker.PickSingleFileAsync();
        return string.IsNullOrEmpty(result?.Path) ? null : result.Path;
    }

    /// <inheritdoc />
    public Task<MusicFileInfo> ProbeAsync(string path) => Task.Run(() => MusicFile.Read(path));

    /// <inheritdoc />
    public bool Exists(string path)
    {
        try
        {
            return File.Exists(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
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
