using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using TenLapse.Themes;
using Windows.Foundation;

namespace TenLapse.Preview;

/// <summary>
/// Hosts the preview's swap chain and its render thread (<see cref="PreviewRenderLoop"/>) while the view is in the
/// window, and tells it the size, the scale and the colors.
/// </summary>
public sealed partial class PreviewView : UserControl
{
    private PreviewSession? _session;
    private PreviewRenderLoop? _loop;

    public PreviewView()
    {
        InitializeComponent();
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
        Loaded += (_, _) => Start();
        Unloaded += (_, _) => Stop();
        SizeChanged += (_, _) => Resize();
        Panel.CompositionScaleChanged += (_, _) => Resize();
        ActualThemeChanged += (_, _) => RefreshColors();
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

    /// <summary>Stops drawing (the window closes).</summary>
    internal void Detach()
    {
        Stop();
        _session = null;
    }

    /// <summary>Reads the colors again (high contrast or the Windows colors changed).</summary>
    internal void RefreshColors()
    {
        if (_loop is null)
        {
            return;
        }

        // On the gate (dark Letterbox) the placeholder's text is the dark theme's Ink Secondary; high contrast uses
        // the system's window and text colors.
        var colors = ThemeTokens.IsHighContrast
            ? new PreviewColors(ThemeTokens.Color("CardColor", this), ThemeTokens.Color("InkColor", this))
            : new PreviewColors(
                (Windows.UI.Color)Application.Current.Resources["LetterboxColor"],
                ThemeTokens.Color("InkSecondaryColor", ElementTheme.Dark));
        _loop.SetColors(colors);
    }

    private void Start()
    {
        if (_session is not { } session || _loop is not null)
        {
            return;
        }

        _loop = new PreviewRenderLoop(Panel, session.Player, session.Images, session.Errors, session.Log);
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

    private void Resize() => _loop?.Resize(new Size(ActualWidth, ActualHeight), Panel.CompositionScaleX);
}
