using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

namespace TenLapse.Shell;

/// <summary>
/// A grid that is a region to UI Automation (#38): a plain Grid has no automation peer, so the landmark and name set on it
/// with <c>AutomationProperties</c> would reach no screen reader. This one is a Group with them, so Narrator's landmark
/// navigation (D) jumps between the stage, the filmstrip, the inspector or the export panel, and the Settings page.
/// It lays out exactly as a Grid.
/// </summary>
public sealed partial class Region : Grid
{
    protected override AutomationPeer OnCreateAutomationPeer() => new RegionAutomationPeer(this);

    private sealed partial class RegionAutomationPeer(Region owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;

        protected override string GetClassNameCore() => nameof(Region);
    }
}
