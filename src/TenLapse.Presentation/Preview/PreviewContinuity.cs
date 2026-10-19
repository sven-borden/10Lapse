using TenLapse.Core.Projects;

namespace TenLapse.Presentation.Preview;

/// <summary>
/// Whether what the preview showed last still belongs to the project it draws now (#35). The render thread keeps the last
/// picture while the next one loads, and a reframe moves the gate and the photo from where they were on screen; both only
/// mean something within one set of photos. A project with no photos, or another project (whose version starts again
/// from 0), starts afresh; so does the arrival of photos, which the render thread handles itself (the develop).
/// </summary>
public static class PreviewContinuity
{
    /// <summary>
    /// True when what was drawn for <paramref name="before"/> (null: nothing yet) must be forgotten before drawing
    /// <paramref name="now"/>: the held picture, the shown frame, and the gate and photo a reframe would start from.
    /// </summary>
    public static bool StartsAfresh(ProjectSnapshot? before, ProjectSnapshot now)
    {
        ArgumentNullException.ThrowIfNull(now);
        return before is null || now.Frames.IsEmpty || now.Version < before.Version;
    }
}
