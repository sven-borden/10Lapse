using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using TenLapse.Presentation;
using TenLapse.Presentation.Preview;
using TenLapse.Services;
using TenLapse.Shell;
using TenLapse.Themes;
using Windows.Foundation;
using Windows.UI.ViewManagement;

namespace TenLapse.Preview;

/// <summary>
/// Hosts the preview's swap chain and its render thread (<see cref="PreviewRenderLoop"/>) while the view is in the
/// window, and tells it the size, the scale and the colors.
/// </summary>
public sealed partial class PreviewView : UserControl
{
    // Kept as a field: its events stop when nothing holds it. High contrast and the Windows colors change through it,
    // not always through the element's theme.
    private readonly UISettings _uiSettings = new();
    private PreviewSession? _session;
    private PreviewRenderLoop? _loop;
    private Presentation.Look.LookPanelViewModel? _lookPanel;

    public PreviewView()
    {
        InitializeComponent();
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
        Loaded += (_, _) => Start();
        Unloaded += (_, _) => Stop();
        SizeChanged += (_, _) => Resize();
        Panel.CompositionScaleChanged += (_, _) => Resize();
        ActualThemeChanged += (_, _) => RefreshColors();
        _uiSettings.ColorValuesChanged += OnColorValuesChanged;
    }

    /// <summary>Draws for <paramref name="session"/> from now on.</summary>
    internal void Attach(PreviewSession session)
    {
        _session = session;
        if (IsLoaded)
        {
            Stop();
            Start();
        }
    }

    /// <summary>
    /// Follows the Look panel's Before and After switch (#45): on Before the preview draws the photos without deflicker.
    /// Set once, with the session.
    /// </summary>
    internal void AttachLook(Presentation.Look.LookPanelViewModel look)
    {
        _lookPanel = look;
        look.ShowsOriginalChanged += (_, _) => _loop?.ShowOriginals(look.ShowsOriginal);
        _loop?.ShowOriginals(look.ShowsOriginal);
    }

    /// <summary>The photos landed: the first frame develops (motion spec, Arrival). Nothing when the preview is not running.</summary>
    internal void Develop() => _loop?.Develop();

    /// <summary>Stops drawing (the window closes).</summary>
    internal void Detach()
    {
        Stop();
        _session = null;
    }

    /// <summary>
    /// Where the photo is drawn: the gate, in this view's DIPs, with its corner radius (the theme crossfade leaves it out of
    /// its picture). Null while the project has no photos.
    /// </summary>
    internal PhotoArea? PhotoOnScreen()
    {
        if (_session is not { } session || session.Player.State.Snapshot is not { Frames.IsEmpty: false } snapshot)
        {
            return null;
        }

        var gate = PreviewLayout.Gate(ActualWidth, ActualHeight, snapshot.VideoSize);
        return gate.IsEmpty ? null : new PhotoArea(this, new Rect(gate.X, gate.Y, gate.Width, gate.Height), Radius());
    }

    private static float Radius() => (float)((CornerRadius)Application.Current.Resources["RadiusCard"]).TopLeft;

    /// <summary>Reads the colors again (high contrast or the Windows colors changed).</summary>
    internal void RefreshColors()
    {
        if (_loop is null)
        {
            return;
        }

        // On the gate (dark Letterbox) the placeholder's text is the dark theme's Ink Secondary; high contrast uses
        // the system's window and text colors. The gate's corners are the card radius token.
        var radius = Radius();
        var look = ThemeTokens.IsHighContrast
            ? new PreviewLook(ThemeTokens.Color("LayerColor", this), ThemeTokens.Color("CardColor", this), ThemeTokens.Color("InkColor", this), radius)
            : new PreviewLook(
                ThemeTokens.Color("LayerColor", this),
                (Windows.UI.Color)Application.Current.Resources["LetterboxColor"],
                ThemeTokens.Color("InkSecondaryColor", ElementTheme.Dark),
                radius);
        _loop.SetLook(look);
    }

    private void Start()
    {
        if (_session is not { } session || _loop is not null)
        {
            return;
        }

        _loop = new PreviewRenderLoop(Panel, session.Player, session.Images, session.Errors, session.Log);
        _loop.ShowOriginals(_lookPanel?.ShowsOriginal ?? false);
        session.Loop = _loop;
        RefreshColors();
        Resize();
    }

    private void Stop()
    {
        if (_session is not null && ReferenceEquals(_session.Loop, _loop))
        {
            _session.Loop = null;
        }

        _loop?.Dispose();
        _loop = null;
    }

    // Raised on a background thread.
    private void OnColorValuesChanged(UISettings sender, object args)
    {
        if (_session is { } session)
        {
            DispatcherQueue.TryEnqueueGuarded(session.Log, RefreshColors, ProjectText.SystemColorsFailed);
        }
    }

    private void Resize() => _loop?.Resize(new Size(ActualWidth, ActualHeight), Panel.CompositionScaleX);
}
