using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using TenLapse.Presentation;
using TenLapse.Presentation.Filmstrip;
using Windows.Graphics.Imaging;

namespace TenLapse.Filmstrip;

/// <summary>
/// One thumbnail (#20), an element the strip's ItemsRepeater recycles: <see cref="Bind"/> gives it an item, and it draws
/// the item's states (FilmstripThumb.xaml) until <see cref="Unbind"/>. The picture is copied into a bitmap the element
/// keeps, so a recycled element reuses its memory and the cache's image can be freed at any time. For UI Automation it
/// is a list item: named "Frame 312, IMG_0312.JPG", selectable, invokable (shows the frame) and scrollable into view.
/// Pointer and keys are the strip's (<see cref="FilmstripView"/>).
/// </summary>
public sealed partial class FilmstripThumb : UserControl
{
    // The selection ring in the 2 px Layer gap, or right outside the playhead ring (which spans 2 to 4 px out).
    private static readonly Thickness _selectionInGap = new(-3);
    private static readonly Thickness _selectionOutside = new(-5);
    private static readonly CornerRadius _selectionInGapCorners = new(6);
    private static readonly CornerRadius _selectionOutsideCorners = new(8);

    private WriteableBitmap? _bitmap;
    private bool _pointerOver;
    private bool _unreadable;
    private string? _gapText;
    private string? _name;

    public FilmstripThumb()
    {
        InitializeComponent();
        PointerEntered += (_, _) => SetPointerOver(true);
        PointerExited += (_, _) => SetPointerOver(false);
        PointerCanceled += (_, _) => SetPointerOver(false);
    }

    /// <summary>The item shown; null while the element waits for reuse.</summary>
    internal FilmstripItem? Item { get; private set; }

    /// <summary>The strip, for the automation patterns.</summary>
    internal FilmstripView? Strip { get; set; }

    /// <summary>True when a picture shows.</summary>
    internal bool HasPicture => Picture.Visibility == Visibility.Visible;

    /// <summary>Shows <paramref name="item"/> at <paramref name="width"/> × <paramref name="height"/> DIPs.</summary>
    internal void Bind(FilmstripItem item, double width, double height)
    {
        Unbind();
        Item = item;
        item.PropertyChanged += OnItemPropertyChanged;
        Resize(width, height);
        ShowLoading();
        Refresh(selectionChanged: false);
    }

    /// <summary>Lets go of the item (the element goes back to the repeater's pool).</summary>
    internal void Unbind()
    {
        if (Item is { } item)
        {
            item.PropertyChanged -= OnItemPropertyChanged;
        }

        Item = null;
        ShowLoading();
    }

    /// <summary>The thumbnail size changed (the slider).</summary>
    internal void Resize(double width, double height)
    {
        Width = width;
        Height = height;
        GapLine.Height = height + 8;
    }

    /// <summary>Shows <paramref name="picture"/> (BGRA8 premultiplied), copied into the element's own bitmap.</summary>
    internal void ShowPicture(SoftwareBitmap picture)
    {
        if (_bitmap is null || _bitmap.PixelWidth != picture.PixelWidth || _bitmap.PixelHeight != picture.PixelHeight)
        {
            _bitmap = new WriteableBitmap(picture.PixelWidth, picture.PixelHeight);
            PictureBrush.ImageSource = _bitmap;
        }

        picture.CopyToBuffer(_bitmap.PixelBuffer);
        _bitmap.Invalidate();
        Picture.Visibility = Visibility.Visible;
        SetUnreadable(false);
    }

    /// <summary>The photo cannot be read: the Letterbox and the warning glyph.</summary>
    internal void ShowUnreadable()
    {
        Picture.Visibility = Visibility.Collapsed;
        SetUnreadable(true);
    }

