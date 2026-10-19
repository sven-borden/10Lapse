using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using TenLapse.Presentation;

namespace TenLapse.Filmstrip;

/// <summary>
/// The filmstrip's list host (#20): the scroller, the repeater and the drag layer. To UI Automation it is the list itself,
/// named "Filmstrip": its children are the thumbnails on screen in playback order (<see cref="FilmstripView.ThumbsOnScreen"/>),
/// its Selection pattern holds the selected ones among them, and its Scroll pattern is the scroller's. Narrator hears removals and undos through it.
/// </summary>
public sealed partial class FilmstripList : Grid
{
    /// <summary>The strip it belongs to.</summary>
    internal FilmstripView? Strip { get; set; }

    protected override AutomationPeer OnCreateAutomationPeer() => new FilmstripListAutomationPeer(this);

    private sealed partial class FilmstripListAutomationPeer(FilmstripList owner) : FrameworkElementAutomationPeer(owner), ISelectionProvider
    {
        public bool CanSelectMultiple => true;

        // Never empty while there are frames: the frame at the playhead stands in for none.
        public bool IsSelectionRequired => true;

        public IRawElementProviderSimple[] GetSelection() =>
            owner.Strip is { } strip
                ? [.. strip.ThumbsOnScreen().Where(thumb => thumb.Item?.IsSelected == true).Select(thumb => ProviderFromPeer(CreatePeerForElement(thumb)))]
                : [];

        protected override IList<AutomationPeer> GetChildrenCore() =>
            owner.Strip is { } strip ? [.. strip.ThumbsOnScreen().Select(CreatePeerForElement)] : [];

        protected override object? GetPatternCore(PatternInterface patternInterface) => patternInterface switch
        {
            PatternInterface.Selection => this,
            PatternInterface.Scroll => owner.Strip?.ScrollPattern(),
            _ => base.GetPatternCore(patternInterface),
        };

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.List;

        protected override string GetClassNameCore() => "Filmstrip";

        protected override string GetNameCore() => ProjectText.FilmstripName;
    }
}
