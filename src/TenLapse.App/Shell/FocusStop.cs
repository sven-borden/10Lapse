using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

namespace TenLapse.Shell;

/// <summary>
/// A place the keyboard focus can rest that does nothing on Enter or Space (#38): the running export's title, where focus
/// lands as the export starts. A screen reader reads its name and heading level (<c>AutomationProperties</c>) as a
/// Group; the progress bar beside it is not a tab stop, so Narrator does not read every percent of a focused bar, and
/// Cancel export is not where a stray Enter lands. It lays out as a ContentControl.
/// </summary>
public sealed partial class FocusStop : ContentControl
{
    public FocusStop()
    {
        IsTabStop = true;
        UseSystemFocusVisuals = true;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new FocusStopAutomationPeer(this);

    private sealed partial class FocusStopAutomationPeer(FocusStop owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;

        protected override string GetClassNameCore() => nameof(FocusStop);
    }
}
