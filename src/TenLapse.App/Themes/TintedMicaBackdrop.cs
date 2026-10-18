using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace TenLapse.Themes;

/// <summary>
/// Mica warmed by a hair (DESIGN.md, Color: neutral chrome next to the warm Layer). Stock Mica takes its hue
/// from the wallpaper and reads cool blue on a blue one, next to the warm Layer surface; this tints it with the
/// Mica color token of the theme the window shows, opaque enough to neutralize the cast while the wallpaper
/// still shows through faintly. The configuration follows the theme, the window's activation (an inactive
/// window shows the token as a flat fallback, as stock Mica does) and high contrast, where the system's own
/// colors take over and no tint is set.
/// </summary>
internal sealed class TintedMicaBackdrop : SystemBackdrop
{
    // How much of the token color covers the wallpaper, and how much of its luminosity the luminosity layer
    // keeps. Measured on a saturated blue wallpaper next to the Layer surface (#FAF9F7 / #282726): stock Mica
    // read #EDF5F9 / #1A2227; 0.7 tint with 0.9 luminosity still #DFE4E6 / #1B272D; these read #F2F2F1 /
    // #1D1E1F, the token (#F3F2F0 / #1F1E1D) with the wallpaper a level or two under it. Below a luminosity
    // of 1 the wallpaper's hue comes back quickly.
    private const float LightTintOpacity = 0.9f;
    private const float LightLuminosityOpacity = 1f;
    private const float DarkTintOpacity = 0.95f;
    private const float DarkLuminosityOpacity = 1f;

    private MicaController? _controller;
    private SystemBackdropConfiguration? _configuration;

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);
        _configuration = GetDefaultSystemBackdropConfiguration(connectedTarget, xamlRoot);
        _controller = new MicaController { Kind = MicaKind.Base };
        Tint();
        _controller.AddSystemBackdropTarget(connectedTarget);
        _controller.SetSystemBackdropConfiguration(_configuration);
    }

    protected override void OnDefaultSystemBackdropConfigurationChanged(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        // The theme or high contrast changed: the default configuration has followed it, the tint follows here.
        base.OnDefaultSystemBackdropConfigurationChanged(target, xamlRoot);
        Tint();
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        base.OnTargetDisconnected(disconnectedTarget);
        if (_controller is not null)
        {
            _controller.RemoveSystemBackdropTarget(disconnectedTarget);
            _controller.Dispose();
            _controller = null;
        }

        _configuration = null;
    }

    /// <summary>Takes the tint again, for a change the configuration does not report (high contrast switched).</summary>
    public void Refresh() => Tint();

    private void Tint()
    {
        if (_controller is null || _configuration is null)
        {
            return;
        }

        if (_configuration.IsHighContrast || ThemeTokens.IsHighContrast)
        {
            _controller.ResetProperties();
            return;
        }

        var dark = _configuration.Theme == SystemBackdropTheme.Dark;
        var mica = ThemeTokens.Color("MicaColor", dark ? ElementTheme.Dark : ElementTheme.Light);
        _controller.TintColor = mica;
        _controller.FallbackColor = mica;
        _controller.TintOpacity = dark ? DarkTintOpacity : LightTintOpacity;
        _controller.LuminosityOpacity = dark ? DarkLuminosityOpacity : LightLuminosityOpacity;
    }
}
