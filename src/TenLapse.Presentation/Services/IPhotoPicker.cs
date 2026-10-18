namespace TenLapse.Presentation.Services;

/// <summary>The Windows pickers behind <c>Choose folder…</c> and <c>Choose photos…</c> (#12). The App implements it; tests use a fake.</summary>
public interface IPhotoPicker
{
    /// <summary>Lets the user choose a folder; null when they cancel.</summary>
    /// <param name="startFolder">The folder to open in, or null for the Pictures library.</param>
    Task<string?> PickFolderAsync(string? startFolder);

    /// <summary>Lets the user choose photos (the formats this PC reads); empty when they cancel.</summary>
    /// <param name="startFolder">The folder to open in, or null for the Pictures library.</param>
    Task<IReadOnlyList<string>> PickPhotosAsync(string? startFolder);
}

/// <summary>
/// Where the pickers open next: the folder the user last chose photos from. <see cref="SessionImportFolders"/> keeps it
/// for the session; #21 keeps it across launches by passing its own implementation (ApplicationData local settings,
/// with a path that works unpackaged) to <see cref="ImportViewModel"/>.
/// </summary>
public interface IImportFolderMemory
{
    /// <summary>The folder the pickers open in, or null for the Pictures library. Read and set on the UI thread.</summary>
    string? LastFolder { get; set; }
}

/// <summary>Remembers the last folder until the app closes.</summary>
public sealed class SessionImportFolders : IImportFolderMemory
{
    /// <inheritdoc />
    public string? LastFolder { get; set; }
}
