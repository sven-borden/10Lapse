using TenLapse.Core.Projects;

namespace TenLapse.Presentation.ProjectFiles;

/// <summary>
/// Reads the pixel size and capture time of an opened project's photos again (#52), through the import's reading phase
/// (<see cref="ImportViewModel"/> implements it): a photo the file knew nothing about, or one changed since the save.
/// </summary>
public interface IFrameDetailsReader
{
    /// <summary>
    /// Reads <paramref name="frames"/> (frames of <paramref name="project"/>) in the background and writes what it finds
    /// back, in place: the order stays the user's, and a photo that cannot be read stays in the project (the preview says
    /// so when it draws it). A project that was clean stays clean: the details are the photo's, not the user's work. Stops
    /// when the project is replaced. On the UI thread; completes when the reading is done, never throws for a photo.
    /// </summary>
    Task ReadDetailsAsync(ProjectViewModel project, IReadOnlyList<Frame> frames);
}