    /// <summary>No picture yet: the Track fill.</summary>
    internal void ShowLoading()
    {
        Picture.Visibility = Visibility.Collapsed;
        SetUnreadable(false);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new FilmstripThumbAutomationPeer(this);

    private void SetUnreadable(bool unreadable)
    {
        if (_unreadable == unreadable)
        {
            return;
        }

        _unreadable = unreadable;
        Unreadable.Visibility = Placeholder.Visibility = unreadable ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetPointerOver(bool over)
    {
        _pointerOver = over;
        Refresh(selectionChanged: false);
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e) =>
        Refresh(selectionChanged: e.PropertyName == nameof(FilmstripItem.IsSelected));

    private void Refresh(bool selectionChanged)
    {
        if (Item is not { } item)
        {
            return;
        }

        PlayheadRing.Visibility = item.IsPlayhead ? Visibility.Visible : Visibility.Collapsed;
        SelectionRing.Visibility = item.IsSelected ? Visibility.Visible : Visibility.Collapsed;

        // Selected and at the playhead: the Ink ring right outside the amber one, so the frame on screen shows whether
        // Delete takes it.
        SelectionRing.Margin = item.IsPlayhead ? _selectionOutside : _selectionInGap;
        SelectionRing.CornerRadius = item.IsPlayhead ? _selectionOutsideCorners : _selectionInGapCorners;
        HoverRing.Visibility = _pointerOver && !item.IsSelected && !item.IsPlayhead ? Visibility.Visible : Visibility.Collapsed;
        Photo.Opacity = item.IsPending ? 0.35 : 1;
        GapMarker.Visibility = item.GapBefore is null ? Visibility.Collapsed : Visibility.Visible;
        var gap = item.GapText;
        if (!string.Equals(gap, _gapText, StringComparison.Ordinal))
        {
            _gapText = gap;
            ToolTipService.SetToolTip(this, gap);
            AutomationProperties.SetHelpText(this, gap ?? string.Empty);
        }

        var name = item.Name;
        if (!string.Equals(name, _name, StringComparison.Ordinal))
        {
            _name = name;
            AutomationProperties.SetName(this, name);
        }

        AutomationProperties.SetItemStatus(this, item.IsPlayhead ? ProjectText.FilmstripPlayheadStatus : string.Empty);
        if (selectionChanged
            && AutomationPeer.ListenerExists(AutomationEvents.PropertyChanged)
            && FrameworkElementAutomationPeer.FromElement(this) is { } peer)
        {
            peer.RaisePropertyChangedEvent(SelectionItemPatternIdentifiers.IsSelectedProperty, !item.IsSelected, item.IsSelected);
        }
    }

    /// <summary>A list item to UI Automation, with the selection of the strip it is in.</summary>
    private sealed partial class FilmstripThumbAutomationPeer(FilmstripThumb owner)
        : FrameworkElementAutomationPeer(owner), ISelectionItemProvider, IInvokeProvider, IScrollItemProvider
    {
        public bool IsSelected => owner.Item?.IsSelected ?? false;

        public IRawElementProviderSimple? SelectionContainer =>
            owner.Strip?.ListPeer() is { } list ? ProviderFromPeer(list) : null;

        public void Select() => owner.Strip?.SelectFromAutomation(owner, FilmstripView.AutomationSelection.Only);

        public void AddToSelection() => owner.Strip?.SelectFromAutomation(owner, FilmstripView.AutomationSelection.Add);

        public void RemoveFromSelection() => owner.Strip?.SelectFromAutomation(owner, FilmstripView.AutomationSelection.Remove);

        public void Invoke() => Select();

        public void ScrollIntoView() => owner.StartBringIntoView();

        protected override object? GetPatternCore(PatternInterface patternInterface) =>
            patternInterface is PatternInterface.SelectionItem or PatternInterface.Invoke or PatternInterface.ScrollItem
                ? this
                : base.GetPatternCore(patternInterface);

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ListItem;

        protected override string GetClassNameCore() => nameof(FilmstripThumb);

        protected override int GetPositionInSetCore() => owner.Item?.Number ?? -1;

        protected override int GetSizeOfSetCore() => owner.Strip?.ItemCount ?? -1;
    }
}
