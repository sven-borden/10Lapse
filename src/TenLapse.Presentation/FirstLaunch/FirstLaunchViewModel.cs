using CommunityToolkit.Mvvm.ComponentModel;
using TenLapse.Core.Timing;

namespace TenLapse.Presentation.FirstLaunch;

/// <summary>
/// The first-launch drop zone (#29): its texts, its drag-over look and whether its small timelapse (Daybreak)
/// may run. The actions are the import's own commands (<see cref="ImportViewModel.ChooseFolderCommand"/>,
/// <see cref="ImportViewModel.ChoosePhotosCommand"/>) and drops go through the shell's drop handling, so there is
/// one import path. Lives on the UI thread like the shell.
/// </summary>
/// <remarks>
/// The drag-over look follows the whole window, as the prototype's does: it shows while a drag the shell would
/// accept is over the window, and goes when the drag drops, leaves the window or is cancelled. A drag leaving one
/// element for another inside the window raises DragLeave and then DragEnter and DragOver at once, while a drag
/// cancelled with Escape raises only DragLeave; so a leave inside the window is a ticket (<see cref="DragLeft"/>)
/// that the view confirms after a short grace (<see cref="ConfirmLeave"/>), and any drag event in between voids it.
/// </remarks>
public sealed partial class FirstLaunchViewModel : ObservableObject
{
    /// <summary>The example's photo count: 300 photos at 30 fps become a 10-second clip.</summary>
    public const int ExamplePhotos = 300;

    /// <summary>How long a leave inside the window waits for the drag to show up again before the look goes.</summary>
    public static readonly TimeSpan DragLeaveGrace = TimeSpan.FromMilliseconds(120);

    private long _dragActivity;

    /// <summary>The example's frame rate.</summary>
    public static Fraction ExampleRate { get; } = new(30, 1);

    /// <summary>The title: what to do at rest, what will happen while a drag is over the window.</summary>
    public string Title => IsDragOver ? ProjectText.FirstLaunchDropTitle : ProjectText.FirstLaunchTitle;

    /// <summary>"10Lapse plays your photos back as a video. Shoot one photo every few seconds, and 300 of them become a 10-second clip at 30 frames per second." (Main.dc.html)</summary>
    public string Explanation { get; } = ProjectText.FirstLaunchExplanation(ExamplePhotos, ExampleRate);

    /// <summary>True while a drag the window would accept is over it: the drop zone shows its drag-over look.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    public partial bool IsDragOver { get; private set; }

    /// <summary>True while the drop zone and its timelapse are on screen (the Empty state, wide enough for it).</summary>
    [ObservableProperty]
    public partial bool IsShown { get; set; }

    /// <summary>True while the window is the active one; the timelapse stops in the background (motion spec, Daybreak).</summary>
    [ObservableProperty]
    public partial bool IsWindowActive { get; set; }

    /// <summary>A drop zone at rest in an active window, not yet on screen.</summary>
    public FirstLaunchViewModel() => IsWindowActive = true;

    /// <summary>
    /// Whether the timelapse runs: on screen, in the active window, and with animations on in Windows. With
    /// animations off it shows its still frame instead (<see cref="Daybreak.StillFrame"/>).
    /// </summary>
    public bool ShouldAnimate(bool animationsEnabled) => animationsEnabled && IsShown && IsWindowActive;

    /// <summary>
    /// A drag moved over the window (DragOver); <paramref name="acceptable"/> is the shell's verdict on the look
    /// (<see cref="DragVerdict.ShowsLook"/>: taken, and counted).
    /// </summary>
    public void DragOver(bool acceptable)
    {
        DragEntered();
        IsDragOver = acceptable;
    }

    /// <summary>A drag entered an element of the window: like any drag event, it voids a leave waiting in <see cref="ConfirmLeave"/>.</summary>
    public void DragEntered() => _dragActivity++;

    /// <summary>The drag left an element but the pointer is still inside the window: a ticket for <see cref="ConfirmLeave"/>.</summary>
    public long DragLeft() => ++_dragActivity;

    /// <summary>Ends the drag-over look if nothing happened to the drag since <paramref name="ticket"/> was taken.</summary>
    public void ConfirmLeave(long ticket)
    {
        if (ticket == _dragActivity)
        {
            IsDragOver = false;
        }
    }

    /// <summary>The drag dropped or left the window.</summary>
    public void DragEnded()
    {
        _dragActivity++;
        IsDragOver = false;
    }
}
