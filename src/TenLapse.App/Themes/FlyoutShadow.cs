using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Shapes;

namespace TenLapse.Themes;

/// <summary>
/// The Flyout shadow of DESIGN.md (Elevation) for anything that floats over content: InfoBars over the stage
/// (#28), the export panel's overlays (#24), the first-launch sample frame (#29). ThemeShadow throws on Windows
/// App SDK 2.5.1 (docs/STATUS.md, Decisions), so this draws the spec's two layers, ambient then key, as
/// Composition drop shadows from the ShadowFlyout* color tokens and the ElevationFlyout* numbers.
/// </summary>
/// <remarks>
/// Put a <see cref="Shape"/> (a <see cref="Rectangle"/> with the floating element's corner radius and any opaque
/// fill) in the same layout cell right behind the floating element, so it always has its size and shape, and call
/// <see cref="Attach"/> on it. The shadow follows its size and the theme. In high contrast the shadow colors are
/// transparent, so nothing is drawn. Dispose it when its view goes for good (the view's <c>Unloaded</c>, or when
/// the overlay is taken out): it holds Composition objects and handlers on the caster. #24 and #29 must do so;
/// the shell disposes the InfoBar's.
/// </remarks>
public sealed class FlyoutShadow : IDisposable
{
    private readonly Shape _caster;
    private readonly CompositionBrush _mask;
    private readonly ContainerVisual _container;
    private readonly DropShadow _ambient;
    private readonly DropShadow _key;
    private bool _disposed;

    private FlyoutShadow(Shape caster)
    {
        _caster = caster;
        var compositor = ElementCompositionPreview.GetElementVisual(caster).Compositor;
        _mask = caster.GetAlphaMask();
        _container = compositor.CreateContainerVisual();
        _ambient = AddLayer(compositor, ThemeTokens.Number("ElevationFlyoutBlur"), ThemeTokens.Number("ElevationFlyoutOffsetY"));
        _key = AddLayer(compositor, ThemeTokens.Number("ElevationFlyoutKeyBlur"), ThemeTokens.Number("ElevationFlyoutKeyOffsetY"));
        ElementCompositionPreview.SetElementChildVisual(caster, _container);

        Resize();
        Recolor();
        caster.SizeChanged += OnSizeChanged;
        caster.ActualThemeChanged += OnActualThemeChanged;
    }

    /// <summary>Casts the Flyout shadow from <paramref name="caster"/> (see the remarks on <see cref="FlyoutShadow"/>).</summary>
    public static FlyoutShadow Attach(Shape caster)
    {
        ArgumentNullException.ThrowIfNull(caster);
        return new FlyoutShadow(caster);
    }

    /// <summary>Takes the colors again, for a theme change the element does not see (high contrast on or off).</summary>
    public void Recolor()
    {
        if (_disposed)
        {
            return;
        }

        _ambient.Color = ThemeTokens.Color("ShadowFlyoutColor", _caster);
        _key.Color = ThemeTokens.Color("ShadowFlyoutKeyColor", _caster);
    }

    /// <summary>Takes the shadow off the caster and releases its Composition objects. Safe to call twice.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _caster.SizeChanged -= OnSizeChanged;
        _caster.ActualThemeChanged -= OnActualThemeChanged;
        ElementCompositionPreview.SetElementChildVisual(_caster, null);

        var layers = _container.Children.ToArray();
        _container.Children.RemoveAll();
        foreach (var layer in layers)
        {
            layer.Dispose();
        }

        _ambient.Dispose();
        _key.Dispose();
        _mask.Dispose();
        _container.Dispose();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => Resize();

    private void OnActualThemeChanged(FrameworkElement sender, object args) => Recolor();

    private DropShadow AddLayer(Compositor compositor, double blur, double offsetY)
    {
        var shadow = compositor.CreateDropShadow();
        shadow.Mask = _mask;
        shadow.BlurRadius = (float)blur;
        shadow.Offset = new Vector3(0, (float)offsetY, 0);

        var sprite = compositor.CreateSpriteVisual();
        sprite.Shadow = shadow;
        _container.Children.InsertAtTop(sprite);
        return shadow;
    }

    private void Resize()
    {
        var size = new Vector2((float)_caster.ActualWidth, (float)_caster.ActualHeight);
        _container.Size = size;
        foreach (var child in _container.Children)
        {
            child.Size = size;
        }
    }
}
