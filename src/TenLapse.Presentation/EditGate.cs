namespace TenLapse.Presentation;

/// <summary>
/// Whether the project may be edited now (<see cref="ShellViewModel.Edits"/>). An export (#24) closes it while it runs:
/// the video is made from the project as it was when the export started, so removing, reordering or adding photos, or
/// starting a new project, waits until it ends. Every edit that is not in the inspector (which the export panel replaces)
/// asks <see cref="Allow"/> first; a refused edit raises <see cref="Refused"/>, and the export says why in an InfoBar.
/// On the UI thread.
/// </summary>
public sealed class EditGate
{
    /// <summary>Raised when <see cref="Allow"/> refuses an edit.</summary>
    public event EventHandler? Refused;

    /// <summary>True while edits may happen.</summary>
    public bool IsOpen { get; private set; } = true;

    /// <summary>True when the edit may go ahead; false, after raising <see cref="Refused"/>, while the gate is closed.</summary>
    public bool Allow()
    {
        if (IsOpen)
        {
            return true;
        }

        Refused?.Invoke(this, EventArgs.Empty);
        return false;
    }

    /// <summary>Stops edits until <see cref="Open"/>.</summary>
    internal void Close() => IsOpen = false;

    /// <summary>Lets edits happen again.</summary>
    internal void Open() => IsOpen = true;
}
