using Microsoft.UI;
using Microsoft.Windows.Storage.Pickers;
using TenLapse.Presentation.Export;

namespace TenLapse.Services;

/// <summary>
/// The Save dialog for the video (#24), on the Windows App SDK's <see cref="FileSavePicker"/>, which takes the window's id
/// (no <c>InitializeWithWindow</c>) and works packaged and unpackaged alike. It suggests the project's name with
/// <c>.mp4</c> (or <c>.gif</c>, #51), in the folder of the last export or in Videos.
/// </summary>
/// <param name="window">The id of the window the dialog belongs to.</param>
internal sealed class SaveVideoPicker(Func<WindowId> window) : ISaveVideoPicker
{
    // How long the remembered folder may take to answer before the dialog opens in Videos instead.
    private static readonly TimeSpan _startFolderLimit = TimeSpan.FromMilliseconds(500);

    // Files the dialog created (empty, after it opened) in this session: the only ones Discard may delete.
    private readonly HashSet<string> _created = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public async Task<string?> PickVideoAsync(string suggestedName, string? startFolder)
    {
        // The suggested name carries the format's extension: .mp4, or .gif for an animated GIF (#51).
        var extension = Path.GetExtension(suggestedName) is { Length: > 0 } suggested ? suggested : ExportText.FileExtension(Core.Projects.VideoFormat.Mp4H264);
        var picker = new FileSavePicker(window())
        {
            SuggestedStartLocation = PickerLocationId.VideosLibrary,
            SuggestedFileName = Path.GetFileNameWithoutExtension(suggestedName),
            DefaultFileExtension = extension,
        };
        picker.FileTypeChoices.Add(ExportText.FileTypeNameFor(extension), [extension]);
        if (await StartFolderAsync(startFolder) is { } folder)
        {
            picker.SuggestedFolder = folder;
        }

        var opened = DateTime.UtcNow;
        var result = await picker.PickSaveFileAsync();
        if (string.IsNullOrEmpty(result?.Path))
        {
            return null;
        }

        // Whether the Windows App SDK's picker creates the chosen file is not documented in what ships with the SDK; if it
        // does, the file is empty and newer than the dialog. Remember it, so a cancelled or failed export can remove it.
        if (CreatedEmpty(result.Path, opened))
        {
            _created.Add(Path.GetFullPath(result.Path));
        }

        return result.Path;
    }

    /// <inheritdoc />
    public void Discard(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            if (_created.Remove(full) && new FileInfo(full) is { Exists: true, Length: 0 })
            {
                File.Delete(full);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // Left as it is: an empty file, nothing lost.
        }
    }

    private static bool CreatedEmpty(string path, DateTime opened)
    {
        try
        {
            var file = new FileInfo(path);
            return file is { Exists: true, Length: 0 } && file.CreationTimeUtc >= opened.AddSeconds(-2);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
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
