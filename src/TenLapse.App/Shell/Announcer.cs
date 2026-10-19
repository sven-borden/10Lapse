using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;

namespace TenLapse.Shell;

/// <summary>
/// The one way the app speaks to a screen reader without moving the focus (#38): a UI Automation notification raised
/// on an element's peer, which Narrator reads out. Every announcement goes through here, so the Debug focus walk's log
/// (<c>--focus-walk</c>) lists each one with its time, which proves what is said and how often without Narrator.
/// </summary>
internal static class Announcer
{
    /// <summary>Raises <paramref name="message"/> on <paramref name="element"/>'s automation peer (UI thread).</summary>
    /// <param name="element">The element it comes from: the panel or list the message is about.</param>
    /// <param name="message">What Narrator says.</param>
    /// <param name="processing">Whether it may be dropped or replaced by the next one under the same activity.</param>
    /// <param name="activityId">The activity: a newer message of the same activity replaces an older one waiting.</param>
    /// <param name="kind">What kind of event it is.</param>
    public static void Announce(
        UIElement element,
        string message,
        AutomationNotificationProcessing processing,
        string activityId,
        AutomationNotificationKind kind = AutomationNotificationKind.Other)
    {
        var peer = FrameworkElementAutomationPeer.FromElement(element) ?? FrameworkElementAutomationPeer.CreatePeerForElement(element);
        peer?.RaiseNotificationEvent(kind, processing, message, activityId);
#if DEBUG
        Debugging.DebugFocusWalk.Note($"announce [{activityId}, {processing}]: {message}");
#endif
    }
}
