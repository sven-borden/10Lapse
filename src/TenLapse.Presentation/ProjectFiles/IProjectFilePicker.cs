namespace TenLapse.Presentation.ProjectFiles;

/// <summary>The Open and Save dialogs for <c>.10lapse</c> files (#52). The App implements them with the Windows App SDK's pickers.</summary>
public interface IProjectFilePicker
{
    /// <summary>Asks for a project file to open, starting in <paramref name="startFolder"/> (Documents when null or gone). Null when cancelled.</summary>
    Task<string?> PickOpenAsync(string? startFolder);

    /// <summary>
    /// Asks where to save the project, suggesting <paramref name="suggestedName"/> (without the extension) in
    /// <paramref name="startFolder"/> (Documents when null or gone). Null when cancelled.
    /// </summary>
    Task<string?> PickSaveAsync(string suggestedName, string? startFolder);
}
